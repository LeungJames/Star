using System.Collections.Generic;
using Godot;

namespace Star;

/// <summary>野外遗迹：带科技标签，只能产出对应科技线的样本。</summary>
public partial class RuinSite : Node3D
{
    public TechLine Line { get; set; }
    public HexCoord Coord { get; set; }
    public bool Depleted { get; set; }
    public string DisplayName => $"{TechManager.LineName(Line)}遗迹";
    public float BeamHeight => 1f;

    public override void _Ready()
    {
        string asset = Line switch
        {
            TechLine.Military => "ruin_military",
            TechLine.Survival => "ruin_survival",
            _ => "ruin_civilization",
        };
        var model = ModelArt.Instantiate(asset);
        model.Position = Vector3.Up * 0.14f;
        AddChild(model);
    }
}

/// <summary>科考队取样、护送样本回基地、研究成功/失败与不稳定科技。</summary>
public class ResearchManager
{
    private readonly BattleManager _battle;
    private readonly TechManager _tech;
    private readonly List<RuinSite> _ruins = new();
    private readonly List<Formation> _teams = new();
    private readonly int[] _samples = new int[3];
    private readonly System.Random _rng = new(31173);

    private const float SampleSeconds = GameBalance.SampleSeconds;
    private const float ResearchSeconds = GameBalance.ResearchSeconds;
    private const int MaxVehicles = GameBalance.MaxResearchVehicles;

    private TechLine? _researchingLine;
    private float _researchElapsed;

    public int Scientists { get; private set; } = 4;
    public int Vehicles { get; private set; }
    public IReadOnlyList<RuinSite> Ruins => _ruins;
    public IReadOnlyList<Formation> Teams => _teams;
    public int TeamCount => _teams.Count;
    public TechLine? ResearchingLine => _researchingLine;
    public float ResearchProgress =>
        _researchingLine == null ? 0f : Mathf.Clamp(_researchElapsed / CurrentResearchSeconds, 0f, 1f);

    private float CurrentResearchSeconds => ResearchSeconds * _tech.SampleResearchTimeMultiplier;

    public ResearchManager(BattleManager battle, TechManager tech)
    {
        _battle = battle;
        _tech = tech;
        BuildRuins();
    }

    public int SampleCount(TechLine line) => _samples[(int)line];

    public RuinSite RuinAtPublic(HexCoord coord) => RuinAt(coord);

    public int AvailableScientists => Mathf.Max(0, Scientists - _teams.Count);

    public int AvailableVehicles => Mathf.Max(0, Vehicles - _teams.Count);

    public void AddVehicle()
    {
        Vehicles = Mathf.Min(MaxVehicles, Vehicles + 1);
        _battle.ShowMessage($"科考车下线，当前 {Vehicles} 辆");
    }

    public bool TryDeployTeam()
    {
        if (AvailableScientists <= 0)
        {
            _battle.ShowMessage("没有可用的科学家，无法组建科考队");
            return false;
        }

        if (AvailableVehicles <= 0)
        {
            _battle.ShowMessage("需要先在产线生产科考车");
            return false;
        }

        var core = _battle.PlayerCoreCoord;
        if (!core.HasValue)
        {
            return false;
        }

        var team = Formation.CreateScienceTeam();
        for (int i = 0; i < HexCoord.Directions.Length; i++)
        {
            if (_battle.TrySpawnFormation(team, core.Value.Neighbor(i)))
            {
                _teams.Add(team);
                _battle.ShowMessage("科考队出发：非战斗单位，依赖补给车续航");
                return true;
            }
        }

        _battle.ShowMessage("基地周围没有空位部署科考队");
        return false;
    }

    public bool TryResearchSample(TechLine line)
    {
        if (_researchingLine != null)
        {
            _battle.ShowMessage($"正在研究{TechManager.LineName(_researchingLine.Value)}样本");
            return false;
        }

        if (_samples[(int)line] <= 0)
        {
            _battle.ShowMessage($"没有{TechManager.LineName(line)}样本，需先取样并运回基地");
            return false;
        }

        _samples[(int)line]--;
        _researchingLine = line;
        _researchElapsed = 0f;
        _battle.ShowMessage($"开始研究{TechManager.LineName(line)}样本，存在失败风险");
        return true;
    }

