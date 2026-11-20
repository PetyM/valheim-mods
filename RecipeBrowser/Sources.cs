using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RecipeBrowser;

/// <summary>
/// Sources cards (SPEC 1.4) and initial biomes (SPEC 1.3): vegetation and its drops, creatures from spawn lists, traders,
/// fishing, beehives and sap collectors, prefabs and creatures from locations and dungeons (Locations). Until the location scan
/// finishes, chests only get a biome from the prefab name (weak source).
/// </summary>
internal sealed class Sources(ZNetScene scene, System.Func<string, Entry> find, System.Action<RecipeCard> add)
{
    private const string Harvest = "Pickup", Mining = "Mining", Chopping = "Chopping", Breaking = "Breaking", Chest = "Chest",
        Drop = "Drop", Fishing = "Fishing", Trade = "Trader";

    private readonly Dictionary<string, SortedSet<int>> vegetation = new();
    private readonly Dictionary<string, int> creatures = new();
    public int VegetationCount => vegetation.Count;
    public int CreatureCount => creatures.Count;

    public void Collect()
    {
        var zone = ZoneSystem.instance;
        if (zone)
            foreach (var veg in zone.m_vegetation.Where(v => v.m_enable && v.m_prefab))
            {
                var ranks = vegetation.TryGetValue(veg.m_prefab.name, out var set) ? set : vegetation[veg.m_prefab.name] = new SortedSet<int>();
                foreach (var rank in Progression.RanksOf(veg.m_biome)) ranks.Add(rank);
                if (Progression.FromMask(veg.m_biome) is not { } lowest) continue;
                foreach (var area in veg.m_prefab.GetComponentsInChildren<SpawnArea>(true))
                    foreach (var spawn in area.m_prefabs.Where(p => p.m_prefab)) Lower(spawn.m_prefab.name, lowest);
                foreach (var spawner in veg.m_prefab.GetComponentsInChildren<CreatureSpawner>(true).Where(s => s.m_creaturePrefab))
                    Lower(spawner.m_creaturePrefab.name, lowest);
            }
        else DataConfig.Log.LogWarning("ZoneSystem does not exist, biomes from vegetation and spawns are missing");
        foreach (var spawn in SpawnLists().SelectMany(l => l.m_spawners).Where(s => s != null && s.m_enabled && !s.m_devDisabled && s.m_prefab))
            if (Progression.Max(Progression.FromMask(spawn.m_biome), Progression.FromKey(spawn.m_requiredGlobalKey)) is { } rank)
                Lower(spawn.m_prefab.name, rank);
        foreach (var pair in Locations.Creatures) Lower(pair.Key, pair.Value.Min);

        var prefabs = scene.m_prefabs.Where(p => p).ToList();
        var children = new HashSet<GameObject>();
        foreach (var prefab in prefabs)
        {
            if (prefab.GetComponent<TreeBase>() is { m_logPrefab: { } log }) children.Add(log);
            if (prefab.GetComponent<TreeLog>() is { m_subLogPrefab: { } sub }) children.Add(sub);
            if (prefab.GetComponent<Destructible>() is { m_spawnWhenDestroyed: { } spawned }) children.Add(spawned);
        }
        foreach (var prefab in prefabs)
        {
            if (prefab.GetComponent<Trader>() is { } trader) AddTrader(prefab, trader);
            if (prefab.GetComponent<Fish>() is { } fish) AddFish(prefab, fish);
            AddProduction(prefab);
            if (prefab.GetComponent<Character>() is { } character)
            {
                if (!string.IsNullOrEmpty(character.m_defeatSetGlobalKey) && Progression.FromKey(character.m_defeatSetGlobalKey) is { } boss && !creatures.ContainsKey(prefab.name))
                    creatures[prefab.name] = boss;
                AddCreature(prefab, character);
                continue;
            }
            if (!children.Contains(prefab) && !prefab.GetComponent<ItemDrop>() && (find(prefab.name) == null || vegetation.ContainsKey(prefab.name)))
                AddHarvest(prefab);
        }
    }

    private void Lower(string creature, int rank)
    {
        if (!creatures.TryGetValue(creature, out var current) || rank < current) creatures[creature] = rank;
    }

    private const float SignatureInterval = 2f;
    private static float signatureAt = float.MinValue;
    private static int signature;

