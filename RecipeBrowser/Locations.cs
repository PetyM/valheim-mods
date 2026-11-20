using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Bootstrap;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SoftReferenceableAssets;
using UnityEngine;

namespace RecipeBrowser;

/// <summary>
/// Biomes of prefabs from locations and their dungeons (SPEC 1.3 and 1.4: chests in locations, altars, creatures only found in locations).
/// Location prefabs are SoftReferences in asset bundles, so they are loaded gradually in a coroutine once per world
/// and Version is incremented when done (the catalog gets rebuilt). Only prefab names and biome ranks are kept.
/// </summary>
internal static class Locations
{
    private const int Batch = 8;
    private const float LoadTimeout = 30f;

    private static ZoneSystem scannedFor;
    private static Dictionary<string, SortedSet<int>> places = new(), creatures = new();

    /// <summary>Incremented when the scan finishes; Catalog.IsStale compares it.</summary>
    public static int Version { get; private set; }
    public static bool Done { get; private set; }
    /// <summary>Prefabs with a ZNetView placed in locations (chests, pickables, ores, traders…) and the biomes the locations are in.</summary>
    public static IReadOnlyDictionary<string, SortedSet<int>> Places => places;
    /// <summary>Creatures from spawners, spawn areas and altars in locations.</summary>
    public static IReadOnlyDictionary<string, SortedSet<int>> Creatures => creatures;

    /// <summary>Starts the scan if it has not run for the current ZoneSystem yet. Call only in a world.</summary>
    public static void EnsureStarted(MonoBehaviour host)
    {
        var zone = ZoneSystem.instance;
        if (!zone || scannedFor == zone) return;
        scannedFor = zone;
        places = new Dictionary<string, SortedSet<int>>();
        creatures = new Dictionary<string, SortedSet<int>>();
        Done = false;
        string key = CacheKey(zone);
        if (LoadCache(key)) return;
        host.StartCoroutine(Scan(zone, key));
    }

    // Location contents depend only on the game version and the installed mods, not on the world:
    // the scan result is cached on disk and rescanned only when that changes.
    private static string CachePath => Path.Combine(BepInEx.Paths.CachePath, "michal.recipebrowser.locations.txt");

    private static string CacheKey(ZoneSystem zone)
    {
        var plugins = Chainloader.PluginInfos.Values.Select(p => $"{p.Metadata.GUID}@{p.Metadata.Version}").OrderBy(p => p, StringComparer.Ordinal);
        var locations = zone.m_locations.Where(l => l != null && l.m_enable && l.m_prefab.IsValid)
            .Select(l => $"{l.m_prefab.Name}:{(int)l.m_biome}").OrderBy(l => l, StringComparer.Ordinal);
        string text = global::Version.GetVersionString() + "|" + string.Join(",", plugins) + "|" + string.Join(",", locations);
        using var sha = SHA1.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }

