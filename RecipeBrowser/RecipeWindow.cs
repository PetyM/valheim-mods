using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RecipeBrowser;

/// <summary>
/// Okno receptu: pohledy (vyrobit, získat, vylepšení, použití), skupiny stanic a karty. Přesun za titulek,
/// změna velikosti za roh po celých kartách. Skupiny se zalamují bez omezení, okno kvůli nim roste nahoru.
/// </summary>
internal sealed class RecipeWindow
{
    private const float SlotSize = 40, Cell = 44, Pad = 16, CardHeight = 132, TabHeight = 30, GroupRowHeight = 28, PagerHeight = 30;
    private const float TabsTop = Pad + 36, GroupsTop = TabsTop + TabHeight + 8, Chrome = GroupsTop + 4 + PagerHeight + 4 + Pad;
    private const float MinWidth = 520, SelectHintWidth = 110;
    private const string All = "Vše";
    private static readonly (View view, string label)[] Views =
        { (View.Recipes, "Jak vyrobit"), (View.Sources, "Jak získat"), (View.Upgrades, "Vylepšení"), (View.Usages, "K čemu je") };

    private readonly RectTransform canvas;
    private readonly Catalog catalog;
    private readonly GameObject handle;
    private readonly Text title, pageLabel;
    private readonly Button back, close, previous, next;
    private readonly Dictionary<View, Button> viewTabs = new();
    private readonly RectTransform groupTabs, cards;
    private readonly Stack<(Entry entry, View view)> history = new();
    private readonly Frame frame;
    private (Entry entry, View view)? current;
    private string group = All;
    private int first, cardsPerPage = 1;
    private float width, groupsHeight;
    private (float, int) rendered;
    private bool dirty = true;

    public GameObject Root { get; }
    public bool IsOpen => Root && Root.activeSelf;

    public RecipeWindow(RectTransform canvas, Catalog catalog, Rect defaultArea)
    {
        this.canvas = canvas;
        this.catalog = catalog;
        Root = GUIManager.Instance.CreateWoodpanel(canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, MinWidth, 400, false);
        handle = Ui.Rect("Handle", Root.transform, 0, 0, MinWidth, TabsTop);
        title = Ui.Label(Root.transform, "", Pad, Pad, 100, 34, 22, colour: Ui.Orange);
        back = Ui.Button(Root.transform, "Zpět", 0, Pad, 90, 30, Back);
        close = Ui.Button(Root.transform, "×", 0, Pad, 34, 30, Close);
        foreach (var (view, _) in Views)
        {
            var selected = view;
            viewTabs[view] = Ui.Button(Root.transform, "", 0, TabsTop, 100, TabHeight, () => Show(current.Value.entry, selected, remember: false));
        }
        groupTabs = (RectTransform)Ui.Rect("Groups", Root.transform, Pad, GroupsTop, 100, 0).transform;
        var cardsGo = Ui.Rect("Cards", Root.transform, 0, GroupsTop, 100, 100);
        cardsGo.AddComponent<Image>().color = Color.clear;
        cardsGo.AddComponent<ScrollArea>().Scrolled = delta => { first += delta; RenderCards(); };
        cards = (RectTransform)cardsGo.transform;
        previous = Ui.Button(Root.transform, "<", Pad, 0, 40, PagerHeight, () => { first -= cardsPerPage; RenderCards(); });
        pageLabel = Ui.Label(Root.transform, "", 0, 0, 100, PagerHeight, 14, TextAnchor.MiddleCenter, Ui.Muted);
        BestFit(pageLabel, 14);
        next = Ui.Button(Root.transform, ">", 0, 0, 40, PagerHeight, () => { first += cardsPerPage; RenderCards(); });

        frame = new Frame(canvas, (RectTransform)Root.transform, Fit, new Vector2(0, CardHeight));
        frame.AddMoveHandle(handle);
        frame.AddResizeHandle();
        frame.Saved = r => UiConfig.Save(UiConfig.WindowRect, Base(r), canvas.rect);
        Reset(defaultArea);
        Root.SetActive(false);
    }

