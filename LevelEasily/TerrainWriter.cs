using System.Collections.Generic;
using UnityEngine;

namespace LevelEasily;

/// <summary>
/// Zápis terénu bez módu na serveru: převezme <see cref="TerrainComp"/> dotčených zón a zapíše přímo do nich.
/// Postup a rizika souběhu: kb/teren-zapis-bez-serveru.md.
/// </summary>
internal static class TerrainWriter
{
    private static IEnumerable<Heightmap> Heightmaps(AreaPlan plan)
    {
        var heightmaps = new List<Heightmap>();
        Heightmap.FindHeightmap(plan.Centre, plan.HalfDiagonal + 1, heightmaps);
        return heightmaps;
    }

    /// <summary>Vytvoří nebo převezme kompilátory terénu; null, když některý nejde použít.</summary>
    public static List<TerrainComp> Claim(AreaPlan plan)
    {
        var compilers = new List<TerrainComp>();
        foreach (var heightmap in Heightmaps(plan))
        {
            var compiler = heightmap.GetAndCreateTerrainCompiler();
            if (!compiler || !compiler.m_nview.IsValid()) return null;
            compiler.m_nview.ClaimOwnership();
            compilers.Add(compiler);
        }
        return compilers.Count > 0 ? compilers : null;
    }

    /// <summary>Po čekání: pořád vlastníme a máme nejnovější data, výšky v meshi odpovídají deltám.</summary>
    public static bool Ready(List<TerrainComp> compilers)
    {
        foreach (var compiler in compilers)
        {
            if (!compiler || !compiler.m_nview.IsValid() || !compiler.m_nview.IsOwner() || !compiler.m_initialized) return false;
        }
        foreach (var compiler in compilers)
        {
            compiler.CheckLoad();
            compiler.m_hmap.Poke();
        }
        return true;
    }

    public static void Write(AreaPlan plan, List<TerrainComp> compilers, bool preserveSoil)
    {
        foreach (var compiler in compilers)
        {
            foreach (var v in plan.Vertices)
                if (v.Selected) compiler.LevelTerrain(new Vector3(v.Grid.x, v.Goal, v.Grid.y), 0, square: true);
            foreach (var cell in AreaPlan.Cells(plan.Area))
                if (IsFlatCell(plan, cell)) PaintDirt(compiler, cell, preserveSoil);
            compiler.m_operations++;
            compiler.m_lastOpPoint = plan.Centre;
            compiler.m_lastOpRadius = plan.HalfDiagonal + 1;
            compiler.Save();
            compiler.m_hmap.Poke();
        }
    }

    private static bool IsFlatCell(AreaPlan plan, Vector2Int cell)
    {
        for (int dx = 0; dx <= 1; dx++)
        for (int dz = 0; dz <= 1; dz++)
            if (!plan.TryGet(cell + new Vector2Int(dx, dz), out var v) || !v.FlatAfterWrite) return false;
        return true;
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
