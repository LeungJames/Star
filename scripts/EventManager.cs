using System;
using System.Collections.Generic;
using Godot;
using static Star.CommandTheme;

namespace Star;

public class GameEvent
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool Major { get; init; }
    public bool Repeatable { get; init; }
    public float RepeatSeconds { get; init; } = 60f;
    public float Cooldown { get; set; }
    public Func<bool> CanTrigger { get; init; } = () => false;
    public List<(string Label, Action Apply)> Options { get; } = new();
    public bool Triggered { get; set; }
}

/// <summary>事件：重大抉择暂停并弹出面板，日常事件在角落提示不打断操作。</summary>
public class EventManager
{
    private readonly BattleManager _battle;
    private readonly FactionManager _factions;
    private readonly ResourceManager _resources;
    private readonly BuildingManager _buildings;
    private readonly NestManager _nests;
    private readonly List<GameEvent> _events = new();

    private Control _majorPanel;
    private Label _majorTitle;
    private Label _majorText;
    private readonly Button[] _majorButtons = new Button[3];
    private PanelContainer _minorPanel;
    private Label _minorTitle;
    private Label _minorText;
    private readonly Button[] _minorButtons = new Button[3];

    private GameEvent _activeMajor;
    private GameEvent _activeMinor;
    private float _minorTimer;
    private float _checkTimer;
    private int _lastStormWarning = -1;
    private bool _pendingRuinEvent;

    public EventManager(BattleManager battle, FactionManager factions, ResourceManager resources,
        BuildingManager buildings, NestManager nests)
    {
        _battle = battle;
        _factions = factions;
        _resources = resources;
        _buildings = buildings;
        _nests = nests;
        BuildEvents();
    }

    public void BuildUi(Control root, Theme theme)
    {
        var modal = new ColorRect { Color = new Color("0b1217c9"), ZIndex = 4 };
        root.AddChild(modal);
        modal.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _majorPanel = modal;
        var panel = Panel(theme, Alert);
        modal.AddChild(panel);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        panel.AddChild(scroll);
        var majorBox = Column(10);
        majorBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(majorBox);
        majorBox.AddChild(Label("重大抉择 · 时间暂停", theme, 13, Alert));
        _majorTitle = Label(string.Empty, theme, 22, Ink, wrap: true);
        majorBox.AddChild(_majorTitle);
        _majorText = Label(string.Empty, theme, 16, Muted, wrap: true);
        majorBox.AddChild(_majorText);
        AddChoiceButtons(majorBox, _majorButtons, ResolveMajor);
        _majorPanel.Hide();

        _minorPanel = Panel(theme, Brass);
        _minorPanel.ZIndex = 2;
        var minorBox = Column(5);
        _minorPanel.AddChild(minorBox);
        _minorTitle = Label(string.Empty, theme, 15, Brass, wrap: true);
        minorBox.AddChild(_minorTitle);
        _minorText = Label(string.Empty, theme, 13, Muted, wrap: true);
        minorBox.AddChild(_minorText);
        var minorChoices = Flow();
        minorBox.AddChild(minorChoices);
        AddChoiceButtons(minorChoices, _minorButtons, ResolveMinor);

        root.AddChild(_minorPanel);
        _minorPanel.Hide();

        void Layout()
        {
            float availableWidth = Mathf.Max(0f, root.Size.X - 24f);
            float width = Mathf.Min(530f, availableWidth);
            float height = Mathf.Min(420f, Mathf.Max(0f, root.Size.Y - 24f));
            Anchor(panel, Control.LayoutPreset.Center, -width * 0.5f, -height * 0.5f, width * 0.5f, height * 0.5f);

            float minorWidth = Mathf.Min(410f, availableWidth);
            float minorHeight = _minorPanel.GetCombinedMinimumSize().Y;
            Anchor(_minorPanel, Control.LayoutPreset.BottomRight, -12f - minorWidth, -82f - minorHeight, -12f, -82f);
        }

        root.Resized += Layout;
        _minorPanel.MinimumSizeChanged += Layout;
        Layout();
    }

