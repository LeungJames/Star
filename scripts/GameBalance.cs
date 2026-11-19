using System.Text;

namespace Star;

/// <summary>
/// 原型阶段的临时平衡数值集中地。改这里即可统一调整难度，不应改变设计方向。
/// </summary>
public static class GameBalance
{
    // 世界时钟
    public const float DayLengthSeconds = 30f;
    public const float StormDurationSeconds = 10f;
    public const float WarningLeadSeconds = 8f;
    public const float FirstStormDelaySeconds = 20f;
    public const float InitialStormGapSeconds = 25f;
    public const float StormGapShrinkFactor = 0.8f;
    public const float MinStormGapSeconds = 10f;
    public const float StormSpeedMultiplier = 0.6f;
    public const float StormFuelMultiplier = 1.8f;
    public const int StormVisionPenalty = 2;
    public const int FinalStormCount = 6;

    // 地图与相机
    public const int MapRadius = 7;
    public const ulong MapSeed = 20261117UL;
    public const float HexSize = 1.6f;
    public const float CameraMinDistance = 18f;
    public const float CameraMaxDistance = 72f;
    public const float CameraPitchDegrees = -56f;

    // 编队燃油
    public const float RefuelRate = 28f;

    // 建筑与基地
    public const float RepairPerSecond = 8f;
    public const float SandstormDamagePerSecond = 2f;
    public const float DisconnectedDecayPerSecond = 0.4f;
    public const int StartMorale = 70;
    public const int BaseHousing = 4;
    public const int HabitatBeds = 8;
    public const int OverpopMoralePenalty = 4;
    public const float MoraleEfficiencyBase = 0.8f;
    public const float MoraleEfficiencyScale = 250f;
    public const float MoraleEfficiencyMin = 0.5f;
    public const float MoraleEfficiencyMax = 1.2f;

    // 资源
    public const float StorageCapacity = 150f;
    public const float OutpostCapacity = 60f;
    public const float FoodPerPersonPerDay = 1f;
    public const float WasteMessageInterval = 8f;
    public const float StartScrap = 60f;
    public const float StartFuel = 40f;
    public const float StartFood = 50f;
    public const float StartMedicine = 10f;
    public const float StartData = 2f;

    // 生产
    public const int MaxFormations = 8;
    public const int MaxAirFormations = 3;
    public const int MaxQueue = 3;
    public const float SurveyVehicleCost = 30f;
    public const float SurveyVehicleSeconds = 6f;

    public static float ScrapPerUnit(FormationKind kind) => kind switch
    {
        FormationKind.Infantry => 8f,
        FormationKind.LightTank => 18f,
        FormationKind.MediumTank => 26f,
        FormationKind.HeavyTank => 36f,
        FormationKind.LightShip => 30f,
        FormationKind.MediumShip => 42f,
        FormationKind.SurveyVehicle => SurveyVehicleCost,
        _ => 20f,
    };

    public static float SecondsPerUnit(FormationKind kind) => kind switch
    {
        FormationKind.Infantry => 1.6f,
        FormationKind.LightTank => 2.6f,
        FormationKind.MediumTank => 3.2f,
        FormationKind.HeavyTank => 4f,
        FormationKind.LightShip => 3.4f,
        FormationKind.MediumShip => 4.2f,
        FormationKind.SurveyVehicle => 8f,
        _ => 3f,
    };

    // 研究
    public const float SampleSeconds = 6f;
    public const float ResearchSeconds = 18f;
    public const int MaxResearchVehicles = 4;
    public const float ResearchBaseSuccess = 0.6f;

    // 巢穴与胜利
    public const float NestGrowthInterval = 30f;
    public const float NestSpawnInterval = 16f;
    public const float NestRespawnDelay = 40f;
    public const int MaxEnemyFormations = 8;
    public const float ArkWorkSeconds = 50f;
    public const float BeaconWorkSeconds = 40f;
    public const float ArkTruckRange = 2f;

