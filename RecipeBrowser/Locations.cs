using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SoftReferenceableAssets;
using UnityEngine;

namespace RecipeBrowser;

/// <summary>
/// Biomy prefabů z lokací a jejich dungeonů (SPEC 1.3 a 1.4: truhly v lokaci, oltáře, tvorové jen z lokací).
/// Prefaby lokací jsou SoftReference v asset bundlech, proto se načítají postupně v coroutině jednou za svět
/// a po dokončení se zvýší Version (katalog se přestaví). Drží se jen jména prefabů a pořadí biomů.
/// </summary>
internal static class Locations
{
    private const int Batch = 8;
    private const float LoadTimeout = 30f;

    private static ZoneSystem scannedFor;
    private static Dictionary<string, SortedSet<int>> places = new(), creatures = new();

    /// <summary>Zvýší se po dokončení skenu; Catalog.IsStale ho porovnává.</summary>
    public static int Version { get; private set; }
    public static bool Done { get; private set; }
    /// <summary>Prefaby se ZNetView umístěné v lokacích (truhly, sběr, rudy, obchodníci…) a biomy, kde lokace stojí.</summary>
    public static IReadOnlyDictionary<string, SortedSet<int>> Places => places;
    /// <summary>Tvorové ze spawnerů, spawn oblastí a oltářů v lokacích.</summary>
    public static IReadOnlyDictionary<string, SortedSet<int>> Creatures => creatures;

    /// <summary>Spustí sken, pokud pro aktuální ZoneSystem ještě neběžel. Volat jen ve světě.</summary>
    public static void EnsureStarted(MonoBehaviour host)
    {
        var zone = ZoneSystem.instance;
        if (!zone || scannedFor == zone) return;
        scannedFor = zone;
        places = new Dictionary<string, SortedSet<int>>();
        creatures = new Dictionary<string, SortedSet<int>>();
        Done = false;
        host.StartCoroutine(Scan(zone));
    }

    private static IEnumerator Scan(ZoneSystem zone)
    {
        var watch = Stopwatch.StartNew();
        // Vlastní mapy, zveřejní se až na konci: sken starého světa nesmí psát do nového.
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
        places = foundPlaces;
        creatures = foundCreatures;
        Done = true;
        Version++;
        DataConfig.Log.LogInfo($"Lokace: {locations.Count} lokací, {roomCount} místností dungeonů, {places.Count} prefabů, {creatures.Count} tvorů, {watch.ElapsedMilliseconds} ms.");
    }

    // Po dávkách: LoadAsync drží referenci, Release ji vrátí (bundle se uvolní, když ho nedrží nikdo jiný).
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
                catch (System.Exception e)
                {
                    DataConfig.Log.LogWarning($"Lokace {item.asset.Name} přeskočena: {e.Message}");
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

    // Vnořený prefab v lokaci se jmenuje jako prefab, případně s " (1)".
    private static string PrefabName(string name)
    {
        int index = name.IndexOf(" (", System.StringComparison.Ordinal);
        return index > 0 ? name.Substring(0, index) : name;
    }
}
