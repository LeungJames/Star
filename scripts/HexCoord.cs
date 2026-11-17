using System;
using Godot;

namespace Star;

/// <summary>尖顶六角格的轴向坐标 (q, r)。</summary>
public readonly struct HexCoord : IEquatable<HexCoord>
{
    public readonly int Q;
    public readonly int R;

    public HexCoord(int q, int r)
    {
        Q = q;
        R = r;
    }

    public static readonly HexCoord[] Directions =
    {
        new(1, 0), new(1, -1), new(0, -1),
        new(-1, 0), new(-1, 1), new(0, 1),
    };

    public HexCoord Neighbor(int direction)
    {
        var d = Directions[direction];
        return new HexCoord(Q + d.Q, R + d.R);
    }

    public int DistanceTo(HexCoord other)
    {
        int dq = Q - other.Q;
        int dr = R - other.R;
        return (Mathf.Abs(dq) + Mathf.Abs(dr) + Mathf.Abs(dq + dr)) / 2;
    }

    public Vector3 ToWorld(float size)
    {
        return new Vector3(size * Mathf.Sqrt(3f) * (Q + R * 0.5f), 0f, size * 1.5f * R);
    }

    public static HexCoord FromWorld(Vector3 world, float size)
    {
        float q = (Mathf.Sqrt(3f) / 3f * world.X - world.Z / 3f) / size;
        float r = (2f / 3f * world.Z) / size;
        return CubeRound(q, r);
    }

    private static HexCoord CubeRound(float q, float r)
    {
        float s = -q - r;
        int rq = Mathf.RoundToInt(q);
        int rr = Mathf.RoundToInt(r);
        int rs = Mathf.RoundToInt(s);
        float dq = Mathf.Abs(rq - q);
        float dr = Mathf.Abs(rr - r);
        float ds = Mathf.Abs(rs - s);

        if (dq > dr && dq > ds)
        {
            rq = -rr - rs;
        }
        else if (dr > ds)
        {
            rr = -rq - rs;
        }

        return new HexCoord(rq, rr);
    }

    public bool Equals(HexCoord other) => Q == other.Q && R == other.R;

    public override bool Equals(object obj) => obj is HexCoord other && Equals(other);

    public override int GetHashCode() => (Q * 397) ^ R;

    public override string ToString() => $"({Q},{R})";
}
