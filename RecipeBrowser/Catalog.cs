using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using Jotunn.Utils;
using UnityEngine;

namespace RecipeBrowser;

internal sealed class Entry(string key, string name, Sprite icon, string mod, string itemName, System.Func<string> details)
{
    private string tooltip;
    public string Tooltip => tooltip ??= $"{details()}\n\n<color=#bbbbbb>{Kind} · {Key} · {Mod} · {Progression.Name(Biome)}</color>";
    public string Key { get; } = key;
    public string Name { get; } = name;
    public Sprite Icon { get; } = icon;
    public string Mod { get; } = mod;
    public string ItemName { get; } = itemName;
    public string Kind => ItemName == null ? "stavba" : "předmět";

    // Kontrakt pro UI a hledání (plní Catalog/Tags/Progression):
    /// <summary>Tagy bez '#', malými písmeny (např. "weapons", "swords", "plains", "set:troll").</summary>
    public HashSet<string> Tags { get; } = new();
    /// <summary>Pořadí biomu podle Progression (0 = Louky), null = neznámý.</summary>
    public int? Biome { get; set; }
    /// <summary>Podkategorie pro řazení v rámci kategorie (např. "swords"), může být null.</summary>
    public string Subcategory { get; set; }
    /// <summary>Úroveň stanice potřebná k výrobě (0 = bez stanice nebo nevyrábí se).</summary>
    public int StationLevel { get; set; }
    /// <summary>Hlavní kategorie jako tag ("weapons", "pieces"…), viz Tags.Categories.</summary>
    public string MainTag { get; set; } = "misc";
    /// <summary>Hodnoty jídla (zdraví, výdrž, eitr), null u předmětů, které nejsou jídlo.</summary>
    public (float Health, float Stamina, float Eitr)? Food { get; set; }
    /// <summary>Název a mód bez mezer, předpočítané pro hledání (bez alokací na každý znak).</summary>
    public string CompactName { get; } = (name ?? "").Replace(" ", "");
    public string CompactMod { get; } = (mod ?? "").Replace(" ", "");

    internal ItemDrop Drop { get; set; }
    internal Piece Piece { get; set; }
    internal bool IsFeast { get; set; }
}

internal readonly struct ItemStack(Entry entry, int amount)
{
    public Entry Entry { get; } = entry;
    public int Amount { get; } = amount;
}

/// <summary>Druh karty: výroba (ObjectDB recept), stavba, zpracování, pěstování, produkce (úl), zdroj bez výroby.</summary>
internal enum CardKind { Craft, Build, Conversion, Grow, Production, Source }

internal sealed class RecipeCard(string title, string group, bool isUpgrade, Entry station, int stationLevel, List<ItemStack> inputs, List<ItemStack> outputs, List<string> notes)
{
    public string Title { get; } = title;
    public string Group { get; } = group;
    public bool IsUpgrade { get; } = isUpgrade;
    public Entry Station { get; } = station;
    public int StationLevel { get; } = stationLevel;
    public List<ItemStack> Inputs { get; } = inputs;
    public List<ItemStack> Outputs { get; } = outputs;
    public List<string> Notes { get; } = notes;

    // Kontrakt pro UI:
    /// <summary>Vanilla recept (jen u výroby z ObjectDB), pro výběr ve vanilla craftingu. Jinak null.</summary>
    public Recipe Recipe { get; set; }
    /// <summary>Kvalita výsledku (1 = výroba, 2+ = vylepšení).</summary>
    public int Quality { get; set; } = 1;
    /// <summary>Biom karty (receptu nebo zdroje), null = neznámý.</summary>
    public int? Biome { get; set; }
    public CardKind Kind { get; set; } = CardKind.Craft;
    /// <summary>Stačí jedna z ingrediencí (Recipe.m_requireOnlyOneIngredient).</summary>
    public bool OnlyOneIngredient { get; set; }

    // Pro výpočet biomu (zdroje): přímý biom, slabý zdroj (obchodník, truhla podle jména), potřebný nástroj.
    internal int? Seed { get; set; }
    internal bool Weak { get; set; }
    internal int ToolTier { get; set; }
    internal System.Func<HitData.DamageTypes, float> ToolDamage { get; set; }
}

internal sealed class Catalog
{
    private const string Vanilla = "Valheim";

