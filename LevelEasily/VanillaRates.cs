using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace LevelEasily;

/// <summary>Sazby vanilla nástrojů načtené z prefabů, aby cena sledovala hru i módy, které je mění.</summary>
internal sealed class VanillaRates
{
    /// <summary>Kolik vanilla švihů musí vrchol nechat nesrovnané, aby se mřížka švihů počítala za hotovou.</summary>
    private const float MaxResidual = 0.05f;

    public Piece LevelPiece { get; }
    public TerrainOp LevelOp { get; }
    public int VerticesPerSwing { get; }
    public float RaiseVolumePerSwing { get; }
    public ItemDrop Stone { get; }
    public int StonePerRaiseSwing { get; }

    private readonly ManualLogSource log;
    private readonly Dictionary<ItemDrop.ItemData.SharedData, float> digVolumes = new();

    private VanillaRates(PieceTable hoe, ManualLogSource log)
    {
        this.log = log;
        LevelPiece = hoe.m_pieces.First(p => p && p.name == "mud_road_v2").GetComponent<Piece>();
        var raise = hoe.m_pieces.First(p => p && p.name == "raise_v2").GetComponent<Piece>();
        LevelOp = LevelPiece.GetComponentInChildren<TerrainOp>(true);
        var levelOp = LevelOp.m_settings;
        var raiseOp = raise.GetComponentInChildren<TerrainOp>(true).m_settings;
        VerticesPerSwing = SmoothVerticesPerSwing(levelOp.m_smoothRadius, levelOp.m_smoothPower);
        RaiseVolumePerSwing = Footprint(raiseOp.m_raiseRadius, raiseOp.m_square,
            d => raiseOp.m_raiseDelta * Falloff(d, raiseOp.m_raiseRadius, raiseOp.m_raisePower, raiseOp.m_square));
        Stone = raise.m_resources[0].m_resItem;
        StonePerRaiseSwing = raise.m_resources[0].m_amount;

        log.LogInfo($"Level ground: smooth r{levelOp.m_smoothRadius} power {levelOp.m_smoothPower} → {VerticesPerSwing} vrcholů na švih; " +
                    $"piece groundPiece={LevelPiece.m_groundPiece} altGround={LevelPiece.m_allowAltGroundPlacement} " +
                    $"dungeon={LevelPiece.m_allowedInDungeons} deepSnow={LevelPiece.m_allowedInDeepSnow}");
        log.LogInfo($"Raise ground: {RaiseVolumePerSwing:0.00} m³ a {StonePerRaiseSwing}× {Stone.name} na švih");
    }

    public static VanillaRates FromHoe(PieceTable hoe, ManualLogSource log) => new(hoe, log);

    /// <summary>Objem, který jeden úder nástrojem do terénu vykope; 0 = nástroj nekope.</summary>
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
                        $"raise={s.m_raise} r{s.m_raiseRadius} delta {s.m_raiseDelta}, square={s.m_square} → {volume:0.00} m³ na úder");
        }
        digVolumes[shared] = volume;
        return volume;
    }

    /// <summary>
    /// Kolik vrcholů srovná jeden švih, když hráč švihá v pravidelné mřížce: k² pro největší rozestup k,
    /// při kterém žádný vrchol nezůstane víc než <see cref="MaxResidual"/> od cíle (smooth jen přibližuje).
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