    // 科技
    public const float CivBonusPerTech = 0.08f;
    public const float SoftCostOwn = 0.55f;
    public const float SoftCostOther = 0.25f;
    public const float TechResearchBaseSeconds = 14f;
    public const float TechResearchPerLineSeconds = 3f;
    public const float MilitaryDamageBonus = 0.15f;
    public const float PowerOutputBonus = 0.25f;
    public const float BuildingEfficiencyBonus = 0.15f;
    public const float SurvivalFuelReduction = 0.3f;
    public const float StorageBonus = 0.25f;
    public const float ProductionTimeReduction = 0.15f;
    public const float ReplenishBonus = 0.25f;
    public const float ResearchSuccessBonusC1 = 0.15f;
    public const float ResearchSuccessBonusC4 = 0.1f;
    public const float SampleResearchTimeMultiplier = 0.75f;
    public const float CivCaptainGrowthBonus = 0.1f;

    // 外交
    public const int StartRelationJunta = -15;
    public const int StartRelationCaravan = 25;
    public const int StartRelationCult = -5;
    public const int WarThreshold = -40;
    public const int ScoutRelationThreshold = -20;
    public const int TruceDays = 3;
    public const int PeaceRelationGain = 15;
    public const int PassivePeaceRelation = -20;
    public const float PassivePeaceChance = 0.25f;
    public const float PeaceScrapCost = 80f;
    public const float PeaceFuelCost = 40f;
    public const float GiftScrapCost = 40f;
    public const int GiftRelation = 8;
    public const int KillRelationPenalty = -10;
    public const int CedeRelationGain = 10;
    public const int FactionUnitLossStrength = 15;
    public const int FactionDailyLossMin = 0;
    public const int FactionDailyLossMax = 4;
    public const int FactionStormLoss = 3;
    public const float CaravanPriceFactor = 0.8f;
    public const float TradeScrapCost = 40f;
    public const float TradeDataCost = 50f;
    public const float TradeDataGain = 25f;
    public const float TradeFuelGain = 30f;
    public const float TradeMedicineGain = 15f;
    public const float TradeCultDataGain = 10f;
    public const int TradeRelationGain = 2;
    public const int TradeJuntaRelationGain = 3;
    public const int TradeCultRelationGain = 5;

    // 事件
    public const float EventCheckInterval = 0.5f;
    public const float MinorEventTimeoutSeconds = 25f;
    public const float FoodLowThreshold = 15f;
    public const float DemandScrapCost = 60f;
    public const float DemandFuelCost = 20f;
    public const float DemandBargainScrapCost = 30f;
    public const float FoodPurchaseScrapCost = 50f;
    public const float FoodPurchaseGain = 40f;
    public const float FoodMedicineCost = 10f;
    public const float FoodMedicineGain = 20f;
    public const float ShelterFuelCost = 10f;
    public const float CaravanEventScrapCost = 30f;
    public const float CaravanEventDataGain = 20f;
    public const float EventFoodCooldown = 90f;
    public const float EventStormCooldown = 5f;
    public const float EventCaravanCooldown = 60f;
    public const float EventCultCooldown = 75f;
    public const float EventOverpopCooldown = 60f;
    public const int EventDemandDay = 2;
    public const int EventCryopodDay = 3;
    public const int EventRefugeeDay = 4;
    public const int EventCaravanDay = 2;
    public const int EventCultDay = 4;