    private readonly Dictionary<string, Entry> entries = new();
    private readonly Dictionary<string, List<RecipeCard>> producing = new();
    private readonly Dictionary<string, List<RecipeCard>> using_ = new();
    private readonly Dictionary<string, List<RecipeCard>> upgrades = new();
    private readonly Dictionary<string, List<RecipeCard>> sources = new();
    private readonly List<RecipeCard> cards = new();
    private readonly Dictionary<Entry, string> stationNames = new();
    private readonly Dictionary<SortMode, IReadOnlyList<Entry>> sorted = new();
    private readonly Dictionary<string, string> tagDescriptions = new();
    private readonly Dictionary<string, string> mods;
    private readonly Filter filter = new();
    private readonly HashSet<string> seeds = new();
    private readonly HashSet<string> buildable = new();
    private static readonly List<RecipeCard> Empty = new();

    public IReadOnlyList<Entry> All { get; }
    public int SourceRecipeCount { get; }
    /// <summary>Počet předmětů v ObjectDB při stavbě; katalog je zastaralý, když nesedí s db.m_items.Count.</summary>
    public int SourceItemCount { get; }
    /// <summary>DataConfig.Version při stavbě; při změně configu katalogu přestavět.</summary>
    public int ConfigVersion { get; }
    public long BuildMilliseconds { get; }

    /// <summary>Je katalog zastaralý vůči ObjectDB a configu?</summary>
    public bool IsStale(ObjectDB db) => !db || db.m_recipes.Count != SourceRecipeCount || db.m_items.Count != SourceItemCount
        || DataConfig.Version != ConfigVersion || Sources.SpawnSignature() != SpawnSignature || Locations.Version != LocationVersion;
    /// <summary>Sources.SpawnSignature při stavbě (spawny módů můžou dorazit až se serverem).</summary>
    public int SpawnSignature { get; }
    /// <summary>Locations.Version při stavbě (lokace se načítají na pozadí a dorazí později).</summary>
    public int LocationVersion { get; }

    public Catalog(ObjectDB db, ZNetScene scene)
    {
        var watch = Stopwatch.StartNew();
        SourceRecipeCount = db.m_recipes.Count;
        SourceItemCount = db.m_items.Count;
        ConfigVersion = DataConfig.Version;
        SpawnSignature = Sources.SpawnSignature(fresh: true);
        LocationVersion = Locations.Version;
        mods = ModsByPrefab();

        foreach (var prefab in db.m_items.Where(p => p))
        {
            var drop = prefab.GetComponent<ItemDrop>();
            var shared = drop ? drop.m_itemData.m_shared : null;
            if (shared == null || shared.m_icons == null || shared.m_icons.Length == 0) continue;
            string mod = ModOf(prefab.name);
            if (filter.RejectItem(prefab.name, shared, mod == Vanilla) is { } reason)
            {
                filter.Hide(prefab.name, shared.m_name, reason);
                continue;
            }
            var entry = Add(prefab.name, Filter.DisplayName(shared.m_name, prefab.name), shared.m_icons[0], () => drop.m_itemData.GetTooltip(), shared.m_name);
            entry.Drop = drop;
        }

        var extensionLinks = new List<(string extension, string station)>();
        foreach (var tool in db.m_items.Where(p => p).Select(p => p.GetComponent<ItemDrop>()).Where(d => d && d.m_itemData.m_shared.m_buildPieces))
        {
            var pieces = tool.m_itemData.m_shared.m_buildPieces.m_pieces.Where(p => p).Select(p => p.GetComponent<Piece>()).Where(p => p && p.m_enabled).ToList();
            if (Find(tool.name) is { } toolEntry && (pieces.Any(p => p.GetComponent<Feast>()) || tool.m_itemData.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.Feaster))
                toolEntry.IsFeast = true;
            foreach (var piece in pieces)
            {
                buildable.Add(piece.name);
                if (piece.GetComponent<StationExtension>() is { } extension && extension.m_craftingStation)
                    extensionLinks.Add((piece.name, extension.m_craftingStation.name));
                if (filter.RejectPiece(piece, ModOf(piece.name) == Vanilla) is { } reason) filter.Hide(piece.name, piece.m_name, reason);
                else AddPieceRecipe(piece, tool);
            }
        }
        foreach (var recipe in db.m_recipes.Where(r => r && r.m_enabled && r.m_item))
            AddCraftingRecipes(recipe);
        int skippedStations = 0;
        foreach (var prefab in scene.m_prefabs.Where(p => p))
            if (!AddConversions(prefab)) skippedStations++;
        var harvest = new Sources(scene, Find, cards.Add);
        harvest.Collect();

        RemoveUnobtainable();
        foreach (var card in cards) Register(card);
        AssignTags();
        var extensions = new Dictionary<Entry, List<Entry>>();
        foreach (var (extension, station) in extensionLinks)
            if (Find(extension) is { } e && Find(station) is { } s)
                (extensions.TryGetValue(s, out var list) ? list : extensions[s] = new List<Entry>()).Add(e);
        Progression.Assign(entries.Values, cards, extensions, Progression.Overrides());
        foreach (var entry in entries.Values) entry.Tags.Add(Progression.Tag(entry.Biome));

        All = entries.Values.OrderBy(e => e.Mod != Vanilla).ThenBy(e => e.Mod).ThenBy(e => e.Name).ToList();
        var counts = new Dictionary<string, int>();
        foreach (var tag in All.SelectMany(e => e.Tags)) counts[tag] = counts.TryGetValue(tag, out var n) ? n + 1 : 1;
        TagCounts = counts;
        ModCounts = All.GroupBy(e => e.Mod).ToDictionary(g => g.Key, g => g.Count());
        BuildMilliseconds = watch.ElapsedMilliseconds;
        DataConfig.Log.LogInfo($"Katalog: {All.Count} položek, {cards.Count} karet, {BuildMilliseconds} ms. Vegetace {harvest.VegetationCount}, tvorů s biomem {harvest.CreatureCount}, " +
            $"biom neznámý u {All.Count(e => !e.Biome.HasValue)}. Přeskočeno {skippedStations} prefabů zpracování mimo tabulky staveb. " +
            (Locations.Done ? $"Lokace: {Locations.Places.Count} prefabů." : "Lokace se ještě načítají (truhly zatím jen podle jména)."));
        DataConfig.Log.LogInfo("Skryto: " + filter.Summary());
    }

