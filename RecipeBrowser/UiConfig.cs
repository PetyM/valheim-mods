using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace RecipeBrowser;

/// <summary>Config UI části: řazení, sbalení a uložené rozložení panelů (relativně k obrazovce).</summary>
internal static class UiConfig
{
    private const string LayoutSection = "Rozložení";

    public static ConfigEntry<SortMode> Sort { get; private set; }
    public static ConfigEntry<bool> Collapsed { get; private set; }
    public static ConfigEntry<bool> BiomeSeparators { get; private set; }
    public static ConfigEntry<bool> ResetLayout { get; private set; }
    public static ConfigEntry<string> ResultsRect { get; private set; }
    public static ConfigEntry<string> BookmarksRect { get; private set; }
    public static ConfigEntry<string> WindowRect { get; private set; }

    public static void Bind(ConfigFile config)
    {
        Sort = config.Bind("Zobrazení", "Řazení", SortMode.Progression, "Řazení výsledků: Progression = postup (biom, kategorie, stanice), Name = název, Mod = mód.");
        BiomeSeparators = config.Bind("Zobrazení", "Oddělovač biomu", false, "Při řazení podle postupu vloží do mřížky řádek s názvem biomu před každou skupinu.");
        Collapsed = config.Bind("Zobrazení", "Sbaleno", false, "Panely jsou sbalené do malé lišty.");
        ResetLayout = config.Bind(LayoutSection, "Obnovit rozložení", false, "Zapnutím vrátíš panely na výchozí místo a velikost (pak se samo vypne).");
        ResultsRect = config.Bind(LayoutSection, "Výsledky", "", "Pozice a velikost panelu výsledků (x;y;šířka;výška jako podíl obrazovky). Prázdné = výchozí.");
        BookmarksRect = config.Bind(LayoutSection, "Záložky", "", "Pozice a velikost panelu záložek. Prázdné = výchozí.");
        WindowRect = config.Bind(LayoutSection, "Okno receptu", "", "Pozice a velikost okna receptu. Prázdné = výchozí.");
    }

    public static void ClearLayout()
    {
        ResultsRect.Value = BookmarksRect.Value = WindowRect.Value = "";
        ResetLayout.Value = false;
    }

    /// <summary>Uložený obdélník převedený do lokálních souřadnic plátna, nebo výchozí.</summary>
    public static Rect Load(ConfigEntry<string> entry, Rect screen, Rect fallback)
    {
        var parts = (entry.Value ?? "").Split(';');
        if (parts.Length != 4) return fallback;
        var v = new float[4];
        for (int i = 0; i < 4; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return fallback;
        if (v[2] <= 0 || v[3] <= 0) return fallback;
        return new Rect(screen.xMin + v[0] * screen.width, screen.yMin + v[1] * screen.height, v[2] * screen.width, v[3] * screen.height);
    }

    public static void Save(ConfigEntry<string> entry, Rect rect, Rect screen) =>
        entry.Value = string.Join(";", new[]
        {
            (rect.xMin - screen.xMin) / screen.width, (rect.yMin - screen.yMin) / screen.height, rect.width / screen.width, rect.height / screen.height,
        }.Select(f => f.ToString("0.#####", CultureInfo.InvariantCulture)));
}
