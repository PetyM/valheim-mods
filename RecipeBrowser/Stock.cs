using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using UnityEngine;

namespace RecipeBrowser;

/// <summary>
/// Kolik má hráč dané položky (inventář, volitelně truhly v okolí) a jestli jde položka vyrobit hned.
/// Výsledky se drží do konce snímku nebo do Invalidate.
/// </summary>
internal static class Stock
{
    private const float ContainerScanInterval = 2f;
    private static readonly string[] ContainerMods = { "craftfromcontainers", "craftyboxes", "craftfromchests", "craftfromcontainer" };
    private static readonly Dictionary<string, int> counts = new();
    private static readonly Dictionary<Entry, bool> craftable = new();
    private static readonly Dictionary<(string, int), bool> stations = new();
    private static readonly List<CraftingStation> buffer = new();
    private static List<Container> nearby = new();
    private static Container[] allContainers = new Container[0];
    private static float scannedAt = -100;
    private static int frame = -1;
    private static bool? containerMod;

    /// <summary>Počítají se i truhly v okolí (podle configu a přítomnosti módu typu CraftFromContainers)?</summary>
    public static bool UsesContainers => DataConfig.Containers?.Value switch
    {
        ContainerMode.Always => true,
        ContainerMode.Auto => containerMod ??= Chainloader.PluginInfos.Values.Any(p =>
            ContainerMods.Any(m => p.Metadata.GUID.ToLowerInvariant().Contains(m) || p.Metadata.Name.ToLowerInvariant().Replace(" ", "").Contains(m))),
        _ => false,
    };

    /// <summary>Počet kusů, -1 = nelze spočítat (stavba nebo hráč není ve světě).</summary>
    public static int Count(Entry entry)
    {
        var player = Player.m_localPlayer;
        if (entry?.ItemName == null || !player) return -1;
        Refresh(player);
        if (counts.TryGetValue(entry.ItemName, out var count)) return count;
        count = player.GetInventory().CountItems(entry.ItemName);
        foreach (var container in nearby)
            if (container && container.GetInventory() is { } inventory) count += inventory.CountItems(entry.ItemName);
        return counts[entry.ItemName] = count;
    }

    /// <summary>
    /// Jde položku teď vyrobit? Některá karta výroby nebo stavby má splněné suroviny (inventář, případně truhly)
    /// a její stanice na potřebné úrovni je v dosahu, nebo stanici nepotřebuje.
    /// </summary>
    public static bool CanCraft(Entry entry, Catalog catalog)
    {
        var player = Player.m_localPlayer;
        if (entry == null || catalog == null || !player) return false;
        Refresh(player);
        if (craftable.TryGetValue(entry, out var result)) return result;
        result = false;
        foreach (var card in catalog.Cards(entry, View.Recipes))
            if (card.Kind is CardKind.Craft or CardKind.Build && !card.IsUpgrade && HasInputs(card) && StationInRange(card, catalog, player))
            {
                result = true;
                break;
            }
        return craftable[entry] = result;
    }

    /// <summary>
    /// Levný otisk stavu, na kterém závisí počty a #craftable: obsah inventáře, stanice, u které hráč stojí, a čtverec
    /// po 4 m, kde stojí (truhly a stanice v dosahu). UI podle změny překreslí karty a přefiltruje.
    /// </summary>
    public static int Signature()
    {
        var player = Player.m_localPlayer;
        if (!player) return 0;
        unchecked
        {
            int hash = 17;
            foreach (var item in player.GetInventory().GetAllItems())
                hash = hash * 31 + (item.m_shared.m_name?.GetHashCode() ?? 0) * 7 + item.m_stack;
            var station = player.GetCurrentCraftingStation();
            hash = hash * 31 + (station ? station.GetInstanceID() : 0);
            var p = player.transform.position;
            return hash * 31 + Mathf.FloorToInt(p.x / 4) * 7919 + Mathf.FloorToInt(p.z / 4);
        }
    }

    /// <summary>Zahodí mezipaměť počtů (volá UI jednou za snímek / při překreslení).</summary>
    public static void Invalidate()
    {
        frame = -1;
        counts.Clear();
        craftable.Clear();
        stations.Clear();
    }

    private static void Refresh(Player player)
    {
        if (frame == Time.frameCount) return;
        Invalidate();
        frame = Time.frameCount;
        nearby = UsesContainers ? FindContainers(player) : new List<Container>();
    }

    private static bool HasInputs(RecipeCard card)
    {
        if (card.Inputs.Count == 0) return true;
        if (card.OnlyOneIngredient) return card.Inputs.Any(i => Count(i.Entry) >= i.Amount);
        foreach (var input in card.Inputs)
            if (Count(input.Entry) < input.Amount) return false;
        return true;
    }

    private static bool StationInRange(RecipeCard card, Catalog catalog, Player player)
    {
        if (card.Station == null) return true;
        string name = catalog.StationName(card.Station);
        if (name == null) return false;
        int level = Mathf.Max(1, card.StationLevel);
        if (stations.TryGetValue((name, level), out var ok)) return ok;
        var position = player.transform.position;
        buffer.Clear();
        CraftingStation.FindStationsInRange(name, position, 100f, buffer);
        ok = buffer.Any(s => s && Vector3.Distance(s.transform.position, position) <= s.GetStationBuildRange() && s.GetLevel() >= level);
        return stations[(name, level)] = ok;
    }

    // Seznam truhel se obnovuje jen jednou za pár sekund (FindObjectsOfType je drahý), dosah a přístup se kontroluje každý snímek.
    private static List<Container> FindContainers(Player player)
    {
        if (Time.realtimeSinceStartup - scannedAt > ContainerScanInterval)
        {
            allContainers = Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
            scannedAt = Time.realtimeSinceStartup;
        }
        var position = player.transform.position;
        float range = DataConfig.ContainerRange?.Value ?? 20f;
        long id = player.GetPlayerID();
        return allContainers.Where(c => c && c.m_nview && c.m_nview.IsValid() && c.GetInventory() != null && !c.GetComponent<TombStone>()
            && Vector3.Distance(c.transform.position, position) <= range && c.CheckAccess(id)
            && (!c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, flash: false))).ToList();
    }
}
