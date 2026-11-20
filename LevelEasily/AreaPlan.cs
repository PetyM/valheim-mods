using System.Collections.Generic;
using UnityEngine;

namespace LevelEasily;

internal enum VertexState { Level, Cut, Fill, UnderPiece, NeedsPickaxe }

internal sealed class VertexPlan
{
    public Vector2Int Grid;
    public Vector3 Point;
    public float Goal;
    public bool Unreachable;
    public VertexState State;
    public float PickVolume;
    /// <summary>Pickaxe volume above max depth: only there can vanilla digging drop stone.</summary>
    public float YieldVolume;
    public float StoneVolume;
    public bool Selected;

    public float Rise => Goal - Point.y;
    public bool Wanted => State is VertexState.Cut or VertexState.Fill;
    public bool FlatAfterWrite => !Unreachable && (State == VertexState.Level || Selected);
}

/// <summary>What should happen to each vertex of the area and how much work it takes with vanilla tools.</summary>
internal sealed class AreaPlan
{
    public const float ChangeThreshold = 0.01f;
    private const float HoeReach = 1f;
    private const float PieceMargin = 0.45f;
    private const float PieceSlack = 0.5f;
    public const int VanillaLevelLimit = 8;
    private const float MaxDepthYield = 7.95f;
    private static readonly Collider[] Overlaps = new Collider[32];

    public RectInt Area { get; }
    public float Target { get; }
    public bool FreeFill { get; }
    public List<VertexPlan> Vertices { get; } = new();
    private readonly Dictionary<Vector2Int, VertexPlan> byGrid = new();

    public Vector3 Centre => new(Area.center.x, Target, Area.center.y);
    public float HalfDiagonal => Mathf.Sqrt(Area.width * Area.width + Area.height * Area.height) / 2f;

    private AreaPlan(RectInt area, float target, bool freeFill)
    {
        Area = area;
        Target = target;
        FreeFill = freeFill;
    }

    public bool TryGet(Vector2Int grid, out VertexPlan vertex) => byGrid.TryGetValue(grid, out vertex);

    public int Count(VertexState state)
    {
        int count = 0;
        foreach (var v in Vertices) if (v.State == state) count++;
        return count;
    }

    /// <param name="freeFill">The hoe may fill up to 1 m for free (the target is not above the terrain under the player).</param>
    public static AreaPlan Measure(RectInt area, float target, bool freeFill, bool hasPickaxe, GameObject ghost, int pieceMask)
    {
        var plan = new AreaPlan(area, target, freeFill);
        var compilers = new Dictionary<Heightmap, TerrainComp>();
        foreach (var grid in VertexPositions(area))
        {
            var point = new Vector3(grid.x, 0, grid.y);
            var heightmap = Heightmap.FindHeightmap(point);
            if (!heightmap || !heightmap.GetWorldHeight(point, out point.y)) continue;
            if (!compilers.TryGetValue(heightmap, out var compiler))
                compilers[heightmap] = compiler = TerrainComp.FindTerrainCompiler(heightmap.transform.position);
            var (baseHeight, smoothDelta) = BaseHeight(heightmap, compiler, point);
            float goal = Mathf.Clamp(target, baseHeight - VanillaLevelLimit, baseHeight + VanillaLevelLimit);
            var vertex = new VertexPlan
            {
                Grid = grid,
                Point = point,
                Goal = goal,
                Unreachable = Mathf.Abs(goal - target) > ChangeThreshold,
            };
            Classify(vertex, smoothDelta, freeFill, hasPickaxe, ghost, pieceMask);
            // Heightmap.AtMaxWorldLevelDepth: 7.95 m below the base height vanilla digging drops nothing.
            vertex.YieldVolume = goal > baseHeight - MaxDepthYield ? vertex.PickVolume : 0;
            plan.Vertices.Add(vertex);
            plan.byGrid[grid] = vertex;
        }
        return plan;
    }

    /// <summary>
    /// Height before player edits (the game clamps ±8 m against it) and the current hoe smooth delta.
    /// The game clamps the sum of the deltas, not each one, hence the clamp of the sum.
    /// </summary>
    public static (float baseHeight, float smoothDelta) BaseHeight(Heightmap heightmap, TerrainComp compiler, Vector3 point)
    {
        if (!compiler || !compiler.m_initialized) return (point.y, 0);
        heightmap.WorldToVertex(point, out int x, out int y);
        int pitch = compiler.m_width + 1;
        if (x < 0 || y < 0 || x >= pitch || y >= pitch) return (point.y, 0);
        int index = y * pitch + x;
        float level = compiler.m_levelDelta[index], smooth = compiler.m_smoothDelta[index];
        return (point.y - Mathf.Clamp(level + smooth, -VanillaLevelLimit, VanillaLevelLimit), smooth);
    }

    private static void Classify(VertexPlan v, float smoothDelta, bool freeFill, bool hasPickaxe, GameObject ghost, int pieceMask)
    {
        float rise = v.Rise;
        if (Mathf.Abs(rise) <= ChangeThreshold)
        {
            v.State = VertexState.Level;
            return;
        }
        if (UnderPiece(v, ghost, pieceMask))
        {
            v.State = VertexState.UnderPiece;
            return;
        }
        if (rise < 0)
        {
            // Vanilla clamps the hoe smooth delta to ±1 m, only the pickaxe digs deeper.
            v.PickVolume = Mathf.Max(0, -rise - (HoeReach + smoothDelta));
            v.State = v.PickVolume > 0 && !hasPickaxe ? VertexState.NeedsPickaxe : VertexState.Cut;
        }
        else
        {
            v.StoneVolume = freeFill ? Mathf.Max(0, rise - (HoeReach - smoothDelta)) : rise;
            v.State = VertexState.Fill;
        }
    }

    private static bool UnderPiece(VertexPlan v, GameObject ghost, int pieceMask)
    {
        float low = Mathf.Min(v.Point.y, v.Goal) - PieceSlack, high = Mathf.Max(v.Point.y, v.Goal) + PieceSlack;
        var centre = new Vector3(v.Point.x, (low + high) / 2, v.Point.z);
        var half = new Vector3(PieceMargin, (high - low) / 2, PieceMargin);
        int hits = Physics.OverlapBoxNonAlloc(centre, half, Overlaps, Quaternion.identity, pieceMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            var piece = Overlaps[i].GetComponentInParent<Piece>();
            if (piece && (!ghost || piece.gameObject != ghost)) return true;
        }
        return false;
    }

    public static IEnumerable<Vector2Int> VertexPositions(RectInt area)
    {
        for (int x = area.xMin; x <= area.xMax; x++)
        for (int z = area.yMin; z <= area.yMax; z++)
            yield return new Vector2Int(x, z);
    }

    public static IEnumerable<Vector2Int> Cells(RectInt area)
    {
        for (int x = area.xMin; x < area.xMax; x++)
        for (int z = area.yMin; z < area.yMax; z++)
            yield return new Vector2Int(x, z);
    }
}