    // Kontrakt pro UI:
    /// <summary>Položky seřazené podle zvoleného řazení (výsledek se cachuje).</summary>
    public IReadOnlyList<Entry> Sorted(SortMode mode)
    {
        if (sorted.TryGetValue(mode, out var list)) return list;
        return sorted[mode] = mode switch
        {
            SortMode.Name => All.OrderBy(e => e.Name).ThenBy(e => e.Key).ToList(),
            SortMode.Mod => All,
            _ => All.OrderBy(e => e.Biome ?? int.MaxValue).ThenBy(e => Tags.CategoryIndex(e.MainTag)).ThenBy(e => Tags.SubcategoryIndex(e.Subcategory))
                .ThenBy(e => e.Subcategory).ThenBy(e => e.StationLevel).ThenBy(e => e.Name).ToList(),
        };
    }

    /// <summary>Všechny tagy s počtem položek, pro našeptávač a čipy.</summary>
    public IReadOnlyDictionary<string, int> TagCounts { get; }
    /// <summary>Všechny módy (jak je ukazuje Entry.Mod) s počtem položek.</summary>
    public IReadOnlyDictionary<string, int> ModCounts { get; }

    /// <summary>Český popis tagu (i "set:…" a "station:…"), null když ho neznáme.</summary>
    public string TagDescription(string tag) => tagDescriptions.TryGetValue(tag, out var text) ? text : Tags.Describe(tag);

    /// <summary>CraftingStation.m_name stanice (pro hledání stanice v dosahu), null když položka není stanice.</summary>
    public string StationName(Entry station) => station != null && stationNames.TryGetValue(station, out var name) ? name : null;

    public Entry Find(string key) => key != null && entries.TryGetValue(key, out var entry) ? entry : null;
    public IReadOnlyList<RecipeCard> Cards(Entry entry, View view) =>
        (view switch { View.Recipes => producing, View.Upgrades => upgrades, View.Sources => sources, _ => using_ }).TryGetValue(entry.Key, out var list) ? list : Empty;

