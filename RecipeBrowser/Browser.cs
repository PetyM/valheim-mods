using BepInEx.Configuration;
using System;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RecipeBrowser;

internal enum View { Recipes, Sources, Upgrades, Usages }

internal enum SortMode { Progression, Name, Mod }

/// <summary>Ties together the results panel with search, the bookmarks panel, the recipe window and the collapsed-panel bars.</summary>
internal sealed class Browser
{
    private const float BarWidth = 250, BarHeight = 52, ResetWidth = 150;

    private readonly RectTransform root;
    private readonly Tooltip tooltip;
    private readonly Catalog catalog;
    private readonly Bookmarks bookmarks;
    private readonly ItemPanel results, bookmarked;
    private readonly SearchBar searchBar;
    private readonly RecipeWindow window;
    private readonly GameObject resultsBar, bookmarksBar;
    private const float StockCheckInterval = 0.5f;
    private bool visible, typing, liveQuery;
    private int stockSignature;
    private float stockCheckedAt;

    public bool Alive => results.Root;
    public bool IsTyping => typing;
    public bool IsRecipeOpen => window.IsOpen;
    public Layout Layout { get; }

    public Browser(Catalog catalog, Bookmarks bookmarks, RectTransform root, Layout layout)
    {
        this.root = root;
        tooltip = new Tooltip(FindTooltipPrefab(InventoryGui.instance), root);
        this.catalog = catalog;
        this.bookmarks = bookmarks;
        Layout = layout;
        Query.IsBookmarked = e => bookmarks.Contains(e.Key);

        results = new ItemPanel(root, layout, false, Open, () => Toggle(UiConfig.ResultsCollapsed));
        searchBar = new SearchBar(results, catalog, Filter);
        results.LayoutHeader = searchBar.Layout;
        results.Frame.Saved = r => UiConfig.Save(UiConfig.ResultsRect, r, root.rect);

        bookmarked = new ItemPanel(root, layout, true, Open, () => Toggle(UiConfig.BookmarksCollapsed), Reorder);
        var title = Ui.Label(bookmarked.Header, "Bookmarks", 0, 0, 100, ItemPanel.RowHeight, 18, colour: Ui.Orange);
        var reset = Ui.Button(bookmarked.Header, "Reset layout", 0, 0, ResetWidth, ItemPanel.RowHeight, ResetLayout);
        reset.GetComponentInChildren<Text>().fontSize = 13;
        var hint = Ui.Label(bookmarked.Header, "A over an item adds / removes it · drag to reorder", 0, 0, 100, 30, 14, colour: Ui.Muted);
        bookmarked.LayoutHeader = inner =>
        {
            Ui.Place(title.gameObject, bookmarked.Header, 0, 0, Mathf.Max(40, bookmarked.HeaderWidth - ResetWidth - 6), ItemPanel.RowHeight);
            Ui.Place(reset.gameObject, bookmarked.Header, Mathf.Max(0, bookmarked.HeaderWidth - ResetWidth), 0, ResetWidth, ItemPanel.RowHeight);
            Ui.Place(hint.gameObject, bookmarked.Header, 0, ItemPanel.RowHeight + 6, inner, 30);
            return ItemPanel.RowHeight + 6 + 30 + 6;
        };
        bookmarked.Frame.Saved = r => UiConfig.Save(UiConfig.BookmarksRect, r, root.rect);

        window = new RecipeWindow(root, catalog, layout.Window);
        resultsBar = CreateBar("Recipes", () => Toggle(UiConfig.ResultsCollapsed));
        bookmarksBar = CreateBar("Bookmarks", () => Toggle(UiConfig.BookmarksCollapsed));
        PlacePanels();
        ApplyCollapsed();
        Filter();
        RenderBookmarks();
    }

