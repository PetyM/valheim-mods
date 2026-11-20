using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LevelEasily;

internal enum Shortage { None, Stamina, HoeWear, PickaxeWear, Stone }

internal readonly struct Cost
{
    public readonly int Vertices, HoeSwings, RaiseSwings, PickHits, Stone;
    public readonly float Stamina, HoeWear, PickWear, Seconds;

    public Cost(int vertices, int hoeSwings, int raiseSwings, int pickHits, int stone, float stamina, float hoeWear, float pickWear, float seconds)
    {
        Vertices = vertices;
        HoeSwings = hoeSwings;
        RaiseSwings = raiseSwings;
        PickHits = pickHits;
        Stone = stone;
        Stamina = stamina;
        HoeWear = hoeWear;
        PickWear = pickWear;
        Seconds = seconds;
    }

    public int AllSwings => HoeSwings + RaiseSwings;
}

/// <summary>Zdroje hráče a ceny jednotlivých úkonů v okamžiku výpočtu.</summary>
internal sealed class Budget
{
    public ItemDrop.ItemData Hoe;
    public ItemDrop.ItemData Pickaxe;
    public float HoeStamina, HoeWear, PickStamina, PickWear, PickVolume, PlaceDelay, PickSeconds;
    public float Stamina, HoeDurability, PickDurability;
    public int Stone;

    public static Budget Of(Player player, VanillaRates rates, float pickSeconds)
    {
        var hoe = player.GetRightItem();
        var budget = new Budget
        {
            Hoe = hoe,
            HoeStamina = player.GetBuildStamina(),
            HoeWear = hoe.m_shared.m_useDurability ? player.GetPlaceDurability(hoe) * Game.m_durabilityRate : 0,
            HoeDurability = hoe.m_shared.m_useDurability ? hoe.m_durability : float.PositiveInfinity,
            Stamina = player.GetStamina(),
            Stone = player.m_noPlacementCost ? int.MaxValue : player.GetInventory().CountItems(rates.Stone.m_itemData.m_shared.m_name),
            PlaceDelay = player.m_placeDelay,
            PickSeconds = pickSeconds,
        };
        budget.Pickaxe = player.GetInventory().GetAllItems()
            .Where(i => i.m_shared.m_skillType == Skills.SkillType.Pickaxes && rates.DigVolumePerHit(i) > 0 &&
                        (!i.m_shared.m_useDurability || i.m_durability > 0))
            .OrderByDescending(i => HitsLeft(i) * rates.DigVolumePerHit(i))
            .FirstOrDefault();
        if (budget.Pickaxe != null)
        {
            var pick = budget.Pickaxe;
            budget.PickVolume = rates.DigVolumePerHit(pick);
            budget.PickStamina = AttackStamina(player, pick);
            budget.PickWear = pick.m_shared.m_useDurability ? pick.m_shared.m_useDurabilityDrain * Game.m_durabilityRate : 0;
            budget.PickDurability = pick.m_shared.m_useDurability ? pick.m_durability : float.PositiveInfinity;
        }
        return budget;
    }

    /// <summary>Kolik úderů nástroj ještě vydrží.</summary>
    private static float HitsLeft(ItemDrop.ItemData tool)
    {
        float wear = tool.m_shared.m_useDurabilityDrain * Game.m_durabilityRate;
        return tool.m_shared.m_useDurability && wear > 0 ? tool.m_durability / wear : float.PositiveInfinity;
    }

    /// <summary>Stejný výpočet jako <c>Attack.GetAttackStamina</c> pro útok nástrojem, který není v ruce.</summary>
    private static float AttackStamina(Player player, ItemDrop.ItemData tool)
    {
        float stamina = tool.m_shared.m_attack.m_attackStamina;
        if (stamina <= 0) return 0;
        stamina *= 1f + player.GetEquipmentAttackStaminaModifier();
        player.GetSEMan().ModifyAttackStaminaUsage(stamina, ref stamina);
        return stamina - stamina * 0.33f * player.GetSkillFactor(tool.m_shared.m_skillType);
    }

    public Cost CostOf(int vertices, float stoneVolume, float pickVolume, VanillaRates rates)
    {
        int raiseSwings = stoneVolume > AreaPlan.ChangeThreshold ? Mathf.CeilToInt(stoneVolume / rates.RaiseVolumePerSwing) : 0;
        int hoeSwings = vertices > 0 ? Mathf.CeilToInt(vertices / (float)rates.VerticesPerSwing) : 0;
        int pickHits = pickVolume > AreaPlan.ChangeThreshold && PickVolume > 0 ? Mathf.CeilToInt(pickVolume / PickVolume) : 0;
        int swings = hoeSwings + raiseSwings;
        return new Cost(vertices, hoeSwings, raiseSwings, pickHits, raiseSwings * rates.StonePerRaiseSwing,
            swings * HoeStamina + pickHits * PickStamina, swings * HoeWear, pickHits * PickWear,
            swings * PlaceDelay + pickHits * PickSeconds);
    }

    private Shortage Check(Cost cost)
    {
        if (cost.Stamina > Stamina) return Shortage.Stamina;
        if (cost.HoeWear > HoeDurability) return Shortage.HoeWear;
        if (cost.PickWear > PickDurability) return Shortage.PickaxeWear;
        if (cost.Stone > Stone) return Shortage.Stone;
        return Shortage.None;
    }

    /// <summary>Vybere vrcholy, na které zdroje stačí, od nejbližších k hráči, a vrátí jejich cenu.</summary>
    /// <param name="allowed">Jen vrcholy, které smí zápis změnit (např. jen v převzatých zónách).</param>
    public (Cost cost, Shortage shortage) Select(AreaPlan plan, Vector3 player, VanillaRates rates, System.Func<VertexPlan, bool> allowed = null)
    {
        wanted.Clear();
        foreach (var v in plan.Vertices)
        {
            v.Selected = false;
            if (v.Wanted && (allowed == null || allowed(v))) wanted.Add(v);
        }
        wanted.Sort((a, b) => Flat(a.Point - player).CompareTo(Flat(b.Point - player)));

        var cost = CostOf(0, 0, 0, rates);
        int count = 0;
        float stone = 0, pick = 0;
        foreach (var v in wanted)
        {
            var next = CostOf(count + 1, stone + v.StoneVolume, pick + v.PickVolume, rates);
            var shortage = Check(next);
            if (shortage != Shortage.None) return (cost, shortage);
            v.Selected = true;
            count++;
            stone += v.StoneVolume;
            pick += v.PickVolume;
            cost = next;
        }
        return (cost, Shortage.None);
    }

    private static readonly List<VertexPlan> wanted = new();

    private static float Flat(Vector3 v) => v.x * v.x + v.z * v.z;
}
