using System.Collections.Generic;
using Godot;

namespace Star;

/// <summary>程序生成的六角格地图，只负责范围、阻挡地形与坐标换算。</summary>
public class HexMap
{
    public const float HexSize = 1.6f;

    private static readonly HexCoord Origin = new(0, 0);

    public int Radius { get; }
    public int BlockedCount => _blocked.Count;

    private readonly HashSet<HexCoord> _blocked = new();

    public HexMap(int radius, ulong seed)
    {
        Radius = radius;
        var rng = new RandomNumberGenerator { Seed = seed };

        // 用随机游走生成成片的阻挡地形，而不是零散的噪点。
        for (int i = 0; i < 14; i++)
        {
            var cell = RandomCell(rng);
            int length = rng.RandiRange(3, 7);
            for (int step = 0; step < length; step++)
            {
                if (Contains(cell))
                {
                    _blocked.Add(cell);
                }

                cell = cell.Neighbor(rng.RandiRange(0, HexCoord.Directions.Length - 1));
            }
        }
    }

    private HexCoord RandomCell(RandomNumberGenerator rng)
    {
        int q = rng.RandiRange(-Radius, Radius);
        int rMin = Mathf.Max(-Radius, -q - Radius);
        int rMax = Mathf.Min(Radius, -q + Radius);
        return new HexCoord(q, rng.RandiRange(rMin, rMax));
    }

    public bool Contains(HexCoord coord) => coord.DistanceTo(Origin) <= Radius;

    public bool IsBlocked(HexCoord coord) => _blocked.Contains(coord);

    public void ClearAround(HexCoord center, int radius)
    {
        foreach (var cell in Cells())
        {
            if (cell.DistanceTo(center) <= radius)
            {
                _blocked.Remove(cell);
            }
        }
    }

    public IEnumerable<HexCoord> Cells()
    {
        for (int q = -Radius; q <= Radius; q++)
        {
            int rMin = Mathf.Max(-Radius, -q - Radius);
            int rMax = Mathf.Min(Radius, -q + Radius);
            for (int r = rMin; r <= rMax; r++)
            {
                yield return new HexCoord(q, r);
            }
        }
    }

    public Vector3 ToWorld(HexCoord coord) => coord.ToWorld(HexSize);

    public HexCoord ToCoord(Vector3 world) => HexCoord.FromWorld(world, HexSize);
}
