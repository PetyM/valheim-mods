using System.Collections.Generic;
using System.Linq;
using Biome = Heightmap.Biome;

namespace RecipeBrowser;

/// <summary>Biome order and computation of entry biomes (SPEC 1.3).</summary>
internal static class Progression
{
    public const int Meadows = 0, BlackForest = 1, Swamp = 2, Ocean = 3, Mountain = 4, Plains = 5, Mistlands = 6, AshLands = 7, DeepNorth = 8;
    public const string UnknownName = "Unknown", UnknownTag = "unknown";

    private static readonly string[] Names = { "Meadows", "Black Forest", "Swamp", "Ocean", "Mountains", "Plains", "Mistlands", "Ashlands", "Deep North" };
    private static readonly string[] TagNames = { "meadows", "blackforest", "swamp", "ocean", "mountains", "plains", "mistlands", "ashlands", "deepnorth" };
    private static readonly (Biome biome, int rank)[] Bits =
    {
        (Biome.Meadows, Meadows), (Biome.BlackForest, BlackForest), (Biome.Swamp, Swamp), (Biome.Ocean, Ocean), (Biome.Mountain, Mountain),
        (Biome.Plains, Plains), (Biome.Mistlands, Mistlands), (Biome.AshLands, AshLands), (Biome.DeepNorth, DeepNorth),
    };
    private static readonly Dictionary<string, int> BossKeys = new()
    {
        ["defeated_eikthyr"] = Meadows, ["defeated_gdking"] = BlackForest, ["defeated_bonemass"] = Swamp, ["defeated_dragon"] = Mountain,
        ["defeated_goblinking"] = Plains, ["defeated_queen"] = Mistlands, ["defeated_fader"] = AshLands,
    };
    private static readonly string[] BossNames = { "Eikthyr", "The Elder", "Bonemass", "Moder", "Yagluth", "The Queen", "Fader" };

    // Manual assignment where game data gives no biome (ocean, bosses in locations, chests everywhere).
    private static readonly Dictionary<string, int> VanillaOverrides = new()
    {
        ["Chitin"] = Ocean, ["SerpentMeat"] = Ocean, ["SerpentScale"] = Ocean, ["TrophySerpent"] = Ocean,
        ["Coins"] = Meadows, ["Amber"] = Meadows, ["AmberPearl"] = BlackForest, ["Ruby"] = BlackForest, ["YmirRemains"] = BlackForest,
        ["TrophyEikthyr"] = Meadows, ["HardAntler"] = Meadows, ["TrophyTheElder"] = BlackForest, ["CryptKey"] = BlackForest,
        ["TrophyBonemass"] = Swamp, ["Wishbone"] = Swamp, ["DragonEgg"] = Mountain, ["TrophyDragonQueen"] = Mountain, ["DragonTear"] = Mountain,
        ["TrophyGoblinKing"] = Plains, ["YagluthDrop"] = Plains, ["TrophySeekerQueen"] = Mistlands, ["QueenDrop"] = Mistlands,
        ["TrophyFader"] = AshLands, ["FaderDrop"] = AshLands,
    };

    public static int Count => Names.Length;
    public static string Name(int rank) => rank >= 0 && rank < Names.Length ? Names[rank] : UnknownName;
    public static string Name(int? rank) => rank.HasValue ? Name(rank.Value) : UnknownName;
    public static string Tag(int? rank) => rank is >= 0 and < 9 ? TagNames[rank.Value] : UnknownTag;
    public static IReadOnlyList<string> Tags => TagNames;

    /// <summary>Lowest rank over the mask bits, null for an empty mask.</summary>
    public static int? FromMask(Biome mask)
    {
        foreach (var (biome, rank) in Bits.OrderBy(b => b.rank))
            if ((mask & biome) != 0) return rank;
        return null;
    }

    public static IEnumerable<int> RanksOf(Biome mask) => Bits.Where(b => (mask & b.biome) != 0).Select(b => b.rank).OrderBy(r => r);

    /// <summary>Biome of the boss whose defeat sets the global key. Unknown key = null.</summary>
    public static int? FromKey(string key) => !string.IsNullOrEmpty(key) && BossKeys.TryGetValue(key.ToLowerInvariant(), out var rank) ? rank : null;

    public static string KeyLabel(string key) => FromKey(key) is { } rank ? $"after defeating {BossNames[rank > Ocean ? rank - 1 : rank]}" : $"key: {key}";

    public static int? Max(int? a, int? b) => a.HasValue && b.HasValue ? System.Math.Max(a.Value, b.Value) : a ?? b;

