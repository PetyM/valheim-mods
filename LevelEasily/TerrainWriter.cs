using System.Collections.Generic;
using UnityEngine;

namespace LevelEasily;

/// <summary>
/// Zápis terénu bez módu na serveru: převezme <see cref="TerrainComp"/> dotčených zón a zapíše přímo do nich.
/// Postup a rizika souběhu: kb/teren-zapis-bez-serveru.md.
/// </summary>
internal static class TerrainWriter
{
    private static readonly List<Heightmap> Found = new();

    /// <summary>Vrcholy, kterých se zápis může dotknout: měněné a rohy natíraných buněk.</summary>
    public static IEnumerable<VertexPlan> Touched(AreaPlan plan)
    {
        foreach (var v in plan.Vertices)
            if (v.Wanted || v.State == VertexState.Level) yield return v;
    }

    /// <summary>
    /// Vytvoří nebo převezme kompilátory jen těch zón, kde leží dotčené vrcholy (vrchol na hraně patří oběma).
    /// Null, když některý nejde použít.
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

    /// <summary>Leží vrchol jen v převzatých zónách?</summary>
    public static bool Covers(Dictionary<Heightmap, TerrainComp> claimed, VertexPlan v)
    {
        Found.Clear();
        Heightmap.FindHeightmap(v.Point, 0, Found);
        if (Found.Count == 0) return false;
        foreach (var heightmap in Found)
            if (!claimed.ContainsKey(heightmap)) return false;
        return true;
    }

    /// <summary>Po čekání: pořád vlastníme a máme nejnovější data, výšky v meshi odpovídají deltám.</summary>
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
    /// Jako <c>TerrainComp.LevelTerrain</c> s poloměrem 0, ale delta se počítá od základní výšky:
    /// LevelTerrain přičítá k oříznuté výšce a při součtu delt nad ±8 m by netrefil cíl.
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

    /// <summary>Buňka bude celá rovná a aspoň jeden její roh se teď mění (už rovné plochy se znovu nenatírají).</summary>
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
    /// Natře buňku hlínou přímo v datech kompilátoru (PaintCleared po vrcholech maže sousedy, viz KB).
    /// Alfa zůstává: v Ashlands znamená lávu, jinde vegetaci.
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

    /// <summary>Obdělaná (g) nebo dlážděná (b) půda; v Deep North je g hloubka sněhu, ne obdělání.</summary>
    private static bool IsWorkedSoil(Heightmap heightmap, Vector3 world, Color paint) =>
        paint.b > 0.5f || (paint.g > 0.5f && heightmap.GetBiome(world + new Vector3(0.5f, 0, 0.5f)) != Heightmap.Biome.DeepNorth);
}
