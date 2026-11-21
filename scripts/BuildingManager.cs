using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Star;

/// <summary>基地建筑：连通判定、电力/人力结算、维修、沙暴损伤与摧毁。</summary>
public class BuildingManager
{
    private const float RepairPerSecond = GameBalance.RepairPerSecond;
    private const float SandstormDamagePerSecond = GameBalance.SandstormDamagePerSecond;
    private const float DisconnectedDecayPerSecond = GameBalance.DisconnectedDecayPerSecond;

    private readonly BattleManager _battle;
    private readonly HexMap _map;
    private readonly List<Building> _buildings = new();
    private readonly Dictionary<HexCoord, Building> _byCoord = new();
    private readonly HashSet<Building> _connected = new();

    private bool _sandstormActive;
    private int _populationLoss;
    private int _populationBonus;
    private int _lastDay = 1;

    public Building Core { get; private set; }
    public IReadOnlyList<Building> Buildings => _buildings;
    public HexCoord? CoreCoord => Core != null ? Core.Coord : null;
    public int PopulationLoss => _populationLoss;
    public int Morale { get; private set; } = GameBalance.StartMorale;
    public int HousingCapacity => GameBalance.BaseHousing + HousingBonus
        + _buildings.Count(b => b.Kind == BuildingKind.Habitat && b.IsAlive) * GameBalance.HabitatBeds;
    public int PowerOutput { get; private set; }
    public int PowerDemand { get; private set; }
    public int PowerSupplied { get; private set; }
    public int ShedCount { get; private set; }
    public int Population { get; private set; }
    public int AssignedJobs { get; private set; }
    public int TotalJobs { get; private set; }
    public int DisconnectedCount => _buildings.Count(b => !b.Connected);

    public BuildingManager(BattleManager battle, HexMap map)
    {
        _battle = battle;
        _map = map;
    }

    public void BuildInitialBase()
    {
        Place(BuildingKind.CoreTower, new HexCoord(0, 5));
        Place(BuildingKind.PowerPlant, new HexCoord(-1, 5));
        Place(BuildingKind.ProductionLine, new HexCoord(1, 5));
        Place(BuildingKind.Barracks, new HexCoord(0, 4));
        Place(BuildingKind.TankFactory, new HexCoord(-2, 5));
        Place(BuildingKind.Hangar, new HexCoord(2, 5));
        Place(BuildingKind.Habitat, new HexCoord(-1, 6));
        Place(BuildingKind.Storage, new HexCoord(1, 6));
        Place(BuildingKind.OutpostStorage, new HexCoord(-4, 1));
        Update(0f);
    }

    /// <summary>步兵减员视为军人人口死亡，不可逆。</summary>
    public void RegisterInfantryLoss(int units)
    {
        if (units <= 0)
        {
            return;
        }

        _populationLoss += units;
        if (_battle != null)
        {
            _battle.ShowMessage($"步兵减员 {units} 人，军人人口永久损失");
        }
    }

    public Building Place(BuildingKind kind, HexCoord coord)
    {
        _map.ClearAround(coord, 0);
        var building = Building.Create(kind, Faction.Player);
        building.Coord = coord;
        building.Position = _map.ToWorld(coord);
        _battle.AddChild(building);
        _buildings.Add(building);
        _byCoord[coord] = building;
        if (kind == BuildingKind.CoreTower)
        {
            Core = building;
        }

        return building;
    }

    public Building At(HexCoord coord)
    {
        return _byCoord.TryGetValue(coord, out var building) ? building : null;
    }

    public bool IsOccupied(HexCoord coord) => _byCoord.ContainsKey(coord);

    public void SetSandstormActive(bool active)
    {
        _sandstormActive = active;
        foreach (var building in _buildings)
        {
            building.UnderSandstorm = active;
        }
    }

    public void Update(float dt)
    {
        if (Core == null || !Core.IsAlive)
        {
            return;
        }

        var clock = _battle.Clock;
        if (clock != null && clock.Day != _lastDay)
        {
            _lastDay = clock.Day;
            if (Population > HousingCapacity)
            {
                ChangeMorale(-GameBalance.OverpopMoralePenalty, "人口超过床位，士气下降");
            }

            int captainMorale = _battle?.GarrisonMoraleBonus ?? 0;
            if (captainMorale > 0)
            {
                ChangeMorale(captainMorale, "驻防队长安抚民心");
            }
        }

        UpdateConnectivity();
        UpdatePopulationAndStaffing();
        UpdatePower();
        UpdateRepair(dt);
        UpdateDecayAndSandstorm(dt);
    }

    public void Damage(Building building, float amount)
    {
        if (building == null || !building.IsAlive)
        {
            return;
        }

        building.Hp = Mathf.Max(0f, building.Hp - amount);
        if (building.Hp <= 0f)
        {
            DestroyBuilding(building);
        }
    }

