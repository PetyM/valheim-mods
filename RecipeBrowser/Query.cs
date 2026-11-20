using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RecipeBrowser;

/// <summary>
/// Parsed query from the search field (SPEC ch. 2): words, @mod, #tag (':' = and, unambiguous prefix),
/// -negation, ^ingredient, '|' = or. All space-separated tokens must match.
/// </summary>
internal sealed class Query
{
    /// <summary>Is the entry bookmarked? Set by the UI (it owns the bookmarks); without it #bookmarked finds nothing.</summary>
    public static Func<Entry, bool> IsBookmarked { get; set; }

    private readonly List<Func<Entry, bool>> terms = new();

    public bool IsEmpty => terms.Count == 0;
    /// <summary>The query depends on player state (#craftable, #bookmarked); the UI should re-filter when that changes.</summary>
    public bool IsLive { get; private set; }

    public static Query Parse(string text, Catalog catalog)
    {
        var query = new Query();
        foreach (var token in Tokens(text))
        {
            var alternatives = token.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(a => query.Alternative(a, catalog)).Where(a => a != null).ToArray();
            if (alternatives.Length == 1) query.terms.Add(alternatives[0]);
            else if (alternatives.Length > 1) query.terms.Add(e => Any(alternatives, e));
        }
        return query;
    }

    public bool Matches(Entry entry) => All(terms, entry);

    // Loops instead of LINQ: Matches runs for every entry on every keystroke.
    private static bool All(IList<Func<Entry, bool>> list, Entry entry)
    {
        for (int i = 0; i < list.Count; i++)
            if (!list[i](entry)) return false;
        return true;
    }

