using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Star;

public class ProductionOrder
{
    public FormationKind Kind { get; init; }
    public int Units { get; init; }
    public Captain Captain { get; init; }
    public float TotalTime { get; init; }
    public float Elapsed { get; set; }
    public float Cost { get; init; }
}

/// <summary>兵营/坦克厂/机库的下单、排队、按效率推进与下发编队。</summary>
public class ProductionManager
{
    public const int MaxFormations = 8;
    public const int MaxAirFormations = 3;
    private const int MaxQueue = 3;

    private readonly BattleManager _battle;
    private readonly ResourceManager _resources;
    private readonly Dictionary<Building, List<ProductionOrder>> _queues = new();
    private readonly List<Captain> _captains = new()
    {
        Captain.Soldier(),
        Captain.Scientist(),
        Captain.Worker(),
    };

    public ProductionManager(BattleManager battle, BuildingManager buildings, ResourceManager resources)
    {
        _battle = battle;
        _resources = resources;
    }

    public IReadOnlyList<Captain> Captains => _captains;

    public static FormationKind[] ModelsFor(BuildingKind kind) => kind switch
    {
        BuildingKind.Barracks => new[] { FormationKind.Infantry },
        BuildingKind.TankFactory => new[] { FormationKind.LightTank, FormationKind.MediumTank, FormationKind.HeavyTank },
        BuildingKind.Hangar => new[] { FormationKind.LightShip, FormationKind.MediumShip },
        _ => System.Array.Empty<FormationKind>(),
    };

    public static string ModelName(FormationKind kind) => kind switch
    {
        FormationKind.Infantry => "步兵",
        FormationKind.LightTank => "轻坦",
        FormationKind.MediumTank => "中坦",
        FormationKind.HeavyTank => "重坦",
        FormationKind.LightShip => "轻型飞船",
        FormationKind.MediumShip => "中型飞船",
        _ => "编队",
    };

    private static float ScrapPerUnit(FormationKind kind) => kind switch
    {
        FormationKind.Infantry => 8f,
        FormationKind.LightTank => 18f,
        FormationKind.MediumTank => 26f,
        FormationKind.HeavyTank => 36f,
        FormationKind.LightShip => 30f,
        FormationKind.MediumShip => 42f,
        _ => 20f,
    };

    private static float SecondsPerUnit(FormationKind kind) => kind switch
    {
        FormationKind.Infantry => 1.6f,
        FormationKind.LightTank => 2.6f,
        FormationKind.MediumTank => 3.2f,
        FormationKind.HeavyTank => 4f,
        FormationKind.LightShip => 3.4f,
        FormationKind.MediumShip => 4.2f,
        _ => 3f,
    };

    public bool IsSupported(Building building) =>
        building != null && building.IsProduction && ModelsFor(building.Kind).Length > 0;

    public int QueueCount(Building building) =>
        _queues.TryGetValue(building, out var queue) ? queue.Count : 0;

    public ProductionOrder CurrentOrder(Building building) =>
        _queues.TryGetValue(building, out var queue) && queue.Count > 0 ? queue[0] : null;

    public string StatusOf(Building building)
    {
        var order = CurrentOrder(building);
        if (order == null)
        {
            return "当前无生产任务";
        }

        float efficiency = building.Efficiency;
        float progress = order.TotalTime > 0f ? Mathf.Clamp(order.Elapsed / order.TotalTime, 0f, 1f) : 0f;
        string paused = efficiency < 0.05f ? "（电力或人力不足，停滞）" : string.Empty;
        int queued = QueueCount(building) - 1;
        string queue = queued > 0 ? $"    队列 {queued}" : string.Empty;
        return $"{ModelName(order.Kind)} ×{order.Units}    {progress * 100f:F0}%{paused}{queue}";
    }