    public void Update(float dt)
    {
        UpdateTeams(dt);
        UpdateResearch(dt);
    }

    public void OnTeamDestroyed(Formation team)
    {
        if (!_teams.Remove(team))
        {
            return;
        }

        Scientists = Mathf.Max(0, Scientists - 1);
        Vehicles = Mathf.Max(0, Vehicles - 1);
        if (team.CarriedSample.HasValue)
        {
            _battle.ShowMessage("科考队被击毁，样本与科学家一并损失");
        }
        else
        {
            _battle.ShowMessage("科考队被击毁，科学家阵亡");
        }
    }

    public string StatusText()
    {
        string research = _researchingLine == null
            ? "研究：空闲"
            : $"研究：{TechManager.LineName(_researchingLine.Value)}样本 {ResearchProgress * 100f:F0}%";
        return $"科考队 {TeamCount}    科学家 {AvailableScientists}/{Scientists}    科考车 {AvailableVehicles}/{Vehicles}\n" +
               $"样本  军工 {_samples[0]}  生存 {_samples[1]}  文明 {_samples[2]}\n{research}";
    }

    private void UpdateTeams(float dt)
    {
        var core = _battle.PlayerCoreCoord;
        foreach (var team in _teams.ToArray())
        {
            if (team.Hp <= 0f)
            {
                continue;
            }

            if (team.CarriedSample.HasValue)
            {
                if (core.HasValue && team.Coord.DistanceTo(core.Value) <= 1)
                {
                    _samples[(int)team.CarriedSample.Value]++;
                    team.CarriedSample = null;
                    _battle.ShowMessage("样本已运回基地，可以投入研究");
                }

                continue;
            }

            if (team.IsMoving)
            {
                continue;
            }

            var ruin = RuinAt(team.Coord);
            if (ruin == null || ruin.Depleted)
            {
                continue;
            }

            team.SampleProgress += dt;
            if (team.SampleProgress < SampleSeconds)
            {
                continue;
            }

            team.CarriedSample = ruin.Line;
            team.SampleProgress = 0f;
            ruin.Depleted = true;
            ruin.Visible = false;
            _battle.ShowMessage($"取样完成：{ruin.DisplayName}样本已装车，需运回基地");

            if (_rng.NextDouble() < 0.5f)
            {
                _battle.OnSampleSurveyed();
            }
        }
    }

    private void UpdateResearch(float dt)
    {
        if (!_researchingLine.HasValue)
        {
            return;
        }

        _researchElapsed += dt * _battle.CaptainResearchMultiplier;
        if (_researchElapsed < CurrentResearchSeconds)
        {
            return;
        }

        var line = _researchingLine.Value;
        _researchingLine = null;
        _researchElapsed = 0f;

        float success = GameBalance.ResearchBaseSuccess + _tech.ResearchSuccessBonus;
        if (_rng.NextDouble() > success)
        {
            _battle.ShowMessage($"{TechManager.LineName(line)}样本研究失败，样本已消耗");
            return;
        }

        _battle.ShowMessage($"{TechManager.LineName(line)}样本研究成功");
        _tech.GrantNext(line);
        var unstable = _tech.RollUnstable();
        if (unstable.HasValue)
        {
            _battle.ShowMessage($"获得不稳定科技：{TechManager.UnstableName(unstable.Value)}（{TechManager.UnstableDescription(unstable.Value)}）");
        }
    }

    private RuinSite RuinAt(HexCoord coord)
    {
        foreach (var ruin in _ruins)
        {
            if (!ruin.Depleted && ruin.Coord.Equals(coord))
            {
                return ruin;
            }
        }

        return null;
    }

    private void BuildRuins()
    {
        AddRuin(TechLine.Military, new HexCoord(3, 2));
        AddRuin(TechLine.Survival, new HexCoord(-4, 3));
        AddRuin(TechLine.Civilization, new HexCoord(2, -4));
    }

    private void AddRuin(TechLine line, HexCoord coord)
    {
        if (!_battle.Map.Contains(coord))
        {
            return;
        }

        _battle.Map.ClearAround(coord, 0);
        var ruin = new RuinSite { Line = line, Coord = coord };
        ruin.Position = _battle.Map.ToWorld(coord);
        _battle.AddChild(ruin);
        _ruins.Add(ruin);
    }
}
