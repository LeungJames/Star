using System.Collections.Generic;
using Godot;

namespace Star;

/// <summary>原型主控：地图、编队、选择与移动、占格、燃油补给、持续交战与战争迷雾。</summary>
public partial class BattleManager : Node3D
{
    private const int MapRadius = GameBalance.MapRadius;
    private const ulong MapSeed = GameBalance.MapSeed;
    private const float RefuelRate = GameBalance.RefuelRate;

    private static readonly HexCoord TankSpawn = new(0, 2);
    private static readonly HexCoord TruckSpawn = new(-3, 3);
    private static readonly HexCoord AirSpawn = new(2, -3);
    private static readonly HexCoord InfantrySpawn = new(-1, 0);
    private static readonly HexCoord RepairSpawn = new(-2, 5);
    private static readonly HexCoord EnemyHeavySpawn = new(6, 0);
    private static readonly HexCoord EnemyLightSpawn = new(-5, -2);
    private static readonly HexCoord CaravanSpawn = new(-6, 1);
    private static readonly HexCoord CaravanSpawn2 = new(-5, 2);
    private static readonly HexCoord CultSpawn = new(-2, 7);
    private static readonly HexCoord CultSpawn2 = new(-3, 7);

    private readonly Dictionary<HexCoord, Formation> _ground = new();
    private readonly Dictionary<HexCoord, Formation> _air = new();
    private readonly List<Formation> _formations = new();
    private readonly List<MeshInstance3D> _pathMarkers = new();
    private readonly Dictionary<Formation, MeshInstance3D> _beams = new();
    private readonly HashSet<HexCoord> _explored = new();
    private readonly HashSet<HexCoord> _visibleToPlayer = new();
    private readonly HashSet<HexCoord> _visibleToEnemy = new();
    private readonly HashSet<Formation> _revealedEnemies = new();

    private HexMap _map;
    private AshLandscape _landscape;
    private CameraRig _camera;
    private Formation _selected;
    private MeshInstance3D _cursor;
    private ShaderMaterial _cursorOk;
    private ShaderMaterial _cursorBad;
    private CylinderMesh _markerMesh;
    private StandardMaterial3D _markerMaterial;
    private BoxMesh _beamMesh;
    private StandardMaterial3D _beamMaterial;
    private BuildingManager _buildings;
    private ResourceManager _resources;
    private TechManager _tech;
    private ResearchManager _research;
    private ProductionManager _production;
    private NestManager _nests;
    private VictoryManager _victory;
    private Building _selectedBuilding;
    private EventManager _events;
    private bool _paused;
    private bool _gameOver;
    private Formation _markerOwner;
    private Label _infoLabel;
    private Label _messageLabel;
    private Label _dayLabel;
    private Label _stormStateLabel;
    private Label _stormCycleLabel;
    private Label _baseStatsLabel;
    private Label _defeatLabel;
    private float _messageTimer;
    private FactionManager _factions;
    private readonly List<EnemyCommander> _commanders = new();
    private Label _victoryLabel;
    private PanelContainer _productionPanel;
    private Label _productionTitle;
    private Label _productionStatus;
    private readonly Button[] _modelButtons = new Button[3];
    private readonly Button[] _sizeButtons = new Button[4];
    private readonly Button[] _captainButtons = new Button[5];
    private Building _uiBuilding;
    private FormationKind _orderKind;
    private int _orderUnits = 3;
    private int _orderCaptain;
    private Control _uiRoot;
    private Theme _hudTheme;
    private PanelContainer _techPanel;
    private Label _techStatusLabel;
    private readonly Dictionary<TechNode, Button> _techButtons = new();
    private PanelContainer _diplomacyPanel;
    private Label _diplomacyStatusLabel;
    private readonly Dictionary<FactionId, Label> _factionLabels = new();
    private PanelContainer _captainPanel;
    private readonly List<(Captain Captain, Label Header, Button[] Buttons)> _captainRows = new();
    private PanelContainer _debugPanel;
    private Label _debugLabel;

    private WastelandAudio _sound;

    internal WorldClock Clock { get; private set; }

    public override void _Ready()
    {
        _map = new HexMap(MapRadius, MapSeed);
        _map.ClearAround(TankSpawn, 2);
        _map.ClearAround(TruckSpawn, 2);
        _map.ClearAround(AirSpawn, 2);
        _map.ClearAround(EnemyHeavySpawn, 1);
        _map.ClearAround(EnemyLightSpawn, 1);
        _map.ClearAround(InfantrySpawn, 1);
        _map.ClearAround(RepairSpawn, 1);
        _map.ClearAround(CaravanSpawn, 1);
        _map.ClearAround(CaravanSpawn2, 0);
        _map.ClearAround(CultSpawn, 1);
        _map.ClearAround(CultSpawn2, 0);
        PlaceRuins();

        _buildings = new BuildingManager(this, _map);
        _buildings.BuildInitialBase();
        _landscape = new AshLandscape(_map, _buildings.Buildings);
        AddChild(_landscape);
        SetupCamera();
        SetupCursor();
        SetupHud();
        Clock = new WorldClock();
        Clock.SandstormWarning += () => ShowMessage("沙暴预警：强风正在逼近，注意收缩阵型");
        Clock.SandstormStarted += () =>
        {
            ShowMessage("沙暴来临！视野下降，油耗上升，移动变慢");
            OnSandstormBuildingDamage(true);
        };
        Clock.SandstormEnded += () =>
        {
            ShowMessage("沙暴结束，视野与行动恢复正常");
            OnSandstormBuildingDamage(false);
        };

        _sound = new WastelandAudio { Name = "Soundscape" };
        _sound.ConnectWorld(Clock, _camera, _buildings.Core?.Position ?? Vector3.Zero);
        AddChild(_sound);

        SpawnFormations();
        _resources = new ResourceManager(this, _buildings);
        _tech = new TechManager(this, _resources);
        _research = new ResearchManager(this, _tech);
        _production = new ProductionManager(this, _resources, _tech, _research);
        _factions = new FactionManager(this, _resources, _buildings);
        BuildTechPanel();
        BuildDiplomacyPanel();
        BuildCaptainPanel();
        BuildDebugPanel();
        _commanders.Add(new EnemyCommander(this, _factions, FactionId.Junta, EnemyHeavySpawn));
        _commanders.Add(new EnemyCommander(this, _factions, FactionId.Caravan, new HexCoord(-6, 2)));
        _commanders.Add(new EnemyCommander(this, _factions, FactionId.Cult, new HexCoord(2, 6)));
        _commanders.Add(new EnemyCommander(this, _factions, FactionId.Alien, new HexCoord(4, -2)));
        _map.ClearAround(new HexCoord(-3, -4), 1);
        _nests = new NestManager(this);
        _victory = new VictoryManager(this, _buildings, _resources);
        _events = new EventManager(this, _factions, _resources, _buildings, _nests);
        _events.BuildUi(_uiRoot, _hudTheme);
        UpdateVisibility();

        UpdateHud();
        UpdateClockHud();
        UpdateBaseStats();
        ShowMessage("点击地图上的编队或建筑查看详情");
    }

