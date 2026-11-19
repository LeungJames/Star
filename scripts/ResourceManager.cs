using System.Collections.Generic;
using Godot;

namespace Star;

public enum ResourceKind
{
    Scrap,
    Fuel,
    Food,
    Medicine,
    Data,
}

/// <summary>五种资源的库存、仓储上限、自动回收、每日食物消耗与仓储摧毁掉落。</summary>
public class ResourceManager
{
    private const float StorageCapacity = 150f;
    private const float OutpostCapacity = 60f;
    private const float FoodPerPersonPerDay = 1f;
    private const float WasteMessageInterval = 8f;

    private readonly BattleManager _battle;
    private readonly BuildingManager _buildings;
    private readonly float[] _amount = new float[5];
    private readonly List<ResourceDrop> _drops = new();

    private int _lastDay = 1;
    private float _wasteTimer;

    public ResourceManager(BattleManager battle, BuildingManager buildings)
    {
        _battle = battle;
        _buildings = buildings;
        _lastDay = battle.Clock?.Day ?? 1;

        _amount[(int)ResourceKind.Scrap] = 60f;
        _amount[(int)ResourceKind.Fuel] = 40f;
        _amount[(int)ResourceKind.Food] = 50f;
        _amount[(int)ResourceKind.Medicine] = 10f;
        _amount[(int)ResourceKind.Data] = 2f;

        BuildNodes();
    }

    public static string NameOf(ResourceKind kind) => kind switch
    {
        ResourceKind.Scrap => "废铁",
        ResourceKind.Fuel => "燃料",
        ResourceKind.Food => "食物",
        ResourceKind.Medicine => "药品",
        _ => "知识数据",
    };

    public float Get(ResourceKind kind) => _amount[(int)kind];

    /// <summary>仓储提供的单种资源上限；断连的仓储不提供上限。</summary>
    public float Capacity => TotalCapacity();

    public float AmountWasted { get; private set; }

    public float CapacityOf(Building building)
    {
        if (building == null)
        {
            return 0f;
        }

        return building.Kind switch
        {
            BuildingKind.Storage => StorageCapacity,
            BuildingKind.OutpostStorage => OutpostCapacity,
            _ => 0f,
        };
    }

    /// <summary>加入库存，返回因超出上限而浪费的数量。</summary>
    public float Add(ResourceKind kind, float amount)
    {
        if (amount <= 0f)
        {
            return 0f;
        }

        int index = (int)kind;
        float cap = Capacity;
        float before = _amount[index];
        _amount[index] = Mathf.Min(cap, before + amount);
        float wasted = before + amount - _amount[index];
        if (wasted > 0f)
        {
            AmountWasted += wasted;
        }

        return wasted;
    }

    public float Consume(ResourceKind kind, float amount)
    {
        int index = (int)kind;
        float taken = Mathf.Min(_amount[index], Mathf.Max(0f, amount));
        _amount[index] -= taken;
        return taken;
    }

    public void Update(float dt)
    {
        _wasteTimer = Mathf.Max(0f, _wasteTimer - dt);

        UpdateDailyFood();
        UpdateNodes(dt);
        UpdatePickups();
        ClampToCapacity();
    }

    /// <summary>仓储被摧毁时，其按容量占比存放的资源掉落在原地，任何一方经过都可拾取。</summary>
    public void OnStorageDestroyed(Building building)
    {
        float weight = CapacityOf(building);
        float total = weight;
        foreach (var other in _buildings.Buildings)
        {
            if (other != building && other.IsAlive)
            {
                total += CapacityOf(other);
            }
        }

        if (weight <= 0f || total <= 0f)
        {
            return;
        }

        bool dropped = false;
        for (int i = 0; i < _amount.Length; i++)
        {
            float share = _amount[i] * weight / total;
            if (share < 0.5f)
            {
                continue;
            }

            _amount[i] -= share;
            SpawnDrop((ResourceKind)i, share, building.Coord);
            dropped = true;
        }

        if (dropped)
        {
            _battle.ShowMessage($"{building.DisplayName} 被摧毁，库存掉落原地");
        }
    }

