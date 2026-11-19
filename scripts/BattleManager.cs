using System.Collections.Generic;
using Godot;

namespace Star;

/// <summary>原型主控：地图、编队、选择与移动、占格、燃油补给、持续交战与战争迷雾。</summary>
public partial class BattleManager : Node3D
{
    private const int MapRadius = 7;
    private const ulong MapSeed = 20261117UL;
    private const float RefuelRate = 28f;

    private static readonly HexCoord TankSpawn = new(0, 2);
    private static readonly HexCoord TruckSpawn = new(-3, 3);
    private static readonly HexCoord AirSpawn = new(2, -3);
    private static readonly HexCoord InfantrySpawn = new(-1, 0);
    private static readonly HexCoord RepairSpawn = new(-2, 5);
    private static readonly HexCoord EnemyHeavySpawn = new(6, 0);
    private static readonly HexCoord EnemyLightSpawn = new(-5, -2);

    private readonly Dictionary<HexCoord, Formation> _ground = new();
    private readonly Dictionary<HexCoord, Formation> _air = new();
    private readonly List<Formation> _formations = new();
    private readonly List<MeshInstance3D> _pathMarkers = new();
    private readonly Dictionary<Formation, MeshInstance3D> _beams = new();
    private readonly HashSet<HexCoord> _explored = new();
    private readonly HashSet<HexCoord> _visibleToPlayer = new();
    private readonly HashSet<HexCoord> _visibleToEnemy = new();
    private readonly Dictionary<HexCoord, MeshInstance3D> _fogCells = new();
    private readonly Dictionary<HexCoord, MeshInstance3D> _tiles = new();
    private readonly HashSet<Formation> _revealedEnemies = new();

    private HexMap _map;
    private CameraRig _camera;
    private Formation _selected;
    private MeshInstance3D _cursor;
    private StandardMaterial3D _cursorOk;
    private StandardMaterial3D _cursorBad;
    private CylinderMesh _markerMesh;
    private StandardMaterial3D _markerMaterial;
    private BoxMesh _beamMesh;
    private StandardMaterial3D _beamMaterial;
    private StandardMaterial3D _fogUnexplored;
    private StandardMaterial3D _fogDim;
    private CylinderMesh _freeHexMesh;
    private StandardMaterial3D _ruinMaterial;
    private BuildingManager _buildings;
    private ResourceManager _resources;
    private Building _selectedBuilding;
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
    private EnemyCommander _enemyCommander;

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
        PlaceRuins();

        SetupEnvironment();
        _buildings = new BuildingManager(this, _map);
        _buildings.BuildInitialBase();
        BuildMapVisual();
        BuildFog();
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

        SpawnFormations();
        _resources = new ResourceManager(this, _buildings);
        UpdateVisibility();
        _enemyCommander = new EnemyCommander(this);

