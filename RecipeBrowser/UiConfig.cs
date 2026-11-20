using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace RecipeBrowser;

/// <summary>UI config: sorting, collapsing and the saved panel layout (relative to the screen).</summary>
internal static class UiConfig
{
    private const string LayoutSection = "Layout";

    public static ConfigEntry<SortMode> Sort { get; private set; }
    public static ConfigEntry<bool> ResultsCollapsed { get; private set; }
    public static ConfigEntry<bool> BookmarksCollapsed { get; private set; }
    public static ConfigEntry<bool> BiomeSeparators { get; private set; }
    public static ConfigEntry<bool> ResetLayout { get; private set; }
    public static ConfigEntry<string> ResultsRect { get; private set; }
    public static ConfigEntry<string> BookmarksRect { get; private set; }
    public static ConfigEntry<string> WindowRect { get; private set; }

    public static void Bind(ConfigFile config)
    {
        Sort = config.Bind("Display", "Sort", SortMode.Progression, "Result sorting: Progression = progression (biome, category, station), Name = name, Mod = mod.");
        BiomeSeparators = config.Bind("Display", "Biome separators", false, "When sorting by progression, inserts a row with the biome name before each group in the grid.");
        ResultsCollapsed = config.Bind("Display", "Collapse results", false, "The results panel is collapsed into a small bar.");
        BookmarksCollapsed = config.Bind("Display", "Collapse bookmarks", false, "The bookmarks panel is collapsed into a small bar.");
        ResetLayout = config.Bind(LayoutSection, "Reset layout", false, "Turn on to return the panels to their default position and size (switches itself off afterwards).");
        ResultsRect = config.Bind(LayoutSection, "Results", "", "Position and size of the results panel (x;y;width;height as a fraction of the screen). Empty = default.");
        BookmarksRect = config.Bind(LayoutSection, "Bookmarks", "", "Position and size of the bookmarks panel. Empty = default.");
        WindowRect = config.Bind(LayoutSection, "Recipe window", "", "Position and size of the recipe window. Empty = default.");
    }

    public static void ClearLayout()
    {
        ResultsRect.Value = BookmarksRect.Value = WindowRect.Value = "";
        ResetLayout.Value = false;
    }

    /// <summary>The saved rectangle converted to local canvas coordinates, or the default.</summary>
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