    private static bool Any(IList<Func<Entry, bool>> list, Entry entry)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i](entry)) return true;
        return false;
    }

    private static IEnumerable<string> Tokens(string text) =>
        Regex.Replace(text ?? "", @"\s*\|\s*", "|").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    private Func<Entry, bool> Alternative(string text, Catalog catalog)
    {
        bool negate = text.Length > 1 && text[0] == '-';
        if (negate) text = text.Substring(1);
        if (text == "-") return null;
        var match = Core(text, catalog);
        if (match == null) return null;
        return negate ? e => !match(e) : match;
    }

    private Func<Entry, bool> Core(string text, Catalog catalog)
    {
        string body = text.Substring(1);
        switch (text[0])
        {
            case '@':
                if (body.Length == 0) return null;
                string mod = Compact(body);
                return e => Contains(e.CompactMod, mod);
            case '#':
                if (body.Trim(':').Length == 0) return null;
                var parts = TagParts(body, catalog).Select(p => TagMatcher(p, catalog)).ToArray();
                return parts.Length == 1 ? parts[0] : e => All(parts, e);
            case '^':
                if (body.Length == 0) return null;
                var results = new HashSet<Entry>(Ingredients(body, catalog).SelectMany(i => catalog.Cards(i, View.Usages)
                    .Where(c => c.Inputs.Any(s => s.Entry == i)).SelectMany(c => c.Outputs).Select(o => o.Entry)));
                return results.Contains;
            default:
                string word = text;
                return e => Contains(e.Name, word) || Contains(e.Key, word) || Contains(e.CompactName, word);
        }
    }

    /// <summary>Splits "weapons:set:troll" into "weapons" and "set:troll" (keeps the set:/station: namespaces together).</summary>
    private static List<string> TagParts(string body, Catalog catalog)
    {
        var raw = body.ToLowerInvariant().Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        for (int i = 0; i < raw.Length; i++)
        {
            bool space = i + 1 < raw.Length && catalog.TagCounts.Keys.Any(t => t.StartsWith(raw[i] + ":"));
            parts.Add(space ? raw[i] + ":" + raw[++i] : raw[i]);
        }
        return parts;
    }

    private static IEnumerable<string> AllTags(Catalog catalog) => catalog.TagCounts.Keys.Concat(Tags.Dynamic);

    private Func<Entry, bool> TagMatcher(string part, Catalog catalog)
    {
        string tag = Resolve(part, catalog);
        if (tag is Tags.Craftable or Tags.Bookmarked) IsLive = true;
        if (tag == Tags.Craftable) return e => Stock.CanCraft(e, catalog);
        if (tag == Tags.Bookmarked) return e => IsBookmarked?.Invoke(e) == true;
        if (tag != null) return e => e.Tags.Contains(tag);
        // Ambiguous prefix: any tag starting with it matches.
        return e =>
        {
            foreach (var t in e.Tags)
                if (t.StartsWith(part, StringComparison.Ordinal)) return true;
            return false;
        };
    }

    /// <summary>Exact tag, or the only tag with the given prefix; null = ambiguous or none.</summary>
    private static string Resolve(string part, Catalog catalog)
    {
        if (catalog.TagCounts.ContainsKey(part) || Tags.Dynamic.Contains(part)) return part;
        var candidates = AllTags(catalog).Where(t => t.StartsWith(part, StringComparison.Ordinal)).Take(2).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static List<Entry> Ingredients(string text, Catalog catalog)
    {
        string compact = Compact(text);
        var exact = catalog.All.Where(e => string.Equals(e.Key, text, StringComparison.OrdinalIgnoreCase) || string.Equals(e.CompactName, compact, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0 ? exact : catalog.All.Where(e => Contains(e.Key, text) || Contains(e.CompactName, compact)).ToList();
    }

    private static bool Contains(string text, string part) => text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    private static string Compact(string text) => text.Replace(" ", "");

    /// <summary>
    /// Suggestions for the last, partially typed token (starts with '#', '@' or '^', also after '-' and '|'): completion, description, result count.
    /// Completion is the token without negation and without the part before '|' (e.g. "#weapons:plains", "@Therzie", "^Wood").
    /// </summary>
    public static IReadOnlyList<Suggestion> Suggest(string text, Catalog catalog)
    {
        var result = new List<Suggestion>();
        if (string.IsNullOrEmpty(text) || char.IsWhiteSpace(text[text.Length - 1]) || catalog == null) return result;
        string token = text.Substring(text.LastIndexOfAny(new[] { ' ', '\t' }) + 1);
        token = token.Substring(token.LastIndexOf('|') + 1).TrimStart('-');
        if (token.Length == 0) return result;
        string body = token.Substring(1);
        switch (token[0])
        {
            case '#':
                SuggestTags(body.ToLowerInvariant(), catalog, result);
                break;
            case '@':
                string mod = Compact(body);
                result.AddRange(catalog.ModCounts.Where(m => Contains(Compact(m.Key), mod))
                    .OrderBy(m => Compact(m.Key).StartsWith(mod, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenByDescending(m => m.Value)
                    .Take(20).Select(m => new Suggestion("@" + Compact(m.Key), m.Key == "Valheim" ? "Vanilla" : "Mod " + m.Key, m.Value)));
                break;
            case '^':
                if (body.Length == 0) break;
                string compact = Compact(body);
                foreach (var e in catalog.All.Where(e => Contains(e.Key, body) || Contains(e.CompactName, compact))
                    .OrderBy(e => e.Key.StartsWith(body, StringComparison.OrdinalIgnoreCase) || e.Name.StartsWith(body, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(e => e.Name))
                {
                    int count = catalog.Cards(e, View.Usages).Where(c => c.Inputs.Any(s => s.Entry == e)).SelectMany(c => c.Outputs).Select(o => o.Entry).Distinct().Count();
                    if (count > 0) result.Add(new Suggestion("^" + e.Key, "Ingredient: " + e.Name, count));
                    if (result.Count >= 20) break;
                }
                break;
        }
        return result;
    }

    private static void SuggestTags(string body, Catalog catalog, List<Suggestion> result)
    {
        var seen = new HashSet<string>();
        foreach (var tag in AllTags(catalog).Where(t => t.StartsWith(body, StringComparison.Ordinal)))
            if (seen.Add(tag) && Count(tag, catalog) is var count and > 0) result.Add(new Suggestion("#" + tag, catalog.TagDescription(tag) ?? "", count));
        int colon = body.LastIndexOf(':');
        if (colon > 0)
        {
            string head = body.Substring(0, colon), partial = body.Substring(colon + 1);
            // Filter the head once, then count candidates only within it (not the whole catalog per tag).
            var headQuery = Parse("#" + head, catalog);
            var pool = new List<Entry>();
            foreach (var e in catalog.All)
                if (headQuery.Matches(e)) pool.Add(e);
            foreach (var tag in AllTags(catalog).Where(t => t.StartsWith(partial, StringComparison.Ordinal) && !t.Contains(':')).Take(40))
            {
                string full = head + ":" + tag;
                if (pool.Count == 0 || !seen.Add(full)) continue;
                Func<Entry, bool> has = tag == Tags.Craftable ? e => Stock.CanCraft(e, catalog)
                    : tag == Tags.Bookmarked ? e => IsBookmarked?.Invoke(e) == true : e => e.Tags.Contains(tag);
                int count = 0;
                foreach (var e in pool)
                    if (has(e)) count++;
                if (count > 0) result.Add(new Suggestion("#" + full, catalog.TagDescription(tag) ?? "", count));
            }
        }
        // Exact match first, then by result count.
        var ordered = result.OrderBy(r => r.Completion == "#" + body ? 0 : 1).ThenByDescending(r => r.Count).Take(20).ToList();
        result.Clear();
        result.AddRange(ordered);
    }

    private static int Count(string tag, Catalog catalog) =>
        catalog.TagCounts.TryGetValue(tag, out var n) ? n
        : tag == Tags.Craftable ? catalog.All.Count(e => Stock.CanCraft(e, catalog))
        : tag == Tags.Bookmarked ? catalog.All.Count(e => IsBookmarked?.Invoke(e) == true) : 0;
}

/// <summary>Autocomplete suggestion. Completion is the whole token including the prefix ('#swords').</summary>
internal readonly record struct Suggestion(string Completion, string Description, int Count);
