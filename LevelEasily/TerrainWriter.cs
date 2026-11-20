using System.Collections.Generic;
using UnityEngine;

namespace LevelEasily;

/// <summary>
/// Terrain write without the mod on the server: takes over the <see cref="TerrainComp"/> of affected zones and writes into them directly.
/// Procedure and race risks: kb/teren-zapis-bez-serveru.md.
/// </summary>
internal static class TerrainWriter
{
    private static readonly List<Heightmap> Found = new();

    /// <summary>Vertices the write may touch: changed ones and corners of painted cells.</summary>
    public static IEnumerable<VertexPlan> Touched(AreaPlan plan)
    {
        foreach (var v in plan.Vertices)
            if (v.Wanted || v.State == VertexState.Level) yield return v;
    }

    /// <summary>
    /// Creates or takes over compilers only of zones containing touched vertices (a vertex on the edge belongs to both).
    /// Null when any of them cannot be used.
    /// </summary>
    public static Dictionary<Heightmap, TerrainComp> Claim(AreaPlan plan)
    {
        var claimed = new Dictionary<Heightmap, TerrainComp>();
        foreach (var v in Touched(plan))
        {
            Found.Clear();
            Heightmap.FindHeightmap(v.Point, 0, Found);
            foreach (var heightmap in Found)
            {
                if (claimed.ContainsKey(heightmap)) continue;
                var compiler = heightmap.GetAndCreateTerrainCompiler();
                if (!compiler || !compiler.m_nview.IsValid()) return null;
                compiler.m_nview.ClaimOwnership();
                claimed[heightmap] = compiler;
            }
        }
        return claimed.Count > 0 ? claimed : null;
    }

    /// <summary>Does the vertex lie only in claimed zones?</summary>
    public static bool Covers(Dictionary<Heightmap, TerrainComp> claimed, VertexPlan v)
    {
        Found.Clear();
        Heightmap.FindHeightmap(v.Point, 0, Found);
        if (Found.Count == 0) return false;
        foreach (var heightmap in Found)
            if (!claimed.ContainsKey(heightmap)) return false;
        return true;
    }

    /// <summary>After the wait: we still own the zones and have the latest data, mesh heights match the deltas.</summary>
    public static bool Ready(Dictionary<Heightmap, TerrainComp> claimed)
    {
        foreach (var compiler in claimed.Values)
            if (!compiler || !compiler.m_nview.IsValid() || !compiler.m_nview.IsOwner() || !compiler.m_initialized) return false;
        foreach (var compiler in claimed.Values)
        {
            compiler.CheckLoad();
            compiler.m_hmap.Poke();
        }
        return true;
    }

    public static void Write(AreaPlan plan, Dictionary<Heightmap, TerrainComp> claimed, bool preserveSoil)
    {
        foreach (var compiler in claimed.Values)
        {
            foreach (var v in plan.Vertices)
                if (v.Selected) SetHeight(compiler, v);
            foreach (var cell in AreaPlan.Cells(plan.Area))
                if (IsPaintedCell(plan, cell)) PaintDirt(compiler, cell, preserveSoil);
            compiler.m_operations++;
            compiler.m_lastOpPoint = plan.Centre;
            compiler.m_lastOpRadius = plan.HalfDiagonal + 1;
            compiler.Save();
            compiler.m_hmap.Poke();
        }
    }

    /// <summary>
    /// Like <c>TerrainComp.LevelTerrain</c> with radius 0, but the delta is computed from the base height:
    /// LevelTerrain adds to the clamped height and would miss the target when the deltas sum beyond ±8 m.
    /// </summary>
    private static void SetHeight(TerrainComp compiler, VertexPlan v)
    {
        var heightmap = compiler.m_hmap;
        heightmap.WorldToVertex(v.Point, out int x, out int y);
        int pitch = compiler.m_width + 1;
        if (x < 0 || y < 0 || x >= pitch || y >= pitch) return;
        if (!heightmap.GetWorldHeight(v.Point, out float height)) return;
        var (baseHeight, _) = AreaPlan.BaseHeight(heightmap, compiler, new Vector3(v.Point.x, height, v.Point.z));
        int index = y * pitch + x;
        compiler.m_levelDelta[index] = Mathf.Clamp(v.Goal - baseHeight, -AreaPlan.VanillaLevelLimit, AreaPlan.VanillaLevelLimit);
        compiler.m_smoothDelta[index] = 0;
        compiler.m_modifiedHeight[index] = true;
    }

    /// <summary>The cell will be fully level and at least one corner changes now (already level areas are not repainted).</summary>
    private static bool IsPaintedCell(AreaPlan plan, Vector2Int cell)
    {
        bool changed = false;
        for (int dx = 0; dx <= 1; dx++)
        for (int dz = 0; dz <= 1; dz++)
        {
            if (!plan.TryGet(cell + new Vector2Int(dx, dz), out var v) || !v.FlatAfterWrite) return false;
            changed |= v.Selected;
        }
        return changed;
    }

    /// <summary>
    /// Paints the cell with dirt directly in the compiler data (per-vertex PaintCleared wipes neighbours, see KB).
    /// Alpha is kept: in the Ashlands it means lava, elsewhere vegetation.
    /// </summary>
    private static void PaintDirt(TerrainComp compiler, Vector2Int cell, bool preserveSoil)
    {
        var heightmap = compiler.m_hmap;
        var world = new Vector3(cell.x, 0, cell.y);
        heightmap.WorldToVertexMask(world, out int x, out int y);
        if (x < 0 || y < 0 || x > compiler.m_width || y > compiler.m_width) return;
        int index = y * compiler.m_pitch + x;
        var current = compiler.m_modifiedPaint[index] ? compiler.m_paintMask[index] : heightmap.GetPaintMask(x, y);
        if (preserveSoil && IsWorkedSoil(heightmap, world, current)) return;
        compiler.m_paintMask[index] = new Color(1f, 0f, 0f, current.a);
        compiler.m_modifiedPaint[index] = true;
    }

    /// <summary>Cultivated (g) or paved (b) ground; in the Deep North g is snow depth, not cultivation.</summary>
    private static bool IsWorkedSoil(Heightmap heightmap, Vector3 world, Color paint) =>
        paint.b > 0.5f || (paint.g > 0.5f && heightmap.GetBiome(world + new Vector3(0.5f, 0, 0.5f)) != Heightmap.Biome.DeepNorth);
}