    private static Dictionary<string, string> ModsByPrefab()
    {
        var folders = Chainloader.PluginInfos.Values.ToDictionary(p => p.Metadata.GUID, p => Path.GetFileName(Path.GetDirectoryName(p.Location)));
        var mods = new Dictionary<string, string>();
        foreach (var modPrefab in ModQuery.GetPrefabs())
        {
            if (!modPrefab.Prefab || modPrefab.SourceMod == null) continue;
            string folder = folders.TryGetValue(modPrefab.SourceMod.GUID, out var f) && f != "plugins" ? f : null;
            mods[modPrefab.Prefab.name] = folder ?? modPrefab.SourceMod.Name;
        }
        return mods;
    }

    private string ModOf(string prefab) => mods.TryGetValue(prefab, out var mod) ? mod : Vanilla;

    private Entry Add(string key, string name, Sprite icon, System.Func<string> details, string itemName = null)
    {
        if (entries.TryGetValue(key, out var existing)) return existing;
        return entries[key] = new Entry(key, name, icon, ModOf(key), itemName, details);
    }

    private Entry PieceEntry(Piece piece)
    {
        if (!piece) return null;
        var entry = Add(piece.name, Filter.DisplayName(piece.m_name, piece.name), piece.m_icon, () => Localization.instance.Localize(piece.m_description));
        entry.Piece = piece;
        if (piece.GetComponent<CraftingStation>() is { } station && station) stationNames[entry] = station.m_name;
        return entry;
    }

    // Stanice receptu; ty, které nejsou v žádné tabulce staveb (fire_pit_haldor, goblin_bed…), skryje RemoveUnobtainable.
    private Entry StationEntry(CraftingStation station)
    {
        if (!station) return null;
        var entry = Find(station.name) ?? Add(station.name, Filter.DisplayName(station.m_name, station.name), station.m_icon, () => "");
        entry.Piece ??= station.GetComponent<Piece>();
        stationNames[entry] = station.m_name;
        return entry;
    }

    private void AddPieceRecipe(Piece piece, ItemDrop tool)
    {
        var output = PieceEntry(piece);
        var inputs = piece.m_resources.Where(r => r.m_resItem && r.m_amount > 0)
            .Select(r => new ItemStack(Find(r.m_resItem.name), r.m_amount)).Where(s => s.Entry != null).ToList();
        string toolName = Localization.instance.Localize(tool.m_itemData.m_shared.m_name);
        cards.Add(new RecipeCard("Stavba", $"Stavba · {toolName}", false, StationEntry(piece.m_craftingStation), 0, inputs,
            new List<ItemStack> { new(output, 1) }, new List<string> { $"Nástroj: {toolName}" }) { Kind = CardKind.Build });
        if (piece.GetComponent<Plant>() is not { } plant || !plant) return;
        seeds.Add(piece.name);
        foreach (var input in inputs) seeds.Add(input.Entry.Key);
        var crops = new List<ItemStack>();
        var notes = new List<string> { $"Zasaď: {output.Name}", $"Roste {plant.m_growTime / 60f:0}–{plant.m_growTimeMax / 60f:0} min" };
        foreach (var pickable in plant.m_grownPrefabs.Where(g => g).Select(g => g.GetComponent<Pickable>()).Where(p => p))
        {
            if (pickable.m_itemPrefab && Find(pickable.m_itemPrefab.name) is { } crop && crops.All(c => c.Entry != crop))
                crops.Add(new ItemStack(crop, Mathf.Max(1, pickable.m_amount)));
            foreach (var extra in pickable.m_extraDrops.m_drops.Where(d => d.m_item && Find(d.m_item.name) != null))
                if (crops.All(c => c.Entry.Key != extra.m_item.name)) crops.Add(new ItemStack(Find(extra.m_item.name), Mathf.Max(1, extra.m_stackMax)));
        }
        if (crops.Count > 0 && inputs.Count > 0)
            cards.Add(new RecipeCard("Pěstování", "Pěstování", false, null, 0, inputs, crops, notes) { Kind = CardKind.Grow });
    }