    /// <summary>Výchozí místo: dole uprostřed oblasti, výška na celé karty s rezervou na jeden řádek skupin.</summary>
    public static Rect DefaultRect(Rect area)
    {
        float w = Mathf.Max(MinWidth, area.width);
        int count = Mathf.Max(2, Mathf.FloorToInt((area.height - Chrome - GroupRowHeight) / CardHeight));
        return new Rect(area.center.x - w / 2, area.yMin, w, Chrome + count * CardHeight);
    }

    public void Reset(Rect defaultArea) => frame.Place(UiConfig.Load(UiConfig.WindowRect, canvas.rect, DefaultRect(defaultArea)));

    private Rect Base(Rect r) => new(r.xMin, r.yMin, r.width, r.height - groupsHeight);

    private Rect Fit(Rect desired)
    {
        width = Mathf.Max(MinWidth, desired.width);
        groupsHeight = GroupLayout(width).rows * GroupRowHeight;
        cardsPerPage = Mathf.Max(1, Mathf.FloorToInt((desired.height - Chrome - groupsHeight) / CardHeight + 0.01f));
        float height = Chrome + groupsHeight + cardsPerPage * CardHeight, cardsTop = GroupsTop + groupsHeight + 4;
        Ui.Place(handle, Root.transform, 0, 0, width, TabsTop);
        Ui.Place(title.gameObject, Root.transform, Pad, Pad, width - Pad * 2 - 140, 34);
        Ui.Place(back.gameObject, Root.transform, width - Pad - 130, Pad, 90, 30);
        Ui.Place(close.gameObject, Root.transform, width - Pad - 34, Pad, 34, 30);
        Ui.Place(groupTabs.gameObject, Root.transform, Pad, GroupsTop, width - Pad * 2, groupsHeight);
        Ui.Place(cards.gameObject, Root.transform, 0, cardsTop, width, cardsPerPage * CardHeight);
        float pagerY = height - Pad - PagerHeight;
        Ui.Place(previous.gameObject, Root.transform, Pad, pagerY, 40, PagerHeight);
        Ui.Place(pageLabel.gameObject, Root.transform, Pad + 44, pagerY, width - Pad * 2 - 88, PagerHeight);
        Ui.Place(next.gameObject, Root.transform, width - Pad - 40, pagerY, 40, PagerHeight);
        if (dirty || rendered != (width, cardsPerPage))
        {
            dirty = false;
            rendered = (width, cardsPerPage);
            LayoutTabs();
            RenderGroupTabs();
            RenderCards();
        }
        return new Rect(desired.xMin, desired.yMax - height, width, height);
    }

    public void Open(Entry entry, View view) => Show(entry, view, remember: true);

    /// <summary>Překreslí karty (počty „máš“, výběr u stanice) bez změny stránky.</summary>
    public void Refresh()
    {
        if (IsOpen) RenderCards();
    }

    public void Back()
    {
        if (history.Count == 0) Close();
        else
        {
            var (entry, view) = history.Pop();
            Show(entry, view, remember: false);
        }
    }

    public void Close()
    {
        history.Clear();
        current = null;
        Root.SetActive(false);
    }

    private void Show(Entry entry, View view, bool remember)
    {
        var nonEmpty = Views.Select(v => v.view).Where(v => catalog.Cards(entry, v).Count > 0).ToList();
        if (!nonEmpty.Contains(view)) view = nonEmpty.DefaultIfEmpty(view).First();
        if (remember && current.HasValue) history.Push(current.Value);
        current = (entry, view);
        first = 0;
        group = All;
        dirty = true;
        Root.SetActive(true);
        Root.transform.SetAsLastSibling();
        title.text = $"{entry.Name}  <size=15><color=#bbbbbb>{entry.Mod}</color></size>";
        // Okno drží spodní hranu a počet karet, skupiny ho zvětší nahoru.
        var r = frame.Rect;
        float rows = GroupLayout(width).rows * GroupRowHeight;
        frame.Place(new Rect(r.xMin, r.yMin, r.width, r.height - groupsHeight + rows));
    }