    private static bool LoadCache(string key)
    {
        try
        {
            if (!File.Exists(CachePath)) return false;
            var lines = File.ReadAllLines(CachePath);
            if (lines.Length == 0 || lines[0] != "key\t" + key) return false;
            var loadedPlaces = new Dictionary<string, SortedSet<int>>();
            var loadedCreatures = new Dictionary<string, SortedSet<int>>();
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) continue;
                var map = parts[0] == "P" ? loadedPlaces : parts[0] == "C" ? loadedCreatures : null;
                map?.Add(parts[1], new SortedSet<int>(parts[2].Split(',').Select(int.Parse)));
            }
            Publish(loadedPlaces, loadedCreatures);
            DataConfig.Log.LogInfo($"Locations: loaded from cache, {places.Count} prefabs, {creatures.Count} creatures.");
            return true;
        }
        catch (Exception e)
        {
            DataConfig.Log.LogWarning($"Location cache unreadable, rescanning: {e.Message}");
            return false;
        }
    }

    private static void SaveCache(string key)
    {
        try
        {
            var lines = new List<string> { "key\t" + key };
            lines.AddRange(places.Select(p => $"P\t{p.Key}\t{string.Join(",", p.Value)}"));
            lines.AddRange(creatures.Select(c => $"C\t{c.Key}\t{string.Join(",", c.Value)}"));
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            File.WriteAllLines(CachePath, lines);
        }
        catch (Exception e)
        {
            DataConfig.Log.LogWarning($"Location cache not saved: {e.Message}");
        }
    }

    private static void Publish(Dictionary<string, SortedSet<int>> newPlaces, Dictionary<string, SortedSet<int>> newCreatures)
    {
        places = newPlaces;
        creatures = newCreatures;
        Done = true;
        Version++;
    }

    private static IEnumerator Scan(ZoneSystem zone, string key)
    {
        var watch = Stopwatch.StartNew();
        // Own maps, published only at the end: a scan of an old world must not write into the new one.
        var foundPlaces = new Dictionary<string, SortedSet<int>>();
        var foundCreatures = new Dictionary<string, SortedSet<int>>();
        void ScanPrefab(GameObject go, List<int> ranks) => Locations.ScanPrefab(go, ranks, foundPlaces, foundCreatures);
        var dungeons = new List<(Room.Theme themes, List<int> ranks)>();
        var locations = zone.m_locations.Where(l => l != null && l.m_enable && l.m_prefab.IsValid)
            .Select(l => (asset: l.m_prefab, ranks: Progression.RanksOf(l.m_biome).ToList())).Where(l => l.ranks.Count > 0).ToList();
        for (var step = Load(locations, (go, ranks) =>
             {
                 ScanPrefab(go, ranks);
                 foreach (var generator in go.GetComponentsInChildren<DungeonGenerator>(true)) dungeons.Add((generator.m_themes, ranks));
             }); step.MoveNext();)
            yield return step.Current;
        int roomCount = 0;
        if (DungeonDB.instance && dungeons.Count > 0)
        {
            var rooms = DungeonDB.GetRooms().Where(r => r != null && r.m_enabled && r.m_prefab.IsValid)
                .Select(r => (asset: r.m_prefab, ranks: dungeons.Where(d => (d.themes & r.m_theme) != 0).SelectMany(d => d.ranks).Distinct().ToList()))
                .Where(r => r.ranks.Count > 0).ToList();
            roomCount = rooms.Count;
            for (var step = Load(rooms, ScanPrefab); step.MoveNext();) yield return step.Current;
        }
        if (scannedFor != zone) yield break;
        Publish(foundPlaces, foundCreatures);
        SaveCache(key);
        DataConfig.Log.LogInfo("Locations: dungeon themes " + string.Join(", ", dungeons.Select(d => d.themes.ToString()).Distinct()) +
            $"; all rooms {DungeonDB.GetRooms()?.Count ?? 0}, room themes " + string.Join(", ", (DungeonDB.GetRooms() ?? new List<DungeonDB.RoomData>()).Where(r => r != null).GroupBy(r => r.m_theme).Select(g => $"{g.Key} {g.Count()}")));
        DataConfig.Log.LogInfo($"Locations: {locations.Count} locations, {roomCount} dungeon rooms, {places.Count} prefabs, {creatures.Count} creatures, {watch.ElapsedMilliseconds} ms (cached for next start).");
    }

    // In batches: LoadAsync holds a reference, Release returns it (the bundle is unloaded when nobody else holds it).
    private static IEnumerator Load(List<(SoftReference<GameObject> asset, List<int> ranks)> list, System.Action<GameObject, List<int>> scan)
    {
        for (int i = 0; i < list.Count && scannedFor; i += Batch)
        {
            var batch = list.Skip(i).Take(Batch).ToList();
            foreach (var item in batch) item.asset.LoadAsync();
            float deadline = Time.realtimeSinceStartup + LoadTimeout;
            while (batch.Any(b => b.asset.IsLoading) && Time.realtimeSinceStartup < deadline) yield return null;
            foreach (var item in batch)
                try
                {
                    if (item.asset.IsLoaded && item.asset.Asset is { } go) scan(go, item.ranks);
                }
                catch (Exception e)
                {
                    DataConfig.Log.LogWarning($"Location {item.asset.Name} skipped: {e.Message}");
                }
                finally
                {
                    item.asset.Release();
                }
        }
    }

    private static void ScanPrefab(GameObject go, List<int> ranks, Dictionary<string, SortedSet<int>> places, Dictionary<string, SortedSet<int>> creatures)
    {
        foreach (var view in go.GetComponentsInChildren<ZNetView>(true))
        {
            Mark(places, PrefabName(view.gameObject.name), ranks);
            if (view.GetComponent<Character>()) Mark(creatures, PrefabName(view.gameObject.name), ranks);
        }
        foreach (var spawner in go.GetComponentsInChildren<CreatureSpawner>(true).Where(s => s.m_creaturePrefab)) Mark(creatures, spawner.m_creaturePrefab.name, ranks);
        foreach (var area in go.GetComponentsInChildren<SpawnArea>(true))
            foreach (var spawn in area.m_prefabs.Where(p => p.m_prefab)) Mark(creatures, spawn.m_prefab.name, ranks);
        foreach (var bowl in go.GetComponentsInChildren<OfferingBowl>(true).Where(b => b.m_bossPrefab)) Mark(creatures, bowl.m_bossPrefab.name, ranks);
    }

    private static void Mark(Dictionary<string, SortedSet<int>> map, string name, List<int> ranks)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!map.TryGetValue(name, out var set)) map[name] = set = new SortedSet<int>();
        set.UnionWith(ranks);
    }

    // A nested prefab in a location is named like the prefab, possibly with " (1)".
    private static string PrefabName(string name)
    {
        int index = name.IndexOf(" (", System.StringComparison.Ordinal);
        return index > 0 ? name.Substring(0, index) : name;
    }
}
