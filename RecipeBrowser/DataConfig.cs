using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace RecipeBrowser;

/// <summary>Where "have" counts come from: Auto = chests only when a CraftFromContainers-like mod is running.</summary>
internal enum ContainerMode { Auto, Always, Never }

/// <summary>Config of the data side (catalog, filter, biomes, counts). Bound by Plugin.Awake.</summary>
internal static class DataConfig
{
    public static ManualLogSource Log { get; } = Logger.CreateLogSource("RecipeBrowser.Data");
    public static ConfigEntry<bool> HideUnobtainable { get; private set; }
    public static ConfigEntry<string> IgnoreList { get; private set; }
    public static ConfigEntry<bool> LogFilter { get; private set; }
    public static ConfigEntry<string> BiomeOverrides { get; private set; }
    public static ConfigEntry<ContainerMode> Containers { get; private set; }
    public static ConfigEntry<float> ContainerRange { get; private set; }
    public static ConfigEntry<bool> ScanLocations { get; private set; }

    /// <summary>Incremented when a setting that changes catalog contents changes (the UI then rebuilds the catalog).</summary>
    public static int Version { get; private set; }

    public static void Bind(ConfigFile config)
    {
        HideUnobtainable = Rebuilds(config.Bind("Catalog", "Hide unobtainable", true,
            "Hides items with no source and no use (no recipe, processing, drop, pickup, trader or ingredient use)."));
        IgnoreList = Rebuilds(config.Bind("Catalog", "Ignore list", "",
            "Prefabs that are never shown. Comma separated: exact name, '*suffix' or 'prefix*' (e.g. 'SwordCheat, *_nochest, NPC_*')."));
        LogFilter = config.Bind("Catalog", "Log filter", false, "Logs every hidden entry and the reason (debugging).");
        BiomeOverrides = Rebuilds(config.Bind("Biomes", "Biome overrides", "",
            "Manual biome for entries, takes precedence over the computed one. Format 'Prefab=biome', comma separated, biome as a tag (meadows, blackforest, swamp, ocean, mountains, plains, mistlands, ashlands, deepnorth)."));
        ScanLocations = config.Bind("Biomes", "Scan locations", true,
            "After entering a world, gradually loads location and dungeon prefabs (chests, altars, creatures) for more accurate biomes and Sources. Turn off if it makes the game stutter after entering a world.");
        Containers = config.Bind("Counts", "Nearby chests", ContainerMode.Auto,
            "Include nearby chests in the \"have\" counts. Auto = only when CraftFromContainers or a similar mod is running, Always = always, Never = inventory only.");
        ContainerRange = config.Bind("Counts", "Chest range", 20f, new ConfigDescription("Chest range in meters.", new AcceptableValueRange<float>(2f, 100f)));
    }

    private static ConfigEntry<T> Rebuilds<T>(ConfigEntry<T> entry)
    {
        entry.SettingChanged += (_, _) => Version++;
        return entry;
    }

    /// <summary>Ignore list as a predicate over the prefab name.</summary>
    public static System.Func<string, bool> Ignored()
    {
        var patterns = (IgnoreList?.Value ?? "").Split(',', ';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var exact = new HashSet<string>(patterns.Where(p => !p.Contains('*')), System.StringComparer.OrdinalIgnoreCase);
        var suffixes = patterns.Where(p => p.StartsWith("*")).Select(p => p.Substring(1)).ToList();
        var prefixes = patterns.Where(p => p.EndsWith("*") && !p.StartsWith("*")).Select(p => p.Substring(0, p.Length - 1)).ToList();
        return name => exact.Contains(name)
            || suffixes.Any(s => name.EndsWith(s, System.StringComparison.OrdinalIgnoreCase))
            || prefixes.Any(p => name.StartsWith(p, System.StringComparison.OrdinalIgnoreCase));
    }
}