    // 队长
    public const int CaptainMaxLevel = 5;
    public const float CaptainGarrisonXpPerSecond = 2f;
    public const float CaptainXpPerDamageDealt = 0.15f;
    public const float CaptainXpPerDamageTaken = 0.05f;
    public const float CaptainGarrisonRadius = 2f;
    public const float CaptainOffSpecialtyScale = 0.5f;
    public const float CaptainReplenishSkill = 0.4f;
    public const float CaptainGarrisonArmorSkill = 0.15f;
    public const int CaptainBattleVisionSkill = 2;
    public const int CaptainSensorSkill = 3;
    public const float CaptainDamageSkill = 0.12f;
    public const float CaptainResearchSkill = 0.15f;
    public const float CaptainRepairSkill = 0.5f;
    public const float CaptainFuelSkill = 0.2f;
    public const float CaptainProductionSkill = 0.1f;
    public const float CaptainRecruitSkill = 0.25f;
    public const int CaptainMoraleSkill = 1;
    public const float CaptainRationSkill = 0.1f;

    /// <summary>升到下一级所需经验；索引为当前等级。</summary>
    private static readonly int[] CaptainLevelXp = { 0, 100, 180, 260, 340 };

    public static int XpToNext(int level) =>
        level >= 1 && level < CaptainLevelXp.Length ? CaptainLevelXp[level] : 0;

    /// <summary>调试面板用：关键平衡数值摘要。</summary>
    public static string Summary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("【时钟】");
        builder.AppendLine($"日长 {DayLengthSeconds:F0}s  沙暴 {StormDurationSeconds:F0}s  预警 {WarningLeadSeconds:F0}s");
        builder.AppendLine($"首轮沙暴 {FirstStormDelaySeconds:F0}s  初始间隔 {InitialStormGapSeconds:F0}s  终局 {FinalStormCount} 轮");
        builder.AppendLine($"沙暴移速 ×{StormSpeedMultiplier:F2}  油耗 ×{StormFuelMultiplier:F2}  视野 -{StormVisionPenalty}");
        builder.AppendLine("【基地与资源】");
        builder.AppendLine($"初始士气 {StartMorale}  基础床位 {BaseHousing}  居住舱 +{HabitatBeds}  超员 -{OverpopMoralePenalty}/日");
        builder.AppendLine($"仓储 {StorageCapacity:F0}  前哨 {OutpostCapacity:F0}  食物 {FoodPerPersonPerDay:F1}/人/日");
        builder.AppendLine($"初始 废铁 {StartScrap:F0} 燃料 {StartFuel:F0} 食物 {StartFood:F0} 药品 {StartMedicine:F0} 数据 {StartData:F0}");
        builder.AppendLine("【生产与研究】");
        builder.AppendLine($"编队上限 {MaxFormations}  空中 {MaxAirFormations}  队列 {MaxQueue}  科考车 {SurveyVehicleCost:F0}/{SurveyVehicleSeconds:F0}s");
        builder.AppendLine($"取样 {SampleSeconds:F0}s  样本研究 {ResearchSeconds:F0}s  基础成功率 {ResearchBaseSuccess:P0}");
        builder.AppendLine("【外交】");
        builder.AppendLine($"初始关系 军政府 {StartRelationJunta} 商队 {StartRelationCaravan} 教团 {StartRelationCult}");
        builder.AppendLine($"宣战阈值 {WarThreshold}  侦察阈值 {ScoutRelationThreshold}  和谈 铁{PeaceScrapCost:F0}/油{PeaceFuelCost:F0}  停战 {TruceDays} 天");
        builder.AppendLine("【事件】");
        builder.AppendLine($"检查间隔 {EventCheckInterval:F1}s  日常事件决策 {MinorEventTimeoutSeconds:F0}s");
        builder.AppendLine("【队长】");
        builder.AppendLine($"最高 {CaptainMaxLevel} 级  升级经验 {CaptainLevelXp[1]}/{CaptainLevelXp[2]}/{CaptainLevelXp[3]}/{CaptainLevelXp[4]}");
        builder.AppendLine($"驻防 {CaptainGarrisonXpPerSecond:F1} XP/s  伤害 {CaptainXpPerDamageDealt:F2}/点  受伤 {CaptainXpPerDamageTaken:F2}/点  软适配 ×{CaptainOffSpecialtyScale:F1}");
        return builder.ToString().TrimEnd();
    }
}
