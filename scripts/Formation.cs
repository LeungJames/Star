using System.Collections.Generic;
using Godot;

namespace Star;

public enum FormationKind
{
    LightTank,
    HeavyTank,
    LightShip,
    SupplyTruck,
}

public enum Faction
{
    Player,
    Enemy,
}

/// <summary>战场上的编队，占一格；地面与空中分别占用各自的格子层。</summary>
public partial class Formation : Node3D
{
    public FormationKind Kind { get; private set; }
    public Faction Faction { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public int Units { get; private set; }
    public float Speed { get; private set; }
    public float MaxFuel { get; private set; }
    public float Fuel { get; set; }
    public float FuelDrainPerSecond { get; private set; }
    public float MaxCargo { get; private set; }
    public float Cargo { get; set; }

    public float MaxHp { get; private set; }
    public float Hp { get; set; }
    public float Armor { get; private set; }
    public float Penetration { get; private set; }
    public int Range { get; private set; }
    public float BaseDps { get; private set; }
    public int VisionRadius { get; private set; }

    public HexCoord Coord { get; set; }
    public readonly List<HexCoord> Path = new();
    public readonly List<HexCoord> Reserved = new();
    public Formation RefuelTarget { get; set; }
    public Formation CurrentTarget { get; set; }
    public MeshInstance3D Highlight { get; private set; }

    public bool IsAir => Kind == FormationKind.LightShip;
    public bool IsTruck => Kind == FormationKind.SupplyTruck;
    public bool IsMoving => Path.Count > 0;
    public bool IsCombatant => BaseDps > 0f && Range > 0;
    public bool IsPlayer => Faction == Faction.Player;

    /// <summary>按耐久比例折算的存活单位数，至少保留 1 个。</summary>
    public int AliveUnits => Hp <= 0f ? 0 : Mathf.Max(1, Mathf.CeilToInt(Units * Hp / MaxHp));

    /// <summary>攻击力随存活单位数线性下降。</summary>
    public float AttackScale => Units > 0 ? AliveUnits / (float)Units : 0f;

    public float BeamHeight => IsAir ? 2.9f : 0.6f;

    private Node3D _body;
    private Node3D _healthBar;
    private MeshInstance3D _healthFill;
    private float _hoverTime;

    public static Formation CreateLightTank()
    {
        return new Formation
        {
            Kind = FormationKind.LightTank,
            Faction = Faction.Player,
            DisplayName = "轻坦编队 · 3 辆",
            Units = 3,
            Speed = 3.1f,
            MaxFuel = 100f,
            Fuel = 45f,
            FuelDrainPerSecond = 3.2f,
            MaxHp = 120f,
            Hp = 120f,
            Armor = 45f,
            Penetration = 55f,
            Range = 4,
            BaseDps = 26f,
            VisionRadius = 4,
        };
    }

    public static Formation CreateLightShip()
    {
        return new Formation
        {
            Kind = FormationKind.LightShip,
            Faction = Faction.Player,
            DisplayName = "轻型飞船编队 · 3 艘",
            Units = 3,
            Speed = 5.4f,
            MaxFuel = 100f,
            Fuel = 70f,
            FuelDrainPerSecond = 2.4f,
            MaxHp = 90f,
            Hp = 90f,
            Armor = 30f,
            Penetration = 70f,
            Range = 5,
            BaseDps = 34f,
            VisionRadius = 6,
        };
    }

    public static Formation CreateSupplyTruck()
    {
        return new Formation
        {
            Kind = FormationKind.SupplyTruck,
            Faction = Faction.Player,
            DisplayName = "补给车",
            Units = 1,
            Speed = 2.8f,
            MaxFuel = 100f,
            Fuel = 100f,
            FuelDrainPerSecond = 0f,
            MaxCargo = 300f,
            Cargo = 300f,
            MaxHp = 110f,
            Hp = 110f,
            Armor = 20f,
            VisionRadius = 3,
        };
    }

    public static Formation CreateEnemyHeavyTank()
    {
        return new Formation
        {
            Kind = FormationKind.HeavyTank,
            Faction = Faction.Enemy,
            DisplayName = "敌方重坦编队 · 3 辆",
            Units = 3,
            Speed = 1.8f,
            MaxFuel = 100f,
            Fuel = 100f,
            FuelDrainPerSecond = 4.5f,
            MaxHp = 260f,
            Hp = 260f,
            Armor = 120f,
            Penetration = 90f,
            Range = 4,
            BaseDps = 16f,
            VisionRadius = 4,
        };
    }

    public static Formation CreateEnemyLightTank()
    {
        return new Formation
        {
            Kind = FormationKind.LightTank,
            Faction = Faction.Enemy,
            DisplayName = "敌方轻坦编队 · 3 辆",
            Units = 3,
            Speed = 2.9f,
            MaxFuel = 100f,
            Fuel = 100f,
            FuelDrainPerSecond = 3.2f,
            MaxHp = 120f,
            Hp = 120f,
            Armor = 40f,
            Penetration = 55f,
            Range = 4,
            BaseDps = 12f,
            VisionRadius = 4,
        };
    }

    public override void _Ready()
    {
        BuildVisual();
        BuildHealthBar();
    }

    public override void _Process(double delta)
    {
        if (IsAir && _body != null)
        {
            _hoverTime += (float)delta;
            _body.Position = new Vector3(0f, Mathf.Sin(_hoverTime * 2.2f) * 0.12f, 0f);
        }

        if (_healthBar == null)
        {
            return;
        }

        float ratio = MaxHp > 0f ? Mathf.Clamp(Hp / MaxHp, 0f, 1f) : 0f;
        _healthFill.Scale = new Vector3(Mathf.Max(ratio, 0.001f), 1f, 1f);
        _healthFill.Position = new Vector3(-0.64f * (1f - ratio), 0f, 0.01f);
    }

    private void BuildVisual()
    {
        _body = new Node3D { Name = "Body" };
        AddChild(_body);

        switch (Kind)
        {
            case FormationKind.LightTank:
                BuildTanks(1f);
                break;
            case FormationKind.HeavyTank:
                BuildTanks(1.28f);
                break;
            case FormationKind.LightShip:
                BuildShips();
                break;
            case FormationKind.SupplyTruck:
                BuildTruck();
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
                InnerRadius = 0.95f,
                OuterRadius = 1.12f,
                Rings = 32,
                RingSegments = 8,
            },
            MaterialOverride = highlightMaterial,
            Position = new Vector3(0f, 0.3f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(highlight);
        Highlight = highlight;
    }

    private void BuildHealthBar()
    {
        _healthBar = new Node3D { Position = new Vector3(0f, IsAir ? 4.1f : 1.3f, 0f) };
        AddChild(_healthBar);

        var backgroundMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.02f, 0.02f, 0.8f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        };
        var fillColor = Faction == Faction.Player
            ? new Color(0.45f, 0.95f, 0.4f, 0.95f)
            : new Color(0.95f, 0.35f, 0.3f, 0.95f);
        var fillMaterial = new StandardMaterial3D
        {
            AlbedoColor = fillColor,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 1,
        };

        _healthBar.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.35f, 0.16f) },
            MaterialOverride = backgroundMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _healthFill = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.28f, 0.1f) },
            MaterialOverride = fillMaterial,
            Position = new Vector3(0f, 0f, 0.01f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _healthBar.AddChild(_healthFill);
    }

    private void BuildTanks(float scale)
    {
        bool enemy = Faction == Faction.Enemy;
        var hull = new StandardMaterial3D
        {
            AlbedoColor = enemy ? new Color(0.48f, 0.25f, 0.21f) : new Color(0.5f, 0.55f, 0.34f),
            Roughness = 0.85f,
        };
        var turret = new StandardMaterial3D
        {
            AlbedoColor = enemy ? new Color(0.3f, 0.15f, 0.13f) : new Color(0.3f, 0.33f, 0.23f),
            Roughness = 0.85f,
        };
        var offsets = new[]
        {
            new Vector3(-0.55f, 0f, 0.35f),
            new Vector3(0.55f, 0f, 0.35f),
            new Vector3(0f, 0f, -0.5f),
        };

        foreach (var offset in offsets)
        {
            AddBox(_body, new Vector3(0.62f, 0.26f, 0.9f) * scale, offset + new Vector3(0f, 0.33f, 0f), hull);
            AddBox(_body, new Vector3(0.34f, 0.18f, 0.36f) * scale, offset + new Vector3(0f, 0.55f, -0.06f), turret);
        }
    }

    private void BuildShips()
    {
        var hull = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.64f, 0.7f), Metallic = 0.5f, Roughness = 0.4f };
        var canopy = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.55f, 0.65f), Roughness = 0.2f };
        var engine = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.9f, 1f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.8f, 1f),
            EmissionEnergyMultiplier = 2f,
        };
        var offsets = new[]
        {
            new Vector3(-0.5f, 0f, 0.3f),
            new Vector3(0.5f, 0f, 0.3f),
            new Vector3(0f, 0f, -0.55f),
        };

        foreach (var offset in offsets)
        {
            AddBox(_body, new Vector3(0.55f, 0.16f, 1.05f), offset + new Vector3(0f, 3.2f, 0f), hull);
            AddBox(_body, new Vector3(0.3f, 0.18f, 0.34f), offset + new Vector3(0f, 3.32f, -0.2f), canopy);
            AddBox(_body, new Vector3(0.22f, 0.08f, 0.14f), offset + new Vector3(0f, 3.2f, 0.58f), engine);
        }
    }

    private void BuildTruck()
    {
        var chassis = new StandardMaterial3D { AlbedoColor = new Color(0.66f, 0.42f, 0.18f), Roughness = 0.9f };
        var cab = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.6f, 0.32f), Roughness = 0.8f };
        var tank = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.62f, 0.64f), Metallic = 0.6f, Roughness = 0.35f };

        AddBox(_body, new Vector3(0.62f, 0.28f, 1.05f), new Vector3(0f, 0.34f, 0f), chassis);
        AddBox(_body, new Vector3(0.58f, 0.3f, 0.34f), new Vector3(0f, 0.42f, -0.62f), cab);
        AddCylinder(_body, 0.22f, 0.68f, new Vector3(0f, 0.62f, 0.2f), tank);
    }

    private static void AddBox(Node3D parent, Vector3 size, Vector3 position, Material material)
    {
        var instance = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
        };
        parent.AddChild(instance);
    }

    private static void AddCylinder(Node3D parent, float radius, float height, Vector3 position, Material material)
    {
        var instance = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 12,
            },
            MaterialOverride = material,
            Position = position,
            RotationDegrees = new Vector3(90f, 0f, 0f),
        };
        parent.AddChild(instance);
    }
}