    private void UpdateDailyFood()
    {
        var clock = _battle.Clock;
        if (clock == null || clock.Day == _lastDay)
        {
            return;
        }

        _lastDay = clock.Day;
        float need = _buildings.Population * FoodPerPersonPerDay;
        float got = Consume(ResourceKind.Food, need);
        if (got < need - 0.01f)
        {
            _battle.ShowMessage("食物不足，人口开始挨饿");
        }
    }

    private void UpdateNodes(float dt)
    {
        // 除燃料外，资源采集后自动流回基地仓库。
        foreach (var node in _nodes)
        {
            Add(node.Kind, node.Rate * dt);
        }
    }

    private void UpdatePickups()
    {
        if (_drops.Count == 0)
        {
            return;
        }

        foreach (var drop in _drops.ToArray())
        {
            foreach (var formation in _battle.Formations)
            {
                if (formation.Hp <= 0f || !formation.Coord.Equals(drop.Coord))
                {
                    continue;
                }

                if (formation.IsPlayer)
                {
                    float wasted = Add(drop.Kind, drop.Amount);
                    string suffix = wasted > 0.5f ? "（仓储已满，部分浪费）" : string.Empty;
                    _battle.ShowMessage($"{formation.DisplayName} 回收 {NameOf(drop.Kind)} +{drop.Amount:F0}{suffix}");
                }
                else
                {
                    _battle.ShowMessage($"敌方拾取了 {NameOf(drop.Kind)} ×{drop.Amount:F0}");
                }

                _drops.Remove(drop);
                drop.QueueFree();
                break;
            }
        }
    }

    private void ClampToCapacity()
    {
        float cap = Capacity;
        float wasted = 0f;
        for (int i = 0; i < _amount.Length; i++)
        {
            if (_amount[i] > cap)
            {
                wasted += _amount[i] - cap;
                _amount[i] = cap;
            }
        }

        if (wasted <= 0f)
        {
            return;
        }

        AmountWasted += wasted;
        if (_wasteTimer <= 0f)
        {
            _wasteTimer = WasteMessageInterval;
            _battle.ShowMessage("仓储已满，超出的资源被浪费");
        }
    }

    private float TotalCapacity()
    {
        float total = 0f;
        foreach (var building in _buildings.Buildings)
        {
            if (!building.IsAlive || !building.Connected)
            {
                continue;
            }

            total += CapacityOf(building);
        }

        return total;
    }

    private void SpawnDrop(ResourceKind kind, float amount, HexCoord coord)
    {
        var drop = new ResourceDrop { Kind = kind, Amount = amount, Coord = coord };
        drop.Position = _battle.Map.ToWorld(coord) + new Vector3(0f, 0.35f, 0f);
        _battle.AddChild(drop);
        _drops.Add(drop);
    }

    private void BuildNodes()
    {
        // 已经建成的野外采集点，产出后自动回仓。
        AddNode(ResourceKind.Scrap, new HexCoord(-4, -3), 0.8f);
        AddNode(ResourceKind.Food, new HexCoord(-2, -4), 0.5f);
        AddNode(ResourceKind.Medicine, new HexCoord(-5, 1), 0.1f);
        AddNode(ResourceKind.Data, new HexCoord(3, 4), 0.05f);
    }

    private void AddNode(ResourceKind kind, HexCoord coord, float rate)
    {
        if (!_battle.Map.Contains(coord))
        {
            return;
        }

        _battle.Map.ClearAround(coord, 0);
        var node = new ResourceNode { Kind = kind, Rate = rate };
        node.Position = _battle.Map.ToWorld(coord);
        _battle.AddChild(node);
        _nodes.Add(node);
    }

    private readonly List<ResourceNode> _nodes = new();
}

