using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Star;

/// <summary>异种巢穴：随时间成长变强，孵化异种集群，被清剿后在别处重生。</summary>
public partial class Nest : Node3D
{
    public HexCoord Coord { get; set; }
    public int Level { get; set; } = 1;
    public float MaxHp { get; set; } = 220f;
    public float Hp { get; set; } = 220f;
    public float BeamHeight => 1.1f;
    public bool IsAlive => Hp > 0f;
    public string DisplayName => $"异种巢穴 Lv.{Level}";

    private float _time;

    public override void _Ready()
    {
        var flesh = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.14f, 0.26f), Roughness = 0.85f };
        var vein = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.32f, 0.52f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.8f, 0.2f, 0.4f),
            EmissionEnergyMultiplier = 1.2f,
        };

        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.35f,
                BottomRadius = 0.95f,
                Height = 0.7f,
                RadialSegments = 9,
            },
            MaterialOverride = flesh,
            Position = new Vector3(0f, 0.35f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.4f, Height = 0.58f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = flesh,
            Position = new Vector3(0f, 0.85f, 0f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.16f, Height = 0.28f, RadialSegments = 10, Rings = 4 },
            MaterialOverride = vein,
            Position = new Vector3(0f, 1.05f, 0f),
        });
    }

    public override void _Process(double delta)
    {
        if (!IsAlive)
        {
            return;
        }

        _time += (float)delta;
        float pulse = 1f + Mathf.Sin(_time * 2.4f) * 0.04f;
        Scale = new Vector3(pulse, 1f, pulse);
    }
}

public class NestManager
{
    private const float GrowthInterval = 30f;
    private const float SpawnInterval = 16f;
    private const float RespawnDelay = 40f;
    private const int MaxEnemyFormations = 8;

    private readonly BattleManager _battle;
    private readonly List<Nest> _nests = new();
    private readonly RandomNumberGenerator _rng = new() { Seed = 90210 };
    private float _growthTimer;
    private float _spawnTimer;
    private float _respawnTimer = -1f;

    public NestManager(BattleManager battle)
    {
        _battle = battle;
        SpawnNest(new HexCoord(4, -2), 1);
    }

    public IReadOnlyList<Nest> Nests => _nests;

    public int HighestLevel => _nests.Count > 0 ? _nests.Max(n => n.Level) : 0;

    public void Update(float dt)
    {
        if (_respawnTimer >= 0f)
        {
            _respawnTimer -= dt;
            if (_respawnTimer < 0f)
            {
                SpawnRespawnedNest();
            }
        }

        if (_nests.Count == 0)
        {
            return;
        }

        _growthTimer += dt;
        if (_growthTimer >= GrowthInterval)
        {
            _growthTimer = 0f;
            foreach (var nest in _nests)
            {
                Grow(nest);
            }
        }

        _spawnTimer += dt;
        if (_spawnTimer >= SpawnInterval)
        {
            _spawnTimer = 0f;
            foreach (var nest in _nests)
            {
                TrySpawnAlien(nest);
            }
        }
    }

    public void Damage(Nest nest, float amount)
    {
        if (nest == null || !nest.IsAlive || amount <= 0f)
        {
            return;
        }

        nest.Hp = Mathf.Max(0f, nest.Hp - amount);
        if (nest.Hp <= 0f)
        {
            DestroyNest(nest);
        }
    }

    private void Grow(Nest nest)
    {
        nest.Level++;
        nest.MaxHp += 80f;
        nest.Hp = Mathf.Min(nest.MaxHp, nest.Hp + 80f);
        _battle.ShowMessage($"{nest.DisplayName} 正在成长，放任不管会更难清理");
    }

    private void TrySpawnAlien(Nest nest)
    {
        int enemies = _battle.Formations.Count(f => !f.IsPlayer && f.Hp > 0f);
        if (enemies >= MaxEnemyFormations)
        {
            return;
        }

        int units = Mathf.Clamp(2 + nest.Level, 2, 6);
        var alien = Formation.CreateAlienSwarm(units);
        for (int i = 0; i < HexCoord.Directions.Length; i++)
        {
            var coord = nest.Coord.Neighbor(i);
            if (_battle.TrySpawnFormation(alien, coord))
            {
                _battle.ShowMessage($"{nest.DisplayName} 孵化了 {alien.DisplayName}");
                return;
            }
        }
    }

    private void DestroyNest(Nest nest)
    {
        _nests.Remove(nest);
        if (nest.IsInsideTree())
        {
            nest.QueueFree();
        }

        _battle.Resources?.Add(ResourceKind.Scrap, 40f);
        _battle.Resources?.Add(ResourceKind.Data, 6f);
        _battle.ShowMessage($"{nest.DisplayName} 被清剿，获得废铁 40、知识数据 6；巢穴会在别处重生");
        if (_respawnTimer < 0f)
        {
            _respawnTimer = RespawnDelay;
        }
    }