    private static void AddChoiceButtons(Container parent, Button[] buttons, Action<int> resolve)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var button = Button(string.Empty);
            button.Pressed += () => resolve(index);
            buttons[i] = button;
            parent.AddChild(button);
        }
    }

    public void NotifySampleTaken()
    {
        _pendingRuinEvent = true;
    }

    public void Update(float dt)
    {
        foreach (var gameEvent in _events)
        {
            if (gameEvent.Cooldown > 0f)
            {
                gameEvent.Cooldown = Mathf.Max(0f, gameEvent.Cooldown - dt);
            }
        }

        if (_activeMajor != null)
        {
            return;
        }

        if (_activeMinor != null)
        {
            _minorTimer -= dt;
            _minorTitle.Text = $"{_activeMinor.Title}（{Mathf.Max(0, Mathf.CeilToInt(_minorTimer))} 秒后默认处理）";
            if (_minorTimer <= 0f)
            {
                ResolveMinor(0);
            }

            return;
        }

        _checkTimer += dt;
        if (_checkTimer < GameBalance.EventCheckInterval)
        {
            return;
        }

        _checkTimer = 0f;
        foreach (var gameEvent in _events)
        {
            if ((!gameEvent.Repeatable && gameEvent.Triggered) || gameEvent.Cooldown > 0f
                || !gameEvent.CanTrigger())
            {
                continue;
            }

            Show(gameEvent);
            break;
        }
    }

    private void Show(GameEvent gameEvent)
    {
        if (gameEvent.Id != "storm")
        {
            _battle.PlayAudioCue(gameEvent.Major ? WastelandAudio.Cue.Decision : WastelandAudio.Cue.Notice);
        }

        if (gameEvent.Major)
        {
            _activeMajor = gameEvent;
            _majorTitle.Text = gameEvent.Title;
            _majorText.Text = gameEvent.Text;
            for (int i = 0; i < _majorButtons.Length; i++)
            {
                bool active = i < gameEvent.Options.Count;
                _majorButtons[i].Visible = active;
                _majorButtons[i].Text = active ? gameEvent.Options[i].Label : string.Empty;
            }

            _majorPanel.Visible = true;
            _battle.SetPaused(true);
            return;
        }

        _activeMinor = gameEvent;
        _minorTimer = GameBalance.MinorEventTimeoutSeconds;
        _minorTitle.Text = $"{gameEvent.Title}（{GameBalance.MinorEventTimeoutSeconds:F0} 秒后默认处理）";
        _minorText.Text = gameEvent.Text;
        for (int i = 0; i < _minorButtons.Length; i++)
        {
            bool active = i < gameEvent.Options.Count;
            _minorButtons[i].Visible = active;
            _minorButtons[i].Text = active ? gameEvent.Options[i].Label : string.Empty;
        }

        _minorPanel.Visible = true;
    }

    private void ResolveMajor(int index)
    {
        if (_activeMajor == null)
        {
            return;
        }

        var gameEvent = _activeMajor;
        _activeMajor = null;
        _majorPanel.Visible = false;
        _battle.SetPaused(false);
        Apply(gameEvent, index);
    }

    private void ResolveMinor(int index)
    {
        if (_activeMinor == null)
        {
            return;
        }

        var gameEvent = _activeMinor;
        _activeMinor = null;
        _minorPanel.Visible = false;
        Apply(gameEvent, index);
    }

    private void Apply(GameEvent gameEvent, int index)
    {
        if (gameEvent.Repeatable)
        {
            gameEvent.Cooldown = gameEvent.RepeatSeconds;
        }
        else
        {
            gameEvent.Triggered = true;
        }

        if (gameEvent.Id == "storm") _lastStormWarning = _battle.Clock.StormCount;
        if (gameEvent.Id == "ruin") _pendingRuinEvent = false;
        if (gameEvent.Options.Count == 0)
        {
            return;
        }

        index = Mathf.Clamp(index, 0, gameEvent.Options.Count - 1);
        gameEvent.Options[index].Apply();
        _battle.ShowMessage($"事件：{gameEvent.Title} → {gameEvent.Options[index].Label}");
    }

    private void BuildEvents()
    {
        Add(new GameEvent
        {
            Id = "demand",
            Title = "军政府索要物资",
            Text = "军政府的使者带着一队装甲车停在防线外：交出物资，或者准备好打一仗。",
            Major = true,
            CanTrigger = () => _battle.Clock.Day >= GameBalance.EventDemandDay,
        }, options: new[]
        {
            ("缴纳废铁 60、燃料 20", (Action)(() =>
            {
                Consume(ResourceKind.Scrap, GameBalance.DemandScrapCost);
                Consume(ResourceKind.Fuel, GameBalance.DemandFuelCost);
                _factions.ApplyRelation(FactionId.Junta, GameBalance.PeaceRelationGain);
            })),
            ("拒绝", (Action)(() =>
            {
                _factions.ApplyRelation(FactionId.Junta, -25);
                _buildings.ChangeMorale(-3, "军政府威胁让居民不安");
            })),
            ("讨价还价", (Action)(() =>
            {
                Consume(ResourceKind.Scrap, GameBalance.DemandBargainScrapCost);
                _factions.ApplyRelation(FactionId.Junta, -5);
                _buildings.ChangeMorale(-2, "被军政府记恨");
            })),
        });

        Add(new GameEvent
        {
            Id = "food",
            Title = "粮仓见底",
            Text = "配给员报告，仓库里的食物撑不了几天了。",
            Major = true,
            Repeatable = true,
            RepeatSeconds = GameBalance.EventFoodCooldown,
            CanTrigger = () => _resources.Get(ResourceKind.Food) < GameBalance.FoodLowThreshold,
        }, options: new[]
        {
            ("削减配给", (Action)(() =>
            {
                _buildings.ChangeMorale(-12, "削减配给，士气低落");
            })),
            ("向商队采购", (Action)(() =>
            {
                Consume(ResourceKind.Scrap, GameBalance.FoodPurchaseScrapCost);
                _resources.Add(ResourceKind.Food, GameBalance.FoodPurchaseGain);
                _factions.ApplyRelation(FactionId.Caravan, GameBalance.TradeCultRelationGain);
            })),
            ("动用药品换粮", (Action)(() =>
            {
                Consume(ResourceKind.Medicine, GameBalance.FoodMedicineCost);
                _resources.Add(ResourceKind.Food, GameBalance.FoodMedicineGain);
                _buildings.ChangeMorale(3, "勉强填饱肚子");
            })),
        });

        Add(new GameEvent
        {
            Id = "cryopod",
            Title = "失联冷冻舱信号",
            Text = "侦察队捕捉到方舟号冷冻舱的微弱信标，救回来的是人手，也是更多要吃饭的嘴。",
            Major = true,
            CanTrigger = () => _battle.Clock.Day >= GameBalance.EventCryopodDay,
        }, options: new[]
        {
            ("派队解救", (Action)(() =>
            {
                _buildings.AddPopulation(8);
                Consume(ResourceKind.Food, 15f);
                _buildings.ChangeMorale(8, "救回同伴，士气振奋");
            })),
            ("只记录坐标", (Action)(() =>
            {
                _resources.Add(ResourceKind.Data, 15f);
                _buildings.ChangeMorale(-3, "没能救回同伴");
            })),
            ("放弃", (Action)(() =>
            {
                _buildings.ChangeMorale(-5, "放弃同伴让人心寒");
            })),
        });

        Add(new GameEvent
        {
            Id = "refugees",
            Title = "流浪者求收留",
            Text = "一群带着病容的流浪者请求进入基地。收留他们是人手，也是负担。",
            Major = true,
            CanTrigger = () => _battle.Clock.Day >= GameBalance.EventRefugeeDay,
        }, options: new[]
        {
            ("全部收留", (Action)(() =>
            {
                _buildings.AddPopulation(5);
                Consume(ResourceKind.Medicine, 8f);
                _buildings.ChangeMorale(6, "收留难民，基地更有人气");
                _factions.ApplyRelation(FactionId.Cult, 5);
            })),
            ("只收孩子", (Action)(() =>
            {
                _buildings.AddPopulation(2);
                Consume(ResourceKind.Food, 10f);
                _buildings.ChangeMorale(3, "至少救下了孩子");
            })),
            ("拒绝", (Action)(() =>
            {
                _buildings.ChangeMorale(-6, "关闭大门，士气受挫");
                _factions.ApplyRelation(FactionId.Cult, -4);
            })),
        });

        Add(new GameEvent
        {
            Id = "mutiny",
            Title = "士气濒临崩溃",
            Text = "有人开始公开抱怨分配不公，再压下去就要出乱子。",
            Major = true,
            CanTrigger = () => _buildings.Morale < 30,
        }, options: new[]
        {
            ("发放补给安抚", (Action)(() =>
            {
                Consume(ResourceKind.Food, 20f);
                _buildings.ChangeMorale(12, "补给到位，情绪缓和");
            })),
            ("严惩带头者", (Action)(() =>
            {
                _buildings.LosePopulation(1);
                _buildings.ChangeMorale(-5, "高压手段让人噤声");
            })),
            ("削减工时", (Action)(() =>
            {
                Consume(ResourceKind.Scrap, 30f);
                _buildings.ChangeMorale(8, "减少工时，士气回升");
            })),
        });

        Add(new GameEvent
        {
            Id = "storm",
            Title = "沙暴前夜",
            Major = false,
            Text = "预警响起：沙暴将在短时间内抵达。",
            Repeatable = true,
            RepeatSeconds = GameBalance.EventStormCooldown,
            CanTrigger = () => _battle.Clock.IsWarning && _battle.Clock.StormCount > _lastStormWarning,
        }, options: new[]
        {
            ("加固建筑（废铁 40）", (Action)(() =>
            {
                Consume(ResourceKind.Scrap, 40f);
                _buildings.ChangeMorale(4, "提前加固，居民安心");
            })),
            ("提前收工避风（燃料 10）", (Action)(() =>
            {
                Consume(ResourceKind.Fuel, GameBalance.ShelterFuelCost);
                _buildings.ChangeMorale(6, "全员避风，士气稳定");
            })),
            ("照常运转", (Action)(() =>
            {
                _buildings.ChangeMorale(-3, "有人在沙暴中受伤");
            })),
        });

        Add(new GameEvent
        {
            Id = "caravan",
            Title = "商队路过",
            Major = false,
            Text = "一支商队在防区外停下，表示愿意做点买卖。",
            Repeatable = true,
            RepeatSeconds = GameBalance.EventCaravanCooldown,
            CanTrigger = () => _battle.Clock.Day >= GameBalance.EventCaravanDay,
        }, options: new[]
        {
            ("交易（废铁 30）", (Action)(() =>
            {
                Consume(ResourceKind.Scrap, GameBalance.CaravanEventScrapCost);
                _resources.Add(ResourceKind.Data, GameBalance.CaravanEventDataGain);
                _factions.ApplyRelation(FactionId.Caravan, GameBalance.TradeJuntaRelationGain);
            })),
            ("驱离", (Action)(() =>
            {
                _factions.ApplyRelation(FactionId.Caravan, -6);
                _buildings.ChangeMorale(-2, "赶走商队，居民不满");
            })),
        });

        Add(new GameEvent
        {
            Id = "cult",
            Title = "教团传教",
            Major = false,
            Text = "教团的传教士在围墙外宣讲旧文明的启示。",
            Repeatable = true,
            RepeatSeconds = GameBalance.EventCultCooldown,
            CanTrigger = () => _battle.Clock.Day >= GameBalance.EventCultDay,
        }, options: new[]
        {
            ("接受", (Action)(() =>
            {
                _resources.Add(ResourceKind.Data, 10f);
                _buildings.ChangeMorale(-3, "部分居民被教义吸引");
                _factions.ApplyRelation(FactionId.Cult, 8);
            })),
            ("拒绝", (Action)(() =>
            {
                _factions.ApplyRelation(FactionId.Cult, -6);
                _buildings.ChangeMorale(2, "守住自己的规矩");
            })),
        });

        Add(new GameEvent
        {
            Id = "overpop",
            Title = "人口超员",
            Major = false,
            Text = "床位不够了，走廊里都挤满了人。",
            Repeatable = true,
            RepeatSeconds = GameBalance.EventOverpopCooldown,
            CanTrigger = () => _buildings.Population > _buildings.HousingCapacity,
        }, options: new[]
        {
            ("搭简易棚（废铁 50）", (Action)(() =>
            {
                Consume(ResourceKind.Scrap, 50f);
                _buildings.AddHousing(4);
            })),
            ("轮流休息", (Action)(() =>
            {
                _buildings.ChangeMorale(-8, "拥挤让人疲惫");
            })),
        });

        Add(new GameEvent
        {
            Id = "ruin",
            Title = "遗迹异动",
            Major = false,
            Text = "取样设备的震动传得很远，附近的巢穴似乎被惊动了。",
            Repeatable = true,
            RepeatSeconds = 0f,
            CanTrigger = () => _pendingRuinEvent,
        }, options: new[]
        {
            ("深入调查", (Action)(() =>
            {
                _resources.Add(ResourceKind.Data, 10f);
                _nests.TriggerAggro();
                _factions.ForceScout(FactionId.Cult);
            })),
            ("立刻撤离", (Action)(() =>
            {
                _buildings.ChangeMorale(-2, "仓促撤离");
            })),
        });
    }

    private void Add(GameEvent gameEvent, (string Label, Action Apply)[] options)
    {
        foreach (var option in options)
        {
            gameEvent.Options.Add(option);
        }

        _events.Add(gameEvent);
    }

    private void Consume(ResourceKind kind, float amount)
    {
        _resources.Consume(kind, amount);
    }
}