    private void LayoutTabs()
    {
        if (!current.HasValue)
        {
            foreach (var tab in viewTabs.Values) tab.gameObject.SetActive(false);
            return;
        }
        var (entry, view) = current.Value;
        var shown = Views.Where(v => catalog.Cards(entry, v.view).Count > 0).ToList();
        float tabWidth = (width - Pad * 2 - 8 * Mathf.Max(0, shown.Count - 1)) / Mathf.Max(1, shown.Count);
        foreach (var (tabView, label) in Views)
        {
            var tab = viewTabs[tabView];
            int index = shown.FindIndex(v => v.view == tabView);
            tab.gameObject.SetActive(index >= 0);
            if (index < 0) continue;
            Ui.Place(tab.gameObject, Root.transform, Pad + index * (tabWidth + 8), TabsTop, tabWidth, TabHeight);
            SetTabLabel(tab, $"{label} ({catalog.Cards(entry, tabView).Count})", tabView == view);
        }
    }

    private static void SetTabLabel(Button tab, string text, bool active)
    {
        var label = tab.GetComponentInChildren<Text>();
        label.text = text;
        label.color = active ? Ui.Orange : Ui.Muted;
    }

    private static void BestFit(Text label, int max)
    {
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 10;
        label.resizeTextMaxSize = max;
    }

    private IReadOnlyList<RecipeCard> CurrentCards() => current.HasValue ? catalog.Cards(current.Value.entry, current.Value.view) : new List<RecipeCard>();

