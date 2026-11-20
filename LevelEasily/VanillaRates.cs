using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace LevelEasily;

/// <summary>Vanilla tool rates read from prefabs, so the cost follows the game and any mods that change them.</summary>
internal sealed class VanillaRates
{
    /// <summary>How far from the target a vertex may stay after a grid of vanilla swings for the grid to count as done.</summary>
    private const float MaxResidual = 0.05f;

    public Piece LevelPiece { get; }
    public TerrainOp LevelOp { get; }
    public Piece RaisePiece { get; }
    public TerrainOp RaiseOp { get; }
    public int VerticesPerSwing { get; }
    public float RaiseVolumePerSwing { get; }
    public ItemDrop Stone { get; }
    public int StonePerRaiseSwing { get; }

    private readonly ManualLogSource log;
    private readonly Dictionary<ItemDrop.ItemData.SharedData, float> digVolumes = new();
    private readonly Dictionary<ItemDrop.ItemData.SharedData, DigYield> digYields = new();

    private VanillaRates(PieceTable hoe, ManualLogSource log)
    {
        this.log = log;
        LevelPiece = hoe.m_pieces.First(p => p && p.name == "mud_road_v2").GetComponent<Piece>();
        var raise = RaisePiece = hoe.m_pieces.First(p => p && p.name == "raise_v2").GetComponent<Piece>();
        RaiseOp = raise.GetComponentInChildren<TerrainOp>(true);
        LevelOp = LevelPiece.GetComponentInChildren<TerrainOp>(true);
        var levelOp = LevelOp.m_settings;
        var raiseOp = raise.GetComponentInChildren<TerrainOp>(true).m_settings;
        VerticesPerSwing = SmoothVerticesPerSwing(levelOp.m_smoothRadius, levelOp.m_smoothPower);
        RaiseVolumePerSwing = Footprint(raiseOp.m_raiseRadius, raiseOp.m_square,
            d => raiseOp.m_raiseDelta * Falloff(d, raiseOp.m_raiseRadius, raiseOp.m_raisePower, raiseOp.m_square));
        Stone = raise.m_resources[0].m_resItem;
        StonePerRaiseSwing = raise.m_resources[0].m_amount;

        log.LogInfo($"Level ground: smooth r{levelOp.m_smoothRadius} power {levelOp.m_smoothPower} → {VerticesPerSwing} vertices per swing; " +
                    $"piece groundPiece={LevelPiece.m_groundPiece} altGround={LevelPiece.m_allowAltGroundPlacement} " +
                    $"dungeon={LevelPiece.m_allowedInDungeons} deepSnow={LevelPiece.m_allowedInDeepSnow}");
        log.LogInfo($"Effects: Level ground place {Describe(LevelPiece.m_placeEffect)}, op {Describe(LevelOp.m_onPlacedEffect)}; " +
                    $"Raise ground place {Describe(raise.m_placeEffect)}, op {Describe(RaiseOp.m_onPlacedEffect)}");
        log.LogInfo($"Raise ground: {RaiseVolumePerSwing:0.00} m³ and {StonePerRaiseSwing}× {Stone.name} per swing");
    }

    /// <summary>Effect prefab names, to check in the log which sounds and particles the mod plays.</summary>
    public static string Describe(EffectList effects) =>
        effects?.m_effectPrefabs == null || effects.m_effectPrefabs.Length == 0
            ? "none"
            : string.Join(", ", effects.m_effectPrefabs.Where(e => e.m_prefab).Select(e => e.m_prefab.name));

    public static VanillaRates FromHoe(PieceTable hoe, ManualLogSource log) => new(hoe, log);