    private void AddCraftingRecipes(Recipe recipe)
    {
        var result = Find(recipe.m_item.name);
        if (result == null) return;
        int maxQuality = Mathf.Max(1, recipe.m_item.m_itemData.m_shared.m_maxQuality);
        for (int quality = recipe.m_noCraftOnlyUpgrade ? 2 : 1; quality <= maxQuality; quality++)
        {
            var inputs = new List<ItemStack>();
            if (quality > 1) inputs.Add(new ItemStack(result, 1));
            inputs.AddRange(recipe.m_resources.Where(r => r.m_resItem && r.GetAmount(quality) > 0)
                .Select(r => new ItemStack(Find(r.m_resItem.name), r.GetAmount(quality))).Where(s => s.Entry != null));
            var notes = new List<string>();
            if (quality > 1) notes.Add($"Vstupní předmět musí mít úroveň {quality - 1}");
            if (recipe.m_requireOnlyOneIngredient) notes.Add("Stačí jedna z ingrediencí");
            var station = recipe.GetRequiredStation(quality);
            string title = quality == 1 ? "Výroba" : $"Vylepšení na úroveň {quality}";
            var stationEntry = StationEntry(station);
            cards.Add(new RecipeCard(title, stationEntry?.Name ?? "Ruční výroba", quality > 1, stationEntry, station ? recipe.GetRequiredStationLevel(quality) : 0,
                inputs, new List<ItemStack> { new(result, quality == 1 ? recipe.m_amount : 1) }, notes)
            {
                Kind = CardKind.Craft, Recipe = recipe, Quality = quality, OnlyOneIngredient = recipe.m_requireOnlyOneIngredient,
            });
        }
    }

    /// <summary>Zpracování v peci, kvasné kádi, na ohništi. False = prefab zpracovává, ale není v žádné tabulce staveb.</summary>
    private bool AddConversions(GameObject prefab)
    {
        var smelter = prefab.GetComponent<Smelter>();
        var fermenter = prefab.GetComponent<Fermenter>();
        var cooking = prefab.GetComponent<CookingStation>();
        if (!smelter && !fermenter && !cooking) return true;
        var station = Find(prefab.name);
        if (station == null)
        {
            if (DataConfig.LogFilter?.Value == true) DataConfig.Log.LogInfo($"Zpracování {prefab.name} přeskočeno: není v tabulce staveb");
            return false;
        }
        if (smelter)
        {
            var fuel = smelter.m_fuelItem ? $"Palivo: {smelter.m_fuelPerProduct}× {Find(smelter.m_fuelItem.name)?.Name}" : null;
            foreach (var c in smelter.m_conversion)
                AddConversion(station, c.m_from, 1, c.m_to, 1, fuel, $"{smelter.m_secPerProduct:0} s za kus");
        }
        if (fermenter)
            foreach (var c in fermenter.m_conversion)
                AddConversion(station, c.m_from, 1, c.m_to, c.m_producedItems, null, $"{fermenter.m_fermentationDuration / 60f:0} min");
        if (cooking)
        {
            var fuel = cooking.m_useFuel && cooking.m_fuelItem ? $"Palivo: {Find(cooking.m_fuelItem.name)?.Name}" : null;
            foreach (var c in cooking.m_conversion)
                AddConversion(station, c.m_from, 1, c.m_to, 1, fuel, $"{c.m_cookTime:0} s");
        }
        return true;
    }

    private void AddConversion(Entry station, ItemDrop from, int fromAmount, ItemDrop to, int toAmount, string fuel, string time)
    {
        var input = from ? Find(from.name) : null;
        var output = to ? Find(to.name) : null;
        if (input == null || output == null) return;
        var notes = new List<string> { time };
        if (fuel != null) notes.Add(fuel);
        cards.Add(new RecipeCard("Zpracování", station.Name, false, station, 0, new List<ItemStack> { new(input, fromAmount) },
            new List<ItemStack> { new(output, toAmount) }, notes) { Kind = CardKind.Conversion });
    }

