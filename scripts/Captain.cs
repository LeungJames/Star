namespace Star;

public enum CaptainOrigin
{
    Soldier,
    Scientist,
    Worker,
    Civilian,
}

/// <summary>挂在编队上的队长，软适配对口编队获得完整加成，不对口则减半。</summary>
public class Captain
{
    public string Name { get; init; } = string.Empty;
    public CaptainOrigin Origin { get; init; }
    public bool IsAlive { get; set; } = true;
    public bool InUse { get; set; }

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

    public bool Matches(FormationKind kind) => Origin switch
    {
        CaptainOrigin.Soldier => kind is FormationKind.Infantry or FormationKind.LightTank
            or FormationKind.MediumTank or FormationKind.HeavyTank,
        CaptainOrigin.Scientist => kind is FormationKind.LightShip or FormationKind.MediumShip,
        CaptainOrigin.Worker => kind is FormationKind.SupplyTruck or FormationKind.RepairTruck,
        _ => false,
    };

    public static Captain Soldier() => new() { Name = "铁锤", Origin = CaptainOrigin.Soldier };
    public static Captain Scientist() => new() { Name = "星图", Origin = CaptainOrigin.Scientist };
    public static Captain Worker() => new() { Name = "扳手", Origin = CaptainOrigin.Worker };
}
