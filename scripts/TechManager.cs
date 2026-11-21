using System.Collections.Generic;
using Godot;

namespace Star;

public enum TechLine
{
    Military,
    Survival,
    Civilization,
}

public class TechNode
{
    public string Id { get; init; } = string.Empty;
    public TechLine Line { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public float BaseCost { get; init; }
    public bool Unlocked { get; set; }
}

public enum UnstableKind
{
    OverloadWeapon,
    ExperimentalArmor,
    HighOutputEngine,
}

/// <summary>三条科技线：同线递增、跨线惩罚；文明线作为乘数放大另两条线的数值效果。</summary>
public class TechManager
{
    private readonly BattleManager _battle;
    private readonly ResourceManager _resources;
    private readonly List<TechNode> _nodes = new();
    private readonly List<UnstableKind> _unstable = new();
    private readonly System.Random _rng = new(5150);

    private TechNode _researching;
    private float _elapsed;

    public TechManager(BattleManager battle, ResourceManager resources)
    {
        _battle = battle;
        _resources = resources;
        BuildNodes();
    }

    public IReadOnlyList<TechNode> Nodes => _nodes;
    public TechNode Researching => _researching;
    public IReadOnlyList<UnstableKind> Unstable => _unstable;
    public float ResearchProgress =>
        _researching == null ? 0f : Mathf.Clamp(_elapsed / ResearchSeconds(_researching), 0f, 1f);

    public static string LineName(TechLine line) => line switch
    {
        TechLine.Military => "军工",
        TechLine.Survival => "生存",
        _ => "文明",
    };

    public static string UnstableName(UnstableKind kind) => kind switch
    {
        UnstableKind.OverloadWeapon => "过载武器",
        UnstableKind.ExperimentalArmor => "试验装甲",
        _ => "高效引擎",
    };

    public static string UnstableDescription(UnstableKind kind) => kind switch
    {
        UnstableKind.OverloadWeapon => "+25% 伤害，但油耗 +30%",
        UnstableKind.ExperimentalArmor => "+25 护甲，但需要发电站在线",
        _ => "+20% 移速，但油耗 +50%",
    };

    public int Count(TechLine line)
    {
        int count = 0;
        foreach (var node in _nodes)
        {
            if (node.Line == line && node.Unlocked)
            {
                count++;
            }
        }

        return count;
    }

    public bool IsUnlocked(string id)
    {
        foreach (var node in _nodes)
        {
            if (node.Id == id)
            {
                return node.Unlocked;
            }
        }

        return false;
    }

    /// <summary>软互斥：本线已点亮越多越贵，其他两线点亮也会抬高成本。</summary>
    public float Cost(TechNode node)
    {
        int own = Count(node.Line);
        int other = 0;
        foreach (var candidate in _nodes)
        {
            if (candidate.Line != node.Line && candidate.Unlocked)
            {
                other++;
            }
        }

        return Mathf.Ceil(node.BaseCost * (1f + GameBalance.SoftCostOwn * own)
            * (1f + GameBalance.SoftCostOther * other));
    }

    public float ResearchSeconds(TechNode node)
    {
        return GameBalance.TechResearchBaseSeconds + GameBalance.TechResearchPerLineSeconds * Count(node.Line);
    }

    private int CivCount => Count(TechLine.Civilization);

    private float CivMultiplier => 1f + GameBalance.CivBonusPerTech * CivCount;

    public float DamageMultiplier =>
        (1f + (IsUnlocked("M1") ? GameBalance.MilitaryDamageBonus * CivMultiplier : 0f))
        * (_unstable.Contains(UnstableKind.OverloadWeapon) ? 1.25f : 1f);

    public int VisionBonus => IsUnlocked("M2") ? Mathf.Max(1, Mathf.RoundToInt(CivMultiplier)) : 0;

    public float PowerOutputMultiplier => 1f + (IsUnlocked("S1") ? GameBalance.PowerOutputBonus * CivMultiplier : 0f);

    public float BuildingEfficiencyMultiplier =>
        1f + (IsUnlocked("S2") ? GameBalance.BuildingEfficiencyBonus * CivMultiplier : 0f);

    public float FuelMultiplier
    {
        get
        {
            float value = 1f;
            if (IsUnlocked("S3"))
            {
                value *= Mathf.Max(0.5f, 1f - GameBalance.SurvivalFuelReduction * CivMultiplier);
            }

            if (_unstable.Contains(UnstableKind.OverloadWeapon))
            {
                value *= 1.3f;
            }

            if (_unstable.Contains(UnstableKind.HighOutputEngine))
            {
                value *= 1.5f;
            }

            return value;
        }
    }

    public float SpeedMultiplier => _unstable.Contains(UnstableKind.HighOutputEngine) ? 1.2f : 1f;

    public bool HasExperimentalArmor => _unstable.Contains(UnstableKind.ExperimentalArmor);

    public float StorageMultiplier => 1f + (IsUnlocked("S4") ? GameBalance.StorageBonus * CivMultiplier : 0f);

    public bool HeavyTankUnlocked => IsUnlocked("M3");

    public bool MediumShipUnlocked => IsUnlocked("M4");

