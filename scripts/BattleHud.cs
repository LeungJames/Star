using System.Collections.Generic;
using System.Text;
using Godot;
using static Star.CommandTheme;

namespace Star;

public partial class BattleManager
{
    private const float HudMargin = 12f;
    private const string CommandInstruction = "点击地图目的地，或点击待补给的地面编队";
    private readonly Label[] _resourceAmounts = new Label[5];
    private readonly List<(PanelContainer Panel, float Width, float Height)> _hudMenus = new();
    private PanelContainer _hudHeader;
    private VBoxContainer _headerColumn;
    private HBoxContainer _headerRow;
    private HBoxContainer _resourceRow;
    private VBoxContainer _clockBlock;
    private Label _scienceResourceName;
    private PanelContainer _ordersBar;
    private PanelContainer _inspector;
    private Label _inspectorHeading;
    private ScrollContainer _inspectorScroll;
    private PanelContainer _toast;
    private Button _issueOrder;
    private Button _productionShortcut;
    private Button _replenishShortcut;
    private Button _baseShortcut;
    private Control _hudMenuLayer;
    private PanelContainer _openHudMenu;
    private PanelContainer _basePanel;
    private PanelContainer _helpPanel;
    private bool _commandArmed;

    private void SetupHud()
    {
        var layer = new CanvasLayer { Name = "CommandHud" };
        AddChild(layer);
        _hudTheme = CommandTheme.Create();
        _uiRoot = new Control { Theme = _hudTheme, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(_uiRoot);
        _uiRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        BuildStatusStrip();
        BuildInspector();
        BuildOrdersBar();

        _toast = Panel(_hudTheme);
        _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        _toast.ZIndex = 3;
        _messageLabel = Label(string.Empty, _hudTheme, 14, Brass, wrap: true);
        _messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.AddChild(_messageLabel);
        _uiRoot.AddChild(_toast);
        _toast.Visible = false;
        _messageLabel.MinimumSizeChanged += LayoutHud;

        _hudMenuLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiRoot.AddChild(_hudMenuLayer);
        _hudMenuLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color("0b12178c") };
        _hudMenuLayer.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        shade.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                CloseHudMenu();
            }
        };
        _hudMenuLayer.Hide();

        BuildProductionPanel();
        BuildBasePanel();
        BuildHelpPanel();
        _defeatLabel = BuildEnding("核心塔被摧毁\n火种计划失败", Alert);
        _victoryLabel = BuildEnding(string.Empty, Cool);
        _uiRoot.Resized += LayoutHud;
        _hudHeader.MinimumSizeChanged += LayoutHud;
        LayoutHud();
    }

    private void BuildStatusStrip()
    {
        _hudHeader = Panel(_hudTheme, Brass);
        _hudHeader.Name = "StatusStrip";
        _uiRoot.AddChild(_hudHeader);
        _headerColumn = Column(7);
        _hudHeader.AddChild(_headerColumn);
        _headerRow = Row(13);
        _headerColumn.AddChild(_headerRow);

        var identity = Column(1);
        identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _headerRow.AddChild(identity);
        identity.AddChild(Label("方舟号", _hudTheme, 23));
        identity.AddChild(Label("灰烬 · 殖民指挥", _hudTheme, 12, Brass));

        _resourceRow = Row(5);
        _resourceRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _headerRow.AddChild(_resourceRow);
        AddResource(ResourceKind.Scrap, new Color("e8ba92"));
        AddResource(ResourceKind.Fuel, new Color("e8c174"));
        AddResource(ResourceKind.Food, new Color("bdce9a"));
        AddResource(ResourceKind.Medicine, new Color("e9a29c"));
        AddResource(ResourceKind.Data, Cool);

        _clockBlock = Column(1);
        _clockBlock.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _clockBlock.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _headerRow.AddChild(_clockBlock);
        _dayLabel = Label("第 1 日", _hudTheme, 13);
        _stormStateLabel = Label(string.Empty, _hudTheme, 16, Cool);
        _stormCycleLabel = Label(string.Empty, _hudTheme, 11, Muted);
        _clockBlock.AddChild(_dayLabel);
        _clockBlock.AddChild(_stormStateLabel);
        _clockBlock.AddChild(_stormCycleLabel);
    }

    private void AddResource(ResourceKind resource, Color accent)
    {
        var cell = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = ResourceManager.NameOf(resource),
        };
        cell.AddThemeStyleboxOverride("panel", Surface(new Color("252f33"), Edge, 5, 4));
        _resourceRow.AddChild(cell);
        var row = Row(7);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        cell.AddChild(row);
        var symbol = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(16, 18),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        symbol.Draw += () => DrawResourceIcon(symbol, resource, accent);
        row.AddChild(symbol);

        var copy = Column(0);
        copy.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        row.AddChild(copy);
        var name = Label(ResourceManager.NameOf(resource), _hudTheme, 11, Muted);
        copy.AddChild(name);
        if (resource == ResourceKind.Data)
        {
            _scienceResourceName = name;
        }
        var amount = Label("0", _hudTheme, 19, accent);
        copy.AddChild(amount);
        _resourceAmounts[(int)resource] = amount;
    }

    private static void DrawResourceIcon(Control icon, ResourceKind resource, Color tint)
    {
        switch (resource)
        {
            case ResourceKind.Scrap:
                icon.DrawColoredPolygon(new Vector2[] { new(2, 8), new(7, 4), new(14, 6), new(15, 11), new(10, 15), new(3, 13) }, tint);
                icon.DrawLine(new Vector2(3, 8), new Vector2(10, 10), new Color("4a3730"), 1.4f, true);
                icon.DrawLine(new Vector2(10, 10), new Vector2(14, 7), new Color("4a3730"), 1.4f, true);
                break;
            case ResourceKind.Fuel:
                icon.DrawColoredPolygon(new Vector2[] { new(8, 2), new(13, 9), new(13, 12), new(11, 15), new(5, 15), new(3, 12), new(3, 9) }, tint);
                break;
            case ResourceKind.Food:
                icon.DrawLine(new Vector2(8, 16), new Vector2(8, 3), tint, 1.8f, true);
                icon.DrawLine(new Vector2(8, 10), new Vector2(3, 6), tint, 2.1f, true);
                icon.DrawLine(new Vector2(8, 12), new Vector2(13, 8), tint, 2.1f, true);
                icon.DrawLine(new Vector2(8, 7), new Vector2(12, 4), tint, 1.8f, true);
                break;
            case ResourceKind.Medicine:
                icon.DrawRect(new Rect2(6, 3, 4, 12), tint);
                icon.DrawRect(new Rect2(2, 7, 12, 4), tint);
                break;
            case ResourceKind.Data:
                icon.DrawRect(new Rect2(3, 4, 10, 10), tint, false, 1.5f, true);
                icon.DrawRect(new Rect2(6, 7, 4, 4), tint);
                icon.DrawLine(new Vector2(6, 1), new Vector2(6, 4), tint);
                icon.DrawLine(new Vector2(10, 14), new Vector2(10, 17), tint);
                break;
        }
    }

    private void BuildInspector()
    {
        _inspector = Panel(_hudTheme, Cool);
        _uiRoot.AddChild(_inspector);
        var body = Column(7);
        _inspector.AddChild(body);
        _inspectorHeading = Label(string.Empty, _hudTheme, 18, Cool, wrap: true);
        body.AddChild(_inspectorHeading);
        _inspectorScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 150),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        body.AddChild(_inspectorScroll);
        _infoLabel = Label(string.Empty, _hudTheme, 14, wrap: true);
        _infoLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _inspectorScroll.AddChild(_infoLabel);
        _infoLabel.MinimumSizeChanged += LayoutHud;
        var actions = Row(5);
        body.AddChild(actions);
        _replenishShortcut = AddAction(actions, "补员", "贴近补给车或返回产线补员（R）", () => TryStartReplenish(_selected));
        _productionShortcut = AddAction(actions, "编队生产", "查看可生产的编队", () => ToggleHudMenu(_productionPanel));
        _inspector.Hide();
        _inspector.MinimumSizeChanged += LayoutHud;
    }

    private void BuildOrdersBar()
    {
        _ordersBar = Panel(_hudTheme);
        _ordersBar.Name = "OrdersBar";
        _uiRoot.AddChild(_ordersBar);
        var row = Row(4);
        _ordersBar.AddChild(row);
        _issueOrder = AddAction(row, "指令", "选中编队后点击这里，再点击地图目标", ArmCommand);
        AddAction(row, "科研", "科技与逆向工程（T）", () => ToggleHudMenu(_techPanel));
        AddAction(row, "外交", "查看势力关系（G）", () => ToggleHudMenu(_diplomacyPanel));
        AddAction(row, "队长", "分配队长技能（C）", () => ToggleHudMenu(_captainPanel));
        _baseShortcut = AddAction(row, "基地", "基地运转与任务目标", () => ToggleHudMenu(_basePanel));
        AddAction(row, "帮助", "查看地图操作与快捷键", () => ToggleHudMenu(_helpPanel));
    }

    private static Button AddAction(Container parent, string text, string tip, System.Action pressed)
    {
        var button = Button(text, tip);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private void ArmCommand()
    {
        if (_paused || _gameOver || _selected == null)
        {
            return;
        }
        if (_commandArmed)
        {
            CancelHudCommand();
        }
        else
        {
            _commandArmed = true;
            ShowMessage(CommandInstruction);
        }
        UpdateHudActions();
    }

    private void CancelHudCommand()
    {
        if (!_commandArmed)
        {
            return;
        }
        _commandArmed = false;
        if (_messageLabel.Text == CommandInstruction)
        {
            _toast.Hide();
            _messageTimer = 0f;
        }
    }

    private void BuildBasePanel()
    {
        var box = CreateHudMenu(out _basePanel, "基地状态", Cool, 480f, 420f);
        _baseStatsLabel = Label(string.Empty, _hudTheme, 16, wrap: true);
        box.AddChild(_baseStatsLabel);
        var actions = Flow();
        box.AddChild(actions);
        AddAction(actions, "修复方舟", "投入资源与工程能力修复方舟号（V）", () => _victory.TryStartArk());
        AddAction(actions, "建设信标", "依靠通信阵列与电力建立救援信标（B）", () => _victory.TryStartBeacon());
    }

    private void BuildHelpPanel()
    {
        var box = CreateHudMenu(out _helpPanel, "地图操作", Brass, 480f, 375f);
        box.AddChild(Label("选择与移动", _hudTheme, 15, Cool));
        box.AddChild(Label("点击地图上的编队或建筑查看详情。右键下达移动指令，也可点击「指令」后再选地图目标。补给车可点击友方地面编队加油。", _hudTheme, 15, wrap: true));
        box.AddChild(Label("镜头", _hudTheme, 15, Cool));
        box.AddChild(Label("方向键或 WASD 平移，滚轮缩放，按住鼠标中键拖动。", _hudTheme, 15, wrap: true));
        box.AddChild(Label("快捷键", _hudTheme, 15, Cool));
        box.AddChild(Label("T 科研　G 外交　C 队长　R 补员\nV 修复方舟　B 建设信标　F1 调试　Esc 返回", _hudTheme, 15, wrap: true));
    }

    private Label BuildEnding(string text, Color accent)
    {
        var curtain = new ColorRect { Color = new Color("11191bcc"), ZIndex = 8 };
        _uiRoot.AddChild(curtain);
        curtain.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        curtain.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = Panel(_hudTheme, accent);
        center.AddChild(panel);
        var label = Label(text, _hudTheme, 28, accent);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(label);
        curtain.Hide();
        label.Hide();
        label.VisibilityChanged += () => curtain.Visible = label.Visible;
        return label;
    }

    private VBoxContainer CreateHudMenu(out PanelContainer panel, string title, Color accent, float width, float height)
    {
        panel = Panel(_hudTheme, accent);
        var column = Column(10);
        panel.AddChild(column);
        var header = Row(8);
        column.AddChild(header);
        var heading = Label(title, _hudTheme, 22, accent, wrap: true);
        heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(heading);
        var close = Button("返回", "Esc");
        close.Pressed += CloseHudMenu;
        header.AddChild(close);
        var divider = new ColorRect { Color = Edge, CustomMinimumSize = new Vector2(0, 1) };
        column.AddChild(divider);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);
        var body = Column(9);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(body);
        panel.Hide();
        _hudMenuLayer.AddChild(panel);
        _hudMenus.Add((panel, width, height));
        LayoutHud();
        return body;
    }

    private void ToggleHudMenu(PanelContainer panel)
    {
        if (panel == null || _paused || _gameOver)
        {
            return;
        }
        if (_openHudMenu == panel)
        {
            CloseHudMenu();
            return;
        }
        _openHudMenu?.Hide();
        _openHudMenu = panel;
        _hudMenuLayer.Show();
        panel.Show();
        PlayAudioCue(WastelandAudio.Cue.Ui);
        LayoutHud();
    }

    private void CloseHudMenu()
    {
        _openHudMenu?.Hide();
        _openHudMenu = null;
        _hudMenuLayer.Hide();
        LayoutHud();
    }

    private void LayoutHud()
    {
        if (_hudMenuLayer == null || _uiRoot.Size.X <= 0f)
        {
            return;
        }

        float width = _uiRoot.Size.X;
        float height = _uiRoot.Size.Y;
        bool compact = width < 720f;
        if (compact && _resourceRow.GetParent() == _headerRow)
        {
            _resourceRow.Reparent(_headerColumn, false);
            _clockBlock.SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.ShrinkEnd;
            _scienceResourceName.Text = "数据";
        }
        else if (!compact && _resourceRow.GetParent() == _headerColumn)
        {
            _resourceRow.Reparent(_headerRow, false);
            _headerRow.MoveChild(_resourceRow, 1);
            _clockBlock.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            _scienceResourceName.Text = "知识数据";
        }

        float headerHeight = _hudHeader.GetCombinedMinimumSize().Y;
        Anchor(_hudHeader, Control.LayoutPreset.TopWide, HudMargin, HudMargin, -HudMargin, HudMargin + headerHeight);
        float footerHeight = _ordersBar.GetCombinedMinimumSize().Y;
        float footerTop = footerHeight + HudMargin * 2f;
        float ordersWidth = Mathf.Min(660f, width - HudMargin * 2f);
        Anchor(_ordersBar, Control.LayoutPreset.CenterBottom,
            -ordersWidth * 0.5f, -footerHeight - HudMargin, ordersWidth * 0.5f, -HudMargin);
        float infoWidth = Mathf.Min(342f, width - HudMargin * 2f);
        float availableInfoHeight = Mathf.Clamp(height - headerHeight - footerTop - 110f, 72f, 168f);
        _inspectorScroll.CustomMinimumSize = new Vector2(0, Mathf.Clamp(_infoLabel.GetCombinedMinimumSize().Y + 2f, 40f, availableInfoHeight));
        float inspectorHeight = _inspector.GetCombinedMinimumSize().Y;
        Anchor(_inspector, Control.LayoutPreset.BottomLeft, HudMargin, -footerTop - inspectorHeight, HudMargin + infoWidth, -footerTop);

        float toastWidth = Mathf.Min(540f, width - HudMargin * 2f);
        float toastTop = headerHeight + HudMargin * 2f;
        float toastHeight = _toast.GetCombinedMinimumSize().Y;
        if (_openHudMenu != null)
        {
            Anchor(_toast, Control.LayoutPreset.CenterBottom,
                -toastWidth * 0.5f, -HudMargin - toastHeight, toastWidth * 0.5f, -HudMargin);
        }
        else
        {
            Anchor(_toast, Control.LayoutPreset.CenterTop, -toastWidth * 0.5f, toastTop, toastWidth * 0.5f,
                toastTop + toastHeight);
        }

        foreach (var (panel, maxWidth, maxHeight) in _hudMenus)
        {
            float panelWidth = Mathf.Min(maxWidth, width - HudMargin * 2f);
            float panelHeight = Mathf.Min(maxHeight, Mathf.Max(98f, height - HudMargin * 2f - 42f));
            Anchor(panel, Control.LayoutPreset.Center, -panelWidth * 0.5f, -panelHeight * 0.5f - 16f,
                panelWidth * 0.5f, panelHeight * 0.5f - 16f);
        }
    }

    private void UpdateHudActions()
    {
        _inspector.Visible = _selected != null || _selectedBuilding != null;
        _inspectorHeading.Text = _selectedBuilding != null
            ? $"{(_selectedBuilding.IsPlayer ? "我方" : "敌方")} · {_selectedBuilding.DisplayName}"
            : _selected?.DisplayName ?? string.Empty;
        _productionShortcut.Visible = _uiBuilding != null;
        _replenishShortcut.Visible = _selected?.CanReplenish == true;
        _issueOrder.Disabled = _selected == null;
        _issueOrder.Text = _commandArmed ? "取消" : "指令";
        _issueOrder.Modulate = _commandArmed ? Brass : Colors.White;
    }

    private void UpdateHudResources()
    {
        for (int i = 0; i < _resourceAmounts.Length; i++)
        {
            _resourceAmounts[i].Text = _resources.Get((ResourceKind)i).ToString("F0");
        }
        _baseShortcut.TooltipText =
            $"人口 {_buildings.Population}/{_buildings.HousingCapacity} · 供电 {_buildings.PowerSupplied}/{_buildings.PowerDemand}";
    }
    private void BuildDiplomacyPanel()
    {
        if (_uiRoot == null || _factions == null)
        {
            return;
        }

        var box = CreateHudMenu(out _diplomacyPanel, "外交与势力", Brass, 560f, 500f);

        foreach (var state in _factions.States)
        {
            var card = Panel(_hudTheme);
            box.AddChild(card);
            var content = Column(6);
            card.AddChild(content);
            var label = Label(string.Empty, _hudTheme, 15, Cool, wrap: true);
            content.AddChild(label);
            _factionLabels[state.Id] = label;

            var row = Flow();
            content.AddChild(row);
            var id = state.Id;
            AddAction(row, "送礼", "", () => { _factions.TryGift(id); RefreshDiplomacyPanel(); });
            AddAction(row, "交易", "", () => { _factions.TryTrade(id); RefreshDiplomacyPanel(); });
            AddAction(row, "和谈", "", () => { _factions.TryNegotiatePeace(id); RefreshDiplomacyPanel(); });
            AddAction(row, "让出前哨", "", () => { _factions.TryCedeOutpost(id); RefreshDiplomacyPanel(); });
        }

        _diplomacyStatusLabel = Label(string.Empty, _hudTheme, 14, Muted, wrap: true);
        box.AddChild(_diplomacyStatusLabel);

        RefreshDiplomacyPanel();
    }

    private void RefreshDiplomacyPanel()
    {
        if (_diplomacyPanel == null || _factions == null)
        {
            return;
        }

        foreach (var pair in _factionLabels)
        {
            var state = _factions.Get(pair.Key);
            if (state == null)
            {
                continue;
            }

            pair.Value.Text = $"{state.Name}  关系 {state.Relation}  {state.StateName}  战力 {state.Strength}";
        }

        _diplomacyStatusLabel.Text = _factions.StatusText();
    }

    private void BuildCaptainPanel()
    {
        if (_uiRoot == null || _production == null)
        {
            return;
        }

        var box = CreateHudMenu(out _captainPanel, "队长成长", Cool, 670f, 510f);

        foreach (var captain in _production.Captains)
        {
            var card = Panel(_hudTheme);
            box.AddChild(card);
            var block = Column(6);
            card.AddChild(block);

            var header = Label(string.Empty, _hudTheme, 15, Cool, wrap: true);
            block.AddChild(header);

            var row = Flow();
            block.AddChild(row);

            var skillDefs = captain.SkillLine;
            var buttons = new Button[skillDefs.Count];
            for (int i = 0; i < skillDefs.Count; i++)
            {
                var def = skillDefs[i];
                var button = new Button
                {
                    Theme = _hudTheme,
                    CustomMinimumSize = new Vector2(0f, 40f),
                    FocusMode = Control.FocusModeEnum.None,
                    TooltipText = def.Description,
                };
                button.Pressed += () =>
                {
                    if (captain.Learn(def.Skill))
                    {
                        ShowMessage($"{captain.Name} 学会 {def.Name}：{def.Description}");
                    }

                    RefreshCaptainPanel();
                };
                row.AddChild(button);
                buttons[i] = button;
            }

            _captainRows.Add((captain, header, buttons));
        }

        RefreshCaptainPanel();
    }

    private void RefreshCaptainPanel()
    {
        if (_captainPanel == null || _production == null)
        {
            return;
        }

        foreach (var row in _captainRows)
        {
            var captain = row.Captain;
            string assignment = "待命";
            if (!captain.IsAlive)
            {
                assignment = "已阵亡";
            }
            else
            {
                foreach (var formation in _formations)
                {
                    if (formation.Captain == captain && formation.Hp > 0f)
                    {
                        assignment = $"编队 {formation.DisplayName}";
                        break;
                    }
                }
            }

            row.Header.Text =
                $"{captain.Name} · {captain.OriginName}（擅长{captain.Specialty}）  {captain.LevelText}" +
                $"  技能点 {captain.SkillPoints}  {assignment}";

            var defs = captain.SkillLine;
            for (int i = 0; i < row.Buttons.Length && i < defs.Count; i++)
            {
                var def = defs[i];
                var button = row.Buttons[i];
                bool learned = captain.Has(def.Skill);
                button.Text = learned ? $"✓ {def.Name}" : $"{def.Name}（Lv{def.RequiredLevel}）";
                button.Disabled = learned || !captain.CanLearn(def.Skill);
                button.Modulate = learned ? new Color(0.7f, 0.95f, 0.7f) : Colors.White;
            }
        }
    }

    private void BuildDebugPanel()
    {
        if (_uiRoot == null)
        {
            return;
        }

        var box = CreateHudMenu(out _debugPanel, "关键数值", Muted, 640f, 560f);
        _debugLabel = Label(string.Empty, _hudTheme, 13, Muted, wrap: true);
        box.AddChild(_debugLabel);

        RefreshDebugPanel();
    }

    private void RefreshDebugPanel()
    {
        if (_debugLabel == null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(GameBalance.Summary());
        builder.AppendLine("【实时状态】");
        builder.AppendLine($"第 {Clock.Day} 日  {(Clock.IsStorm ? "沙暴中" : Clock.IsWarning ? "沙暴预警" : "正常")}" +
                           $"  沙暴轮次 {Clock.StormCount}");
        builder.AppendLine($"士气 {_buildings.Morale}  人口 {_buildings.Population}/{_buildings.HousingCapacity}" +
                           $"  岗位 {_buildings.AssignedJobs}/{_buildings.TotalJobs}");
        builder.AppendLine($"资源 铁 {_resources.Get(ResourceKind.Scrap):F0}  油 {_resources.Get(ResourceKind.Fuel):F0}" +
                           $"  食 {_resources.Get(ResourceKind.Food):F0}  药 {_resources.Get(ResourceKind.Medicine):F0}" +
                           $"  数据 {_resources.Get(ResourceKind.Data):F0}");
        foreach (var state in _factions.States)
        {
            builder.AppendLine($"{state.Name} 关系 {state.Relation}  {state.StateName}  战力 {state.Strength}");
        }

        foreach (var captain in _production.Captains)
        {
            string status = !captain.IsAlive ? "阵亡" : captain.InUse ? "在编" : "待命";
            builder.AppendLine($"队长 {captain.Name} Lv.{captain.Level}" +
                               $"{(captain.MaxLevel ? string.Empty : $" XP {captain.Xp:F0}/{captain.XpToNext:F0}")}" +
                               $"  技能点 {captain.SkillPoints}  {status}");
        }

        _debugLabel.Text = builder.ToString().TrimEnd();
    }

    private void BuildTechPanel()
    {
        if (_uiRoot == null || _tech == null)
        {
            return;
        }

        var box = CreateHudMenu(out _techPanel, "科技与逆向工程", Cool, 700f, 530f);
        _techStatusLabel = Label(string.Empty, _hudTheme, 14, Muted, wrap: true);
        box.AddChild(_techStatusLabel);

        TechLine[] lines = { TechLine.Military, TechLine.Survival, TechLine.Civilization };
        foreach (var line in lines)
        {
            var lineColor = line switch
            {
                TechLine.Military => Alert,
                TechLine.Survival => new Color("bdce9a"),
                _ => Cool,
            };
            box.AddChild(Label(TechManager.LineName(line) + "线", _hudTheme, 15, lineColor));
            var row = Flow();
            box.AddChild(row);
            foreach (var node in _tech.Nodes)
            {
                if (node.Line != line)
                {
                    continue;
                }

                var button = new Button
                {
                    Theme = _hudTheme,
                    CustomMinimumSize = new Vector2(91f, 48f),
                    FocusMode = Control.FocusModeEnum.None,
                    TooltipText = node.Description,
                };
                var target = node;
                button.Pressed += () => { _tech.TryStart(target); RefreshTechPanel(); };
                row.AddChild(button);
                _techButtons[node] = button;
            }
        }

        box.AddChild(Label("遗迹样本", _hudTheme, 15, Brass));
        var actionRow = Flow();
        box.AddChild(actionRow);

        var deploy = new Button
        {
            Text = "部署科考队",
            Theme = _hudTheme,
            CustomMinimumSize = new Vector2(0f, 40f),
            FocusMode = Control.FocusModeEnum.None,
        };
        deploy.Pressed += () => { _research.TryDeployTeam(); RefreshTechPanel(); };
        actionRow.AddChild(deploy);

        foreach (var line in lines)
        {
            var target = line;
            var button = new Button
            {
                Text = $"研究{TechManager.LineName(line)}样本",
                Theme = _hudTheme,
                CustomMinimumSize = new Vector2(0f, 40f),
                FocusMode = Control.FocusModeEnum.None,
            };
            button.Pressed += () => { _research.TryResearchSample(target); RefreshTechPanel(); };
            actionRow.AddChild(button);
        }

        RefreshTechPanel();
    }

    private void RefreshTechPanel()
    {
        if (_techPanel == null || _tech == null)
        {
            return;
        }

        foreach (var pair in _techButtons)
        {
            var node = pair.Key;
            var button = pair.Value;
            float cost = _tech.Cost(node);
            button.Text = node.Unlocked ? $"✓{node.Name}" : $"{node.Name}\n{cost:F0} 数据";
            button.Disabled = node.Unlocked || (_tech.Researching != null && _tech.Researching != node);
            button.Modulate = node.Unlocked ? new Color(0.65f, 0.95f, 0.65f) : Colors.White;
        }

        var unstable = new System.Text.StringBuilder();
        foreach (var kind in _tech.Unstable)
        {
            unstable.Append($"{TechManager.UnstableName(kind)} ");
        }

        string progress = _tech.Researching == null
            ? "科技研究：空闲"
            : $"科技研究：{_tech.Researching.Name} {_tech.ResearchProgress * 100f:F0}%";
        _techStatusLabel.Text =
            $"{progress}\n{_research.StatusText()}\n不稳定科技：{(unstable.Length > 0 ? unstable.ToString() : "无")}";
    }

    private void BuildProductionPanel()
    {
        var theme = _hudTheme;
        var box = CreateHudMenu(out _productionPanel, "编队生产", Brass, 570f, 485f);
        _productionTitle = Label("生产", theme, 18, Cool);
        box.AddChild(_productionTitle);

        box.AddChild(Label("型号", theme, 13, Muted));
        var modelRow = Flow();
        box.AddChild(modelRow);
        var modelGroup = new ButtonGroup();
        for (int i = 0; i < _modelButtons.Length; i++)
        {
            var button = MakeToggleButton("—", theme);
            button.ButtonGroup = modelGroup;
            int index = i;
            button.Pressed += () => OnModelSelected(index);
            _modelButtons[i] = button;
            modelRow.AddChild(button);
        }

        box.AddChild(Label("人数", theme, 13, Muted));
        var sizeRow = Flow();
        box.AddChild(sizeRow);
        var sizeGroup = new ButtonGroup();
        for (int i = 0; i < _sizeButtons.Length; i++)
        {
            int units = i + 3;
            var button = MakeToggleButton($"{units}", theme);
            button.ButtonGroup = sizeGroup;
            button.Pressed += () => { _orderUnits = units; RefreshProductionPanel(); };
            _sizeButtons[i] = button;
            sizeRow.AddChild(button);
        }

        box.AddChild(Label("队长", theme, 13, Muted));
        var captainRow = Flow();
        box.AddChild(captainRow);
        var captainGroup = new ButtonGroup();
        for (int i = 0; i < _captainButtons.Length; i++)
        {
            int index = i;
            var button = MakeToggleButton(i == 0 ? "无队长" : "队长", theme);
            button.ButtonGroup = captainGroup;
            button.Pressed += () => { _orderCaptain = index; RefreshProductionPanel(); };
            _captainButtons[i] = button;
            captainRow.AddChild(button);
        }

        _productionStatus = Label(string.Empty, theme, 13, Cool, wrap: true);
        _productionStatus.CustomMinimumSize = new Vector2(0f, 34f);
        box.AddChild(_productionStatus);

        var orderButton = new Button
        {
            Text = "下单生产",
            Theme = theme,
            CustomMinimumSize = new Vector2(0f, 40f),
            FocusMode = Control.FocusModeEnum.None,
        };
        orderButton.Pressed += OrderSelected;
        box.AddChild(orderButton);
    }

    private static Button MakeToggleButton(string text, Theme theme)
    {
        return new Button
        {
            Text = text,
            Theme = theme,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(56f, 40f),
            FocusMode = Control.FocusModeEnum.None,
        };
    }

    private void OnModelSelected(int index)
    {
        var models = _uiBuilding != null && _production != null
            ? _production.ModelsFor(_uiBuilding.Kind)
            : System.Array.Empty<FormationKind>();
        if (index < models.Length)
        {
            _orderKind = models[index];
        }

        RefreshProductionPanel();
    }

    private void OrderSelected()
    {
        if (_uiBuilding == null || _production == null)
        {
            return;
        }

        var captains = _production.Captains;
        Captain captain = _orderCaptain > 0 && _orderCaptain <= captains.Count
            ? captains[_orderCaptain - 1]
            : null;
        _production.Order(_uiBuilding, _orderKind, _orderUnits, captain);
        RefreshProductionPanel();
    }

    private void ConfigureProductionPanel(Building building)
    {
        _uiBuilding = building;
        var models = _production.ModelsFor(building.Kind);
        _orderKind = models.Length > 0 ? models[0] : default;
        _orderUnits = 3;
        _orderCaptain = 0;
        RefreshProductionPanel();
    }

    private void RefreshProductionPanel()
    {
        if (_productionPanel == null)
        {
            return;
        }

        if (_uiBuilding == null || _production == null || !_production.IsSupported(_uiBuilding))
        {
            if (_openHudMenu == _productionPanel)
            {
                CloseHudMenu();
            }
            return;
        }

        _productionTitle.Text = _uiBuilding.DisplayName;

        var models = _production.ModelsFor(_uiBuilding.Kind);
        for (int i = 0; i < _modelButtons.Length; i++)
        {
            bool active = i < models.Length;
            _modelButtons[i].Visible = active;
            _modelButtons[i].Disabled = !active;
            if (active)
            {
                _modelButtons[i].Text = ProductionManager.ModelName(models[i]);
                _modelButtons[i].ButtonPressed = models[i] == _orderKind;
            }
        }

        for (int i = 0; i < _sizeButtons.Length; i++)
        {
            _sizeButtons[i].ButtonPressed = i + 3 == _orderUnits;
        }

        var captains = _production.Captains;
        for (int i = 0; i < _captainButtons.Length; i++)
        {
            if (i == 0)
            {
                _captainButtons[i].Text = "无队长";
                _captainButtons[i].Disabled = false;
            }
            else if (i - 1 < captains.Count)
            {
                var captain = captains[i - 1];
                _captainButtons[i].Text = $"{captain.Name}·{captain.OriginName}";
                _captainButtons[i].Disabled = !captain.IsAlive || captain.InUse;
            }
            else
            {
                _captainButtons[i].Disabled = true;
            }

            _captainButtons[i].ButtonPressed = i == _orderCaptain;
        }

        _productionStatus.Text =
            _production.StatusOf(_uiBuilding) +
            $"\n废铁 {_resources.Get(ResourceKind.Scrap):F0}    合成编队 {CountPlayerCombatFormations()} / {ProductionManager.MaxFormations}";
    }

    private int CountPlayerCombatFormations()
    {
        int count = 0;
        foreach (var formation in _formations)
        {
            if (formation.IsPlayer && formation.Hp > 0f && formation.IsCombatFormation)
            {
                count++;
            }
        }

        return count;
    }

    private void UpdateHud()
    {
        UpdateHudActions();
        if (_uiBuilding != null && _productionPanel.Visible)
        {
            RefreshProductionPanel();
        }

        if (_selectedBuilding != null)
        {
            var building = _selectedBuilding;
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
                $"耐久: {building.Hp:F0} / {building.MaxHp:F0}    护甲: {building.Armor:F0}\n" +
                $"{connected}\n{powerLine}\n{jobs}    效率: {building.Efficiency * 100f:F0}%{storage}" +
                $"{(building.UnderSandstorm ? "\n沙暴中：结构持续受损" : string.Empty)}";
            return;
        }

        if (_selected == null)
        {
            return;
        }

        var formation = _selected;
        string stormLine = Clock.IsStorm ? "\n沙暴影响：视野 -2 格，移速 ×0.6，油耗 ×1.8" : string.Empty;
        if (formation.IsScienceTeam)
        {
            string task;
            if (formation.CarriedSample.HasValue)
            {
                task = $"携带{TechManager.LineName(formation.CarriedSample.Value)}样本，返回核心塔卸货";
            }
            else
            {
                var ruin = _research.RuinAtPublic(formation.Coord);
                task = ruin != null && !ruin.Depleted
                    ? $"正在取样 {formation.SampleProgress / 6f * 100f:F0}%"
                    : "前往带科技标签的遗迹驻留取样";
            }

            _infoLabel.Text =
                $"耐久: {formation.Hp:F0} / {formation.MaxHp:F0}    燃油: {formation.Fuel:F0} / {formation.MaxFuel:F0}\n" +
                $"状态: {task}\n科考队不占 8 编队名额，依赖补给车续航，被击毁会损失科学家与样本";
            return;
        }

        if (formation.IsRepairTruck)
        {
            string task = formation.RepairTargetBuilding == null
                ? formation.IsMoving
                    ? "前往目标"
                    : "待命（贴近受损建筑自动作业）"
                : $"维修中 → {formation.RepairTargetBuilding.DisplayName}";
            _infoLabel.Text =
                $"耐久: {formation.Hp:F0} / {formation.MaxHp:F0}\n状态: {task}{stormLine}\n" +
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
                $"载油: {formation.Cargo:F0} / {formation.MaxCargo:F0}\n状态: {task}{stormLine}\n向友方地面编队下达指令即可前往加油";
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
                : formation.CurrentNestTarget != null
                    ? $"清剿巢穴 → {formation.CurrentNestTarget.DisplayName}"
                    : formation.IsMoving
                        ? "移动中"
                        : "待命";
        string captainLine = formation.Captain == null
            ? string.Empty
            : $"\n队长: {formation.Captain.Name}（{formation.Captain.OriginName}·擅长{formation.Captain.Specialty}）" +
              $" {formation.Captain.LevelText}    伤害 ×{formation.CaptainDamageMultiplier:0.###}" +
              (formation.Garrisoned ? "  [驻防]" : string.Empty);
        string replenishLine = formation.CanReplenish
            ? "\n按 R 补员：贴近补给车较慢，回兵营 / 坦克厂 / 机库更快"
            : string.Empty;
        _infoLabel.Text =
            $"燃油: {formation.Fuel:F0} / {formation.MaxFuel:F0}{fuelState}\n" +
            $"{combat}状态: {status}\n{restriction}{stormLine}{captainLine}{replenishLine}";
    }

    private void UpdateClockHud()
    {
        _dayLabel.Text = $"第 {Clock.Day} 日";
        if (Clock.IsStorm)
        {
            _stormStateLabel.Text = $"沙暴中 · 剩余 {WorldClock.FormatSeconds(Clock.StormRemaining)}";
            _stormStateLabel.AddThemeColorOverride("font_color", Alert);
        }
        else if (Clock.IsWarning)
        {
            _stormStateLabel.Text = $"沙暴预警 · {WorldClock.FormatSeconds(Clock.TimeToNextStorm)}";
            _stormStateLabel.AddThemeColorOverride("font_color", Brass);
        }
        else
        {
            _stormStateLabel.Text = $"距沙暴 {WorldClock.FormatSeconds(Clock.TimeToNextStorm)}";
            _stormStateLabel.AddThemeColorOverride("font_color", Cool);
        }

        _stormCycleLabel.Text = $"沙暴轮次 {Clock.StormCount}    间隔 {Clock.GapSeconds:F0} 秒";
    }

    private void UpdateBaseStats()
    {
        if (_baseStatsLabel == null || _buildings == null)
        {
            return;
        }

        UpdateHudResources();
        if (!_basePanel.Visible)
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
            $"{power}\n限电建筑 {_buildings.ShedCount} 座    断连 {_buildings.DisconnectedCount} 座\n" +
            $"人口 {_buildings.Population}    岗位 {_buildings.AssignedJobs} / {_buildings.TotalJobs}\n" +
            $"士气 {_buildings.Morale}    床位 {_buildings.HousingCapacity}\n" +
            $"建筑 {_buildings.Buildings.Count} 座{resources}\n{_victory?.StatusText()}";
    }

}
