using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* pathfinding over GridSystem's walkable grid cells (8 directions, diagonals do not cut corners).
/// Returns a path of cell indices (including the goal, excluding the start); null / empty if not found.
/// </summary>
public static class AStarPathfinder
{
    private class Node
    {
        public Vector2Int cell;
        public int g, h;
        public Node parent;
        public int F => g + h;
    }

    // Eight directions
    private static readonly Vector2Int[] Dirs =
    {
        new Vector2Int( 1, 0), new Vector2Int(-1, 0),
        new Vector2Int( 0, 1), new Vector2Int( 0,-1),
        new Vector2Int( 1, 1), new Vector2Int( 1,-1),
        new Vector2Int(-1, 1), new Vector2Int(-1,-1),
    };

    private const int CostStraight = 10;
    private const int CostDiagonal = 14;

    /// <summary>
    /// Finds the cell path from start to goal. allowDiagonal=false for four directions only.
    /// Returns a cell list excluding start and ending with goal; null if unreachable.
    /// </summary>
    public static List<Vector2Int> FindPath(GridSystem grid, Vector2Int start, Vector2Int goal, bool allowDiagonal = true)
    {
        if (grid == null) return null;
        if (!grid.InBounds(start) || !grid.InBounds(goal)) return null;
        if (!grid.IsWalkable(goal)) return null;   // Goal not walkable, fail immediately

        var open = new List<Node>();
        var openMap = new Dictionary<Vector2Int, Node>();
        var closed = new HashSet<Vector2Int>();

        Node startNode = new Node { cell = start, g = 0, h = Heuristic(start, goal) };
        open.Add(startNode);
        openMap[start] = startNode;

        int guard = grid.cols * grid.rows + 10;   // Infinite loop guard

        while (open.Count > 0 && guard-- > 0)
        {
            // Take the lowest F
            Node current = open[0];
            for (int i = 1; i < open.Count; i++)
                if (open[i].F < current.F || (open[i].F == current.F && open[i].h < current.h))
                    current = open[i];

            if (current.cell == goal)
                return Retrace(current);

            open.Remove(current);
            openMap.Remove(current.cell);
            closed.Add(current.cell);

            int dirCount = allowDiagonal ? 8 : 4;
            for (int d = 0; d < dirCount; d++)
            {
                Vector2Int nc = current.cell + Dirs[d];
                if (!grid.IsWalkable(nc) || closed.Contains(nc)) continue;

                bool diagonal = Dirs[d].x != 0 && Dirs[d].y != 0;
                if (diagonal)
                {
                    // No corner cutting on diagonals: both orthogonal neighbors must be walkable
                    Vector2Int a = new Vector2Int(current.cell.x + Dirs[d].x, current.cell.y);
                    Vector2Int b = new Vector2Int(current.cell.x, current.cell.y + Dirs[d].y);
                    if (!grid.IsWalkable(a) || !grid.IsWalkable(b)) continue;
                }

                int stepCost = diagonal ? CostDiagonal : CostStraight;
                int tentativeG = current.g + stepCost;

                if (openMap.TryGetValue(nc, out Node existing))
                {
                    if (tentativeG < existing.g)
                    {
                        existing.g = tentativeG;
                        existing.parent = current;
                    }
                }
                else
                {
                    Node n = new Node { cell = nc, g = tentativeG, h = Heuristic(nc, goal), parent = current };
                    open.Add(n);
                    openMap[nc] = n;
                }
            }
        }
        return null;   // Unreachable
    }

    // Diagonal distance heuristic (for eight directions)
    private static int Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return CostStraight * (dx + dy) + (CostDiagonal - 2 * CostStraight) * Mathf.Min(dx, dy);
    }

    private static List<Vector2Int> Retrace(Node end)
    {
        var path = new List<Vector2Int>();
        Node n = end;
        while (n.parent != null)   // Exclude the start
        {
            path.Add(n.cell);
            n = n.parent;
        }
        path.Reverse();
        return path;
    }
}