/// <summary>野外的资源采集点外观，不影响占格规则。</summary>
public partial class ResourceNode : Node3D
{
    public ResourceKind Kind { get; set; }
    public float Rate { get; set; }

    private float _time;
    private Node3D _body;

    public override void _Ready()
    {
        _body = new Node3D();
        AddChild(_body);

        var baseMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.2f, 0.17f),
            Roughness = 1f,
        };
        AddPart(new CylinderMesh
        {
            TopRadius = 0.62f,
            BottomRadius = 0.68f,
            Height = 0.16f,
            RadialSegments = 6,
        }, baseMaterial, new Vector3(0f, 0.08f, 0f));

        switch (Kind)
        {
            case ResourceKind.Scrap:
                var rust = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.36f, 0.22f), Roughness = 0.95f };
                AddPart(new BoxMesh { Size = new Vector3(0.5f, 0.22f, 0.4f) }, rust, new Vector3(-0.12f, 0.24f, 0.05f));
                AddPart(new BoxMesh { Size = new Vector3(0.42f, 0.2f, 0.34f) }, rust, new Vector3(0.16f, 0.42f, -0.08f));
                break;
            case ResourceKind.Food:
                var green = new StandardMaterial3D { AlbedoColor = new Color(0.38f, 0.62f, 0.3f), Roughness = 0.85f };
                AddPart(new CylinderMesh
                {
                    TopRadius = 0.28f,
                    BottomRadius = 0.28f,
                    Height = 0.55f,
                    RadialSegments = 10,
                }, green, new Vector3(0f, 0.42f, 0f));
                break;
            case ResourceKind.Medicine:
                var white = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.9f, 0.88f), Roughness = 0.7f };
                var red = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.2f, 0.18f), Roughness = 0.7f };
                AddPart(new BoxMesh { Size = new Vector3(0.46f, 0.3f, 0.34f) }, white, new Vector3(0f, 0.3f, 0f));
                AddPart(new BoxMesh { Size = new Vector3(0.1f, 0.2f, 0.36f) }, red, new Vector3(0f, 0.3f, 0f));
                break;
            default:
                var glow = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.35f, 0.85f, 1f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    EmissionEnabled = true,
                    Emission = new Color(0.3f, 0.75f, 1f),
                    EmissionEnergyMultiplier = 1.4f,
                };
                AddPart(new SphereMesh { Radius = 0.26f, Height = 0.52f, RadialSegments = 12, Rings = 6 }, glow,
                    new Vector3(0f, 0.45f, 0f));
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_body == null)
        {
            return;
        }

        _time += (float)delta;
        _body.Position = new Vector3(0f, Mathf.Sin(_time * 1.6f) * 0.05f, 0f);
    }

    private void AddPart(Mesh mesh, Material material, Vector3 position)
    {
        _body.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Position = position,
        });
    }
}

/// <summary>仓储被摧毁后掉落在地面的资源，任何一方经过都会拾取。</summary>
public partial class ResourceDrop : Node3D
{
    public ResourceKind Kind { get; set; }
    public float Amount { get; set; }
    public HexCoord Coord { get; set; }

    private float _time;

    public override void _Ready()
    {
        var color = Kind switch
        {
            ResourceKind.Scrap => new Color(0.6f, 0.38f, 0.22f),
            ResourceKind.Food => new Color(0.4f, 0.68f, 0.32f),
            ResourceKind.Medicine => new Color(0.9f, 0.9f, 0.88f),
            ResourceKind.Data => new Color(0.35f, 0.8f, 1f),
            _ => new Color(0.6f, 0.6f, 0.6f),
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color * 0.35f,
            EmissionEnergyMultiplier = 0.8f,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.3f, 0.45f) },
            MaterialOverride = material,
        });
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Position = new Vector3(Position.X, 0.35f + Mathf.Sin(_time * 2f) * 0.06f, Position.Z);
        Rotation = new Vector3(0f, _time * 0.8f, 0f);
    }
}