    /// <summary>
    /// Fingerprint of all spawn lists read by Collect (number of lists and spawns); changes when mods add spawns after syncing
    /// with the server. FindObjectsOfTypeAll is not free, so outside a catalog build (fresh) it is recomputed at most every 2 s.
    /// </summary>
    public static int SpawnSignature(bool fresh = false)
    {
        if (!fresh && Time.realtimeSinceStartup - signatureAt < SignatureInterval) return signature;
        signatureAt = Time.realtimeSinceStartup;
        unchecked
        {
            int sum = 0;
            foreach (var list in SpawnLists()) sum = sum * 31 + 1 + list.m_spawners.Count;
            return signature = sum;
        }
    }

    private static IEnumerable<SpawnSystemList> SpawnLists()
    {
        var lists = new HashSet<SpawnSystemList>(Resources.FindObjectsOfTypeAll<SpawnSystemList>());
        foreach (var system in SpawnSystem.m_instances.Where(s => s)) lists.UnionWith(system.m_spawnLists.Where(l => l));
        if (ZoneSystem.instance && ZoneSystem.instance.m_zoneCtrlPrefab && ZoneSystem.instance.m_zoneCtrlPrefab.GetComponent<SpawnSystem>() is { } ctrl)
            lists.UnionWith(ctrl.m_spawnLists.Where(l => l));
        return lists;
    }

    private void AddCreature(GameObject prefab, Character character)
    {
        if (prefab.GetComponent<CharacterDrop>() is not { } drop) return;
        var outputs = new List<ItemStack>();
        var notes = new List<string>();
        foreach (var d in drop.m_drops.Where(d => d.m_prefab && d.m_chance > 0))
            if (find(d.m_prefab.name) is { } entry)
            {
                outputs.Add(new ItemStack(entry, Mathf.Max(1, d.m_amountMax)));
                notes.Add($"{entry.Name} {Amount(d.m_amountMin, d.m_amountMax)} {Percent(d.m_chance)}{(d.m_onePerPlayer ? " per player" : "")}");
            }
        if (outputs.Count == 0) return;
        if (character.m_boss) notes.Insert(0, "Boss");
        string name = Filter.DisplayName(character.m_name, prefab.name);
        add(new RecipeCard($"{Drop} · {name}", Drop, false, null, 0, new List<ItemStack>(), outputs, notes)
        {
            Kind = CardKind.Source,
            Seed = creatures.TryGetValue(prefab.name, out var rank) ? rank : null,
        });
    }

    private void AddTrader(GameObject prefab, Trader trader)
    {
        string lower = prefab.name.ToLowerInvariant();
        int? traderRank = Locations.Places.TryGetValue(prefab.name, out var placed) && placed.Count > 0 ? placed.Min
            : lower.Contains("haldor") ? Progression.BlackForest : lower.Contains("hildir") ? Progression.Meadows
            : lower.Contains("bogwitch") ? Progression.Swamp : null;
        string name = Filter.DisplayName(trader.m_name, prefab.name);
        var coins = find("Coins");
        foreach (var item in trader.m_items.Where(i => i.m_prefab))
        {
            if (find(item.m_prefab.name) is not { } entry) continue;
            var notes = new List<string> { $"Price: {item.m_price} coins" };
            if (!string.IsNullOrEmpty(item.m_requiredGlobalKey)) notes.Add("Requires: " + Progression.KeyLabel(item.m_requiredGlobalKey));
            var inputs = coins != null ? new List<ItemStack> { new(coins, item.m_price) } : new List<ItemStack>();
            add(new RecipeCard($"{Trade} · {name}", Trade, false, null, 0, inputs, new List<ItemStack> { new(entry, Mathf.Max(1, item.m_stack)) }, notes)
            {
                Kind = CardKind.Source,
                Weak = true,
                Seed = Progression.Max(traderRank, Progression.FromKey(item.m_requiredGlobalKey)),
            });
        }
    }