    /// <summary>Volume one terrain hit with the tool digs out; 0 = the tool does not dig.</summary>
    public float DigVolumePerHit(ItemDrop.ItemData tool)
    {
        var shared = tool.m_shared;
        if (digVolumes.TryGetValue(shared, out float volume)) return volume;
        volume = 0;
        var op = shared.m_spawnOnHitTerrain ? shared.m_spawnOnHitTerrain.GetComponentInChildren<TerrainOp>(true) : null;
        if (op)
        {
            var s = op.m_settings;
            float levelDepth = s.m_level ? Mathf.Max(0, -s.m_levelOffset) : 0;
            float raiseDepth = s.m_raise ? Mathf.Max(0, -s.m_raiseDelta) : 0;
            float radius = Mathf.Max(s.m_level ? s.m_levelRadius : 0, s.m_raise ? s.m_raiseRadius : 0);
            volume = Footprint(radius, s.m_square, d =>
                Mathf.Max(d <= s.m_levelRadius || s.m_square ? levelDepth : 0,
                    d <= s.m_raiseRadius ? raiseDepth * Falloff(d, s.m_raiseRadius, s.m_raisePower, s.m_square) : 0));
            log.LogInfo($"{shared.m_name}: {op.name} level={s.m_level} r{s.m_levelRadius} offset {s.m_levelOffset}, " +
                        $"raise={s.m_raise} r{s.m_raiseRadius} delta {s.m_raiseDelta}, square={s.m_square} → {volume:0.00} m³ per hit");
        }
        digVolumes[shared] = volume;
        return volume;
    }

    /// <summary>What a vanilla terrain hit drops (<c>TerrainOp.OnPlaced</c>): the item and the average amount per hit.</summary>
    public DigYield YieldPerHit(ItemDrop.ItemData tool)
    {
        var shared = tool.m_shared;
        if (digYields.TryGetValue(shared, out var yield)) return yield;
        var op = shared.m_spawnOnHitTerrain ? shared.m_spawnOnHitTerrain.GetComponentInChildren<TerrainOp>(true) : null;
        var drop = op && op.m_spawnOnPlaced ? op.m_spawnOnPlaced.GetComponent<ItemDrop>() : null;
        // Random.value <= chance, then Random.Range(1, max + 1) pieces.
        yield = drop
            ? new DigYield(drop, Mathf.Clamp01(op.m_chanceToSpawn) * (1 + Mathf.Max(1, op.m_maxSpawned)) / 2f, op.m_spawnAtMaxLevelDepth)
            : new DigYield(null, 0, false);
        log.LogInfo(drop
            ? $"{shared.m_name}: a hit drops {drop.name} with chance {op.m_chanceToSpawn}, 1–{op.m_maxSpawned} pcs " +
              $"(average {yield.PerHit:0.00}), also at max depth {op.m_spawnAtMaxLevelDepth}"
            : $"{shared.m_name}: a terrain hit drops nothing");
        digYields[shared] = yield;
        return yield;
    }

    /// <summary>
    /// How many vertices one swing levels when the player swings in a regular grid: k² for the largest spacing k
    /// at which no vertex stays further than <see cref="MaxResidual"/> from the target (smoothing only approaches it).
    /// </summary>
    private static int SmoothVerticesPerSwing(float radius, float power)
    {
        int reach = Mathf.CeilToInt(radius);
        for (int k = 2 * reach; k > 1; k--)
        {
            float worst = 0;
            for (int ox = 0; ox < k; ox++)
            for (int oz = 0; oz < k; oz++)
                worst = Mathf.Max(worst, Residual(ox, oz, k, reach, radius, power));
            if (worst <= MaxResidual) return k * k;
        }
        return 1;
    }

    private static float Residual(int ox, int oz, int spacing, int reach, float radius, float power)
    {
        float residual = 1;
        for (int i = Mathf.FloorToInt((ox - reach) / (float)spacing); i * spacing <= ox + reach; i++)
        for (int j = Mathf.FloorToInt((oz - reach) / (float)spacing); j * spacing <= oz + reach; j++)
        {
            float d = Vector2.Distance(new Vector2(ox, oz), new Vector2(i * spacing, j * spacing));
            if (d < radius) residual *= Mathf.Pow(d / radius, power);
        }
        return residual;
    }

    private static float Falloff(float distance, float radius, float power, bool square) =>
        square || power <= 0 ? 1f : Mathf.Pow(Mathf.Max(0, 1f - distance / radius), power);

    private static float Footprint(float radius, bool square, Func<float, float> weight)
    {
        int reach = Mathf.CeilToInt(radius);
        float sum = 0;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            float distance = Mathf.Sqrt(x * x + z * z);
            if (!square && distance > radius) continue;
            sum += weight(distance);
        }
        return sum;
    }
}

internal readonly struct DigYield
{
    public readonly ItemDrop Item;
    public readonly float PerHit;
    public readonly bool AtMaxDepth;

    public DigYield(ItemDrop item, float perHit, bool atMaxDepth)
    {
        Item = item;
        PerHit = perHit;
        AtMaxDepth = atMaxDepth;
    }
}
