using System.Collections.Generic;
using Godot;

namespace Star;

public enum FormationKind
{
    LightTank,
    LightShip,
    SupplyTruck,
}

/// <summary>战场上的编队，占一格；地面与空中分别占用各自的格子层。</summary>
public partial class Formation : Node3D
{
    public FormationKind Kind { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public float Speed { get; private set; }
    public float MaxFuel { get; private set; }
    public float Fuel { get; set; }
    public float FuelDrainPerSecond { get; private set; }
    public float MaxCargo { get; private set; }
    public float Cargo { get; set; }

    public HexCoord Coord { get; set; }
    public readonly List<HexCoord> Path = new();
    public readonly List<HexCoord> Reserved = new();
    public Formation RefuelTarget { get; set; }
    public MeshInstance3D Highlight { get; private set; }

    public bool IsAir => Kind == FormationKind.LightShip;
    public bool IsTruck => Kind == FormationKind.SupplyTruck;
    public bool IsMoving => Path.Count > 0;

    private Node3D _body;
    private float _hoverTime;

    public static Formation CreateLightTank()
    {
        return new Formation
        {
            Kind = FormationKind.LightTank,
            DisplayName = "轻坦编队 · 3 辆",
            Speed = 3.1f,
            MaxFuel = 100f,
            Fuel = 45f,
            FuelDrainPerSecond = 3.2f,
        };
    }

    public static Formation CreateLightShip()
    {
        return new Formation
        {
            Kind = FormationKind.LightShip,
            DisplayName = "轻型飞船编队 · 3 艘",
            Speed = 5.4f,
            MaxFuel = 100f,
            Fuel = 70f,
            FuelDrainPerSecond = 2.4f,
        };
    }

    public static Formation CreateSupplyTruck()
    {
        return new Formation
        {
            Kind = FormationKind.SupplyTruck,
            DisplayName = "补给车",
            Speed = 2.8f,
            MaxFuel = 100f,
            Fuel = 100f,
            FuelDrainPerSecond = 0f,
            MaxCargo = 300f,
            Cargo = 300f,
        };
    }

    public override void _Ready()
    {
        BuildVisual();
    }

    public override void _Process(double delta)
    {
        if (!IsAir || _body == null)
        {
            return;
        }

        _hoverTime += (float)delta;
        _body.Position = new Vector3(0f, Mathf.Sin(_hoverTime * 2.2f) * 0.12f, 0f);
    }

    private void BuildVisual()
    {
        _body = new Node3D { Name = "Body" };
        AddChild(_body);

        switch (Kind)
        {
            case FormationKind.LightTank:
                BuildTanks();
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

    private void BuildTanks()
    {
        var hull = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.55f, 0.34f), Roughness = 0.85f };
        var turret = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.33f, 0.23f), Roughness = 0.85f };
        var offsets = new[]
        {
            new Vector3(-0.55f, 0f, 0.35f),
            new Vector3(0.55f, 0f, 0.35f),
            new Vector3(0f, 0f, -0.5f),
        };

        foreach (var offset in offsets)
        {
            AddBox(_body, new Vector3(0.62f, 0.26f, 0.9f), offset + new Vector3(0f, 0.33f, 0f), hull);
            AddBox(_body, new Vector3(0.34f, 0.18f, 0.36f), offset + new Vector3(0f, 0.55f, -0.06f), turret);
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