    private (List<(string name, string text, float x, float y, float w)> tabs, int rows) GroupLayout(float windowWidth)
    {
        var tabs = new List<(string, string, float, float, float)>();
        var list = CurrentCards();
        var groups = list.GroupBy(c => c.Group).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count())).ToList();
        if (groups.Count < 2) return (tabs, 0);
        float x = 0, y = 0, inner = windowWidth - Pad * 2;
        foreach (var (name, count) in groups.Prepend((All, list.Count)))
        {
            string text = $"{name} ({count})";
            float w = Mathf.Min(inner, 22 + text.Length * 8.5f);
            if (x > 0 && x + w > inner) { x = 0; y += GroupRowHeight; }
            tabs.Add((name, text, x, y, w));
            x += w + 6;
        }
        return (tabs, Mathf.RoundToInt(y / GroupRowHeight) + 1);
    }

    private void RenderGroupTabs()
    {
        foreach (Transform child in groupTabs) UnityEngine.Object.Destroy(child.gameObject);
        foreach (var (name, text, x, y, w) in GroupLayout(width).tabs)
            SetTabLabel(Ui.Button(groupTabs, text, x, y, w, GroupRowHeight - 4, () => { group = name; first = 0; RenderGroupTabs(); RenderCards(); }), text, name == group);
    }

    private void RenderCards()
    {
        foreach (Transform child in cards) UnityEngine.Object.Destroy(child.gameObject);
        if (!current.HasValue) return;
        Stock.Invalidate();
        var list = CurrentCards().Where(c => group == All || c.Group == group).ToList();
        first = Mathf.Clamp(first, 0, Mathf.Max(0, list.Count - cardsPerPage));
        int last = Mathf.Min(list.Count, first + cardsPerPage);
        string range = list.Count == 0 ? "0" : last - first <= 1 ? $"{first + 1}" : $"{first + 1}–{last}";
        pageLabel.text = $"{range} z {list.Count}  ·  kolečko listuje · levé tlačítko = jak vyrobit, pravé = k čemu je · Backspace zpět · klik na kartu = vybrat ve výrobě";
        if (list.Count == 0) Ui.Label(cards, "Nic nenalezeno", Pad, 0, width - Pad * 2, 40, 18, colour: Ui.Muted);
        for (int i = first; i < last; i++)
            RenderCard(list[i], (i - first) * CardHeight);
    }

    private void RenderCard(RecipeCard card, float top)
    {
        float cardWidth = width - Pad * 2;
        bool selectable = CanSelect(card);
        var cardRoot = Ui.Rect("Card", cards, Pad, top, cardWidth, CardHeight - 8);
        cardRoot.AddComponent<Image>().color = selectable ? new Color(0.3f, 0.18f, 0.04f, 0.45f) : new Color(0, 0, 0, 0.3f);
        if (card.Recipe != null) cardRoot.AddComponent<Clicks>().Left = () => Select(card);
        float x = 8;
        if (card.Station != null)
        {
            Slot.Create(cardRoot.transform, x, 4, 32, false, Open).Show(card.Station);
            x += 40;
        }
        string station = card.Station == null ? "" : $"  ·  {card.Station.Name}" + (card.StationLevel > 1 ? $" úroveň {card.StationLevel}" : "");
        string biome = card.Biome.HasValue ? $"  <size=14><color=#bbbbbb>· {Progression.Name(card.Biome.Value)}</color></size>" : "";
        var heading = Ui.Label(cardRoot.transform, card.Title + station + biome, x, 4, cardWidth - x - 8 - (selectable ? SelectHintWidth : 0), 32, 18, colour: Ui.Orange);
        BestFit(heading, 18);
        if (selectable) Ui.Label(cardRoot.transform, "klik = vybrat", cardWidth - SelectHintWidth - 4, 4, SelectHintWidth, 32, 13, TextAnchor.MiddleRight, Ui.Enough);

        x = 8;
        var parts = new List<string>();
        foreach (var input in card.Inputs)
        {
            int held = Stock.Count(input.Entry);
            bool enough = held < 0 || held >= input.Amount;
            Slot.Create(cardRoot.transform, x, 40, SlotSize, false, Open).Show(input.Entry, input.Amount, enough ? Ui.Enough : Ui.Missing);
            string have = held < 0 ? "" : $" ({held})";
            parts.Add($"<color={(enough ? "#9be29b" : "#ff7070")}>{input.Amount}× {input.Entry.Name}{have}</color>");
            x += Cell;
        }
        if (card.Inputs.Count > 0)
        {
            Ui.Label(cardRoot.transform, "→", x, 40, 36, SlotSize, 24, TextAnchor.MiddleCenter);
            x += 40;
        }
        foreach (var output in card.Outputs)
        {
            Slot.Create(cardRoot.transform, x, 40, SlotSize, false, Open).Show(output.Entry, output.Amount);
            x += Cell;
        }
        var notes = card.Notes.Where(n => !string.IsNullOrEmpty(n));
        if (parts.Count > 0) notes = notes.Prepend(string.Join(", ", parts));
        var label = Ui.Label(cardRoot.transform, string.Join("  ·  ", notes), 8, 84, cardWidth - 16, CardHeight - 8 - 86, 14, TextAnchor.UpperLeft, Ui.Muted);
        BestFit(label, 14);
    }

    // --- Výběr receptu ve vanilla craftingu ---

    private static bool CanSelect(RecipeCard card)
    {
        var player = Player.m_localPlayer;
        if (card.Recipe == null || !player || !InventoryGui.IsVisible()) return false;
        var required = card.Recipe.GetRequiredStation(Mathf.Max(1, card.Quality));
        if (!required) return card.Quality <= 1;
        var station = player.GetCurrentCraftingStation();
        return station && station.m_name == required.m_name;
    }

    private static void Select(RecipeCard card)
    {
        var gui = InventoryGui.instance;
        if (!gui || !CanSelect(card))
        {
            Message("Recept vybereš, až budeš stát u správné stanice.");
            return;
        }
        bool upgrade = card.Quality > 1;
        if (upgrade ? !gui.InUpradeTab() : !gui.InCraftTab())
        {
            var tab = upgrade ? gui.m_tabUpgrade : gui.m_tabCraft;
            if (!tab.gameObject.activeSelf)
            {
                Message(upgrade ? "Tahle stanice neumí vylepšovat." : "Tahle stanice neumí vyrábět.");
                return;
            }
            if (upgrade) gui.OnTabUpgradePressed();
            else gui.OnTabCraftPressed();
        }
        var available = gui.m_availableRecipes;
        int index = available.FindIndex(p => p.Recipe == card.Recipe && (!upgrade || p.ItemData != null && p.ItemData.m_quality == card.Quality - 1));
        if (index < 0 && upgrade) index = available.FindIndex(p => p.Recipe == card.Recipe);
        if (index < 0)
        {
            Message(upgrade ? "Ve vylepšení není: chybí předmět v inventáři nebo úroveň." : "Ve výrobě není: recept ještě neznáš.");
            return;
        }
        gui.SetRecipe(index, center: true);
    }

    private static void Message(string text)
    {
        if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
    }
}
