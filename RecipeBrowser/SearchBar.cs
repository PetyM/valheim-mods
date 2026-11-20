using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RecipeBrowser;

/// <summary>
/// Hlavička panelu výsledků: pole hledání, přepínač řazení, čipy tagů a našeptávač (SPEC 2, 3).
/// Čipy jsou jen zkratka, stav je vždy v textu hledání.
/// </summary>
internal sealed class SearchBar
{
    private const float ChipHeight = 26, ChipRow = 30, SuggestionHeight = 24, SortWidth = 140;
    private const int MaxSuggestions = 8;
    private static readonly Dictionary<SortMode, string> SortLabels = new()
        { [SortMode.Progression] = "Řadit: postup", [SortMode.Name] = "Řadit: název", [SortMode.Mod] = "Řadit: mód" };
    private static string lastText = "";

    private readonly ItemPanel panel;
    private readonly Catalog catalog;
    private readonly Action changed;
    private readonly InputField input;
    private readonly Button sort;
    private readonly List<(Button button, string tag, float width)> chips = new();
    private readonly RectTransform overlay;
    private readonly Press overlayHover;
    private readonly List<(GameObject row, Image background, Text label)> rows = new();
    private IReadOnlyList<Suggestion> suggestions = new List<Suggestion>();
    private bool caretToEnd;

    public string Text => input.text;
    public SortMode Sort => UiConfig.Sort.Value;
    public bool IsActive => input.isFocused || overlayHover.Hovered;