    private void UpdateConnectivity()
    {
        _connected.Clear();
        _connected.Add(Core);
        var queue = new Queue<Building>();
        queue.Enqueue(Core);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var building in _buildings)
            {
                if (_connected.Contains(building) || !building.IsAlive)
                {
                    continue;
                }

                if (current.Coord.DistanceTo(building.Coord) <= 1)
                {
                    _connected.Add(building);
                    queue.Enqueue(building);
                }
            }
        }

        foreach (var building in _buildings)
        {
            bool connected = _connected.Contains(building);
            if (connected == building.Connected)
            {
                continue;
            }

            building.Connected = connected;
            if (!connected && building.IsAlive && building.IsPlayer)
            {
                _battle.ShowMessage($"{building.DisplayName} 与核心断开，效率下降");
            }
        }

        float efficiencyMultiplier = (_battle.Tech != null ? _battle.Tech.BuildingEfficiencyMultiplier : 1f)
            * MoraleEfficiency;
        foreach (var building in _buildings)
        {
            building.EfficiencyMultiplier = efficiencyMultiplier;
        }
    }

    /// <summary>士气 0-100，低于 40 开始拖慢建筑效率，高于 80 有额外产出。</summary>
    public float MoraleEfficiency => Mathf.Clamp(
        GameBalance.MoraleEfficiencyBase + Morale / GameBalance.MoraleEfficiencyScale,
        GameBalance.MoraleEfficiencyMin,
        GameBalance.MoraleEfficiencyMax);

    public void ChangeMorale(int delta, string reason)
    {
        Morale = Mathf.Clamp(Morale + delta, 0, 100);
        if (delta != 0 && !string.IsNullOrEmpty(reason))
        {
            _battle.ShowMessage($"{reason}（士气 {Morale}）");
        }
    }

    public void AddPopulation(int amount)
    {
        _populationBonus += Mathf.Max(0, amount);
    }

    public void LosePopulation(int amount)
    {
        _populationLoss += Mathf.Max(0, amount);
    }

    public int HousingBonus { get; private set; }

    public void AddHousing(int beds)
    {
        HousingBonus += Mathf.Max(0, beds);
    }

    private void UpdatePopulationAndStaffing()
    {
        Population = Mathf.Max(1, HousingCapacity + _populationBonus - _populationLoss);
        TotalJobs = _buildings.Where(b => b.IsAlive).Sum(b => b.JobSlots);

        int available = Population;
        foreach (var building in _buildings
                     .Where(b => b.IsAlive)
                     .OrderBy(b => b.Connected ? 0 : 1)
                     .ThenBy(b => b.Priority))
        {
            int staffed = Mathf.Min(building.JobSlots, available);
            building.StaffedJobs = staffed;
            available -= staffed;
        }

        AssignedJobs = _buildings.Where(b => b.IsAlive).Sum(b => b.StaffedJobs);
    }

    private void UpdatePower()
    {
        PowerOutput = 0;
        foreach (var building in _buildings.Where(b => b.IsAlive && b.Kind == BuildingKind.PowerPlant))
        {
            if (!building.Connected)
            {
                continue;
            }

            float staff = building.JobSlots > 0 ? building.StaffedJobs / (float)building.JobSlots : 1f;
            float tech = _battle.Tech != null ? _battle.Tech.PowerOutputMultiplier : 1f;
            PowerOutput += Mathf.RoundToInt(building.PowerOutput * staff * tech);
        }

        var consumers = _buildings
            .Where(b => b.IsAlive && b.Connected && b.Kind != BuildingKind.PowerPlant && b.PowerLoad > 0)
            .OrderByDescending(b => b.Priority)
            .ToList();

        PowerDemand = consumers.Sum(b => b.PowerLoad);
        PowerSupplied = PowerDemand;
        ShedCount = 0;
        foreach (var building in _buildings)
        {
            building.PowerSatisfied = true;
        }

        int index = 0;
        while (PowerSupplied > PowerOutput && index < consumers.Count)
        {
            var building = consumers[index++];
            building.PowerSatisfied = false;
            PowerSupplied -= building.PowerLoad;
            ShedCount++;
        }
    }

    private void UpdateRepair(float dt)
    {
        foreach (var formation in _battle.Formations.Where(f => f.Kind == FormationKind.RepairTruck && f.Hp > 0f))
        {
            if (formation.IsMoving)
            {
                formation.RepairTargetBuilding = null;
                continue;
            }

            Building best = null;
            int bestDistance = int.MaxValue;
            foreach (var building in _buildings)
            {
                if (!building.IsAlive || !building.IsPlayer || building.Hp >= building.MaxHp)
                {
                    continue;
                }

                int distance = building.Coord.DistanceTo(formation.Coord);
                if (distance <= 1 && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = building;
                }
            }

            formation.RepairTargetBuilding = best;
            if (best != null)
            {
                best.Hp = Mathf.Min(best.MaxHp,
                    best.Hp + RepairPerSecond * formation.CaptainRepairMultiplier * dt);
            }
        }
    }

    private void UpdateDecayAndSandstorm(float dt)
    {
        foreach (var building in _buildings.ToList())
        {
            if (!building.IsAlive)
            {
                continue;
            }

            if (!building.Connected && building.Kind != BuildingKind.CoreTower)
            {
                building.Hp = Mathf.Max(0f, building.Hp - DisconnectedDecayPerSecond * dt);
            }

            if (_sandstormActive)
            {
                building.Hp = Mathf.Max(0f, building.Hp - SandstormDamagePerSecond * dt);
            }

            if (building.Hp <= 0f)
            {
                DestroyBuilding(building);
            }
        }
    }

    private void DestroyBuilding(Building building)
    {
        bool wasCore = building == Core;
        _buildings.Remove(building);
        _byCoord.Remove(building.Coord);
        _map.AddRuins(building.Coord);
        _battle.MakeTileRuins();

        foreach (var formation in _battle.Formations)
        {
            if (formation.RepairTargetBuilding == building)
            {
                formation.RepairTargetBuilding = null;
            }

            if (formation.CurrentBuildingTarget == building)
            {
                formation.CurrentBuildingTarget = null;
            }
        }

        building.QueueFree();
        _battle.OnBuildingDestroyed(building);

        if (wasCore)
        {
            Core = null;
            _battle.OnCoreDestroyed();
            return;
        }

        _battle.ShowMessage($"{building.DisplayName} 被摧毁，原地留下废墟");
        _battle.OnStorageDestroyed(building);
    }
}
