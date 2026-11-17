using System.Collections.Generic;
using Godot;

namespace Star;

/// <summary>原型主控：生成地图与编队，处理选择、移动指令、占格、燃油与补给。</summary>
public partial class BattleManager : Node3D
{
    private const int MapRadius = 7;
    private const ulong MapSeed = 20261117UL;
    private const float RefuelRate = 28f;

    private static readonly HexCoord TankSpawn = new(0, 2);
    private static readonly HexCoord TruckSpawn = new(-3, 3);
    private static readonly HexCoord AirSpawn = new(2, -3);

    private readonly Dictionary<HexCoord, Formation> _ground = new();
    private readonly Dictionary<HexCoord, Formation> _air = new();
    private readonly List<Formation> _formations = new();
    private readonly List<MeshInstance3D> _pathMarkers = new();

    private HexMap _map;
    private CameraRig _camera;
    private Formation _selected;
    private MeshInstance3D _cursor;
    private StandardMaterial3D _cursorOk;
    private StandardMaterial3D _cursorBad;
    private CylinderMesh _markerMesh;
    private StandardMaterial3D _markerMaterial;
    private Formation _markerOwner;
    private Label _infoLabel;
    private Label _messageLabel;
    private float _messageTimer;

    public override void _Ready()
    {
        _map = new HexMap(MapRadius, MapSeed);
        _map.ClearAround(TankSpawn, 2);
        _map.ClearAround(TruckSpawn, 2);
        _map.ClearAround(AirSpawn, 2);

        SetupEnvironment();
        BuildMapVisual();
        SetupCamera();
        SetupCursor();
        SetupHud();
        SpawnFormations();

        ShowMessage("左键选中编队，右键下达移动命令");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateMovement(dt);
        UpdateRefuel(dt);
        UpdateCursor();
        UpdateHud();
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

        var root = new Node3D { Name = "Map" };
        AddChild(root);

        foreach (var coord in _map.Cells())
        {
            bool blocked = _map.IsBlocked(coord);
            var tile = new MeshInstance3D
            {
                Mesh = blocked ? blockedMesh : freeMesh,
                MaterialOverride = blocked ? blockedMaterial : freeMaterial,
                Position = _map.ToWorld(coord) + new Vector3(0f, blocked ? 0.55f : 0.1f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 0f),
            };
            root.AddChild(tile);
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
        ui.AddChild(hintPanel);
        hintPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft, Control.LayoutPresetMode.Minsize, 16);

        var infoPanel = MakePanel(theme);
        _infoLabel = MakeLabel(string.Empty, theme);
        _infoLabel.CustomMinimumSize = new Vector2(340f, 112f);
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
        SpawnFormation(Formation.CreateSupplyTruck(), TruckSpawn);
        SpawnFormation(Formation.CreateLightShip(), AirSpawn);
    }

    private void SpawnFormation(Formation formation, HexCoord coord)
    {
        formation.Coord = coord;
        formation.Position = _map.ToWorld(coord);
        AddChild(formation);
        _formations.Add(formation);
        TableOf(formation)[coord] = formation;
    }

    private Dictionary<HexCoord, Formation> TableOf(Formation formation) => formation.IsAir ? _air : _ground;

    private void SelectAt(Vector2 screenPosition)
    {
        var picked = PickFormation(screenPosition, 48f);
        if (picked == null && TryGroundPoint(screenPosition, out var world))
        {
            var coord = _map.ToCoord(world);
            if (_ground.TryGetValue(coord, out var ground))
            {
                picked = ground;
            }
            else if (_air.TryGetValue(coord, out var air))
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
    }

    private void CommandAt(Vector2 screenPosition)
    {
        if (_selected == null)
        {
            ShowMessage("先左键选中一个编队");
            return;
        }

        var picked = PickFormation(screenPosition, 46f);
        if (_selected.IsTruck && picked != null && !picked.IsTruck && !picked.IsAir)
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

    private bool IssueMove(Formation formation, HexCoord goal)
    {
        if (!_map.Contains(goal))
        {
            ShowMessage("目标超出地图范围");
            return false;
        }

        if (!formation.IsAir && _map.IsBlocked(goal))
        {
            ShowMessage("目标格是阻挡地形，无法进入");
            return false;
        }

        var table = TableOf(formation);
        if (table.TryGetValue(goal, out var occupant) && occupant != formation)
        {
            ShowMessage("目标格已被占用，请绕行");
            return false;
        }

        if (goal.Equals(formation.Coord))
        {
            ShowMessage("编队已在该格");
            return false;
        }

        if (formation.Fuel <= 0f)
        {
            ShowMessage("燃油耗尽，无法移动");
            return false;
        }

        var path = Pathfinder.FindPath(_map, coord => IsBlockedFor(formation, coord), formation.Coord, goal);
        if (path == null)
        {
            ShowMessage("找不到可通行路线");
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

        DrawPathMarkers(formation);
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

        var table = TableOf(formation);
        return table.TryGetValue(coord, out var owner) && owner != formation;
    }

    private bool CanEnter(Formation formation, HexCoord coord)
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

            float remaining = formation.Speed * dt;
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
                formation.Fuel = Mathf.Max(0f, formation.Fuel - formation.FuelDrainPerSecond * dt);
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
        if (!ok && _selected.IsTruck && _ground.TryGetValue(coord, out var ground) && ground != _selected && !ground.IsTruck)
        {
            ok = true;
        }

        _cursor.MaterialOverride = ok ? _cursorOk : _cursorBad;
    }

    private void UpdateHud()
    {
        if (_selected == null)
        {
            _infoLabel.Text = "未选中编队\n左键点击编队进行选择";
            return;
        }

        var formation = _selected;
        if (formation.IsTruck)
        {
            string task = formation.RefuelTarget == null
                ? "待命"
                : formation.IsMoving
                    ? $"前往 {formation.RefuelTarget.DisplayName}"
                    : $"为 {formation.RefuelTarget.DisplayName} 加油中";
            _infoLabel.Text =
                $"补给车\n载油: {formation.Cargo:F0} / {formation.MaxCargo:F0}\n状态: {task}\n选中补给车后右键友军地面编队可前往加油";
            return;
        }

        string fuelState = formation.Fuel <= 0f ? "（耗尽）" : string.Empty;
        string restriction = formation.IsAir
            ? "空中编队：忽略地面阻挡与地面编队占格"
            : "地面编队：受地形与编队占格限制";
        _infoLabel.Text =
            $"{formation.DisplayName}\n燃油: {formation.Fuel:F0} / {formation.MaxFuel:F0}{fuelState}\n" +
            $"状态: {(formation.IsMoving ? "移动中" : "待命")}\n{restriction}";
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

    private void ShowMessage(string text)
    {
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