    private void AddFish(GameObject prefab, Fish fish)
    {
        if (find(prefab.name) is not { } entry) return;
        int? rank = creatures.TryGetValue(prefab.name, out var r) ? r : null;
        var outputs = new List<ItemStack> { new(entry, 1) };
        var notes = new List<string>();
        AddTable(fish.m_extraDrops, outputs, notes);
        var baits = fish.m_baits.Where(b => b.m_bait && find(b.m_bait.name) != null).ToList();
        if (baits.Count == 0) baits.Add(null);
        foreach (var bait in baits)
        {
            var inputs = bait == null ? new List<ItemStack>() : new List<ItemStack> { new(find(bait.m_bait.name), 1) };
            var cardNotes = bait == null ? notes : notes.Prepend($"Bite chance: {Percent(bait.m_chance)}").ToList();
            add(new RecipeCard($"{Fishing} · {entry.Name}", Fishing, false, null, 0, inputs, outputs, cardNotes) { Kind = CardKind.Source, Seed = rank });
        }
    }

    private void AddProduction(GameObject prefab)
    {
        if (find(prefab.name) is not { } station) return;
        if (prefab.GetComponent<Beehive>() is { m_honeyItem: { } honey } && find(honey.name) is { } honeyEntry)
            add(new RecipeCard("Production", station.Name, false, station, 0, new List<ItemStack>(), new List<ItemStack> { new(honeyEntry, 1) },
                new List<string> { "Fills up over time" }) { Kind = CardKind.Production });
        if (prefab.GetComponent<SapCollector>() is { m_spawnItem: { } sap } collector && find(sap.name) is { } sapEntry)
            add(new RecipeCard("Production", station.Name, false, station, 0, new List<ItemStack>(), new List<ItemStack> { new(sapEntry, 1) },
                new List<string> { $"{collector.m_secPerUnit:0} s each" }) { Kind = CardKind.Production });
        if (prefab.GetComponent<Incinerator>() is { } incinerator && incinerator.m_conversions != null)
            foreach (var conversion in incinerator.m_conversions.Where(c => c.m_result && find(c.m_result.name) != null))
            {
                var inputs = conversion.m_requirements.Where(q => q.m_resItem && find(q.m_resItem.name) != null)
                    .Select(q => new ItemStack(find(q.m_resItem.name), q.m_amount)).ToList();
                if (inputs.Count == 0) continue;
                add(new RecipeCard("Processing", station.Name, false, station, 0, inputs,
                    new List<ItemStack> { new(find(conversion.m_result.name), conversion.m_resultAmount) }, new List<string> { "Any one ingredient" })
                { Kind = CardKind.Conversion, OnlyOneIngredient = true });
            }
    }

