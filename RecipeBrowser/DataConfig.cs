using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace RecipeBrowser;

/// <summary>Odkud brát počty „máš“: Auto = truhly jen když běží mód typu CraftFromContainers.</summary>
internal enum ContainerMode { Auto, Always, Never }

/// <summary>Config datové části (katalog, filtr, biomy, počty). Binduje Plugin.Awake.</summary>
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

    /// <summary>Zvýší se při změně nastavení, které mění obsah katalogu (UI pak katalog přestaví).</summary>
    public static int Version { get; private set; }

    public static void Bind(ConfigFile config)
    {
        HideUnobtainable = Rebuilds(config.Bind("Katalog", "Skrýt nedostupné", true,
            "Skryje předměty bez zdroje i bez použití (žádný recept, zpracování, drop, sběr, obchodník ani ingredience)."));
        IgnoreList = Rebuilds(config.Bind("Katalog", "Ignorovat", "",
            "Prefaby, které se nikdy neukážou. Oddělené čárkou: přesné jméno, '*přípona' nebo 'předpona*' (např. 'SwordCheat, *_nochest, NPC_*')."));
        LogFilter = config.Bind("Katalog", "Logovat filtr", false, "Do logu vypíše každou skrytou položku a důvod (ladění).");
        BiomeOverrides = Rebuilds(config.Bind("Biomy", "Přepsat biom", "",
            "Ruční biom položek, má přednost před výpočtem. Formát 'Prefab=biom' oddělené čárkou, biom jako tag (meadows, blackforest, swamp, ocean, mountains, plains, mistlands, ashlands, deepnorth)."));
        ScanLocations = config.Bind("Biomy", "Načíst lokace", true,
            "Po vstupu do světa postupně načte prefaby lokací a dungeonů (truhly, oltáře, tvorové) pro přesnější biomy a „Jak získat“. Vypni, pokud to při vstupu do světa zasekává hru.");
        Containers = config.Bind("Počty", "Truhly v okolí", ContainerMode.Auto,
            "Počítat do „máš“ i obsah truhel v okolí. Auto = jen když běží CraftFromContainers nebo podobný mód, Always = vždy, Never = jen inventář.");
        ContainerRange = config.Bind("Počty", "Dosah truhel", 20f, new ConfigDescription("Dosah truhel v metrech.", new AcceptableValueRange<float>(2f, 100f)));
    }

    private static ConfigEntry<T> Rebuilds<T>(ConfigEntry<T> entry)
    {
        entry.SettingChanged += (_, _) => Version++;
        return entry;
    }

    /// <summary>Ignore list jako predikát nad jménem prefabu.</summary>
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
