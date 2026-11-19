using System.Collections.Generic;
using Godot;

namespace Star;

public enum FactionId
{
    Player,
    Junta,
    Caravan,
    Cult,
    Alien,
}

public class FactionState
{
    public FactionId Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Relation { get; set; }
    public bool AtWar { get; set; }
    public bool Eliminated { get; set; }
    public int Strength { get; set; } = 100;
    public int TruceUntilDay { get; set; }
    public bool ScoutRequested { get; set; }

    public string StateName => Eliminated ? "已消亡" : AtWar ? "战争" : "和平";
}

/// <summary>三个阵营的独立关系、交易、和谈停战、相互攻伐与战力衰减。</summary>
public class FactionManager
{
    private readonly BattleManager _battle;
    private readonly ResourceManager _resources;
    private readonly BuildingManager _buildings;
    private readonly Dictionary<FactionId, FactionState> _states = new();
    private readonly System.Random _rng = new(20261119);
    private int _lastDay;

    public FactionManager(BattleManager battle, ResourceManager resources, BuildingManager buildings)
    {
        _battle = battle;
        _resources = resources;
        _buildings = buildings;
        _lastDay = battle.Clock?.Day ?? 1;

        Add(FactionId.Junta, "军政府", "掌握兵力和矿产，索要资源与地盘", GameBalance.StartRelationJunta);
        Add(FactionId.Caravan, "商队", "什么都做交易，情报灵通，价格随局势浮动", GameBalance.StartRelationCaravan);
        Add(FactionId.Cult, "教团", "握有旧文明技术与信仰，排外，但能提供非标准科技", GameBalance.StartRelationCult);
    }

    public IEnumerable<FactionState> States => _states.Values;

    public FactionState Get(FactionId id) => _states.GetValueOrDefault(id);

    public bool IsHostile(FactionId id) => id == FactionId.Alien
        || (_states.TryGetValue(id, out var state) && state.AtWar && !state.Eliminated);

    public bool CanTrade(FactionId id) =>
        _states.TryGetValue(id, out var state) && !state.AtWar && !state.Eliminated && state.Relation >= 0;

    public void ApplyRelation(FactionId id, int delta)
    {
        if (!_states.TryGetValue(id, out var state) || state.Eliminated)
        {
            return;
        }

        state.Relation = Mathf.Clamp(state.Relation + delta, -100, 100);
        if (delta != 0)
        {
            _battle.ShowMessage($"{state.Name}关系 {(delta > 0 ? "+" : string.Empty)}{delta}，当前 {state.Relation}");
        }

        CheckWar(id, state);
    }

    public bool TryGift(FactionId id)
    {
        if (!_states.TryGetValue(id, out var state) || state.Eliminated)
        {
            return false;
        }

        const float cost = GameBalance.GiftScrapCost;
        if (_resources.Get(ResourceKind.Scrap) < cost)
        {
            _battle.ShowMessage("废铁不足，无法送礼");
            return false;
        }

        _resources.Consume(ResourceKind.Scrap, cost);
        ApplyRelation(id, GameBalance.GiftRelation);
        _battle.ShowMessage($"向{state.Name}送出物资，关系改善");
        return true;
    }

    public bool TryTrade(FactionId id)
    {
        if (!CanTrade(id))
        {
            _battle.ShowMessage("当前关系或状态不允许交易");
            return false;
        }

        var state = _states[id];
        float priceFactor = state.Id == FactionId.Caravan
            ? GameBalance.CaravanPriceFactor
            : Mathf.Clamp(1.2f - state.Relation / 250f, 0.9f, 1.6f);

        switch (id)
        {
            case FactionId.Caravan:
            {
                float cost = GameBalance.TradeScrapCost * priceFactor;
                if (_resources.Get(ResourceKind.Scrap) < cost)
                {
                    _battle.ShowMessage($"商队开价 {cost:F0} 废铁，库存不足");
                    return false;
                }

                _resources.Consume(ResourceKind.Scrap, cost);
                _resources.Add(ResourceKind.Data, GameBalance.TradeDataGain);
                ApplyRelation(id, GameBalance.TradeRelationGain);
                _battle.ShowMessage($"与商队交易：废铁 -{cost:F0}，知识数据 +25");
                return true;
            }
            case FactionId.Junta:
            {
                float cost = GameBalance.TradeDataCost * priceFactor;
                if (_resources.Get(ResourceKind.Data) < cost)
                {
                    _battle.ShowMessage($"军政府索要 {cost:F0} 知识数据，库存不足");
                    return false;
                }

                _resources.Consume(ResourceKind.Data, cost);
                _resources.Add(ResourceKind.Fuel, GameBalance.TradeFuelGain);
                ApplyRelation(id, GameBalance.TradeJuntaRelationGain);
                _battle.ShowMessage($"与军政府交易：知识数据 -{cost:F0}，燃料 +30");
                return true;
            }
            default:
            {
                float cost = GameBalance.TradeScrapCost * priceFactor;
                if (_resources.Get(ResourceKind.Scrap) < cost)
                {
                    _battle.ShowMessage($"教团要求 {cost:F0} 废铁作为供奉");
                    return false;
                }

                _resources.Consume(ResourceKind.Scrap, cost);
                _resources.Add(ResourceKind.Medicine, GameBalance.TradeMedicineGain);
                _resources.Add(ResourceKind.Data, GameBalance.TradeCultDataGain);
                ApplyRelation(id, GameBalance.TradeCultRelationGain);
                _battle.ShowMessage($"与教团交易：废铁 -{cost:F0}，药品 +15，知识数据 +10");
                return true;
            }
        }
    }