        ShowMessage("左键选中编队或建筑，右键下达移动命令");
    }

    public override void _Process(double delta)
    {
        if (_gameOver)
        {
            return;
        }

        float dt = (float)delta;
        Clock.Update(dt);
        _enemyCommander.Update(dt);
        UpdateMovement(dt);
        UpdateRefuel(dt);
        _buildings.Update(dt);
        _resources.Update(dt);
        UpdateVisibility();
        UpdateCombat(dt);
        UpdateBeams();
        UpdateCursor();
        UpdateHud();
        UpdateClockHud();
        UpdateBaseStats();
        UpdateMessage(dt);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton button || !button.Pressed)
        {
            return;
        }

        if (button.ButtonIndex == MouseButton.Left)
        {
            SelectAt(button.Position);
        }
        else if (button.ButtonIndex == MouseButton.Right)
        {
            CommandAt(button.Position);
        }
    }

    private void SetupEnvironment()
    {
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.3f, 0.36f, 0.44f),
            SkyHorizonColor = new Color(0.63f, 0.59f, 0.5f),
            GroundHorizonColor = new Color(0.34f, 0.32f, 0.27f),
            GroundBottomColor = new Color(0.09f, 0.09f, 0.08f),
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMaterial },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.85f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52f, -35f, 0f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        AddChild(sun);
    }

    private void BuildMapVisual()
    {
        var freeMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.24f, 0.3f, 0.21f),
            Roughness = 0.95f,
        };
        var ruinMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.37f, 0.31f, 0.22f),
            Roughness = 1f,
        };
        var blockedMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.17f, 0.16f, 0.15f),
            Roughness = 1f,
        };
        var freeMesh = new CylinderMesh
        {
            TopRadius = HexMap.HexSize * 0.97f,
            BottomRadius = HexMap.HexSize * 0.97f,
            Height = 0.2f,
            RadialSegments = 6,
        };
        var blockedMesh = new CylinderMesh
        {
            TopRadius = HexMap.HexSize * 0.97f,
            BottomRadius = HexMap.HexSize * 0.97f,
            Height = 1.1f,
            RadialSegments = 6,
        };

        _freeHexMesh = freeMesh;
        _ruinMaterial = ruinMaterial;

        var root = new Node3D { Name = "Map" };
        AddChild(root);

        foreach (var coord in _map.Cells())
        {
            bool blocked = _map.IsBlocked(coord);
            bool ruins = _map.IsRuins(coord);
            var tile = new MeshInstance3D
            {
                Mesh = blocked ? blockedMesh : freeMesh,
                MaterialOverride = blocked ? blockedMaterial : ruins ? ruinMaterial : freeMaterial,
                Position = _map.ToWorld(coord) + new Vector3(0f, blocked ? 0.55f : 0.1f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 0f),
            };
            root.AddChild(tile);
            _tiles[coord] = tile;
        }
    }

    /// <summary>建筑被摧毁后，把该格切换成可通行、可作掩体的废墟。</summary>
    internal void MakeTileRuins(HexCoord coord)
    {
        if (_freeHexMesh != null && _tiles.TryGetValue(coord, out var tile))
        {
            tile.Mesh = _freeHexMesh;
            tile.MaterialOverride = _ruinMaterial;
            tile.Position = _map.ToWorld(coord) + new Vector3(0f, 0.1f, 0f);
        }

        if (_fogCells.TryGetValue(coord, out var fog))
        {
            fog.Position = _map.ToWorld(coord) + new Vector3(0f, 0.215f, 0f);
        }
    }

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

    private void BuildFog()
    {
        _fogUnexplored = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.015f, 0.02f, 0.025f, 0.93f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _fogDim = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.03f, 0.05f, 0.06f, 0.48f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var mesh = new CylinderMesh
        {
            TopRadius = HexMap.HexSize * 0.99f,
            BottomRadius = HexMap.HexSize * 0.99f,
            Height = 0.02f,
            RadialSegments = 6,
        };

        var root = new Node3D { Name = "Fog" };
        AddChild(root);
        foreach (var coord in _map.Cells())
        {
            bool blocked = _map.IsBlocked(coord);
            var overlay = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = _fogUnexplored,
                Position = _map.ToWorld(coord) + new Vector3(0f, blocked ? 1.12f : 0.215f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(overlay);
            _fogCells[coord] = overlay;
        }
    }

    internal int EffectiveVision(Formation formation)
    {
        int penalty = Clock != null && Clock.IsStorm ? WorldClock.StormVisionPenalty : 0;
        return Mathf.Max(1, formation.VisionRadius - penalty);
    }

    private float WeatherSpeedMultiplier => Clock.IsStorm ? WorldClock.StormSpeedMultiplier : 1f;

    private float WeatherFuelMultiplier => Clock.IsStorm ? WorldClock.StormFuelMultiplier : 1f;

    private void UpdateVisibility()
    {
        _visibleToPlayer.Clear();
        _visibleToEnemy.Clear();
        _fogDim.AlbedoColor = Clock.IsStorm
            ? new Color(0.02f, 0.035f, 0.045f, 0.72f)
            : new Color(0.03f, 0.05f, 0.06f, 0.48f);

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

        foreach (var pair in _fogCells)
        {
            var overlay = pair.Value;
            if (_visibleToPlayer.Contains(pair.Key))
            {
                overlay.Visible = false;
            }
            else if (_explored.Contains(pair.Key))
            {
                overlay.Visible = true;
                overlay.MaterialOverride = _fogDim;
            }
            else
            {
                overlay.Visible = true;
                overlay.MaterialOverride = _fogUnexplored;
            }
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
        _camera = new CameraRig { Current = true };
        AddChild(_camera);
    }

    private void SetupCursor()
    {
        _cursorOk = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 1f, 0.45f, 0.32f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _cursorBad = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.35f, 0.3f, 0.32f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
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
            Mesh = new CylinderMesh
            {
                TopRadius = 1.18f,
                BottomRadius = 1.18f,
                Height = 0.05f,
                RadialSegments = 6,
            },
            MaterialOverride = _cursorOk,
            RotationDegrees = new Vector3(0f, 30f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_cursor);
    }

    private void SetupHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(ui);

        var font = new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "SimHei" },
        };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 15 };

        var hintPanel = MakePanel(theme);
        var hintBox = new VBoxContainer();
        hintBox.AddThemeConstantOverride("separation", 3);
        hintPanel.AddChild(hintBox);
        hintBox.AddChild(MakeLabel("STAR · 实时六角格战斗原型", theme, 20, new Color(0.82f, 0.9f, 0.66f)));
        hintBox.AddChild(MakeLabel("左键：选中编队    右键：移动 / 加油指令", theme));
        hintBox.AddChild(MakeLabel("WASD：平移相机    滚轮缩放    中键拖动平移", theme));
        hintBox.AddChild(MakeLabel("编队进入射程后自动持续开火", theme, 15, new Color(0.95f, 0.78f, 0.55f)));
        hintBox.AddChild(MakeLabel("迷雾：未探索区域被遮蔽，敌人进入视野才会现身", theme, 15, new Color(0.62f, 0.78f, 0.95f)));
        hintBox.AddChild(MakeLabel("沙暴季：视野下降、油耗上升、移动变慢", theme, 15, new Color(0.95f, 0.7f, 0.45f)));
        hintBox.AddChild(MakeLabel("建筑：维修车贴住受损建筑即可就地维修", theme, 15, new Color(0.72f, 0.9f, 0.7f)));
        hintBox.AddChild(MakeLabel("资源：除燃料外自动回仓；仓储满则浪费，被摧毁会掉落库存", theme, 15, new Color(0.85f, 0.82f, 0.6f)));
        ui.AddChild(hintPanel);
        hintPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft, Control.LayoutPresetMode.Minsize, 16);

        var infoPanel = MakePanel(theme);
        _infoLabel = MakeLabel(string.Empty, theme);
        _infoLabel.CustomMinimumSize = new Vector2(360f, 182f);
        infoPanel.AddChild(_infoLabel);
        ui.AddChild(infoPanel);
        infoPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.Minsize, 16);
        infoPanel.GrowVertical = Control.GrowDirection.Begin;

        _messageLabel = MakeLabel(string.Empty, theme, 18, new Color(1f, 0.93f, 0.72f));
        _messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _messageLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _messageLabel.AddThemeConstantOverride("outline_size", 5);
        ui.AddChild(_messageLabel);
        _messageLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide, Control.LayoutPresetMode.Minsize, 16);
        _messageLabel.OffsetTop -= 96f;
        _messageLabel.OffsetBottom -= 96f;

        var clockPanel = MakePanel(theme);
        var clockBox = new VBoxContainer();
        clockBox.AddThemeConstantOverride("separation", 2);
        clockPanel.AddChild(clockBox);
        _dayLabel = MakeLabel("第 1 日", theme, 20, new Color(0.85f, 0.92f, 0.78f));
        _dayLabel.CustomMinimumSize = new Vector2(210f, 0f);
        _stormStateLabel = MakeLabel("距沙暴 00:20", theme, 16, new Color(0.7f, 0.85f, 1f));
        _stormStateLabel.CustomMinimumSize = new Vector2(210f, 0f);
        _stormCycleLabel = MakeLabel("沙暴轮次 0    间隔 25 秒", theme, 13, new Color(0.62f, 0.66f, 0.6f));
        _stormCycleLabel.CustomMinimumSize = new Vector2(210f, 0f);
        clockBox.AddChild(_dayLabel);
        clockBox.AddChild(_stormStateLabel);
        clockBox.AddChild(_stormCycleLabel);
        ui.AddChild(clockPanel);
        clockPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight, Control.LayoutPresetMode.Minsize, 16);
        clockPanel.GrowHorizontal = Control.GrowDirection.Begin;

        var basePanel = MakePanel(theme);
        _baseStatsLabel = MakeLabel(string.Empty, theme, 14, new Color(0.78f, 0.86f, 0.78f));
        _baseStatsLabel.CustomMinimumSize = new Vector2(330f, 0f);
        basePanel.AddChild(_baseStatsLabel);
        ui.AddChild(basePanel);
        basePanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 16);
        basePanel.GrowHorizontal = Control.GrowDirection.Begin;
        basePanel.GrowVertical = Control.GrowDirection.Begin;

        _defeatLabel = MakeLabel("核心塔被摧毁\n方舟计划失败", theme, 44, new Color(1f, 0.36f, 0.3f));
        _defeatLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _defeatLabel.VerticalAlignment = VerticalAlignment.Center;
        _defeatLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _defeatLabel.AddThemeConstantOverride("outline_size", 8);
        _defeatLabel.Visible = false;
        ui.AddChild(_defeatLabel);
        _defeatLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    private static PanelContainer MakePanel(Theme theme)
    {
        var panel = new PanelContainer { Theme = theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.04f, 0.06f, 0.05f, 0.78f) };
        style.SetCornerRadiusAll(6);
        style.ContentMarginLeft = 12f;
        style.ContentMarginRight = 12f;
        style.ContentMarginTop = 8f;
        style.ContentMarginBottom = 8f;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static Label MakeLabel(string text, Theme theme, int fontSize = 15, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            Theme = theme,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        if (color.HasValue)
        {
            label.AddThemeColorOverride("font_color", color.Value);
        }

        return label;
    }

    private void SpawnFormations()
    {
        SpawnFormation(Formation.CreateLightTank(), TankSpawn);
        SpawnFormation(Formation.CreateInfantry(), InfantrySpawn);
        SpawnFormation(Formation.CreateSupplyTruck(), TruckSpawn);
        SpawnFormation(Formation.CreateRepairTruck(), RepairSpawn);
        SpawnFormation(Formation.CreateLightShip(), AirSpawn);
        SpawnFormation(Formation.CreateEnemyHeavyTank(), EnemyHeavySpawn);
        SpawnFormation(Formation.CreateEnemyLightTank(), EnemyLightSpawn);
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

    private Dictionary<HexCoord, Formation> TableOf(Formation formation) => formation.IsAir ? _air : _ground;

    internal IReadOnlyList<Formation> Formations => _formations;
    internal HexMap Map => _map;

    private void SelectAt(Vector2 screenPosition)
    {
        var picked = PickFormation(screenPosition, 48f);
        if (picked == null && TryGroundPoint(screenPosition, out var world))
        {
            var coord = _map.ToCoord(world);
            if (_buildings != null && _buildings.At(coord) is { } building)
            {
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

        SetSelected(picked);
    }

    private void SetSelected(Formation formation)
    {
        _selected = formation;
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
    }

    private void CommandAt(Vector2 screenPosition)
    {
        if (_selected == null)
        {
            ShowMessage("先左键选中一个编队");
            return;
        }

        var picked = PickFormation(screenPosition, 46f);
        if (_selected.IsTruck && picked != null && picked.IsPlayer && !picked.IsTruck && !picked.IsAir)
        {
            OrderRefuel(_selected, picked);
            return;
        }

        if (!TryGroundPoint(screenPosition, out var world))
        {
            return;
        }

        IssueMove(_selected, _map.ToCoord(world));
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

    private void OrderRefuel(Formation truck, Formation target)
    {
        if (target.Fuel >= target.MaxFuel - 0.5f)
        {
            ShowMessage("目标燃油已满");
            return;
        }

        if (truck.Cargo <= 0f)
        {
            ShowMessage("补给车已空");
            return;
        }

        if (truck.Coord.DistanceTo(target.Coord) <= 1)
        {
            truck.RefuelTarget = target;
            ShowMessage("补给车开始加油");
            return;
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
            return;
        }

        if (IssueMove(truck, goal.Value))
        {
            truck.RefuelTarget = target;
            ShowMessage("补给车前往目标加油");
        }
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

            float remaining = formation.Speed * WeatherSpeedMultiplier * dt;
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
                    formation.Fuel - formation.FuelDrainPerSecond * WeatherFuelMultiplier * dt);
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

    private void UpdateCombat(float dt)
    {
        var pendingDamage = new Dictionary<Formation, float>();
        var pendingBuildingDamage = new Dictionary<Building, float>();

        foreach (var attacker in _formations)
        {
            attacker.CurrentTarget = null;
            attacker.CurrentBuildingTarget = null;
            if (!attacker.IsCombatant || attacker.Hp <= 0f)
            {
                continue;
            }

            var target = FindTarget(attacker);
            attacker.CurrentTarget = target;
            if (target != null)
            {
                float penetration = EffectivePenetration(attacker, target);
                float multiplier = PenetrationMultiplier(penetration, target.Armor);
                float dps = attacker.BaseDps * attacker.AttackScale * multiplier;
                pendingDamage[target] = pendingDamage.GetValueOrDefault(target) + dps * dt;
                continue;
            }

            var building = FindBuildingTarget(attacker);
            if (building == null)
            {
                continue;
            }

            attacker.CurrentBuildingTarget = building;
            float buildingMultiplier = PenetrationMultiplier(
                EffectivePenetrationAgainstBuilding(attacker, building.Coord),
                building.Armor);
            pendingBuildingDamage[building] =
                pendingBuildingDamage.GetValueOrDefault(building)
                + attacker.BaseDps * attacker.AttackScale * buildingMultiplier * dt;
        }

        foreach (var pair in pendingDamage)
        {
            var target = pair.Key;
            if (target.Hp <= 0f)
            {
                continue;
            }

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
        var table = TableOf(formation);
        if (table.TryGetValue(formation.Coord, out var owner) && owner == formation)
        {
            table.Remove(formation.Coord);
        }

        ReleaseReservations(formation);
        formation.Path.Clear();
        formation.CurrentTarget = null;
        _formations.Remove(formation);

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
        _cursor.Position = _map.ToWorld(coord) + new Vector3(0f, 0.3f, 0f);

        bool ok = CanEnter(_selected, coord);
        if (!ok && _selected.IsTruck && _ground.TryGetValue(coord, out var ground)
            && ground != _selected && ground.IsPlayer && !ground.IsTruck)
        {
            ok = true;
        }

        _cursor.MaterialOverride = ok ? _cursorOk : _cursorBad;
    }

    private void UpdateHud()
    {
        if (_selectedBuilding != null)
        {
            var building = _selectedBuilding;
            string owner = building.IsPlayer ? "我方" : "敌方";
            string powerLine = building.Kind == BuildingKind.PowerPlant
                ? $"发电: {building.PowerOutput}（受岗位与连通影响）"
                : building.PowerLoad > 0
                    ? $"电力负载: {building.PowerLoad}    供电: {(building.PowerSatisfied ? "正常" : "被限电 ×0.5")}"
                    : "无电力负载";
            string connected = building.Connected ? "已连通" : "断连（效率 ×0.5，持续衰减）";
            string jobs = building.JobSlots > 0
                ? $"岗位: {building.StaffedJobs} / {building.JobSlots}"
                : "岗位: 无需求";
            string storage = _resources != null && _resources.CapacityOf(building) > 0f
                ? $"\n仓储上限: +{_resources.CapacityOf(building):F0} / 种资源（断连时不生效）"
                : string.Empty;
            _infoLabel.Text =
                $"{owner}{building.DisplayName}\n耐久: {building.Hp:F0} / {building.MaxHp:F0}    护甲: {building.Armor:F0}\n" +
                $"{connected}\n{powerLine}\n{jobs}    效率: {building.Efficiency * 100f:F0}%{storage}" +
                $"{(building.UnderSandstorm ? "\n沙暴中：结构持续受损" : string.Empty)}";
            return;
        }

        if (_selected == null)
        {
            _infoLabel.Text = "未选中单位\n左键点击编队或建筑进行选择";
            return;
        }

        var formation = _selected;
        string stormLine = Clock.IsStorm ? "\n沙暴影响：视野 -2 格，移速 ×0.6，油耗 ×1.8" : string.Empty;
        if (formation.IsRepairTruck)
        {
            string task = formation.RepairTargetBuilding == null
                ? formation.IsMoving
                    ? "前往目标"
                    : "待命（贴近受损建筑自动作业）"
                : $"维修中 → {formation.RepairTargetBuilding.DisplayName}";
            _infoLabel.Text =
                $"维修车\n耐久: {formation.Hp:F0} / {formation.MaxHp:F0}\n状态: {task}{stormLine}\n" +
                "维修车无武装，贴近受损建筑即可持续修复";
            return;
        }

        if (formation.IsTruck)
        {
            string task = formation.RefuelTarget == null
                ? "待命"
                : formation.IsMoving
                    ? $"前往 {formation.RefuelTarget.DisplayName}"
                    : $"为 {formation.RefuelTarget.DisplayName} 加油中";
            _infoLabel.Text =
                $"补给车\n载油: {formation.Cargo:F0} / {formation.MaxCargo:F0}\n状态: {task}{stormLine}\n选中补给车后右键友军地面编队可前往加油";
            return;
        }

        string fuelState = formation.Fuel <= 0f ? "（耗尽）" : string.Empty;
        string combat;
        if (formation.IsCombatant)
        {
            string mitigation = string.Empty;
            if (formation.CurrentTarget != null)
            {
                float multiplier = PenetrationMultiplier(
                    EffectivePenetration(formation, formation.CurrentTarget),
                    formation.CurrentTarget.Armor);
                mitigation = $"\n目标装甲: {formation.CurrentTarget.Armor:F0}    穿甲修正: {multiplier * 100f:F0}%";
            }

            float effectivePen = formation.Penetration;
            string penNotes = string.Empty;
            if (formation.CurrentTarget != null
                && formation.Coord.DistanceTo(formation.CurrentTarget.Coord) <= 1
                && formation.CloseRangePenMultiplier > 1f)
            {
                effectivePen *= formation.CloseRangePenMultiplier;
                penNotes += $"贴身 ×{formation.CloseRangePenMultiplier:0.#}";
            }

            if (_map.IsRuins(formation.Coord) && formation.RuinsPenMultiplier > 1f)
            {
                effectivePen *= formation.RuinsPenMultiplier;
                penNotes += penNotes.Length > 0
                    ? $"，废墟 ×{formation.RuinsPenMultiplier:0.#}"
                    : $"废墟 ×{formation.RuinsPenMultiplier:0.#}";
            }

            string penLine = penNotes.Length > 0 ? $"\n穿甲加成: 有效 {effectivePen:F0}（{penNotes}）" : string.Empty;

            combat =
                $"存活: {formation.AliveUnits}/{formation.Units}    耐久: {formation.Hp:F0} / {formation.MaxHp:F0}\n" +
                $"当前输出: {CurrentDps(formation):F1} / 秒{mitigation}\n" +
                $"护甲: {formation.Armor:F0}    穿甲: {formation.Penetration:F0}    射程: {formation.Range} 格    视野: {EffectiveVision(formation)} 格" +
                $"{penLine}\n";
        }
        else
        {
            combat = $"耐久: {formation.Hp:F0} / {formation.MaxHp:F0}    视野: {EffectiveVision(formation)} 格\n";
        }

        string restriction = formation.IsAir
            ? "空中编队：忽略地面阻挡与地面编队占格"
            : "地面编队：受地形与编队占格限制";
        string status = formation.CurrentTarget != null
            ? $"交战中 → {formation.CurrentTarget.DisplayName}"
            : formation.CurrentBuildingTarget != null
                ? $"攻击建筑 → {formation.CurrentBuildingTarget.DisplayName}"
                : formation.IsMoving
                    ? "移动中"
                    : "待命";
        _infoLabel.Text =
            $"{formation.DisplayName}\n燃油: {formation.Fuel:F0} / {formation.MaxFuel:F0}{fuelState}\n" +
            $"{combat}状态: {status}\n{restriction}{stormLine}";
    }

    private void UpdateClockHud()
    {
        _dayLabel.Text = $"第 {Clock.Day} 日";
        if (Clock.IsStorm)
        {
            _stormStateLabel.Text = $"沙暴中 · 剩余 {WorldClock.FormatSeconds(Clock.StormRemaining)}";
            _stormStateLabel.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.28f));
        }
        else if (Clock.IsWarning)
        {
            _stormStateLabel.Text = $"沙暴预警 · {WorldClock.FormatSeconds(Clock.TimeToNextStorm)}";
            _stormStateLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.35f));
        }
        else
        {
            _stormStateLabel.Text = $"距沙暴 {WorldClock.FormatSeconds(Clock.TimeToNextStorm)}";
            _stormStateLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        }

        _stormCycleLabel.Text = $"沙暴轮次 {Clock.StormCount}    间隔 {Clock.GapSeconds:F0} 秒";
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

    private void UpdateBaseStats()
    {
        if (_baseStatsLabel == null || _buildings == null)
        {
            return;
        }

        string power = _buildings.Core == null
            ? "核心：已损毁"
            : $"电力 {_buildings.PowerSupplied} / {_buildings.PowerDemand}（产能 {_buildings.PowerOutput}）";
        string resources = _resources == null
            ? string.Empty
            : $"\n废铁 {_resources.Get(ResourceKind.Scrap):F0}  燃料 {_resources.Get(ResourceKind.Fuel):F0}  " +
              $"食物 {_resources.Get(ResourceKind.Food):F0}\n" +
              $"药品 {_resources.Get(ResourceKind.Medicine):F0}  知识数据 {_resources.Get(ResourceKind.Data):F0}  " +
              $"上限 {_resources.Capacity:F0}";
        _baseStatsLabel.Text =
            $"基地状态\n{power}\n限电建筑 {_buildings.ShedCount} 座    断连 {_buildings.DisconnectedCount} 座\n" +
            $"人口 {_buildings.Population}    岗位 {_buildings.AssignedJobs} / {_buildings.TotalJobs}\n" +
            $"建筑 {_buildings.Buildings.Count} 座{resources}";
    }

    internal void OnStorageDestroyed(Building building)
    {
        _resources?.OnStorageDestroyed(building);
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
            _messageLabel.Text = string.Empty;
        }
    }

    internal void ShowMessage(string text)
    {
        if (_messageLabel == null)
        {
            return;
        }

        _messageLabel.Text = text;
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
                Position = _map.ToWorld(coord) + new Vector3(0f, 0.32f, 0f),
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