    public override void _Process(double delta)
    {
        if (_gameOver)
        {
            return;
        }

        float dt = (float)delta;
        if (_paused)
        {
            UpdateMessage(dt);
            return;
        }

        Clock.Update(dt);
        _landscape.UpdateWeather(dt, Clock.IsStorm, Clock.IsWarning);
        _tech.Update(dt);
        _research.Update(dt);
        _nests.Update(dt);
        _victory.Update(dt);
        _factions.Update(dt);
        foreach (var commander in _commanders)
        {
            commander.Update(dt);
        }
        UpdateMovement(dt);
        UpdateRefuel(dt);
        _buildings.Update(dt);
        _resources.Update(dt);
        _production.Update(dt);
        _events.Update(dt);
        UpdateCaptains(dt);
        UpdateReplenish(dt);
        UpdateVisibility();
        UpdateCombat(dt);
        RegisterInfantryLosses();
        UpdateBeams();
        UpdateCursor();
        UpdateHud();
        UpdateClockHud();
        UpdateBaseStats();
        if (_techPanel != null && _techPanel.Visible)
        {
            RefreshTechPanel();
        }

        if (_diplomacyPanel != null && _diplomacyPanel.Visible)
        {
            RefreshDiplomacyPanel();
        }

        if (_captainPanel != null && _captainPanel.Visible)
        {
            RefreshCaptainPanel();
        }

        if (_debugPanel != null && _debugPanel.Visible)
        {
            RefreshDebugPanel();
        }

        UpdateMessage(dt);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_paused || _gameOver)
        {
            return;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.R)
            {
                TryStartReplenish(_selected);
                return;
            }

            if (key.Keycode == Key.V)
            {
                _victory.TryStartArk();
                return;
            }

            if (key.Keycode == Key.B)
            {
                _victory.TryStartBeacon();
                return;
            }