    public bool Order(Building building, FormationKind kind, int units, Captain captain)
    {
        if (!IsSupported(building))
        {
            _battle.ShowMessage("该建筑不能生产编队");
            return false;
        }

        if (!ModelsFor(building.Kind).Contains(kind))
        {
            _battle.ShowMessage("该建筑不能生产此型号");
            return false;
        }

        if (units < 3 || units > 6)
        {
            _battle.ShowMessage("编队人数需在 3 到 6 之间");
            return false;
        }

        if (CountPlayerCombatFormations() >= MaxFormations)
        {
            _battle.ShowMessage("编队总数已达上限 8");
            return false;
        }

        if (kind is FormationKind.LightShip or FormationKind.MediumShip
            && CountPlayerAirFormations() >= MaxAirFormations)
        {
            _battle.ShowMessage("空中编队已达上限 3");
            return false;
        }

        if (captain != null && (!captain.IsAlive || captain.InUse))
        {
            _battle.ShowMessage("该队长当前不可用");
            return false;
        }

        if (!_queues.TryGetValue(building, out var queue))
        {
            queue = new List<ProductionOrder>();
            _queues[building] = queue;
        }

        if (queue.Count >= MaxQueue)
        {
            _battle.ShowMessage("生产队列已满");
            return false;
        }

        float cost = ScrapPerUnit(kind) * units;
        if (_resources.Get(ResourceKind.Scrap) < cost - 0.01f)
        {
            _battle.ShowMessage($"废铁不足（需要 {cost:F0}）");
            return false;
        }

        _resources.Consume(ResourceKind.Scrap, cost);

        if (captain != null)
        {
            captain.InUse = true;
        }

        queue.Add(new ProductionOrder
        {
            Kind = kind,
            Units = units,
            Captain = captain,
            TotalTime = SecondsPerUnit(kind) * units,
            Cost = cost,
        });
        _battle.ShowMessage($"{building.DisplayName} 开始生产 {ModelName(kind)} ×{units}，消耗废铁 {cost:F0}");
        return true;
    }

    public void Update(float dt)
    {
        foreach (var pair in _queues.ToList())
        {
            var building = pair.Key;
            var queue = pair.Value;
            if (!building.IsAlive)
            {
                foreach (var order in queue)
                {
                    if (order.Captain != null)
                    {
                        order.Captain.InUse = false;
                    }
                }

                _queues.Remove(building);
                continue;
            }

            if (queue.Count == 0)
            {
                _queues.Remove(building);
                continue;
            }

            var current = queue[0];
            float efficiency = building.Efficiency;
            if (efficiency < 0.05f)
            {
                continue;
            }

            current.Elapsed += dt * efficiency;
            if (current.Elapsed < current.TotalTime)
            {
                continue;
            }

            if (CountPlayerCombatFormations() >= MaxFormations)
            {
                current.Elapsed = current.TotalTime;
                continue;
            }

            if (TryDeploy(building, current))
            {
                queue.RemoveAt(0);
                if (queue.Count == 0)
                {
                    _queues.Remove(building);
                }
            }
        }
    }

    private bool TryDeploy(Building building, ProductionOrder order)
    {
        var formation = CreateFormation(order);
        for (int i = 0; i < HexCoord.Directions.Length; i++)
        {
            var coord = building.Coord.Neighbor(i);
            if (_battle.TrySpawnFormation(formation, coord))
            {
                formation.Fuel = formation.MaxFuel;
                _battle.ShowMessage($"{building.DisplayName} 下线 {formation.DisplayName}");
                return true;
            }
        }

        return false;
    }

    private static Formation CreateFormation(ProductionOrder order) => order.Kind switch
    {
        FormationKind.Infantry => Formation.CreateInfantry(order.Units, order.Captain),
        FormationKind.LightTank => Formation.CreateLightTank(order.Units, order.Captain),
        FormationKind.MediumTank => Formation.CreateMediumTank(order.Units, order.Captain),
        FormationKind.HeavyTank => Formation.CreateHeavyTank(order.Units, order.Captain),
        FormationKind.LightShip => Formation.CreateLightShip(order.Units, order.Captain),
        _ => Formation.CreateMediumShip(order.Units, order.Captain),
    };

    private int CountPlayerCombatFormations() =>
        _battle.Formations.Count(f => f.IsPlayer && f.Hp > 0f && f.IsCombatFormation);

    private int CountPlayerAirFormations() =>
        _battle.Formations.Count(f => f.IsPlayer && f.Hp > 0f && f.IsAir);
}