    private GameObject CreateBar(string title, Action expand)
    {
        var go = GUIManager.Instance.CreateWoodpanel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, BarWidth, BarHeight, false);
        Ui.Label(go.transform, title, 12, 10, 100, 32, 18, colour: Ui.Orange);
        Ui.Button(go.transform, "Expand", BarWidth - 122, 10, 110, 32, expand);
        go.SetActive(false);
        return go;
    }

    private void PlacePanels()
    {
        var screen = root.rect;
        results.Frame.Place(UiConfig.Load(UiConfig.ResultsRect, screen, Layout.Results));
        bookmarked.Frame.Place(UiConfig.Load(UiConfig.BookmarksRect, screen, Layout.Bookmarks));
        PlaceBars();
    }

    private void PlaceBars()
    {
        Ui.PlaceLocal((RectTransform)resultsBar.transform, root, results.Frame.Rect.min, Vector2.zero);
        Ui.PlaceLocal((RectTransform)bookmarksBar.transform, root, bookmarked.Frame.Rect.min, Vector2.zero);
    }

    public void ResetLayout()
    {
        UiConfig.ClearLayout();
        PlacePanels();
        window.Reset(Layout.Window);
    }

    private static GameObject FindTooltipPrefab(InventoryGui gui)
    {
        var source = gui.m_playerGrid.GetComponentsInChildren<UITooltip>(true)
            .Concat(gui.m_playerGrid.m_elementPrefab.GetComponentsInChildren<UITooltip>(true))
            .Concat(gui.GetComponentsInChildren<UITooltip>(true))
            .FirstOrDefault(t => t.m_tooltipPrefab);
        Log?.Invoke(source ? $"Tooltip from {source.name} ({source.m_tooltipPrefab.name})" : "Tooltip prefab not found");
        return source ? source.m_tooltipPrefab : null;
    }

    public static Action<string> Log { get; set; }

    public void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        if (!visible)
        {
            searchBar.Defocus();
            SetTyping(false);
            tooltip.Hide();
            window.Root.SetActive(false);
        }
        ApplyCollapsed();
    }

    private void ApplyCollapsed()
    {
        bool resultsCollapsed = UiConfig.ResultsCollapsed.Value, bookmarksCollapsed = UiConfig.BookmarksCollapsed.Value;
        results.Root.SetActive(visible && !resultsCollapsed);
        bookmarked.Root.SetActive(visible && !bookmarksCollapsed);
        resultsBar.SetActive(visible && resultsCollapsed);
        bookmarksBar.SetActive(visible && bookmarksCollapsed);
    }

    /// <summary>Hotkey: if anything is expanded, collapses both panels, otherwise expands both.</summary>
    public void ToggleCollapsed()
    {
        bool collapse = !UiConfig.ResultsCollapsed.Value || !UiConfig.BookmarksCollapsed.Value;
        UiConfig.ResultsCollapsed.Value = UiConfig.BookmarksCollapsed.Value = collapse;
        Collapsed();
    }

    private void Toggle(ConfigEntry<bool> entry)
    {
        entry.Value = !entry.Value;
        Collapsed();
    }

    private void Collapsed()
    {
        if (UiConfig.ResultsCollapsed.Value)
        {
            searchBar.Defocus();
            SetTyping(false);
        }
        PlaceBars();
        ApplyCollapsed();
    }

    public void Destroy()
    {
        SetTyping(false);
        tooltip.Hide();
        UnityEngine.Object.Destroy(results.Root);
        UnityEngine.Object.Destroy(bookmarked.Root);
        UnityEngine.Object.Destroy(window.Root);
        UnityEngine.Object.Destroy(resultsBar);
        UnityEngine.Object.Destroy(bookmarksBar);
    }

    public void Tick()
    {
        searchBar.Tick();
        SetTyping(!UiConfig.ResultsCollapsed.Value && searchBar.IsActive);
        tooltip.Update(Slot.Hovered ? Slot.Hovered.Entry : null);
        if (Time.time - stockCheckedAt < StockCheckInterval) return;
        stockCheckedAt = Time.time;
        int signature = Stock.Signature();
        if (signature == stockSignature) return;
        stockSignature = signature;
        window.Refresh();
        if (liveQuery) Filter(resetPage: false);
    }

    private void SetTyping(bool value)
    {
        if (typing == value) return;
        typing = value;
        GUIManager.BlockInput(value);
    }

    public void Open(Entry entry, View view) => window.Open(entry, view);
    public void Back() => window.Back();
    public void Close() => window.Close();

    public void ToggleBookmark(Entry entry, bool remove)
    {
        if (remove) bookmarks.Remove(entry.Key);
        else bookmarks.Add(entry.Key);
        RenderBookmarks();
        if (liveQuery) Filter(resetPage: false);
    }

    private void Reorder(Entry entry, Entry target)
    {
        bookmarks.Move(entry.Key, target?.Key);
        RenderBookmarks();
    }

    private void Filter() => Filter(resetPage: true);

    private void Filter(bool resetPage)
    {
        var query = Query.Parse(searchBar.Text, catalog);
        liveQuery = query.IsLive;
        results.Section = UiConfig.BiomeSeparators.Value && searchBar.Sort == SortMode.Progression ? e => Progression.Name(e.Biome) : null;
        results.Show(catalog.Sorted(searchBar.Sort).Where(query.Matches).ToList(), resetPage);
    }

    private void RenderBookmarks() =>
        bookmarked.Show(bookmarks.Keys.Select(catalog.Find).Where(e => e != null).ToList(), resetPage: false);
}