    /// <summary>SPEC 1.1 pravidla 4–6: stanice mimo tabulky staveb, duplicity podle m_name a položky bez zdroje i použití.</summary>
    private void RemoveUnobtainable()
    {
        var removed = new HashSet<Entry>();
        // Pravidlo 6: stanice receptu, kterou nejde postavit žádným nástrojem (fire_pit_haldor…), recept zneplatní.
        foreach (var station in stationNames.Keys.Where(s => !buildable.Contains(s.Key)))
        {
            removed.Add(station);
            filter.Hide(station.Key, station.Name, "stanice mimo tabulky staveb");
        }
        cards.RemoveAll(c => removed.Contains(c.Station));
        var produced = new HashSet<Entry>(cards.Where(c => !c.IsUpgrade).SelectMany(c => c.Outputs).Select(o => o.Entry));
        var consumed = new HashSet<Entry>(cards.SelectMany(c => c.Inputs.Select(i => i.Entry).Append(c.Station)).Where(e => e != null));
        var used = new HashSet<Entry>(consumed.Concat(cards.SelectMany(c => c.Outputs.Select(o => o.Entry))));
        var overrides = Progression.Overrides();
        foreach (var group in entries.Values.Where(e => e.Drop && !removed.Contains(e)).GroupBy(e => e.Drop.m_itemData.m_shared.m_name).Where(g => g.Count() > 1))
        {
            // Ingredienci nebo stanici nechat i bez receptu, jinak by zmizely recepty, které ji potřebují.
            var keep = group.Where(e => produced.Contains(e) || consumed.Contains(e)).ToList();
            if (keep.Count == 0) keep = group.Where(used.Contains).ToList();
            if (keep.Count == 0) continue;
            foreach (var entry in group.Except(keep))
            {
                removed.Add(entry);
                filter.Hide(entry.Key, entry.Name, "duplicita");
            }
        }
        if (DataConfig.HideUnobtainable?.Value != false)
            foreach (var entry in entries.Values.Where(e => !removed.Contains(e) && !used.Contains(e) && !overrides.ContainsKey(e.Key) && !(e.Drop && e.Drop.m_itemData.m_shared.m_buildPieces)))
            {
                removed.Add(entry);
                filter.Hide(entry.Key, entry.Name, "bez zdroje i použití");
            }
        if (removed.Count == 0) return;
        foreach (var entry in removed) entries.Remove(entry.Key);
        foreach (var card in cards.Where(c => c.Kind == CardKind.Source)) card.Outputs.RemoveAll(o => removed.Contains(o.Entry));
        cards.RemoveAll(c => c.Outputs.Count == 0 || c.Outputs.Any(o => removed.Contains(o.Entry)) || c.Inputs.Any(i => removed.Contains(i.Entry)) || removed.Contains(c.Station));
    }

    private void AssignTags()
    {
        foreach (var entry in entries.Values)
        {
            if (entry.Drop) Tags.OfItem(entry, entry.Drop.m_itemData.m_shared, entry.IsFeast);
            else Tags.OfPiece(entry, entry.Piece, seeds.Contains(entry.Key));
            if (entry.Drop && seeds.Contains(entry.Key)) entry.Tags.Add("seeds");
            var made = Cards(entry, View.Recipes).Where(c => c.Kind is CardKind.Craft or CardKind.Build).ToList();
            entry.StationLevel = made.Count == 0 ? 0 : made.Min(c => c.Station == null ? 0 : Mathf.Max(1, c.StationLevel));
            foreach (var station in Cards(entry, View.Recipes).Select(c => c.Station).Where(s => s != null).Distinct())
            {
                string tag = Tags.StationTag(station);
                entry.Tags.Add(tag);
                tagDescriptions[tag] = "Vyrábí se na: " + station.Name;
            }
            foreach (var tag in entry.Tags.Where(t => t.StartsWith(Tags.SetPrefix)))
                if (!tagDescriptions.ContainsKey(tag)) tagDescriptions[tag] = "Sada zbroje: " + entry.Name;
        }
    }

    private void Register(RecipeCard card)
    {
        var inputs = card.Inputs.Select(s => s.Entry).Append(card.Station).Where(e => e != null);
        if (card.IsUpgrade)
        {
            foreach (var entry in inputs.Concat(card.Outputs.Select(s => s.Entry)).Distinct()) ListFor(upgrades, entry).Add(card);
            return;
        }
        foreach (var output in card.Outputs.Select(o => o.Entry).Distinct()) ListFor(card.Kind == CardKind.Source ? sources : producing, output).Add(card);
        foreach (var input in inputs.Distinct()) ListFor(using_, input).Add(card);
    }

    private static List<RecipeCard> ListFor(Dictionary<string, List<RecipeCard>> map, Entry entry) =>
        map.TryGetValue(entry.Key, out var list) ? list : map[entry.Key] = new List<RecipeCard>();
}