    public bool TryNegotiatePeace(FactionId id)
    {
        if (!_states.TryGetValue(id, out var state) || !state.AtWar || state.Eliminated)
        {
            _battle.ShowMessage("当前并未与该阵营交战");
            return false;
        }

        const float scrapCost = GameBalance.PeaceScrapCost;
        const float fuelCost = GameBalance.PeaceFuelCost;
        if (_resources.Get(ResourceKind.Scrap) < scrapCost || _resources.Get(ResourceKind.Fuel) < fuelCost)
        {
            _battle.ShowMessage("和谈需要废铁 80、燃料 40；也可以选择让出前哨");
            return false;
        }

        _resources.Consume(ResourceKind.Scrap, scrapCost);
        _resources.Consume(ResourceKind.Fuel, fuelCost);
        MakePeace(id, state);
        return true;
    }

    public bool TryCedeOutpost(FactionId id)
    {
        if (!_states.TryGetValue(id, out var state) || !state.AtWar || state.Eliminated)
        {
            return false;
        }

        Building outpost = null;
        foreach (var building in _buildings.Buildings)
        {
            if (building.IsAlive && building.Kind == BuildingKind.OutpostStorage)
            {
                outpost = building;
                break;
            }
        }

        if (outpost == null)
        {
            _battle.ShowMessage("已经没有可以让出的前哨");
            return false;
        }

        _buildings.Damage(outpost, 99999f);
        state.Relation = Mathf.Clamp(state.Relation + GameBalance.CedeRelationGain, -100, 100);
        MakePeace(id, state);
        return true;
    }

    public void ForceScout(FactionId id)
    {
        if (_states.TryGetValue(id, out var state) && !state.Eliminated)
        {
            state.ScoutRequested = true;
        }
    }

    public bool ConsumeScoutRequest(FactionId id)
    {
        if (_states.TryGetValue(id, out var state) && state.ScoutRequested)
        {
            state.ScoutRequested = false;
            return true;
        }

        return false;
    }

    public void OnUnitLost(FactionId id)
    {
        if (id == FactionId.Player || id == FactionId.Alien)
        {
            return;
        }

        if (!_states.TryGetValue(id, out var state) || state.Eliminated)
        {
            return;
        }

        state.Strength = Mathf.Max(0, state.Strength - GameBalance.FactionUnitLossStrength);
        if (state.Strength > 0)
        {
            return;
        }

        state.Eliminated = true;
        state.AtWar = false;
        _battle.ShowMessage($"{state.Name}已被削弱至消亡");
        _battle.EliminateFactionUnits(id);
    }

    public void Update(float dt)
    {
        var clock = _battle.Clock;
        if (clock == null || clock.Day == _lastDay)
        {
            return;
        }

        _lastDay = clock.Day;
        foreach (var state in _states.Values)
        {
            if (state.Eliminated)
            {
                continue;
            }

            int losses = _rng.Next(GameBalance.FactionDailyLossMin, GameBalance.FactionDailyLossMax + 1)
                + (clock.IsStorm ? GameBalance.FactionStormLoss : 0);
            state.Strength = Mathf.Max(0, state.Strength - losses);
            if (state.Strength == 0)
            {
                state.Eliminated = true;
                state.AtWar = false;
                _battle.ShowMessage($"{state.Name}在混战与沙暴中消亡");
                _battle.EliminateFactionUnits(state.Id);
                continue;
            }

            if (state.AtWar && clock.Day > state.TruceUntilDay + 5
                && state.Relation > GameBalance.PassivePeaceRelation
                && _rng.NextDouble() < GameBalance.PassivePeaceChance)
            {
                MakePeace(state.Id, state);
                _battle.ShowMessage($"{state.Name}主动停战");
                continue;
            }

            CheckWar(state.Id, state);
        }
    }

    public string StatusText()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var state in _states.Values)
        {
            string truce = state.AtWar || state.TruceUntilDay <= (_battle.Clock?.Day ?? 1)
                ? string.Empty
                : $" 停战至第{state.TruceUntilDay}日";
            builder.Append($"{state.Name}  关系 {state.Relation}  {state.StateName}  战力 {state.Strength}{truce}\n");
        }

        return builder.ToString().TrimEnd();
    }

    private void MakePeace(FactionId id, FactionState state)
    {
        state.AtWar = false;
        state.TruceUntilDay = (_battle.Clock?.Day ?? 1) + GameBalance.TruceDays;
        state.Relation = Mathf.Clamp(state.Relation + GameBalance.PeaceRelationGain, -100, 100);
        _battle.ShowMessage($"与{state.Name}和谈成功，停战至第{state.TruceUntilDay}日（不等于结盟）");
    }

    private void CheckWar(FactionId id, FactionState state)
    {
        if (state.AtWar || state.Eliminated)
        {
            return;
        }

        int day = _battle.Clock?.Day ?? 1;
        if (day <= state.TruceUntilDay)
        {
            return;
        }

        if (state.Relation <= GameBalance.WarThreshold)
        {
            state.AtWar = true;
            _battle.ShowMessage($"{state.Name}向玩家宣战");
        }
    }

    private void Add(FactionId id, string name, string description, int relation)
    {
        _states[id] = new FactionState
        {
            Id = id,
            Name = name,
            Description = description,
            Relation = relation,
        };
    }
}