            switch (key.Keycode)
            {
                case Key.T:
                    ToggleHudMenu(_techPanel);
                    RefreshTechPanel();
                    return;
                case Key.G:
                    ToggleHudMenu(_diplomacyPanel);
                    RefreshDiplomacyPanel();
                    return;
                case Key.C:
                    ToggleHudMenu(_captainPanel);
                    RefreshCaptainPanel();
                    return;
                case Key.F1:
                    ToggleHudMenu(_debugPanel);
                    RefreshDebugPanel();
                    return;
                case Key.Escape:
                    CloseHudMenu();
                    CancelHudCommand();
                    UpdateHudActions();
                    return;
            }
        }

        if (@event is not InputEventMouseButton button || !button.Pressed)
        {
            return;
        }

        if (button.ButtonIndex == MouseButton.Left && !_commandArmed)
        {
            SelectAt(button.Position);
            UpdateHud();
        }
        else if (button.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            CancelHudCommand();
            CommandAt(button.Position);
            UpdateHudActions();
        }
    }

    internal void MakeTileRuins() =>
        _landscape.SyncTiles(_visibleToPlayer, _explored, Clock.IsStorm);

    private void PlaceRuins()
    {
        // 玩家侧与敌方重坦附近各留一小片废墟，便于验证步兵的废墟加成。
        foreach (var coord in new[]
                 {
                     new HexCoord(-2, 1), new HexCoord(-1, 1), new HexCoord(-2, 2), new HexCoord(-1, 2),
                     new HexCoord(4, 1), new HexCoord(5, 1), new HexCoord(4, 0), new HexCoord(5, 0),
                 })
        {
            _map.AddRuins(coord);
        }
    }

    internal int EffectiveVision(Formation formation)
    {
        int penalty = Clock != null && Clock.IsStorm ? WorldClock.StormVisionPenalty : 0;
        int tech = formation.IsPlayer && _tech != null ? _tech.VisionBonus : 0;
        int captain = formation.IsPlayer ? formation.CaptainVisionBonus : 0;
        return Mathf.Max(1, formation.VisionRadius - penalty + tech + captain);
    }

    private float WeatherSpeedMultiplier => Clock.IsStorm ? WorldClock.StormSpeedMultiplier : 1f;

    private float WeatherFuelMultiplier => Clock.IsStorm ? WorldClock.StormFuelMultiplier : 1f;

    private void UpdateVisibility()
    {
        _visibleToPlayer.Clear();
        _visibleToEnemy.Clear();
        foreach (var formation in _formations)
        {
            if (formation.Hp <= 0f)
            {
                continue;
            }

            var visibleSet = formation.IsPlayer ? _visibleToPlayer : _visibleToEnemy;
            int vision = EffectiveVision(formation);
            foreach (var cell in _map.Cells())
            {
                if (cell.DistanceTo(formation.Coord) <= vision)
                {
                    visibleSet.Add(cell);
                }
            }
        }

        foreach (var cell in _visibleToPlayer)
        {
            _explored.Add(cell);
        }

        _landscape.SyncTiles(_visibleToPlayer, _explored, Clock.IsStorm);
        foreach (var ruin in _research.Ruins)
        {
            ruin.Visible = !ruin.Depleted && _explored.Contains(ruin.Coord);
        }

        foreach (var nest in _nests.Nests)
        {
            nest.Visible = _visibleToPlayer.Contains(nest.Coord);
        }

        foreach (var formation in _formations)
        {
            if (formation.IsPlayer)
            {
                formation.Visible = true;
                continue;
            }

            bool visible = _visibleToPlayer.Contains(formation.Coord);
            formation.Visible = visible;
            if (visible && _revealedEnemies.Add(formation))
            {
                ShowMessage($"发现 {formation.DisplayName}");
            }
        }
    }

    private void SetupCamera()
    {
        _camera = new CameraRig { Current = true, Focus = new Vector3(3.5f, 0f, 5.8f) };
        AddChild(_camera);
    }

    private void SetupCursor()
    {
        var cursorShader = GD.Load<Shader>("res://shaders/tactical_cursor.gdshader");
        _cursorOk = new ShaderMaterial { Shader = cursorShader };
        _cursorOk.SetShaderParameter("ink", new Color("#8cebd5"));
        _cursorBad = new ShaderMaterial { Shader = cursorShader };
        _cursorBad.SetShaderParameter("ink", new Color("#ff896a"));
        _markerMesh = new CylinderMesh
        {
            TopRadius = 0.32f,
            BottomRadius = 0.32f,
            Height = 0.05f,
            RadialSegments = 12,
        };
        _markerMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.86f, 0.32f, 0.85f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _beamMesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 1f) };
        _beamMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.62f, 0.25f, 0.9f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        _cursor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One * (2f * HexMap.HexSize) },
            MaterialOverride = _cursorOk,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_cursor);
    }

    private void SpawnFormations()
    {
        SpawnFormation(Formation.CreateLightTank(), TankSpawn);
        SpawnFormation(Formation.CreateInfantry(), InfantrySpawn);
        SpawnFormation(Formation.CreateSupplyTruck(), TruckSpawn);
        SpawnFormation(Formation.CreateRepairTruck(), RepairSpawn);
        SpawnFormation(Formation.CreateLightShip(), AirSpawn);
        SpawnFactionUnit(Formation.CreateEnemyHeavyTank(), EnemyHeavySpawn, FactionId.Junta);
        SpawnFactionUnit(Formation.CreateEnemyLightTank(), EnemyLightSpawn, FactionId.Junta);
        SpawnFactionUnit(Formation.CreateEnemyLightTank(), CaravanSpawn, FactionId.Caravan);
        SpawnFactionUnit(Formation.CreateEnemyLightTank(), CaravanSpawn2, FactionId.Caravan);
        SpawnFactionUnit(Formation.CreateEnemyLightTank(), CultSpawn, FactionId.Cult);
        SpawnFactionUnit(Formation.CreateEnemyLightTank(), CultSpawn2, FactionId.Cult);
    }

    private void SpawnFactionUnit(Formation formation, HexCoord coord, FactionId owner)
    {
        formation.Owner = owner;
        SpawnFormation(formation, coord);
    }

    private void SpawnFormation(Formation formation, HexCoord coord)
    {
        formation.Coord = coord;
        formation.Position = _map.ToWorld(coord);
        AddChild(formation);
        _formations.Add(formation);
        TableOf(formation)[coord] = formation;

        var beam = new MeshInstance3D
        {
            Mesh = _beamMesh,
            MaterialOverride = _beamMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(beam);
        _beams[formation] = beam;
    }

    /// <summary>生产完成后在指定格下发编队，占用与阻挡规则与常规编队一致。</summary>
    internal bool TrySpawnFormation(Formation formation, HexCoord coord)
    {
        if (!_map.Contains(coord))
        {
            return false;
        }

        if (!formation.IsAir
            && (_map.IsBlocked(coord) || (_buildings != null && _buildings.IsOccupied(coord))))
        {
            return false;
        }

        if (TableOf(formation).ContainsKey(coord))
        {
            return false;
        }

        SpawnFormation(formation, coord);
        return true;
    }

    private Dictionary<HexCoord, Formation> TableOf(Formation formation) => formation.IsAir ? _air : _ground;

    internal IReadOnlyList<Formation> Formations => _formations;
    internal HexMap Map => _map;
    internal ResourceManager Resources => _resources;
    internal TechManager Tech => _tech;

    internal bool IsGroundCellOccupied(HexCoord coord) =>
        _ground.ContainsKey(coord) || (_buildings != null && _buildings.IsOccupied(coord));

    internal void PlayAudioCue(WastelandAudio.Cue cue) => _sound?.PlayCue(cue);

    private void SelectAt(Vector2 screenPosition)
    {
        var picked = PickFormation(screenPosition, 48f);
        if (picked == null && TryGroundPoint(screenPosition, out var world))
        {
            var coord = _map.ToCoord(world);
            if (_buildings != null && _buildings.At(coord) is { } building)
            {
                if (_selectedBuilding != building)
                {
                    PlayAudioCue(WastelandAudio.Cue.Select);
                }

                SetSelectedBuilding(building);
                return;
            }

            if (_ground.TryGetValue(coord, out var ground) && ground.IsPlayer)
            {
                picked = ground;
            }
            else if (_air.TryGetValue(coord, out var air) && air.IsPlayer)
            {
                picked = air;
            }
        }

        if (picked != null && picked != _selected)
        {
            PlayAudioCue(WastelandAudio.Cue.Select);
        }

        SetSelected(picked);
    }

    private void SetSelected(Formation formation)
    {
        _selected = formation;
        CancelHudCommand();
        foreach (var f in _formations)
        {
            if (f.Highlight != null)
            {
                f.Highlight.Visible = f == _selected;
            }
        }

        if (_selectedBuilding != null)
        {
            _selectedBuilding.SetSelected(false);
            _selectedBuilding = null;
        }

        if (_uiBuilding != null)
        {
            _uiBuilding = null;
            RefreshProductionPanel();
        }
    }

    private void SetSelectedBuilding(Building building)
    {
        if (_selectedBuilding == building)
        {
            return;
        }

        if (_selectedBuilding != null)
        {
            _selectedBuilding.SetSelected(false);
        }

        SetSelected(null);
        _selectedBuilding = building;
        building?.SetSelected(true);

        if (building != null && _production != null && _production.IsSupported(building))
        {
            ConfigureProductionPanel(building);
        }
        else
        {
            _uiBuilding = null;
            RefreshProductionPanel();
        }
    }

    internal void OnBuildingDestroyed(Building building)
    {
        if (_uiBuilding == building)
        {
            _uiBuilding = null;
            RefreshProductionPanel();
        }

        if (_selectedBuilding == building)
        {
            _selectedBuilding = null;
        }
    }

    private void CommandAt(Vector2 screenPosition)
    {
        if (_selected == null)
        {
            ShowMessage("先左键选中一个编队");
            PlayAudioCue(WastelandAudio.Cue.Denied);
            return;
        }

        var picked = PickFormation(screenPosition, 46f);
        if (_selected.IsTruck && picked != null && picked.IsPlayer && !picked.IsTruck && !picked.IsAir)
        {
            PlayAudioCue(OrderRefuel(_selected, picked) ? WastelandAudio.Cue.Order : WastelandAudio.Cue.Denied);
            return;
        }

        if (!TryGroundPoint(screenPosition, out var world))
        {
            return;
        }

        bool ordered = IssueMove(_selected, _map.ToCoord(world));
        PlayAudioCue(ordered ? WastelandAudio.Cue.Order : WastelandAudio.Cue.Denied);
    }

    internal bool IssueMove(Formation formation, HexCoord goal, bool playerOrder = true)
    {
        if (!_map.Contains(goal))
        {
            if (playerOrder)
            {
                ShowMessage("目标超出地图范围");
            }

            return false;
        }

        if (!formation.IsAir && _map.IsBlocked(goal))
        {
            if (playerOrder)
            {
                ShowMessage("目标格是阻挡地形，无法进入");
            }

            return false;
        }

        if (!formation.IsAir && _buildings != null && _buildings.IsOccupied(goal))
        {
            if (playerOrder)
            {
                ShowMessage("目标格被建筑占用");
            }

            return false;
        }

        var table = TableOf(formation);
        if (table.TryGetValue(goal, out var occupant) && occupant != formation)
        {
            if (playerOrder)
            {
                ShowMessage("目标格已被占用，请绕行");
            }

            return false;
        }

        if (goal.Equals(formation.Coord))
        {
            if (playerOrder)
            {
                ShowMessage("编队已在该格");
            }

            return false;
        }

        if (formation.Fuel <= 0f)
        {
            if (playerOrder)
            {
                ShowMessage("燃油耗尽，无法移动");
            }

            return false;
        }

        var path = Pathfinder.FindPath(_map, coord => IsBlockedFor(formation, coord), formation.Coord, goal);
        if (path == null)
        {
            if (playerOrder)
            {
                ShowMessage("找不到可通行路线");
            }

            return false;
        }

        ReleaseReservations(formation);
        formation.RefuelTarget = null;
        formation.Path.Clear();
        formation.Path.AddRange(path);
        foreach (var coord in path)
        {
            table[coord] = formation;
            formation.Reserved.Add(coord);
        }

        if (playerOrder)
        {
            DrawPathMarkers(formation);
        }

        return true;
    }

    private bool IsBlockedFor(Formation formation, HexCoord coord)
    {
        if (!_map.Contains(coord))
        {
            return true;
        }

        if (!formation.IsAir && _map.IsBlocked(coord))
        {
            return true;
        }

        if (!formation.IsAir && _buildings != null && _buildings.IsOccupied(coord))
        {
            return true;
        }

        var table = TableOf(formation);
        return table.TryGetValue(coord, out var owner) && owner != formation;
    }

    internal bool CanEnter(Formation formation, HexCoord coord)
    {
        return !IsBlockedFor(formation, coord);
    }

    private void ReleaseReservations(Formation formation)
    {
        var table = TableOf(formation);
        foreach (var coord in formation.Reserved)
        {
            if (!coord.Equals(formation.Coord) && table.TryGetValue(coord, out var owner) && owner == formation)
            {
                table.Remove(coord);
            }
        }

        formation.Reserved.Clear();
    }

    private bool OrderRefuel(Formation truck, Formation target)
    {
        if (target.Fuel >= target.MaxFuel - 0.5f)
        {
            ShowMessage("目标燃油已满");
            return false;
        }

        if (truck.Cargo <= 0f)
        {
            ShowMessage("补给车已空");
            return false;
        }

        if (truck.Coord.DistanceTo(target.Coord) <= 1)
        {
            truck.RefuelTarget = target;
            ShowMessage("补给车开始加油");
            return true;
        }

        HexCoord? goal = null;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < HexCoord.Directions.Length; i++)
        {
            var candidate = target.Coord.Neighbor(i);
            if (!CanEnter(truck, candidate))
            {
                continue;
            }

            int distance = truck.Coord.DistanceTo(candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                goal = candidate;
            }
        }

        if (goal == null)
        {
            ShowMessage("目标周围没有空位");
            return false;
        }

        if (!IssueMove(truck, goal.Value))
        {
            return false;
        }

        truck.RefuelTarget = target;
        ShowMessage("补给车前往目标加油");
        return true;
    }

    private void UpdateMovement(float dt)
    {
        foreach (var formation in _formations)
        {
            if (formation.Path.Count == 0)
            {
                continue;
            }

            if (formation.Fuel <= 0f)
            {
                StopForNoFuel(formation);
                continue;
            }

            float remaining = formation.Speed * WeatherSpeedMultiplier
                * (formation.IsPlayer ? _tech.SpeedMultiplier : 1f) * dt;
            float moved = 0f;

            while (remaining > 0f && formation.Path.Count > 0)
            {
                Vector3 target = _map.ToWorld(formation.Path[0]);
                Vector3 position = formation.Position;
                var toTarget = new Vector3(target.X - position.X, 0f, target.Z - position.Z);
                float distance = toTarget.Length();

                if (distance <= remaining)
                {
                    formation.Position = new Vector3(target.X, 0f, target.Z);
                    moved += distance;
                    remaining -= distance;
                    var reached = formation.Path[0];
                    formation.Path.RemoveAt(0);
                    AdvanceTo(formation, reached);
                }
                else
                {
                    var direction = toTarget / distance;
                    formation.Position = position + direction * remaining;
                    formation.Rotation = new Vector3(0f, Mathf.Atan2(direction.X, direction.Z), 0f);
                    moved += remaining;
                    remaining = 0f;
                }
            }

            if (moved > 0.0001f)
            {
                formation.Fuel = Mathf.Max(
                    0f,
                    formation.Fuel - formation.FuelDrainPerSecond * WeatherFuelMultiplier
                    * (formation.IsPlayer ? _tech.FuelMultiplier * formation.CaptainFuelMultiplier : 1f) * dt);
                if (formation.Fuel <= 0f && formation.Path.Count > 0)
                {
                    StopForNoFuel(formation);
                    continue;
                }
            }

            if (formation.Path.Count == 0)
            {
                formation.Reserved.Clear();
                if (_markerOwner == formation)
                {
                    ClearPathMarkers();
                }
            }
        }
    }

    private void AdvanceTo(Formation formation, HexCoord next)
    {
        var table = TableOf(formation);
        if (table.TryGetValue(formation.Coord, out var owner) && owner == formation)
        {
            table.Remove(formation.Coord);
        }

        formation.Coord = next;
        table[next] = formation;
    }

    private void StopForNoFuel(Formation formation)
    {
        formation.Path.Clear();
        ReleaseReservations(formation);
        if (_markerOwner == formation)
        {
            ClearPathMarkers();
        }

        ShowMessage($"{formation.DisplayName} 燃油耗尽，已停下");
    }

    private void UpdateRefuel(float dt)
    {
        foreach (var truck in _formations)
        {
            if (!truck.IsTruck || truck.RefuelTarget == null)
            {
                continue;
            }

            var target = truck.RefuelTarget;
            if (target.Fuel >= target.MaxFuel - 0.01f)
            {
                truck.RefuelTarget = null;
                continue;
            }

            if (truck.Cargo <= 0f)
            {
                truck.RefuelTarget = null;
                ShowMessage("补给车已空");
                continue;
            }

            if (truck.IsMoving)
            {
                continue;
            }

            if (truck.Coord.DistanceTo(target.Coord) > 1)
            {
                truck.RefuelTarget = null;
                ShowMessage("目标已离开补给范围");
                continue;
            }

            float amount = Mathf.Min(RefuelRate * dt, target.MaxFuel - target.Fuel);
            amount = Mathf.Min(amount, truck.Cargo);
            target.Fuel += amount;
            truck.Cargo -= amount;

            if (target.Fuel >= target.MaxFuel - 0.01f)
            {
                truck.RefuelTarget = null;
                ShowMessage($"{target.DisplayName} 已加满");
            }
        }
    }

    /// <summary>坦克与飞船补员：基地更快，补给车在前线更慢且会因补给车被毁中断。</summary>
    private void UpdateReplenish(float dt)
    {
        foreach (var formation in _formations)
        {
            if (!formation.Replenishing || formation.Hp <= 0f)
            {
                continue;
            }

            if (formation.Hp >= formation.MaxHp - 0.01f)
            {
                StopReplenish(formation, $"{formation.DisplayName} 补员完成");
                continue;
            }

            Building baseBuilding = null;
            if (formation.ReplenishTruck != null)
            {
                var truck = formation.ReplenishTruck;
                if (truck.Hp <= 0f || truck.IsMoving || truck.Coord.DistanceTo(formation.Coord) > 1)
                {
                    StopReplenish(formation, "补给车被击毁或离开，前线补员中断");
                    continue;
                }
            }
            else
            {
                baseBuilding = AdjacentProductionBuilding(formation.Coord);
                if (baseBuilding == null)
                {
                    StopReplenish(formation, "离开生产建筑，补员中断");
                    continue;
                }
            }

            float rate = (formation.ReplenishTruck != null ? 4f : 12f)
                * (_tech != null ? _tech.ReplenishMultiplier : 1f)
                * formation.CaptainReplenishMultiplier;
            if (baseBuilding != null)
            {
                rate *= Mathf.Max(0.25f, baseBuilding.Efficiency);
            }

            float heal = Mathf.Min(rate * dt, formation.MaxHp - formation.Hp);
            float cost = heal * 0.4f;
            float paid = _resources.Consume(ResourceKind.Scrap, cost);
            if (paid < cost - 0.001f)
            {
                if (cost > 0f)
                {
                    formation.Hp = Mathf.Min(formation.MaxHp, formation.Hp + heal * (paid / cost));
                }

                StopReplenish(formation, "废铁不足，补员中断");
                continue;
            }

            formation.Hp = Mathf.Min(formation.MaxHp, formation.Hp + heal);
        }
    }

    /// <summary>队长经验：战斗按伤害结算，驻防按时间结算，后方同样能培养。</summary>
    private void UpdateCaptains(float dt)
    {
        HexCoord? core = _buildings?.CoreCoord;
        float growth = _tech?.CaptainXpMultiplier ?? 1f;
        foreach (var formation in _formations)
        {
            if (!formation.IsPlayer || formation.Hp <= 0f)
            {
                formation.Garrisoned = false;
                continue;
            }

            bool nearCore = core.HasValue
                && formation.Coord.DistanceTo(core.Value) <= GameBalance.CaptainGarrisonRadius;
            bool nearBuilding = false;
            if (!nearCore && _buildings != null)
            {
                foreach (var building in _buildings.Buildings)
                {
                    if (!building.IsAlive || !building.IsPlayer || !building.Connected)
                    {
                        continue;
                    }

                    if (formation.Coord.DistanceTo(building.Coord) <= 1)
                    {
                        nearBuilding = true;
                        break;
                    }
                }
            }

            formation.Garrisoned = !formation.IsMoving && (nearCore || nearBuilding);
            if (!formation.Garrisoned || formation.Captain == null || !formation.Captain.IsAlive)
            {
                continue;
            }

            int levels = formation.Captain.AddXp(
                GameBalance.CaptainGarrisonXpPerSecond * dt * growth);
            if (levels > 0)
            {
                NotifyCaptainLevel(formation.Captain, levels);
            }
        }
    }

    private void AwardXp(Captain captain, float amount)
    {
        if (captain == null || !captain.IsAlive || amount <= 0f)
        {
            return;
        }

        int levels = captain.AddXp(amount * (_tech?.CaptainXpMultiplier ?? 1f));
        if (levels > 0)
        {
            NotifyCaptainLevel(captain, levels);
        }
    }

    private void NotifyCaptainLevel(Captain captain, int levels)
    {
        ShowMessage($"队长 {captain.Name} 升到 Lv.{captain.Level}，获得 {levels} 技能点（按 C 加点）");
        if (_captainPanel != null && _captainPanel.Visible)
        {
            RefreshCaptainPanel();
        }
    }

    /// <summary>驻防队长提供的基地加成，供生产、研究、士气与配给读取。</summary>
    internal float GarrisonCaptainValue(CaptainSkill skill)
    {
        float total = 0f;
        foreach (var formation in _formations)
        {
            if (formation.IsPlayer && formation.Hp > 0f && formation.Garrisoned
                && formation.Captain is { IsAlive: true })
            {
                total += formation.Captain.ValueGarrison(skill);
            }
        }

        return total;
    }

    internal int GarrisonMoraleBonus =>
        Mathf.FloorToInt(GarrisonCaptainValue(CaptainSkill.CivilMorale));

    internal float CaptainFoodMultiplier =>
        Mathf.Max(0.5f, 1f - GarrisonCaptainValue(CaptainSkill.Rationing));

    internal float CaptainProductionMultiplier =>
        1f + GarrisonCaptainValue(CaptainSkill.ProductionAssist);

    internal float CaptainResearchMultiplier =>
        1f + GarrisonCaptainValue(CaptainSkill.ResearchAssist);

    private void TryStartReplenish(Formation formation)
    {
        if (formation == null || !formation.IsPlayer)
        {
            ShowMessage("先选中一个坦克或飞船编队");
            return;
        }

        if (formation.IsInfantry && formation.Hp < formation.MaxHp)
        {
            ShowMessage("步兵减员等同人口死亡，无法补员，只能重新招募");
            return;
        }

        if (!formation.CanReplenish)
        {
            ShowMessage("该编队无需补员");
            return;
        }

        formation.ReplenishTruck = null;
        foreach (var truck in _formations)
        {
            if (truck.Kind != FormationKind.SupplyTruck || !truck.IsPlayer || truck.Hp <= 0f || truck.IsMoving)
            {
                continue;
            }

            if (truck.Coord.DistanceTo(formation.Coord) <= 1)
            {
                formation.ReplenishTruck = truck;
                break;
            }
        }

        if (formation.ReplenishTruck == null && AdjacentProductionBuilding(formation.Coord) == null)
        {
            ShowMessage("需贴近补给车，或回到兵营 / 坦克厂 / 机库旁补员");
            return;
        }

        formation.Replenishing = true;
        ShowMessage(formation.ReplenishTruck != null ? "补给车开始前线补员（较慢）" : "在基地开始补员");
    }

    private void StopReplenish(Formation formation, string message)
    {
        formation.Replenishing = false;
        formation.ReplenishTruck = null;
        ShowMessage(message);
    }

    private Building AdjacentProductionBuilding(HexCoord coord)
    {
        if (_buildings == null)
        {
            return null;
        }

        foreach (var building in _buildings.Buildings)
        {
            if (!building.IsAlive || !building.Connected || !building.IsPlayer)
            {
                continue;
            }

            if (building.Kind is not (BuildingKind.Barracks or BuildingKind.TankFactory or BuildingKind.Hangar))
            {
                continue;
            }

            if (building.Coord.DistanceTo(coord) <= 1)
            {
                return building;
            }
        }

        return null;
    }

    /// <summary>步兵减员按人口死亡处理，不可逆。</summary>
    private void RegisterInfantryLosses()
    {
        foreach (var formation in _formations)
        {
            if (!formation.IsPlayer || !formation.IsInfantry || formation.Hp <= 0f)
            {
                continue;
            }

            int alive = formation.AliveUnits;
            if (alive < formation.LastAliveUnits)
            {
                _buildings.RegisterInfantryLoss(formation.LastAliveUnits - alive);
            }

            formation.LastAliveUnits = alive;
        }
    }

    private void UpdateCombat(float dt)
    {
        var pendingDamage = new Dictionary<Formation, float>();
        var pendingBuildingDamage = new Dictionary<Building, float>();
        var pendingNestDamage = new Dictionary<Nest, float>();

        foreach (var attacker in _formations)
        {
            attacker.CurrentTarget = null;
            attacker.CurrentBuildingTarget = null;
            attacker.CurrentNestTarget = null;
            if (!attacker.IsCombatant || attacker.Hp <= 0f)
            {
                continue;
            }

            var target = FindTarget(attacker);
            attacker.CurrentTarget = target;
            if (target != null)
            {
                float penetration = EffectivePenetration(attacker, target);
                float armor = target.Armor + (target.IsPlayer ? PlayerArmorBonus : 0f);
                float multiplier = PenetrationMultiplier(penetration, armor);
                float dps = attacker.BaseDps * attacker.AttackScale * multiplier
                    * attacker.CaptainDamageMultiplier * PlayerDamageMultiplier(attacker);
                float applied = dps * dt * (1f - target.CaptainDamageReduction);
                pendingDamage[target] = pendingDamage.GetValueOrDefault(target) + applied;
                AwardXp(attacker.Captain, applied * GameBalance.CaptainXpPerDamageDealt);
                continue;
            }

            var building = FindBuildingTarget(attacker);
            if (building != null)
            {
                attacker.CurrentBuildingTarget = building;
                float buildingMultiplier = PenetrationMultiplier(
                    EffectivePenetrationAgainstBuilding(attacker, building.Coord),
                    building.Armor);
                float buildingDamage = attacker.BaseDps * attacker.AttackScale * buildingMultiplier
                    * attacker.CaptainDamageMultiplier * PlayerDamageMultiplier(attacker) * dt;
                pendingBuildingDamage[building] =
                    pendingBuildingDamage.GetValueOrDefault(building) + buildingDamage;
                AwardXp(attacker.Captain, buildingDamage * GameBalance.CaptainXpPerDamageDealt);
                continue;
            }

            var nest = FindNestTarget(attacker);
            if (nest == null)
            {
                continue;
            }

            attacker.CurrentNestTarget = nest;
            float nestMultiplier = PenetrationMultiplier(
                EffectivePenetrationAgainstBuilding(attacker, nest.Coord), 6f);
            float nestDamage = attacker.BaseDps * attacker.AttackScale * nestMultiplier
                * attacker.CaptainDamageMultiplier * PlayerDamageMultiplier(attacker) * dt;
            pendingNestDamage[nest] = pendingNestDamage.GetValueOrDefault(nest) + nestDamage;
            AwardXp(attacker.Captain, nestDamage * GameBalance.CaptainXpPerDamageDealt);
        }

        foreach (var pair in pendingDamage)
        {
            var target = pair.Key;
            if (target.Hp <= 0f)
            {
                continue;
            }

            AwardXp(target.Captain, pair.Value * GameBalance.CaptainXpPerDamageTaken);
            target.Hp = Mathf.Max(0f, target.Hp - pair.Value);
            if (target.Hp <= 0f)
            {
                DestroyFormation(target);
            }
        }

        foreach (var pair in pendingBuildingDamage)
        {
            _buildings.Damage(pair.Key, pair.Value);
        }

        foreach (var pair in pendingNestDamage)
        {
            _nests.Damage(pair.Key, pair.Value);
        }
    }

    private float PlayerDamageMultiplier(Formation attacker) =>
        attacker.IsPlayer && _tech != null ? _tech.DamageMultiplier : 1f;

    /// <summary>不稳定科技“试验装甲”依赖发电站在线，断电或发电站被毁则失效。</summary>
    private float PlayerArmorBonus =>
        _tech != null && _tech.HasExperimentalArmor && _buildings != null && _buildings.PowerOutput > 0
            ? 25f
            : 0f;

    private Nest FindNestTarget(Formation attacker)
    {
        if (_nests == null || attacker.Owner != FactionId.Player)
        {
            return null;
        }

        Nest best = null;
        int bestDistance = int.MaxValue;
        foreach (var nest in _nests.Nests)
        {
            if (!nest.IsAlive)
            {
                continue;
            }

            int distance = attacker.Coord.DistanceTo(nest.Coord);
            if (distance > attacker.Range || distance >= bestDistance)
            {
                continue;
            }

            if (!IsVisibleTo(attacker.Faction, nest.Coord))
            {
                continue;
            }

            best = nest;
            bestDistance = distance;
        }

        return best;
    }

    private Building FindBuildingTarget(Formation attacker)
    {
        if (_buildings == null)
        {
            return null;
        }

        Building best = null;
        int bestDistance = int.MaxValue;
        foreach (var building in _buildings.Buildings)
        {
            if (!building.IsAlive || building.Faction == attacker.Faction)
            {
                continue;
            }

            if (!_factions.IsHostile(attacker.Owner))
            {
                continue;
            }

            int distance = attacker.Coord.DistanceTo(building.Coord);
            if (distance > attacker.Range || distance >= bestDistance)
            {
                continue;
            }

            if (!IsVisibleTo(attacker.Faction, building.Coord))
            {
                continue;
            }

            best = building;
            bestDistance = distance;
        }

        return best;
    }

    private float EffectivePenetrationAgainstBuilding(Formation attacker, HexCoord buildingCoord)
    {
        float penetration = attacker.Penetration;
        if (attacker.Coord.DistanceTo(buildingCoord) <= 1)
        {
            penetration *= attacker.CloseRangePenMultiplier;
        }

        return penetration;
    }

    private Formation FindTarget(Formation attacker)
    {
        Formation best = null;
        int bestDistance = int.MaxValue;
        foreach (var candidate in _formations)
        {
            if (candidate.Faction == attacker.Faction || candidate.Hp <= 0f)
            {
                continue;
            }

            if (!CanEngage(attacker, candidate))
            {
                continue;
            }

            int distance = attacker.Coord.DistanceTo(candidate.Coord);
            if (distance > attacker.Range || distance >= bestDistance)
            {
                continue;
            }

            if (!IsVisibleTo(attacker.Faction, candidate.Coord))
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>非交战阵营不会主动攻击玩家；玩家可以主动打破和平。</summary>
    private bool CanEngage(Formation attacker, Formation candidate)
    {
        if (attacker.Owner == FactionId.Player)
        {
            return true;
        }

        if (candidate.Owner != FactionId.Player)
        {
            return false;
        }

        return _factions == null || _factions.IsHostile(attacker.Owner);
    }

    /// <summary>穿甲低于装甲时完全打不动；高于装甲时按差值折算。</summary>
    private static float PenetrationMultiplier(float penetration, float armor)
    {
        if (penetration <= 0f || penetration <= armor)
        {
            return 0f;
        }

        return 1f - armor / penetration;
    }

    /// <summary>贴身与废墟会提升穿甲，目前只对步兵生效。</summary>
    private float EffectivePenetration(Formation attacker, Formation target)
    {
        float penetration = attacker.Penetration;
        if (target != null && attacker.Coord.DistanceTo(target.Coord) <= 1)
        {
            penetration *= attacker.CloseRangePenMultiplier;
        }

        if (_map.IsRuins(attacker.Coord))
        {
            penetration *= attacker.RuinsPenMultiplier;
        }

        return penetration;
    }

    private float CurrentDps(Formation formation)
    {
        var target = formation.CurrentTarget;
        float multiplier = target != null
            ? PenetrationMultiplier(EffectivePenetration(formation, target), target.Armor)
            : 1f;
        return formation.BaseDps * formation.AttackScale * multiplier;
    }

    private void DestroyFormation(Formation formation)
    {
        _factions?.OnUnitLost(formation.Owner);
        if (formation.Owner is FactionId.Junta or FactionId.Caravan or FactionId.Cult)
        {
            _factions?.ApplyRelation(formation.Owner, GameBalance.KillRelationPenalty);
        }

        var table = TableOf(formation);
        if (table.TryGetValue(formation.Coord, out var owner) && owner == formation)
        {
            table.Remove(formation.Coord);
        }

        ReleaseReservations(formation);
        formation.Path.Clear();
        formation.CurrentTarget = null;
        formation.Replenishing = false;
        formation.ReplenishTruck = null;

        if (formation.IsScienceTeam)
        {
            _research.OnTeamDestroyed(formation);
        }

        if (formation.IsPlayer && formation.IsInfantry && formation.LastAliveUnits > 0)
        {
            _buildings.RegisterInfantryLoss(formation.LastAliveUnits);
            formation.LastAliveUnits = 0;
        }

        _formations.Remove(formation);

        if (formation.Captain != null)
        {
            formation.Captain.IsAlive = false;
            formation.Captain.InUse = false;
            ShowMessage($"{formation.DisplayName} 覆灭，队长 {formation.Captain.Name} 阵亡");
        }

        if (_markerOwner == formation)
        {
            ClearPathMarkers();
        }

        if (_selected == formation)
        {
            SetSelected(null);
        }

        foreach (var other in _formations)
        {
            if (other.RefuelTarget == formation)
            {
                other.RefuelTarget = null;
            }

            if (other.ReplenishTruck == formation)
            {
                other.ReplenishTruck = null;
                other.Replenishing = false;
            }

            if (other.CurrentTarget == formation)
            {
                other.CurrentTarget = null;
            }
        }

        if (_beams.TryGetValue(formation, out var beam))
        {
            beam.QueueFree();
            _beams.Remove(formation);
        }

        formation.QueueFree();
        ShowMessage($"{formation.DisplayName} 已覆灭");
    }

    private void UpdateBeams()
    {
        foreach (var pair in _beams)
        {
            var attacker = pair.Key;
            var beam = pair.Value;
            if (attacker.Hp <= 0f || !IsVisibleTo(Faction.Player, attacker.Coord))
            {
                beam.Visible = false;
                continue;
            }

            var target = attacker.CurrentTarget;
            var building = attacker.CurrentBuildingTarget;
            var nest = attacker.CurrentNestTarget;
            Vector3 to;
            if (target != null && target.Hp > 0f)
            {
                if (!IsVisibleTo(Faction.Player, target.Coord))
                {
                    beam.Visible = false;
                    continue;
                }

                to = target.GlobalPosition + new Vector3(0f, target.BeamHeight, 0f);
            }
            else if (building != null && building.IsAlive)
            {
                if (!IsVisibleTo(Faction.Player, building.Coord))
                {
                    beam.Visible = false;
                    continue;
                }

                to = building.GlobalPosition + new Vector3(0f, building.BeamHeight, 0f);
            }
            else if (nest != null && nest.IsAlive)
            {
                if (!IsVisibleTo(Faction.Player, nest.Coord))
                {
                    beam.Visible = false;
                    continue;
                }

                to = nest.GlobalPosition + new Vector3(0f, nest.BeamHeight, 0f);
            }
            else
            {
                beam.Visible = false;
                continue;
            }

            Vector3 from = attacker.GlobalPosition + new Vector3(0f, attacker.BeamHeight, 0f);
            Vector3 delta = to - from;
            float length = delta.Length();
            if (length < 0.01f)
            {
                beam.Visible = false;
                continue;
            }

            beam.Visible = true;
            beam.Position = from + delta * 0.5f;
            beam.LookAt(to, Vector3.Up);
            beam.Scale = new Vector3(1f, 1f, length);
            _sound?.ObserveFire(attacker.Kind, from);
        }
    }

    internal bool IsVisibleTo(Faction faction, HexCoord coord)
    {
        return (faction == Faction.Player ? _visibleToPlayer : _visibleToEnemy).Contains(coord);
    }

    private void UpdateCursor()
    {
        if (_selected == null || !TryGroundPoint(GetViewport().GetMousePosition(), out var world))
        {
            _cursor.Visible = false;
            return;
        }

        var coord = _map.ToCoord(world);
        if (!_map.Contains(coord))
        {
            _cursor.Visible = false;
            return;
        }

        _cursor.Visible = true;
        _cursor.Position = _map.ToWorld(coord) + Vector3.Up * (_landscape.SurfaceHeight(coord) + 0.045f);

        bool ok = CanEnter(_selected, coord);
        if (!ok && _selected.IsTruck && _ground.TryGetValue(coord, out var ground)
            && ground != _selected && ground.IsPlayer && !ground.IsTruck)
        {
            ok = true;
        }

        _cursor.MaterialOverride = ok ? _cursorOk : _cursorBad;
    }

    /// <summary>建筑系统接入点：沙暴期间未加固建筑持续受损。</summary>
    private void OnSandstormBuildingDamage(bool active)
    {
        _buildings?.SetSandstormActive(active);
    }

    internal HexCoord? PlayerCoreCoord => _buildings?.CoreCoord;

    internal void OnCoreDestroyed()
    {
        if (_gameOver)
        {
            return;
        }

        _gameOver = true;
        _sound?.EndSession(false);
        SetSelected(null);
        if (_defeatLabel != null)
        {
            _defeatLabel.Visible = true;
        }

        if (_messageLabel != null)
        {
            _messageLabel.Text = "核心塔被摧毁，本局失败";
            _messageTimer = float.MaxValue;
        }
    }

    internal void OnVictory(string reason)
    {
        if (_gameOver)
        {
            return;
        }

        _gameOver = true;
        _sound?.EndSession(true);
        SetSelected(null);
        if (_victoryLabel != null)
        {
            _victoryLabel.Text = $"胜利\n{reason}";
            _victoryLabel.Visible = true;
        }

        if (_messageLabel != null)
        {
            _messageLabel.Text = $"胜利：{reason}";
            _messageTimer = float.MaxValue;
        }
    }

    internal void OnStorageDestroyed(Building building)
    {
        _resources?.OnStorageDestroyed(building);
    }

    internal void EliminateFactionUnits(FactionId owner)
    {
        for (int i = _formations.Count - 1; i >= 0; i--)
        {
            if (_formations[i].Owner == owner)
            {
                DestroyFormation(_formations[i]);
            }
        }
    }

    internal void OnSampleSurveyed()
    {
        _events?.NotifySampleTaken();
    }

    internal void SetPaused(bool paused)
    {
        _paused = paused;
        ShowMessage(paused ? "重大抉择：时间暂停，请做出决定" : "时间恢复流动");
    }

    private void UpdateMessage(float dt)
    {
        if (_messageTimer <= 0f)
        {
            return;
        }

        _messageTimer -= dt;
        if (_messageTimer <= 0f)
        {
            _toast.Hide();
        }
    }

    internal void ShowMessage(string text)
    {
        if (_messageLabel == null)
        {
            return;
        }

        _messageLabel.Text = text;
        _toast.Show();
        _messageTimer = 3.5f;
    }

    private bool TryGroundPoint(Vector2 screenPosition, out Vector3 world)
    {
        world = Vector3.Zero;
        if (_camera == null)
        {
            return false;
        }

        Vector3 origin = _camera.ProjectRayOrigin(screenPosition);
        Vector3 direction = _camera.ProjectRayNormal(screenPosition);
        if (Mathf.Abs(direction.Y) < 0.0001f)
        {
            return false;
        }

        float t = -origin.Y / direction.Y;
        if (t < 0f)
        {
            return false;
        }

        world = origin + direction * t;
        return true;
    }

    private Formation PickFormation(Vector2 screenPosition, float maxPixels)
    {
        Formation best = null;
        float bestDistance = maxPixels;
        foreach (var formation in _formations)
        {
            if (!formation.IsPlayer)
            {
                continue;
            }

            var anchor = formation.GlobalPosition + new Vector3(0f, formation.IsAir ? 3.2f : 0.5f, 0f);
            float distance = _camera.UnprojectPosition(anchor).DistanceTo(screenPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = formation;
            }
        }

        return best;
    }

    private void DrawPathMarkers(Formation formation)
    {
        ClearPathMarkers();
        foreach (var coord in formation.Path)
        {
            var marker = new MeshInstance3D
            {
                Mesh = _markerMesh,
                MaterialOverride = _markerMaterial,
                Position = _map.ToWorld(coord) + Vector3.Up * (_landscape.SurfaceHeight(coord) + 0.1f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(marker);
            _pathMarkers.Add(marker);
        }

        _markerOwner = _pathMarkers.Count > 0 ? formation : null;
    }

    private void ClearPathMarkers()
    {
        foreach (var marker in _pathMarkers)
        {
            marker.QueueFree();
        }

        _pathMarkers.Clear();
        _markerOwner = null;
    }
}