    // Pickup, mining, chopping, breaking and chests: the prefab's drop tables including the chain (tree → log → half logs, rock → fragments).
    private void AddHarvest(GameObject root)
    {
        var found = new Dictionary<string, (List<ItemStack> outputs, List<string> notes, int tier)>();
        string name = null;
        Visit(root, 0);
        if (found.Count == 0) return;
        var ranks = new SortedSet<int>();
        if (vegetation.TryGetValue(root.name, out var grown)) ranks.UnionWith(grown);
        if (Locations.Places.TryGetValue(root.name, out var placed)) ranks.UnionWith(placed);
        bool direct = ranks.Count > 0;
        int? seed = direct ? ranks.Min : Progression.GuessFromName(root.name);
        string biomes = direct ? "Biome: " + string.Join(", ", ranks.Select(Progression.Name)) : null;
        name ??= Filter.CleanName(root.name);
        foreach (var pair in found)
        {
            var (outputs, notes, tier) = pair.Value;
            if (outputs.Count == 0) continue;
            if (tier > 0) notes.Insert(0, pair.Key == Chopping ? $"Axe tier {tier}+" : $"Pickaxe tier {tier}+");
            if (biomes != null) notes.Add(biomes);
            add(new RecipeCard($"{pair.Key} · {name}", pair.Key, false, null, 0, new List<ItemStack>(), outputs, notes)
            {
                Kind = CardKind.Source,
                Seed = seed,
                Weak = !direct,
                ToolTier = pair.Key is Mining or Chopping ? tier : 0,
                ToolDamage = pair.Key == Chopping ? d => d.m_chop : pair.Key == Mining ? d => d.m_pickaxe : null,
            });
        }

        void Visit(GameObject go, int depth)
        {
            if (!go || depth > 4) return;
            if (go.GetComponent<Pickable>() is { } pickable)
            {
                name ??= Named(pickable.m_overrideName);
                if (pickable.m_itemPrefab && find(pickable.m_itemPrefab.name) is { } item)
                {
                    Slot(Harvest, 0).outputs.Add(new ItemStack(item, Mathf.Max(1, pickable.m_amount)));
                    name ??= item.Name;
                }
                var slot = Slot(Harvest, 0);
                AddTable(pickable.m_extraDrops, slot.outputs, slot.notes);
            }
            if (go.GetComponent<MineRock>() is { } mine)
            {
                name ??= Named(mine.m_name);
                var slot = Slot(Mining, mine.m_minToolTier);
                AddTable(mine.m_dropItems, slot.outputs, slot.notes);
            }
            if (go.GetComponent<MineRock5>() is { } mine5)
            {
                name ??= Named(mine5.m_name);
                var slot = Slot(Mining, mine5.m_minToolTier);
                AddTable(mine5.m_dropItems, slot.outputs, slot.notes);
            }
            if (go.GetComponent<TreeBase>() is { } tree)
            {
                var slot = Slot(Chopping, tree.m_minToolTier);
                AddTable(tree.m_dropWhenDestroyed, slot.outputs, slot.notes);
                Visit(tree.m_logPrefab, depth + 1);
            }
            if (go.GetComponent<TreeLog>() is { } log)
            {
                var slot = Slot(Chopping, log.m_minToolTier);
                AddTable(log.m_dropWhenDestroyed, slot.outputs, slot.notes);
                Visit(log.m_subLogPrefab, depth + 1);
            }
            if (go.GetComponent<DropOnDestroyed>() is { } dropOn)
            {
                bool mined = found.ContainsKey(Mining) || go.GetComponent<Destructible>() is { m_minToolTier: > 0 };
                var slot = Slot(found.ContainsKey(Chopping) ? Chopping : mined ? Mining : Breaking, go.GetComponent<Destructible>()?.m_minToolTier ?? 0);
                AddTable(dropOn.m_dropWhenDestroyed, slot.outputs, slot.notes);
            }
            if (go.GetComponent<Container>() is { m_defaultItems: { } items } container && items.m_drops.Count > 0)
            {
                name ??= Named(container.m_name);
                var slot = Slot(Chest, 0);
                AddTable(items, slot.outputs, slot.notes);
            }
            if (go.GetComponent<Destructible>() is { m_spawnWhenDestroyed: { } spawned } destructible)
            {
                if (destructible.m_minToolTier > 0 && spawned.GetComponent<MineRock5>()) Slot(Mining, destructible.m_minToolTier);
                Visit(spawned, depth + 1);
            }
        }

        (List<ItemStack> outputs, List<string> notes, int tier) Slot(string kind, int tier)
        {
            if (!found.TryGetValue(kind, out var slot)) slot = (new List<ItemStack>(), new List<string>(), 0);
            return found[kind] = (slot.outputs, slot.notes, Mathf.Max(slot.tier, tier));
        }
    }

    private static string Named(string token) => string.IsNullOrEmpty(token) || Filter.Missing(token) ? null : Localization.instance.Localize(token);

    // Chance of at least one: m_dropChance × (1 − (1 − weight/total)^max rolls); for m_oneOfEach just m_dropChance.
    private void AddTable(DropTable table, List<ItemStack> outputs, List<string> notes)
    {
        if (table?.m_drops == null || table.m_drops.Count == 0) return;
        float total = table.m_drops.Sum(d => d.m_weight);
        foreach (var d in table.m_drops.Where(d => d.m_item))
        {
            if (find(d.m_item.name) is not { } entry || outputs.Any(o => o.Entry == entry)) continue;
            float chance = table.m_oneOfEach || total <= 0 ? table.m_dropChance
                : table.m_dropChance * (1 - Mathf.Pow(1 - d.m_weight / total, Mathf.Max(1, table.m_dropMax)));
            outputs.Add(new ItemStack(entry, Mathf.Max(1, d.m_stackMax)));
            notes.Add($"{entry.Name} {Amount(d.m_stackMin, d.m_stackMax)} {Percent(chance)}");
        }
    }

    private static string Amount(int min, int max) => min >= max ? $"{Mathf.Max(1, max)}×" : $"{min}–{max}×";
    private static string Percent(float chance) => chance >= 0.995f ? "100%" : chance >= 0.1f ? $"{chance * 100:0}%" : $"{chance * 100:0.#}%";
}
