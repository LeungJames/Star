using System.Collections.Generic;
using System.Linq;

namespace Star;

public enum CaptainOrigin
{
    Soldier,
    Scientist,
    Worker,
    Civilian,
}

/// <summary>按出身分支的队长技能，效果落在已有系统上。</summary>
public enum CaptainSkill
{
    FrontlineReplenish,
    GarrisonArmor,
    BattleVision,
    SensorArray,
    BallisticCalibration,
    ResearchAssist,
    FieldRepair,
    FuelEfficiency,
    ProductionAssist,
    LocalRecruit,
    CivilMorale,
    Rationing,
}

public class CaptainSkillDef
{
    public CaptainSkill Skill { get; init; }
    public CaptainOrigin Origin { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int RequiredLevel { get; init; }
    public float Magnitude { get; init; }
}

/// <summary>
/// 挂在编队上的队长：按出身走技能线，随经验升级并获得技能点；
/// 对口编队获得完整加成，不对口则减半；编队覆灭即永久阵亡。
/// </summary>
public class Captain
{
    private readonly HashSet<CaptainSkill> _skills = new();

    public string Name { get; init; } = string.Empty;
    public CaptainOrigin Origin { get; init; }
    public bool IsAlive { get; set; } = true;
    public bool InUse { get; set; }
    public int Level { get; private set; } = 1;
    public float Xp { get; private set; }
    public int SkillPoints { get; private set; }
    public IReadOnlyCollection<CaptainSkill> Skills => _skills;

    public string OriginName => Origin switch
    {
        CaptainOrigin.Soldier => "军人",
        CaptainOrigin.Scientist => "科学家",
        CaptainOrigin.Worker => "工人",
        _ => "平民",
    };

    public string Specialty => Origin switch
    {
        CaptainOrigin.Soldier => "步兵与坦克",
        CaptainOrigin.Scientist => "飞船与远程",
        CaptainOrigin.Worker => "维修与补给",
        _ => "士气与补员",
    };

    public float XpToNext => GameBalance.XpToNext(Level);
    public bool MaxLevel => Level >= GameBalance.CaptainMaxLevel;

    public string LevelText => MaxLevel ? $"Lv.{Level} 满级" : $"Lv.{Level}  XP {Xp:F0}/{XpToNext:F0}";

    public bool Matches(FormationKind kind) => Origin switch
    {
        CaptainOrigin.Soldier => kind is FormationKind.Infantry or FormationKind.LightTank
            or FormationKind.MediumTank or FormationKind.HeavyTank,
        CaptainOrigin.Scientist => kind is FormationKind.LightShip or FormationKind.MediumShip,
        CaptainOrigin.Worker => kind is FormationKind.SupplyTruck or FormationKind.RepairTruck,
        _ => kind is FormationKind.Infantry or FormationKind.SupplyTruck,
    };

    public bool Has(CaptainSkill skill) => _skills.Contains(skill);

    /// <summary>技能效果；对口编队完整生效，不对口按软适配折半。</summary>
    public float Value(CaptainSkill skill, FormationKind kind)
    {
        if (!_skills.Contains(skill))
        {
            return 0f;
        }

        var def = Find(skill);
        if (def == null)
        {
            return 0f;
        }

        return def.Magnitude * (Matches(kind) ? 1f : GameBalance.CaptainOffSpecialtyScale);
    }

    /// <summary>驻防类技能服务于基地，不区分编队对口，完整生效。</summary>
    public float ValueGarrison(CaptainSkill skill)
    {
        if (!_skills.Contains(skill))
        {
            return 0f;
        }

        var def = Find(skill);
        return def?.Magnitude ?? 0f;
    }

    public int AddXp(float amount)
    {
        if (!IsAlive || amount <= 0f || MaxLevel)
        {
            return 0;
        }

        Xp += amount;
        int gained = 0;
        while (!MaxLevel && Xp >= XpToNext)
        {
            Xp -= XpToNext;
            Level++;
            SkillPoints++;
            gained++;
        }

        if (MaxLevel)
        {
            Xp = 0f;
        }

        return gained;
    }

    public bool CanLearn(CaptainSkill skill)
    {
        var def = Find(skill);
        return IsAlive && def != null && def.Origin == Origin && !_skills.Contains(skill)
            && SkillPoints > 0 && Level >= def.RequiredLevel;
    }

    public bool Learn(CaptainSkill skill)
    {
        if (!CanLearn(skill))
        {
            return false;
        }

        _skills.Add(skill);
        SkillPoints--;
        return true;
    }

