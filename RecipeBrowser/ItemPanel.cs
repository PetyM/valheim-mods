using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RecipeBrowser;

/// <summary>
/// Panel s hlavičkou a mřížkou položek. Hlavička slouží k přesunu, roh ke změně velikosti po celých políčkách.
/// Kolečko listuje po řádcích, tlačítka po stránkách. Obsah hlavičky rozvrhuje vlastník přes LayoutHeader.
/// Volitelně (Section) vloží před každou skupinu řádek s jejím názvem (oddělovač biomu).
/// </summary>
internal sealed class ItemPanel
{
    public const float Pad = 10, HeaderHeight = 76, PagerWidth = 140, RowHeight = 32;
    private const float MinInner = 480, CollapseWidth = 32;
    private const int MaxCells = 40;

    private readonly Layout layout;
    private readonly bool inBookmarks;
    private readonly Action<Entry, View> open;
    private readonly Action<Entry, Entry> reorder;
    private readonly RectTransform header, grid;
    private readonly Button previous, next, collapse;
    private readonly Text pageLabel;
    private readonly List<Slot> slots = new();
    private readonly List<Text> headings = new();
    // Řádky mřížky: buď úsek položek (Start, Count), nebo nadpis skupiny (Heading != null).
    private readonly List<(int Start, int Count, string Heading)> lines = new();
    private List<Entry> entries = new();
    private int columns, rows, first, linedFor = -1;
    private (int, int) built;

    public GameObject Root { get; }
    public Transform Header => header;
    /// <summary>Šířka prvního řádku hlavičky bez stránkování a tlačítka sbalení.</summary>
    public float HeaderWidth { get; private set; }
    public Frame Frame { get; }
    /// <summary>Rozvrhne obsah hlavičky pro danou vnitřní šířku a vrátí její výšku.</summary>
    public Func<float, float> LayoutHeader { get; set; } = _ => RowHeight + 6;
    /// <summary>Název skupiny položky pro oddělovací řádky; null = bez oddělovačů. Platí od dalšího Show.</summary>
    public Func<Entry, string> Section { get; set; }

    public static float Height(int rows, float cell) => Pad * 2 + HeaderHeight + rows * cell;

    public ItemPanel(RectTransform canvas, Layout layout, bool inBookmarks, Action<Entry, View> open, Action onCollapse, Action<Entry, Entry> reorder = null)
    {
        this.layout = layout;
        this.inBookmarks = inBookmarks;
        this.open = open;
        this.reorder = reorder;
        Root = GUIManager.Instance.CreateWoodpanel(canvas, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 400, 200, false);
        var rootRect = (RectTransform)Root.transform;
        Frame = new Frame(canvas, rootRect, Fit, new Vector2(layout.Cell, layout.Cell));
        header = (RectTransform)Ui.Rect("Header", Root.transform, Pad, Pad, 100, HeaderHeight).transform;
        Frame.AddMoveHandle(header.gameObject);
        previous = Ui.Button(header, "<", 0, 0, 32, RowHeight, () => Scroll(-rows));
        pageLabel = Ui.Label(header, "", 0, 0, PagerWidth - 68, RowHeight, 14, TextAnchor.MiddleCenter, Ui.Muted);
        next = Ui.Button(header, ">", 0, 0, 32, RowHeight, () => Scroll(rows));
        collapse = Ui.Button(header, "–", 0, 0, CollapseWidth, RowHeight, onCollapse);

        grid = (RectTransform)Ui.Rect("Grid", Root.transform, Pad, Pad + HeaderHeight, 100, 100).transform;
        grid.gameObject.AddComponent<Image>().color = Color.clear;
        grid.gameObject.AddComponent<ScrollArea>().Scrolled = Scroll;
        Frame.AddResizeHandle();
    }

