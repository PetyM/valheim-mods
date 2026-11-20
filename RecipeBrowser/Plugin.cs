using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace RecipeBrowser;

[BepInPlugin("michal.recipebrowser", "RecipeBrowser", "1.0.0")]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    private ConfigEntry<KeyboardShortcut> recipesKey, usagesKey, bookmarkKey, backKey, collapseKey;
    private ConfigEntry<int> rows;
    private Bookmarks bookmarks;
    private Catalog catalog;
    private ObjectDB catalogSource;
    private Browser browser;
    private const float SettleDelay = 0.15f, RetryDelay = 10f;
    private float retryAt;
    private float openedAt = -1;
    private Layout layout;
    private static Entry hoveredEntry;
    private static bool recipeOpen;
    private static int escapeConsumedFrame = -1;
    private static KeyCode[] actionKeys = new KeyCode[0];

    private void Awake()
    {
        ModQuery.Enable();
        recipesKey = Config.Bind("Keys", "Recipes", new KeyboardShortcut(KeyCode.R), "Recipes for the item under the cursor.");
        usagesKey = Config.Bind("Keys", "Usages", new KeyboardShortcut(KeyCode.U), "Where the item under the cursor is used.");
        bookmarkKey = Config.Bind("Keys", "Bookmark", new KeyboardShortcut(KeyCode.A), "Bookmarks the item under the cursor; in the bookmarks panel removes it.");
        backKey = Config.Bind("Keys", "Back", new KeyboardShortcut(KeyCode.Backspace));
        collapseKey = Config.Bind("Keys", "Collapse", new KeyboardShortcut(KeyCode.Y), "Collapses or expands the panels (only with the inventory open).");
        DataConfig.Bind(Config);
        UiConfig.Bind(Config);
        rows = Config.Bind("Display", "Rows", 5, new ConfigDescription("Number of rows in the bottom panels.", new AcceptableValueRange<int>(2, 10)));
        new Harmony("michal.recipebrowser").PatchAll();
        Browser.Log = Logger.LogInfo;
        bookmarks = new Bookmarks(Path.Combine(BepInEx.Paths.ConfigPath, "michal.recipebrowser.bookmarks.txt"));
    }

    private void Update()
    {
        // Locations are scanned in the background right after entering the world, not when the inventory opens.
        if (DataConfig.ScanLocations.Value && Player.m_localPlayer && ZoneSystem.instance) Locations.EnsureStarted(this);
        if (!Player.m_localPlayer || !InventoryGui.IsVisible() || !GUIManager.CustomGUIFront)
        {
            hoveredEntry = null;
            recipeOpen = false;
            openedAt = -1;
            layout = null;
            if (browser != null && browser.Alive) browser.SetVisible(false);
            return;
        }
        var db = ObjectDB.instance;
        if (!db || !ZNetScene.instance) return;
        // Rebuild when ObjectDB changes (server sync), mod spawns or the catalog config change; Catalog logs the summary.
        if ((catalog == null || catalogSource != db || catalog.IsStale(db)) && Time.time >= retryAt) RebuildCatalog(db);
        if (catalog == null) return;
        if (openedAt < 0) openedAt = Time.time;
        if (layout == null)
        {
            if (Time.time - openedAt < SettleDelay) return;
            var root = (RectTransform)GUIManager.CustomGUIFront.transform;
            layout = Layout.Fit(InventoryGui.instance, root, rows.Value);
            if (browser != null && browser.Alive && browser.Layout != layout)
            {
                browser.Destroy();
                browser = null;
            }
            if (browser == null || !browser.Alive) browser = new Browser(catalog, bookmarks, root, layout);
        }
        if (browser == null || !browser.Alive)
        {
            layout = null;
            return;
        }
        if (UiConfig.ResetLayout.Value) browser.ResetLayout();
        browser.SetVisible(true);
        browser.Tick();
        if (browser.IsRecipeOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            escapeConsumedFrame = Time.frameCount;
            browser.Close();
        }
        recipeOpen = browser.IsRecipeOpen;
        if (browser.IsTyping) return;

        if (backKey.Value.IsDown()) browser.Back();
        if (collapseKey.Value.IsDown()) browser.ToggleCollapsed();
        var hovered = hoveredEntry = HoveredEntry();
        actionKeys = new[] { recipesKey, usagesKey, bookmarkKey }.Select(k => k.Value.MainKey).ToArray();
        if (hovered == null) return;
        if (recipesKey.Value.IsDown()) browser.Open(hovered, View.Recipes);
        if (usagesKey.Value.IsDown()) browser.Open(hovered, View.Usages);
        if (bookmarkKey.Value.IsDown()) browser.ToggleBookmark(hovered, remove: Slot.Hovered && Slot.Hovered.InBookmarks);
    }

    private void RebuildCatalog(ObjectDB db)
    {
        try
        {
            catalog = new Catalog(db, ZNetScene.instance);
        }
        catch (System.Exception e)
        {
            // No retry loop: the error (e.g. caused by another mod) is logged, the old catalog stays and it is retried a bit later.
            Logger.LogError($"Catalog build failed: {e}");
            retryAt = Time.time + RetryDelay;
            return;
        }
        catalogSource = db;
        Stock.Invalidate();
        if (browser != null && browser.Alive) browser.Destroy();
        browser = null;
    }

    private Entry HoveredEntry()
    {
        if (Slot.Hovered) return Slot.Hovered.Entry;
        var gui = InventoryGui.instance;
        foreach (var grid in new[] { gui.m_playerGrid, gui.m_containerGrid })
        {
            var element = grid ? grid.GetHoveredElement() : null;
            if (element == null || grid.m_inventory == null) continue;
            var position = grid.GetElementPos(element);
            var item = grid.m_inventory.GetItemAt(position.x, position.y);
            if (item?.m_dropPrefab) return catalog.Find(item.m_dropPrefab.name);
        }
        foreach (var pair in gui.m_availableRecipes)
            if (pair.InterfaceElement && pair.Recipe && pair.Recipe.m_item && IsPointerOver((RectTransform)pair.InterfaceElement.transform))
                return catalog.Find(pair.Recipe.m_item.name);
        return null;
    }

    private static bool IsPointerOver(RectTransform rect) => rect.rect.Contains(rect.InverseTransformPoint(ZInput.pointerPosition));

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown), typeof(KeyCode), typeof(bool))]
    private static class EscapeClosesRecipeFirst
    {
        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (__result && key == KeyCode.Escape && (recipeOpen || escapeConsumedFrame == Time.frameCount)) __result = false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    private static class SuppressMovementWhileActing
    {
        private static readonly string[] Movement = { "Forward", "Backward", "Left", "Right" };

        private static void Postfix(string name, ref bool __result)
        {
            if (__result && hoveredEntry != null && actionKeys.Any(Input.GetKey) && Movement.Contains(name)) __result = false;
        }
    }
}
