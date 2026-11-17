using System;
using System.Collections.Generic;

namespace Star;

/// <summary>六角格 A* 寻路。</summary>
public static class Pathfinder
{
    public static List<HexCoord> FindPath(HexMap map, Func<HexCoord, bool> blocked, HexCoord start, HexCoord goal)
    {
        if (start.Equals(goal))
        {
            return new List<HexCoord>();
        }

        var open = new PriorityQueue<HexCoord, int>();
        var cameFrom = new Dictionary<HexCoord, HexCoord>();
        var cost = new Dictionary<HexCoord, int> { [start] = 0 };
        open.Enqueue(start, start.DistanceTo(goal));

        while (open.TryDequeue(out var current, out _))
        {
            if (current.Equals(goal))
            {
                return BuildPath(cameFrom, start, goal);
            }

            int currentCost = cost[current];
            for (int i = 0; i < HexCoord.Directions.Length; i++)
            {
                var next = current.Neighbor(i);
                if (!map.Contains(next) || blocked(next))
                {
                    continue;
                }

                int tentative = currentCost + 1;
                if (cost.TryGetValue(next, out int known) && tentative >= known)
                {
                    continue;
                }

                cost[next] = tentative;
                cameFrom[next] = current;
                open.Enqueue(next, tentative + next.DistanceTo(goal));
            }
        }

        return null;
    }

    private static List<HexCoord> BuildPath(Dictionary<HexCoord, HexCoord> cameFrom, HexCoord start, HexCoord goal)
    {
        var path = new List<HexCoord>();
        var current = goal;
        while (!current.Equals(start))
        {
            path.Add(current);
            current = cameFrom[current];
        }

        path.Reverse();
        return path;
    }
}