    private void SpawnRespawnedNest()
    {
        var core = _battle.PlayerCoreCoord;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var coord = RandomCell();
            if (!_battle.Map.Contains(coord) || _battle.Map.IsBlocked(coord))
            {
                continue;
            }

            if (core.HasValue && coord.DistanceTo(core.Value) < 5)
            {
                continue;
            }

            if (_battle.IsGroundCellOccupied(coord))
            {
                continue;
            }

            SpawnNest(coord, 1);
            _battle.ShowMessage("新的异种巢穴在别处出现");
            return;
        }

        _respawnTimer = 5f;
    }

    private HexCoord RandomCell()
    {
        int radius = _battle.Map.Radius;
        int q = _rng.RandiRange(-radius, radius);
        int rMin = Mathf.Max(-radius, -q - radius);
        int rMax = Mathf.Min(radius, -q + radius);
        return new HexCoord(q, _rng.RandiRange(rMin, rMax));
    }

    private void SpawnNest(HexCoord coord, int level)
    {
        _battle.Map.ClearAround(coord, 0);
        var nest = new Nest
        {
            Coord = coord,
            Level = level,
            MaxHp = 220f + (level - 1) * 80f,
        };
        nest.Hp = nest.MaxHp;
        nest.Position = _battle.Map.ToWorld(coord);
        _battle.AddChild(nest);
        _nests.Add(nest);
    }
}

/// <summary>三条胜利条件并存：修复方舟号、立起信标、撑过最后一轮沙暴。</summary>
public class VictoryManager
{
    public const int FinalStormCount = 6;
    private const float ArkWorkSeconds = 50f;
    private const float BeaconWorkSeconds = 40f;
    private const float ArkTruckRange = 2f;

    private readonly BattleManager _battle;
    private readonly BuildingManager _buildings;
    private readonly ResourceManager _resources;
    private readonly Node3D _ark;
    private readonly Node3D _beacon;

    private float _arkProgress;
    private float _beaconProgress;
    private float _messageTimer;

    public bool ArkStarted { get; private set; }
    public bool BeaconStarted { get; private set; }
    public bool Completed { get; private set; }
    public HexCoord ArkCoord { get; } = new(-3, -4);
    public float ArkPercent => ArkWorkSeconds > 0f ? Mathf.Clamp(_arkProgress / ArkWorkSeconds, 0f, 1f) : 0f;
    public float BeaconPercent => BeaconWorkSeconds > 0f ? Mathf.Clamp(_beaconProgress / BeaconWorkSeconds, 0f, 1f) : 0f;

    public VictoryManager(BattleManager battle, BuildingManager buildings, ResourceManager resources)
    {
        _battle = battle;
        _buildings = buildings;
        _resources = resources;
        _ark = BuildArk();
        _beacon = BuildBeacon();
    }

    public string StatusText()
    {
        string ark = ArkStarted ? $"修复中 {ArkPercent * 100f:F0}%" : "未启动（V）";
        string beacon = BeaconStarted ? $"建设中 {BeaconPercent * 100f:F0}%" : "未启动（B）";
        return $"胜利条件\n方舟号 {ark}\n信标 {beacon}\n撑过沙暴 {Mathf.Min(_battle.Clock.StormCount, FinalStormCount)} / {FinalStormCount}";
    }

    public bool TryStartArk()
    {
        if (Completed)
        {
            return false;
        }

        if (ArkStarted)
        {
            _battle.ShowMessage("方舟号已经在修复中");
            return false;
        }

        if (!HasRepairTruckNearArk())
        {
            _battle.ShowMessage("需要维修车贴近方舟号残骸才能开工");
            return false;
        }

        ArkStarted = true;
        _ark.Visible = true;
        _battle.ShowMessage("开始修复方舟号：持续消耗废铁、燃料与知识数据");
        return true;
    }

    public bool TryStartBeacon()
    {
        if (Completed)
        {
            return false;
        }

        if (BeaconStarted)
        {
            _battle.ShowMessage("信标已经在建设中");
            return false;
        }

        const float scrapCost = 80f;
        const float fuelCost = 40f;
        const float dataCost = 10f;
        if (_resources.Get(ResourceKind.Scrap) < scrapCost
            || _resources.Get(ResourceKind.Fuel) < fuelCost
            || _resources.Get(ResourceKind.Data) < dataCost)
        {
            _battle.ShowMessage("信标需要废铁 80、燃料 40、知识数据 10");
            return false;
        }

        _resources.Consume(ResourceKind.Scrap, scrapCost);
        _resources.Consume(ResourceKind.Fuel, fuelCost);
        _resources.Consume(ResourceKind.Data, dataCost);
        BeaconStarted = true;
        _beacon.Visible = true;
        _battle.ShowMessage("信标开始建设：需要电力不被限电");
        return true;
    }