    private Rect Fit(Rect desired)
    {
        float cell = layout.Cell;
        columns = Mathf.Clamp(Mathf.FloorToInt((desired.width - Pad * 2) / cell + 0.01f), Mathf.CeilToInt(MinInner / cell), MaxCells);
        float inner = columns * cell;
        HeaderWidth = inner - PagerWidth - CollapseWidth - 12;
        float headerHeight = LayoutHeader(inner);
        rows = Mathf.Clamp(Mathf.FloorToInt((desired.height - Pad * 2 - headerHeight) / cell + 0.01f), 1, MaxCells);

        Ui.Place(header.gameObject, Root.transform, Pad, Pad, inner, headerHeight);
        float pagerX = inner - CollapseWidth - 6 - PagerWidth;
        Ui.Place(previous.gameObject, header, pagerX, 0, 32, RowHeight);
        Ui.Place(pageLabel.gameObject, header, pagerX + 34, 0, PagerWidth - 68, RowHeight);
        Ui.Place(next.gameObject, header, pagerX + PagerWidth - 32, 0, 32, RowHeight);
        Ui.Place(collapse.gameObject, header, inner - CollapseWidth, 0, CollapseWidth, RowHeight);
        Ui.Place(grid.gameObject, Root.transform, Pad, Pad + headerHeight, inner, rows * cell);
        BuildSlots();

        float width = inner + Pad * 2, height = Pad * 2 + headerHeight + rows * cell;
        return new Rect(desired.xMin, desired.yMax - height, width, height);
    }

    private void BuildSlots()
    {
        if (built == (columns, rows))
        {
            Refresh();
            return;
        }
        built = (columns, rows);
        foreach (var slot in slots) UnityEngine.Object.Destroy(slot.gameObject);
        foreach (var heading in headings) UnityEngine.Object.Destroy(heading.gameObject);
        slots.Clear();
        headings.Clear();
        float cell = layout.Cell, offset = (cell - layout.SlotSize) / 2;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
                slots.Add(Slot.Create(grid, column * cell + offset, row * cell + offset, layout.SlotSize, inBookmarks, open, reorder));
            headings.Add(Ui.Label(grid, "", offset, row * cell, columns * cell - offset * 2, cell, 16, TextAnchor.LowerLeft, Ui.Orange));
        }
        Refresh();
    }

    public void Show(List<Entry> list, bool resetPage)
    {
        entries = list;
        linedFor = -1;
        if (resetPage) first = 0;
        Refresh();
    }

    /// <summary>Posun o celé řádky (first je index prvního viditelného řádku).</summary>
    private void Scroll(int deltaRows)
    {
        first += deltaRows;
        Refresh();
    }

    private void BuildLines()
    {
        if (linedFor == columns) return;
        linedFor = columns;
        lines.Clear();
        string current = null;
        for (int i = 0; i < entries.Count;)
        {
            if (Section != null && (lines.Count == 0 || (Section(entries[i]) ?? "") != current))
                lines.Add((0, 0, current = Section(entries[i]) ?? ""));
            int start = i;
            while (i < entries.Count && i - start < columns && (Section == null || (Section(entries[i]) ?? "") == current)) i++;
            lines.Add((start, i - start, null));
        }
    }

    private void Refresh()
    {
        if (slots.Count == 0) return;
        BuildLines();
        first = Mathf.Clamp(first, 0, Mathf.Max(0, lines.Count - rows));
        for (int row = 0; row < rows; row++)
        {
            var line = first + row < lines.Count ? lines[first + row] : (0, 0, null);
            headings[row].text = line.Heading ?? "";
            headings[row].gameObject.SetActive(line.Heading != null);
            for (int column = 0; column < columns; column++)
                slots[row * columns + column].Show(column < line.Count ? entries[line.Start + column] : null);
        }
        int pages = Mathf.Max(1, Mathf.CeilToInt(lines.Count / (float)rows));
        int page = first + rows >= lines.Count ? pages : first / rows + 1;
        pageLabel.text = $"{page}/{pages}";
    }
}
