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
            return staff * connection * power;
        }
    }

    public float Height => Kind == BuildingKind.CoreTower ? 3f : 1.3f;
    public float BeamHeight => Kind == BuildingKind.CoreTower ? 1.8f : 0.7f;

    public MeshInstance3D Highlight { get; private set; }

    private MeshInstance3D _healthFill;
    private Node3D _body;

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
        _body = new Node3D { Name = "Body" };
        AddChild(_body);

        bool enemy = Faction == Faction.Enemy;
        var wall = new StandardMaterial3D
        {
            AlbedoColor = enemy ? new Color(0.42f, 0.22f, 0.2f) : new Color(0.42f, 0.45f, 0.4f),
            Roughness = 0.9f,
        };
        var trim = new StandardMaterial3D
        {
            AlbedoColor = enemy ? new Color(0.3f, 0.14f, 0.12f) : new Color(0.24f, 0.27f, 0.24f),
            Roughness = 0.85f,
        };
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.4f, 0.9f, 1f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.35f, 0.8f, 1f),
            EmissionEnergyMultiplier = 1.6f,
        };
        var solar = new StandardMaterial3D
        {
            AlbedoColor = enemy ? new Color(0.3f, 0.2f, 0.2f) : new Color(0.18f, 0.22f, 0.28f),
            Metallic = 0.4f,
            Roughness = 0.5f,
        };

        switch (Kind)
        {
            case BuildingKind.CoreTower:
                AddBox(_body, new Vector3(1.7f, 0.5f, 1.7f), new Vector3(0f, 0.45f, 0f), trim);
                AddCylinder(_body, 0.5f, 0.5f, 2.3f, new Vector3(0f, 1.75f, 0f), wall, 8);
                AddBox(_body, new Vector3(0.55f, 0.55f, 0.55f), new Vector3(0f, 2.95f, 0f), glow);
                break;
            case BuildingKind.PowerPlant:
                AddBox(_body, new Vector3(1.5f, 0.4f, 1.5f), new Vector3(0f, 0.4f, 0f), trim);
                AddCylinder(_body, 0.42f, 0.42f, 1.1f, new Vector3(-0.42f, 0.95f, 0.1f), wall, 10);
                AddCylinder(_body, 0.42f, 0.42f, 1.1f, new Vector3(0.42f, 0.95f, 0.1f), wall, 10);
                AddBox(_body, new Vector3(1.1f, 0.06f, 0.5f), new Vector3(0f, 1.08f, -0.55f), solar);
                break;
            case BuildingKind.ProductionLine:
                AddBox(_body, new Vector3(1.5f, 0.45f, 1.7f), new Vector3(0f, 0.42f, 0f), trim);
                AddBox(_body, new Vector3(1.1f, 0.5f, 0.55f), new Vector3(0f, 0.9f, -0.35f), wall);
                AddBox(_body, new Vector3(0.5f, 0.3f, 0.35f), new Vector3(0.35f, 0.75f, 0.45f), wall);
                AddBox(_body, new Vector3(0.5f, 0.3f, 0.35f), new Vector3(-0.35f, 0.75f, 0.45f), wall);
                break;
            case BuildingKind.Barracks:
                AddBox(_body, new Vector3(1.5f, 0.5f, 1.35f), new Vector3(0f, 0.45f, 0f), trim);
                AddBox(_body, new Vector3(1.2f, 0.55f, 0.9f), new Vector3(0f, 0.95f, -0.1f), wall);
                AddBox(_body, new Vector3(0.42f, 0.5f, 0.08f), new Vector3(0f, 0.6f, 0.72f), glow);
                break;
            case BuildingKind.TankFactory:
                AddBox(_body, new Vector3(1.6f, 0.45f, 1.6f), new Vector3(0f, 0.42f, 0f), trim);
                AddBox(_body, new Vector3(1.25f, 0.7f, 1.25f), new Vector3(0f, 1f, 0f), wall);
                AddCylinder(_body, 0.18f, 0.18f, 0.9f, new Vector3(0.55f, 1.75f, -0.4f), solar, 8);
                AddBox(_body, new Vector3(0.6f, 0.16f, 0.6f), new Vector3(-0.35f, 1.45f, 0.35f), glow);
                break;
            case BuildingKind.Hangar:
                AddBox(_body, new Vector3(1.7f, 0.35f, 1.55f), new Vector3(0f, 0.38f, 0f), trim);
                AddCylinder(_body, 0.85f, 0.85f, 0.9f, new Vector3(0f, 0.9f, 0f), wall, 12);
                AddBox(_body, new Vector3(0.9f, 0.12f, 0.7f), new Vector3(0f, 1.38f, 0f), solar);
                AddBox(_body, new Vector3(0.5f, 0.12f, 0.1f), new Vector3(0f, 0.55f, 0.8f), glow);
                break;
            case BuildingKind.Habitat:
                AddCylinder(_body, 0.85f, 0.85f, 0.55f, new Vector3(0f, 0.48f, 0f), trim, 12);
                AddSphere(_body, 0.62f, new Vector3(0f, 1.05f, 0f), wall);
                AddBox(_body, new Vector3(0.45f, 0.1f, 0.12f), new Vector3(0f, 1.25f, 0.55f), glow);
                break;
            case BuildingKind.Storage:
            case BuildingKind.OutpostStorage:
                AddBox(_body, new Vector3(1.55f, 0.35f, 1.55f), new Vector3(0f, 0.38f, 0f), trim);
                AddBox(_body, new Vector3(0.55f, 0.45f, 0.55f), new Vector3(-0.42f, 0.75f, -0.32f), wall);
                AddBox(_body, new Vector3(0.55f, 0.45f, 0.55f), new Vector3(0.42f, 0.75f, -0.32f), wall);
                AddBox(_body, new Vector3(0.55f, 0.35f, 0.55f), new Vector3(0f, 0.7f, 0.42f), wall);
                break;
        }

        var highlightMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.65f, 1f, 0.55f, 0.95f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var highlight = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = 1f,
                OuterRadius = 1.18f,
                Rings = 32,
                RingSegments = 8,
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
        var barRoot = new Node3D { Position = new Vector3(0f, Height + 0.55f, 0f) };
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
                ? new Color(0.45f, 0.95f, 0.4f, 0.95f)
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

    private static void AddBox(Node3D parent, Vector3 size, Vector3 position, Material material)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
        });
    }

    private static void AddCylinder(Node3D parent, float topRadius, float bottomRadius, float height,
        Vector3 position, Material material, int segments)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = topRadius,
                BottomRadius = bottomRadius,
                Height = height,
                RadialSegments = segments,
            },
            MaterialOverride = material,
            Position = position,
        });
    }

    private static void AddSphere(Node3D parent, float radius, Vector3 position, Material material)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 16, Rings = 8 },
            MaterialOverride = material,
            Position = position,
        });
    }
}