    public bool BeaconUnlocked => IsUnlocked("M5");

    public bool ArkUnlocked => IsUnlocked("S5");

    public float ProductionTimeMultiplier =>
        IsUnlocked("C2") ? 1f - GameBalance.ProductionTimeReduction : 1f;

    public float ReplenishMultiplier =>
        IsUnlocked("C3") ? 1f + GameBalance.ReplenishBonus * CivMultiplier : 1f;

    public float ResearchSuccessBonus =>
        (IsUnlocked("C1") ? GameBalance.ResearchSuccessBonusC1 : 0f)
        + (IsUnlocked("C4") ? GameBalance.ResearchSuccessBonusC4 : 0f);

    public float SampleResearchTimeMultiplier =>
        IsUnlocked("C4") ? GameBalance.SampleResearchTimeMultiplier : 1f;

    public float CaptainXpMultiplier => 1f + GameBalance.CivCaptainGrowthBonus * CivCount;

    public bool TryStart(TechNode node)
    {
        if (node == null || node.Unlocked)
        {
            return false;
        }

        if (_researching != null)
        {
            _battle.ShowMessage($"正在研究 {_researching.Name}，完成后再开新项目");
            return false;
        }

        float cost = Cost(node);
        if (_resources.Get(ResourceKind.Data) < cost - 0.01f)
        {
            _battle.ShowMessage($"知识数据不足：{node.Name} 需要 {cost:F0}");
            return false;
        }

        _resources.Consume(ResourceKind.Data, cost);
        _researching = node;
        _elapsed = 0f;
        _battle.ShowMessage($"开始研究 {node.Name}（{LineName(node.Line)}线）");
        return true;
    }

    public void Update(float dt)
    {
        if (_researching == null)
        {
            return;
        }

        _elapsed += dt;
        if (_elapsed < ResearchSeconds(_researching))
        {
            return;
        }

        var node = _researching;
        _researching = null;
        _elapsed = 0f;
        node.Unlocked = true;
        _battle.PlayAudioCue(WastelandAudio.Cue.Complete);
        _battle.ShowMessage($"科技点亮：{node.Name}（{node.Description}）");
    }

    /// <summary>遗迹样本研究成功时，跳过一级前置直接点亮该线下一项（不能直接拿顶级）。</summary>
    public bool GrantNext(TechLine line)
    {
        int total = 0;
        foreach (var node in _nodes)
        {
            if (node.Line == line)
            {
                total++;
            }
        }

        int position = 0;
        TechNode candidate = null;
        foreach (var node in _nodes)
        {
            if (node.Line != line)
            {
                continue;
            }

            position++;
            if (!node.Unlocked)
            {
                candidate = node;
                break;
            }
        }

        if (candidate == null || position >= total)
        {
            return false;
        }

        candidate.Unlocked = true;
        _battle.PlayAudioCue(WastelandAudio.Cue.Complete);
        _battle.ShowMessage($"逆向工程跳级点亮：{candidate.Name}");
        return true;
    }

    public UnstableKind? RollUnstable()
    {
        var pool = new List<UnstableKind>();
        foreach (UnstableKind kind in System.Enum.GetValues(typeof(UnstableKind)))
        {
            if (!_unstable.Contains(kind))
            {
                pool.Add(kind);
            }
        }

        if (pool.Count == 0)
        {
            return null;
        }

        var picked = pool[_rng.Next(0, pool.Count)];
        _unstable.Add(picked);
        return picked;
    }

    private void BuildNodes()
    {
        Add("M1", TechLine.Military, "武器校准", "+15% 伤害", 22f);
        Add("M2", TechLine.Military, "侦察光学", "视野 +1", 24f);
        Add("M3", TechLine.Military, "重坦解锁", "坦克厂可生产重坦", 30f);
        Add("M4", TechLine.Military, "中型飞船", "机库可生产中飞船", 34f);
        Add("M5", TechLine.Military, "通信阵列", "解锁信标建设前置", 40f);

        Add("S1", TechLine.Survival, "高效发电", "发电产能 +25%", 20f);
        Add("S2", TechLine.Survival, "岗位优化", "建筑效率 +15%", 22f);
        Add("S3", TechLine.Survival, "燃油精炼", "油耗降低", 28f);
        Add("S4", TechLine.Survival, "仓储扩容", "仓储上限 +25%", 30f);
        Add("S5", TechLine.Survival, "工程体系", "解锁方舟修复前置", 36f);

        Add("C1", TechLine.Civilization, "科研方法", "样本研究成功率 +15%", 18f);
        Add("C2", TechLine.Civilization, "教育体系", "生产时间 -15%", 20f);
        Add("C3", TechLine.Civilization, "后勤网络", "补员速度 +25%", 22f);
        Add("C4", TechLine.Civilization, "逆向工程", "样本研究更快且成功率 +10%", 26f);
    }

    private void Add(string id, TechLine line, string name, string description, float cost)
    {
        _nodes.Add(new TechNode
        {
            Id = id,
            Line = line,
            Name = name,
            Description = description,
            BaseCost = cost,
        });
    }
}