    public SearchBar(ItemPanel panel, Catalog catalog, Action changed)
    {
        this.panel = panel;
        this.catalog = catalog;
        this.changed = changed;
        var go = GUIManager.Instance.CreateInputField(panel.Header, Vector2.zero, Vector2.zero, Vector2.zero,
            InputField.ContentType.Standard, "Hledat… (#tag, @mód, -vyřadit, ^ingredience, a|b)", 18, 300, ItemPanel.RowHeight);
        input = go.GetComponent<InputField>();
        input.text = lastText;
        input.onValueChanged.AddListener(_ => OnTextChanged());
        sort = Ui.Button(panel.Header, SortLabels[Sort], 0, 0, SortWidth, ItemPanel.RowHeight, CycleSort);
        sort.GetComponentInChildren<Text>().fontSize = 14;
        foreach (var (tag, label) in Tags.Chips)
        {
            var token = "#" + tag;
            var button = Ui.Button(panel.Header, label, 0, 0, 80, ChipHeight, () => Toggle(token, negate: false));
            var text = button.GetComponentInChildren<Text>();
            text.fontSize = 14;
            button.gameObject.AddComponent<Clicks>().Right = () => Toggle(token, negate: true);
            chips.Add((button, token, text.preferredWidth + 22));
        }

        overlay = (RectTransform)Ui.Rect("Suggestions", panel.Root.transform, 0, 0, 300, 10).transform;
        overlay.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.06f, 0.04f, 0.95f);
        overlayHover = overlay.gameObject.AddComponent<Press>();
        overlay.gameObject.SetActive(false);
        UpdateChips();
    }

    /// <summary>Rozvrhne hlavičku pro vnitřní šířku panelu, vrátí výšku hlavičky.</summary>
    public float Layout(float inner)
    {
        float searchWidth = Mathf.Max(120, panel.HeaderWidth - SortWidth - 6);
        Ui.Place(input.gameObject, panel.Header, 0, 0, searchWidth, ItemPanel.RowHeight);
        Ui.Place(sort.gameObject, panel.Header, searchWidth + 6, 0, SortWidth, ItemPanel.RowHeight);
        float x = 0, y = ItemPanel.RowHeight + 6;
        foreach (var (button, _, width) in chips)
        {
            if (x > 0 && x + width > inner) { x = 0; y += ChipRow; }
            Ui.Place(button.gameObject, panel.Header, x, y, width, ChipHeight);
            x += width + 4;
        }
        Ui.Place(overlay.gameObject, panel.Root.transform, ItemPanel.Pad, ItemPanel.Pad + ItemPanel.RowHeight + 2, Mathf.Max(searchWidth, 360), overlay.sizeDelta.y);
        return y + ChipRow + 6;
    }

    public void Tick()
    {
        if (caretToEnd && input.isFocused)
        {
            input.caretPosition = input.text.Length;
            caretToEnd = false;
        }
        if (input.isFocused && suggestions.Count > 0 && Input.GetKeyDown(KeyCode.Tab)) Complete(suggestions[0]);
        bool show = IsActive && suggestions.Count > 0;
        if (overlay.gameObject.activeSelf != show) overlay.gameObject.SetActive(show);
        if (show) overlay.SetAsLastSibling();
    }

    public void Defocus()
    {
        if (input.isFocused) input.DeactivateInputField();
        overlay.gameObject.SetActive(false);
    }

    private void OnTextChanged()
    {
        lastText = input.text;
        UpdateChips();
        UpdateSuggestions();
        changed();
    }

    private void CycleSort()
    {
        UiConfig.Sort.Value = (SortMode)(((int)Sort + 1) % Enum.GetValues(typeof(SortMode)).Length);
        sort.GetComponentInChildren<Text>().text = SortLabels[Sort];
        changed();
    }

    private void Toggle(string token, bool negate)
    {
        var tokens = input.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        bool hadPositive = tokens.RemoveAll(t => Same(t, token)) > 0;
        bool hadNegative = tokens.RemoveAll(t => Same(t, "-" + token)) > 0;
        if (negate ? !hadNegative : !hadPositive) tokens.Add(negate ? "-" + token : token);
        input.text = tokens.Count == 0 ? "" : string.Join(" ", tokens) + " ";
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void UpdateChips()
    {
        var tokens = input.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var (button, tag, _) in chips)
        {
            var label = button.GetComponentInChildren<Text>();
            label.color = tokens.Any(t => Same(t, tag)) ? Ui.Orange : tokens.Any(t => Same(t, "-" + tag)) ? Ui.Missing : Ui.Muted;
        }
    }

    private void UpdateSuggestions()
    {
        suggestions = Query.Suggest(input.text, catalog) ?? new List<Suggestion>();
        int count = Mathf.Min(MaxSuggestions, suggestions.Count);
        while (rows.Count < count) rows.Add(CreateRow(rows.Count));
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].row.SetActive(i < count);
            if (i >= count) continue;
            var s = suggestions[i];
            rows[i].label.text = $"<color=#ffa13d>{s.Completion}</color>  {s.Description}  <color=#bbbbbb>({s.Count})</color>";
            rows[i].background.color = i == 0 ? new Color(1, 0.63f, 0.24f, 0.18f) : Color.clear;
        }
        overlay.sizeDelta = new Vector2(overlay.sizeDelta.x, count * SuggestionHeight + 4);
    }

    private (GameObject, Image, Text) CreateRow(int index)
    {
        var row = Ui.Rect("Suggestion", overlay, 2, 2 + index * SuggestionHeight, 0, SuggestionHeight);
        var rt = (RectTransform)row.transform;
        rt.anchorMax = new Vector2(1, 1);
        rt.sizeDelta = new Vector2(-4, SuggestionHeight);
        var background = row.AddComponent<Image>();
        var label = Ui.Label(row.transform, "", 6, 0, 0, SuggestionHeight, 15);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.sizeDelta = new Vector2(-12, SuggestionHeight);
        row.AddComponent<Press>().Pressed = () =>
        {
            if (index < suggestions.Count) Complete(suggestions[index]);
        };
        return (row, background, label);
    }

    private void Complete(Suggestion suggestion)
    {
        input.text = Replace(input.text, suggestion.Completion) + " ";
        input.ActivateInputField();
        caretToEnd = true;
    }

    /// <summary>Nahradí rozepsaný poslední token doplněním, zachová negaci, část před '|' a před poslední ':'.</summary>
    private static string Replace(string text, string completion)
    {
        int start = text.LastIndexOf(' ') + 1;
        string token = text.Substring(start);
        int bar = token.LastIndexOf('|');
        string before = token.Substring(0, bar + 1), core = token.Substring(bar + 1);
        string negation = core.StartsWith("-") ? "-" : "";
        core = core.Substring(negation.Length);
        if (completion.StartsWith("-")) negation = "";
        if (before.Length > 0 && completion.StartsWith(before)) before = "";
        int colon = core.LastIndexOf(':');
        if (colon > 0 && completion.StartsWith("#") && !completion.StartsWith(core.Substring(0, colon + 1), StringComparison.OrdinalIgnoreCase))
            completion = core.Substring(0, colon + 1) + completion.Substring(1);
        return text.Substring(0, start) + before + negation + completion;
    }
}