    public void Update(float dt)
    {
        if (Completed)
        {
            return;
        }

        _messageTimer = Mathf.Max(0f, _messageTimer - dt);

        if (ArkStarted && _arkProgress < ArkWorkSeconds)
        {
            UpdateArk(dt);
        }

        if (BeaconStarted && _beaconProgress < BeaconWorkSeconds)
        {
            UpdateBeacon(dt);
        }

        if (_battle.Clock.StormCount >= FinalStormCount && !_battle.Clock.IsStorm)
        {
            Win("撑过最后一轮沙暴");
        }
    }

    private void UpdateArk(float dt)
    {
        if (!HasRepairTruckNearArk())
        {
            Notify("维修车离开，方舟号修复暂停");
            return;
        }

        float scrap = 0.8f * dt;
        float fuel = 0.4f * dt;
        float data = 0.08f * dt;
        if (_resources.Get(ResourceKind.Scrap) < scrap
            || _resources.Get(ResourceKind.Fuel) < fuel
            || _resources.Get(ResourceKind.Data) < data)
        {
            Notify("资源不足，方舟号修复暂停");
            return;
        }

        _resources.Consume(ResourceKind.Scrap, scrap);
        _resources.Consume(ResourceKind.Fuel, fuel);
        _resources.Consume(ResourceKind.Data, data);
        _arkProgress += dt;
        if (_arkProgress >= ArkWorkSeconds)
        {
            Win("方舟号修复完成");
        }
    }

    private void UpdateBeacon(float dt)
    {
        if (_buildings.ShedCount > 0)
        {
            Notify("电力被限电，信标建设暂停");
            return;
        }

        _beaconProgress += dt;
        _beacon.Scale = new Vector3(1f, Mathf.Max(0.2f, 0.3f + BeaconPercent * 0.7f), 1f);
        if (_beaconProgress >= BeaconWorkSeconds)
        {
            Win("信标立起，等待救援");
        }
    }

    private bool HasRepairTruckNearArk()
    {
        foreach (var formation in _battle.Formations)
        {
            if (formation.IsPlayer && formation.Kind == FormationKind.RepairTruck && formation.Hp > 0f
                && formation.Coord.DistanceTo(ArkCoord) <= ArkTruckRange)
            {
                return true;
            }
        }

        return false;
    }

    private void Notify(string message)
    {
        if (_messageTimer > 0f)
        {
            return;
        }

        _messageTimer = 4f;
        _battle.ShowMessage(message);
    }

    private void Win(string reason)
    {
        if (Completed)
        {
            return;
        }

        Completed = true;
        _battle.OnVictory(reason);
    }

    private Node3D BuildArk()
    {
        var root = new Node3D { Position = _battle.Map.ToWorld(ArkCoord) + new Vector3(0f, 0.2f, 0f) };
        _battle.AddChild(root);

        var hullMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.44f, 0.48f), Metallic = 0.4f, Roughness = 0.6f };
        var scorch = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.17f, 0.15f), Roughness = 1f };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.6f, 0.7f, 2.6f) },
            MaterialOverride = hullMaterial,
            Position = new Vector3(0f, 0.45f, 0f),
            RotationDegrees = new Vector3(0f, 18f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.7f, 0.25f, 1f) },
            MaterialOverride = scorch,
            Position = new Vector3(0.2f, 0.75f, -0.6f),
            RotationDegrees = new Vector3(0f, 18f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8 },
            MaterialOverride = hullMaterial,
            Position = new Vector3(-0.7f, 0.6f, 0.8f),
        });
        root.Visible = false;
        return root;
    }

    private Node3D BuildBeacon()
    {
        var core = _buildings.CoreCoord ?? new HexCoord(0, 5);
        var root = new Node3D { Position = _battle.Map.ToWorld(core) + new Vector3(0f, 0.4f, 1.1f) };
        _battle.AddChild(root);

        var metal = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.62f, 0.66f), Metallic = 0.6f, Roughness = 0.35f };
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.5f, 0.9f, 1f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.4f, 0.85f, 1f),
            EmissionEnergyMultiplier = 2f,
        };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.22f, Height = 2.2f, RadialSegments = 8 },
            MaterialOverride = metal,
            Position = new Vector3(0f, 1.1f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.36f, RadialSegments = 10, Rings = 5 },
            MaterialOverride = glow,
            Position = new Vector3(0f, 2.35f, 0f),
        });
        root.Visible = false;
        return root;
    }
}
