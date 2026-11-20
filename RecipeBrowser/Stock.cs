using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using UnityEngine;

namespace RecipeBrowser;

/// <summary>
/// How many of an entry the player has (inventory, optionally nearby chests) and whether it can be crafted right now.
/// Results are cached until the end of the frame or until Invalidate.
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

    /// <summary>Are nearby chests counted too (per config and presence of a CraftFromContainers-like mod)?</summary>
    public static bool UsesContainers => DataConfig.Containers?.Value switch
    {
        ContainerMode.Always => true,
        ContainerMode.Auto => containerMod ??= Chainloader.PluginInfos.Values.Any(p =>
            ContainerMods.Any(m => p.Metadata.GUID.ToLowerInvariant().Contains(m) || p.Metadata.Name.ToLowerInvariant().Replace(" ", "").Contains(m))),
        _ => false,
    };

    /// <summary>Item count, -1 = cannot be counted (a piece, or the player is not in a world).</summary>
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
    /// Can the entry be crafted now? Some crafting or build card has its resources available (inventory, possibly chests)
    /// and its station at the required level is in range, or it needs no station.
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
    /// Cheap fingerprint of the state that counts and #craftable depend on: inventory contents, the station the player stands at, and the
    /// 4 m grid square they stand in (chests and stations in range). On change the UI redraws cards and refilters.
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

    /// <summary>Drops the count cache (called by the UI once per frame / on redraw).</summary>
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

    // The chest list is refreshed only every few seconds (FindObjectsOfType is expensive); range and access are checked every frame.
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