    /// <summary>Guesses the biome from the prefab name (chests, pickables outside vegetation). Weak source only.</summary>
    public static int? GuessFromName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("meadow")) return Meadows;
        if (n.Contains("blackforest") || n.Contains("forestcrypt") || n.Contains("trollcave")) return BlackForest;
        if (n.Contains("swamp") || n.Contains("sunkencrypt")) return Swamp;
        if (n.Contains("mountain") || n.Contains("frostcave")) return Mountain;
        if (n.Contains("plains") || n.Contains("heath") || n.Contains("goblin")) return Plains;
        if (n.Contains("mistland") || n.Contains("dvergr")) return Mistlands;
        if (n.Contains("ashland") || n.Contains("charred")) return AshLands;
        return null;
    }

    public static int? ParseRank(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (int.TryParse(text, out var number)) return number >= 0 && number < Names.Length ? number : null;
        for (int i = 0; i < Names.Length; i++)
            if (TagNames[i] == text || Names[i].ToLowerInvariant() == text) return i;
        return null;
    }

    /// <summary>Vanilla table extended with user overrides from config (they take precedence).</summary>
    public static Dictionary<string, int> Overrides()
    {
        var result = new Dictionary<string, int>(VanillaOverrides);
        foreach (var pair in (DataConfig.BiomeOverrides?.Value ?? "").Split(',', ';'))
        {
            var parts = pair.Split('=');
            if (parts.Length != 2 || parts[0].Trim().Length == 0) continue;
            if (ParseRank(parts[1]) is { } rank) result[parts[0].Trim()] = rank;
            else DataConfig.Log.LogWarning($"Unknown biome in override: {pair}");
        }
        return result;
    }

    /// <summary>
    /// Propagates biomes to a fixed point: sources give the initial biome, a crafting card has max(inputs, station at the given level),
    /// an entry has the min over all its cards. Weak sources (traders, chests by name) only fill in what stayed unknown.
    /// </summary>
    public static void Assign(ICollection<Entry> entries, IReadOnlyList<RecipeCard> cards, Dictionary<Entry, List<Entry>> extensions, Dictionary<string, int> overrides)
    {
        var biome = new Dictionary<Entry, int>();
        var fixedEntries = new HashSet<Entry>();
        foreach (var entry in entries)
            if (overrides.TryGetValue(entry.Key, out var rank))
            {
                biome[entry] = rank;
                fixedEntries.Add(entry);
            }
        var tools = entries.Where(e => e.Drop && e.Drop.m_itemData.m_shared.m_toolTier > 0).ToList();
        var active = cards.Where(c => !c.IsUpgrade && !c.Weak).ToList();
        Run(active, null);
        // Weak sources may only fill in what stayed unknown after the first pass; active cards can then lower it further.
        var seeded = new HashSet<Entry>(biome.Keys);
        Run(cards.Where(c => c.Weak && c.Outputs.Any(o => !seeded.Contains(o.Entry))).Concat(active).ToList(), seeded);
        foreach (var entry in entries) entry.Biome = biome.TryGetValue(entry, out var rank) ? rank : null;
        foreach (var card in cards) card.Biome = Cost(card);

        void Run(List<RecipeCard> list, HashSet<Entry> frozenForWeak)
        {
            for (int iteration = 0, changed = 1; changed > 0 && iteration < 100; iteration++)
            {
                changed = 0;
                foreach (var card in list)
                    if (!card.IsUpgrade && Cost(card) is { } cost)
                        foreach (var output in card.Outputs)
                            if (!fixedEntries.Contains(output.Entry) && !(card.Weak && frozenForWeak != null && frozenForWeak.Contains(output.Entry))
                                && (!biome.TryGetValue(output.Entry, out var current) || cost < current))
                            {
                                biome[output.Entry] = cost;
                                changed++;
                            }
            }
        }

        int? Known(Entry entry) => entry != null && biome.TryGetValue(entry, out var rank) ? rank : null;

        int? Cost(RecipeCard card)
        {
            int? cost = card.Kind == CardKind.Source ? card.Seed : null;
            if (card.Kind == CardKind.Source && !cost.HasValue) return null;
            var known = card.Inputs.Select(i => Known(i.Entry)).ToList();
            if (card.OnlyOneIngredient && known.Count > 0)
            {
                if (known.Min() is not { } cheapest) return null;
                cost = Max(cost, cheapest);
            }
            else if (known.Any(k => !k.HasValue)) return null;
            else if (known.Count > 0) cost = Max(cost, known.Max());
            if (card.Station != null)
            {
                if (StationCost(card.Station, card.StationLevel) is not { } station) return null;
                cost = Max(cost, station);
            }
            if (card.ToolTier > 0 && card.ToolDamage != null)
                cost = Max(cost, tools.Where(t => t.Drop.m_itemData.m_shared.m_toolTier >= card.ToolTier && card.ToolDamage(t.Drop.m_itemData.m_shared.m_damages) > 0)
                    .Select(Known).Where(k => k.HasValue).DefaultIfEmpty(null).Min());
            return cost;
        }

        int? StationCost(Entry station, int level)
        {
            var own = Known(station);
            if (!own.HasValue || level < 2 || !extensions.TryGetValue(station, out var list)) return own;
            var ranks = list.Select(Known).Where(k => k.HasValue).Select(k => k.Value).OrderBy(r => r).ToList();
            return ranks.Count == 0 ? own : Max(own, ranks[System.Math.Min(level - 2, ranks.Count - 1)]);
        }
    }
}