    public IReadOnlyList<CaptainSkillDef> SkillLine =>
        Catalog.Where(def => def.Origin == Origin).ToList();

    public static CaptainSkillDef Find(CaptainSkill skill) =>
        Catalog.FirstOrDefault(def => def.Skill == skill);

    public static Captain Soldier() => new() { Name = "铁锤", Origin = CaptainOrigin.Soldier };
    public static Captain Scientist() => new() { Name = "星图", Origin = CaptainOrigin.Scientist };
    public static Captain Worker() => new() { Name = "扳手", Origin = CaptainOrigin.Worker };
    public static Captain Civilian() => new() { Name = "灰雀", Origin = CaptainOrigin.Civilian };

    public static readonly CaptainSkillDef[] Catalog =
    {
        new()
        {
            Skill = CaptainSkill.FrontlineReplenish,
            Origin = CaptainOrigin.Soldier,
            Name = "前线整补",
            Description = "补员速度提升，补给车在前线同样受益",
            RequiredLevel = 1,
            Magnitude = GameBalance.CaptainReplenishSkill,
        },
        new()
        {
            Skill = CaptainSkill.GarrisonArmor,
            Origin = CaptainOrigin.Soldier,
            Name = "驻防工事",
            Description = "驻防不动时受到的伤害降低",
            RequiredLevel = 2,
            Magnitude = GameBalance.CaptainGarrisonArmorSkill,
        },
        new()
        {
            Skill = CaptainSkill.BattleVision,
            Origin = CaptainOrigin.Soldier,
            Name = "战场嗅觉",
            Description = "所属编队视野扩大",
            RequiredLevel = 3,
            Magnitude = GameBalance.CaptainBattleVisionSkill,
        },
        new()
        {
            Skill = CaptainSkill.SensorArray,
            Origin = CaptainOrigin.Scientist,
            Name = "侦测阵列",
            Description = "所属编队视野大幅扩大",
            RequiredLevel = 1,
            Magnitude = GameBalance.CaptainSensorSkill,
        },
        new()
        {
            Skill = CaptainSkill.BallisticCalibration,
            Origin = CaptainOrigin.Scientist,
            Name = "弹道校准",
            Description = "所属编队伤害提升",
            RequiredLevel = 2,
            Magnitude = GameBalance.CaptainDamageSkill,
        },
        new()
        {
            Skill = CaptainSkill.ResearchAssist,
            Origin = CaptainOrigin.Scientist,
            Name = "研究协作",
            Description = "驻防时基地样本研究速度提升",
            RequiredLevel = 3,
            Magnitude = GameBalance.CaptainResearchSkill,
        },
        new()
        {
            Skill = CaptainSkill.FieldRepair,
            Origin = CaptainOrigin.Worker,
            Name = "战地维修",
            Description = "维修车修复建筑的速度提升",
            RequiredLevel = 1,
            Magnitude = GameBalance.CaptainRepairSkill,
        },
        new()
        {
            Skill = CaptainSkill.FuelEfficiency,
            Origin = CaptainOrigin.Worker,
            Name = "节能驾驶",
            Description = "所属编队油耗降低",
            RequiredLevel = 2,
            Magnitude = GameBalance.CaptainFuelSkill,
        },
        new()
        {
            Skill = CaptainSkill.ProductionAssist,
            Origin = CaptainOrigin.Worker,
            Name = "产线调度",
            Description = "驻防时基地生产速度提升",
            RequiredLevel = 3,
            Magnitude = GameBalance.CaptainProductionSkill,
        },
        new()
        {
            Skill = CaptainSkill.LocalRecruit,
            Origin = CaptainOrigin.Civilian,
            Name = "就地征召",
            Description = "补员速度提升，适合后方养兵",
            RequiredLevel = 1,
            Magnitude = GameBalance.CaptainRecruitSkill,
        },
        new()
        {
            Skill = CaptainSkill.CivilMorale,
            Origin = CaptainOrigin.Civilian,
            Name = "民心稳定",
            Description = "驻防时每日士气提升",
            RequiredLevel = 2,
            Magnitude = GameBalance.CaptainMoraleSkill,
        },
        new()
        {
            Skill = CaptainSkill.Rationing,
            Origin = CaptainOrigin.Civilian,
            Name = "配给管理",
            Description = "驻防时人口食物消耗降低",
            RequiredLevel = 3,
            Magnitude = GameBalance.CaptainRationSkill,
        },
    };
}
