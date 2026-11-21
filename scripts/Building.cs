using Godot;

namespace Star;

public enum BuildingKind
{
    CoreTower,
    PowerPlant,
    ProductionLine,
    Barracks,
    TankFactory,
    Hangar,
    Habitat,
    Storage,
    OutpostStorage,
}

/// <summary>占据一个六角格的基地建筑，提供电力、岗位或仓储。</summary>
public partial class Building : Node3D
{
    public BuildingKind Kind { get; private set; }
    public Faction Faction { get; private set; } = Faction.Player;
    public string DisplayName { get; private set; } = string.Empty;
    public HexCoord Coord { get; set; }
    public float MaxHp { get; private set; }
    public float Hp { get; set; }
    public float Armor { get; private set; }
    public int PowerOutput { get; private set; }
    public int PowerLoad { get; private set; }
    public int Priority { get; private set; }
    public int JobSlots { get; private set; }

    public bool Connected { get; set; } = true;
    public bool PowerSatisfied { get; set; } = true;
    public int StaffedJobs { get; set; }
    public float EfficiencyMultiplier { get; set; } = 1f;
    public bool UnderSandstorm { get; set; }
    public bool IsAlive => Hp > 0f;
    public bool IsPlayer => Faction == Faction.Player;
    public bool IsProduction => Kind is BuildingKind.Barracks or BuildingKind.TankFactory
        or BuildingKind.Hangar or BuildingKind.ProductionLine;

    public float Efficiency
    {
        get
        {
            float staff = JobSlots > 0 ? StaffedJobs / (float)JobSlots : 1f;
            float connection = Connected ? 1f : 0.5f;
            float power = PowerSatisfied ? 1f : 0.5f;
            return staff * connection * power * EfficiencyMultiplier;
        }
    }

    private float VisualHeight => Kind switch
    {
        BuildingKind.CoreTower => 3.7f,
        BuildingKind.TankFactory => 2.2f,
        BuildingKind.Barracks or BuildingKind.Hangar or BuildingKind.PowerPlant
            or BuildingKind.ProductionLine or BuildingKind.OutpostStorage => 1.9f,
        BuildingKind.Habitat => 1.55f,
        _ => 1.45f,
    };
    public float BeamHeight => Kind == BuildingKind.CoreTower ? 1.8f : 0.7f;

    public MeshInstance3D Highlight { get; private set; }

    private MeshInstance3D _healthFill;

    public static Building Create(BuildingKind kind, Faction faction)
    {
        var building = new Building
        {
            Kind = kind,
            Faction = faction,
            DisplayName = kind switch
            {
                BuildingKind.CoreTower => "核心塔",
                BuildingKind.PowerPlant => "发电站",
                BuildingKind.ProductionLine => "产线",
                BuildingKind.Barracks => "兵营",
                BuildingKind.TankFactory => "坦克厂",
                BuildingKind.Hangar => "机库",
                BuildingKind.Habitat => "居住舱",
                BuildingKind.Storage => "仓储",
                _ => "前哨仓储",
            },
            MaxHp = kind switch
            {
                BuildingKind.CoreTower => 600f,
                BuildingKind.PowerPlant => 240f,
                BuildingKind.ProductionLine => 260f,
                BuildingKind.Barracks => 240f,
                BuildingKind.TankFactory => 280f,
                BuildingKind.Hangar => 260f,
                BuildingKind.Habitat => 200f,
                BuildingKind.Storage => 220f,
                _ => 180f,
            },
            Armor = kind == BuildingKind.CoreTower ? 12f : 6f,
            PowerOutput = kind == BuildingKind.PowerPlant ? 36 : 0,
            PowerLoad = kind switch
            {
                BuildingKind.CoreTower => 8,
                BuildingKind.ProductionLine => 24,
                BuildingKind.Barracks => 6,
                BuildingKind.TankFactory => 6,
                BuildingKind.Hangar => 6,
                BuildingKind.Habitat => 10,
                BuildingKind.Storage => 4,
                BuildingKind.OutpostStorage => 2,
                _ => 0,
            },
            Priority = kind switch
            {
                BuildingKind.CoreTower => 0,
                BuildingKind.PowerPlant => 0,
                BuildingKind.ProductionLine => 2,
                BuildingKind.Barracks => 2,
                BuildingKind.TankFactory => 2,
                BuildingKind.Hangar => 2,
                BuildingKind.Habitat => 3,
                BuildingKind.Storage => 4,
                _ => 5,
            },
            JobSlots = kind switch
            {
                BuildingKind.PowerPlant => 4,
                BuildingKind.ProductionLine => 6,
                BuildingKind.Barracks => 2,
                BuildingKind.TankFactory => 2,
                BuildingKind.Hangar => 2,
                BuildingKind.Storage => 3,
                BuildingKind.OutpostStorage => 3,
                _ => 0,
            },
        };
        building.Hp = building.MaxHp;
        return building;
    }

    public override void _Ready()
    {
        BuildVisual();
        BuildHealthBar();
    }

    public override void _Process(double delta)
    {
        if (_healthFill == null)
        {
            return;
        }

        float ratio = MaxHp > 0f ? Mathf.Clamp(Hp / MaxHp, 0f, 1f) : 0f;
        _healthFill.Scale = new Vector3(Mathf.Max(ratio, 0.001f), 1f, 1f);
        _healthFill.Position = new Vector3(-0.66f * (1f - ratio), 0f, 0.01f);
    }

    public void SetSelected(bool selected)
    {
        if (Highlight != null)
        {
            Highlight.Visible = selected;
        }
    }

    private void BuildVisual()
    {
        string asset = Kind switch
        {
            BuildingKind.CoreTower => "core_tower",
            BuildingKind.PowerPlant => "power_plant",
            BuildingKind.ProductionLine => "production_line",
            BuildingKind.Barracks => "barracks",
            BuildingKind.TankFactory => "tank_factory",
            BuildingKind.Hangar => "hangar",
            BuildingKind.Habitat => "habitat",
            BuildingKind.Storage => "storage",
            _ => "outpost_storage",
        };

        var body = ModelArt.Instantiate(asset);
        body.Position = Vector3.Up * 0.14f;
        AddChild(body);

        var highlightMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.59f, 0.96f, 0.87f, 0.95f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var highlight = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = 1f,
                OuterRadius = 1.18f,
                Rings = 24,
                RingSegments = 4,
            },
            MaterialOverride = highlightMaterial,
            Position = new Vector3(0f, 0.32f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(highlight);
        Highlight = highlight;
    }

    private void BuildHealthBar()
    {
        var barRoot = new Node3D { Position = new Vector3(0f, VisualHeight + 0.23f, 0f) };
        AddChild(barRoot);

        var background = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.02f, 0.02f, 0.8f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        };
        var fill = new StandardMaterial3D
        {
            AlbedoColor = Faction == Faction.Player
                ? new Color(0.37f, 0.77f, 0.68f, 0.95f)
                : new Color(0.95f, 0.35f, 0.3f, 0.95f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 1,
        };

        barRoot.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.4f, 0.16f) },
            MaterialOverride = background,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _healthFill = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.32f, 0.1f) },
            MaterialOverride = fill,
            Position = new Vector3(0f, 0f, 0.01f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        barRoot.AddChild(_healthFill);
    }
}
