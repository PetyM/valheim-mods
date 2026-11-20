using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Anim = ItemDrop.ItemData.AnimationState;
using Skill = Skills.SkillType;
using Type = ItemDrop.ItemData.ItemType;

namespace RecipeBrowser;

/// <summary>Tagy položek (SPEC 2.1): anglicky bez '#', v našeptávači s českým popisem.</summary>
internal static class Tags
{
    public const string Craftable = "craftable", Bookmarked = "bookmarked", SetPrefix = "set:", StationPrefix = "station:";

    /// <summary>Hlavní kategorie v pořadí pro řazení.</summary>
    public static readonly string[] Categories = { "materials", "food", "potions", "weapons", "armor", "ammo", "tools", "pieces", "trophies", "fish", "misc" };

    private static readonly (string tag, string description)[] Known =
    {
        ("materials", "Materiály"), ("food", "Jídlo"), ("potions", "Lektvary a medoviny"), ("weapons", "Zbraně"), ("armor", "Zbroj"),
        ("ammo", "Munice a návnady"), ("tools", "Nástroje"), ("pieces", "Stavby"), ("trophies", "Trofeje"), ("fish", "Ryby"), ("misc", "Ostatní"),
        ("swords", "Meče"), ("axes", "Sekery"), ("battleaxes", "Bitevní sekery"), ("clubs", "Palice"), ("sledges", "Obouruční kladiva"),
        ("spears", "Oštěpy"), ("polearms", "Atgeiry"), ("knives", "Nože"), ("fists", "Pěstní zbraně"), ("bows", "Luky"), ("crossbows", "Kuše"),
        ("staffs", "Magické hole"), ("pickaxes", "Krumpáče"), ("shields", "Štíty"), ("tower", "Velké štíty (bez odrazu)"), ("buckler", "Malé štíty (puklíře)"),
        ("1h", "Jednoruční"), ("2h", "Obouruční"),
        ("helmet", "Helmy"), ("chest", "Hrudní zbroj"), ("legs", "Kalhoty"), ("cape", "Pláště"), ("gloves", "Rukavice"), ("utility", "Doplňky (pás, prsten)"),
        ("trinket", "Talismany"),
        ("health", "Jídlo: převažuje zdraví"), ("stamina", "Jídlo: převažuje výdrž"), ("eitr", "Jídlo: převažuje eitr"), ("balanced", "Jídlo: vyvážené"),
        ("meads", "Medoviny"), ("drinks", "Nápoje"), ("feasts", "Hostiny"), ("seeds", "Semínka a sazenice"),
        ("crafting", "Stavby: výroba a stanice"), ("building", "Stavby: dřevo"), ("stone", "Stavby: kámen"), ("furniture", "Stavby: nábytek"),
        (Craftable, "Teď jde vyrobit (inventář a stanice v dosahu)"), (Bookmarked, "V záložkách"), (Progression.UnknownTag, "Biom neznámý"),
    };

    private static readonly Dictionary<string, string> Descriptions = Known.ToDictionary(k => k.tag, k => k.description);
    private static readonly Dictionary<string, int> Order = Known.Select((k, i) => (k.tag, i)).ToDictionary(k => k.tag, k => k.i);

    /// <summary>Čipy pod polem hledání: tag a krátký český popisek.</summary>
    public static readonly (string Tag, string Label)[] Chips =
    {
        ("weapons", "Zbraně"), ("armor", "Zbroj"), ("food", "Jídlo"), ("potions", "Lektvary"), ("materials", "Materiály"), ("ammo", "Munice"),
        ("tools", "Nástroje"), ("pieces", "Stavby"), ("trophies", "Trofeje"), ("fish", "Ryby"), ("misc", "Ostatní"),
        (Craftable, "Vyrobitelné"), (Bookmarked, "Záložky"),
    };

    /// <summary>Tagy, které se počítají za běhu (nejsou v Entry.Tags).</summary>
    public static readonly string[] Dynamic = { Craftable, Bookmarked };

    public static string Describe(string tag)
    {
        if (Descriptions.TryGetValue(tag, out var text)) return text;
        int rank = Progression.Tags.ToList().IndexOf(tag);
        return rank >= 0 ? "Biom: " + Progression.Name(rank) : null;
    }

    public static int CategoryIndex(string tag) => System.Array.IndexOf(Categories, tag) is var i and >= 0 ? i : Categories.Length;
    public static int SubcategoryIndex(string tag) => tag != null && Order.TryGetValue(tag, out var i) ? i : int.MaxValue;

    /// <summary>Malá písmena, bez mezer a diakritiky mimo [a-z0-9_].</summary>
    public static string Sanitize(string text) => Regex.Replace((text ?? "").ToLowerInvariant().Replace(' ', '_'), "[^a-z0-9_]", "");

    public static string StationTag(Entry station)
    {
        string key = station.Key.StartsWith("piece_", System.StringComparison.OrdinalIgnoreCase) ? station.Key.Substring(6) : station.Key;
        return StationPrefix + Sanitize(key);
    }

    /// <summary>Hlavní kategorie, podkategorie a tagy předmětu.</summary>
    public static void OfItem(Entry entry, ItemDrop.ItemData.SharedData s, bool isFeast)
    {
        var tags = entry.Tags;
        string main, sub = null;
        if (s.m_food > 0 || s.m_foodStamina > 0 || s.m_foodEitr > 0) entry.Food = (s.m_food, s.m_foodStamina, s.m_foodEitr);
        var type = s.m_itemType;
        switch (type)
        {
            case Type.Consumable when isFeast:
                main = "food";
                sub = "feasts";
                tags.Add("feasts");
                break;
            case Type.Consumable when s.m_food > 0 || s.m_foodStamina > 0 || s.m_foodEitr > 0:
                main = "food";
                sub = FoodKind(s);
                tags.Add(sub);
                if (s.m_isDrink) tags.Add("drinks");
                break;
            case Type.Consumable:
                main = "potions";
                if (s.m_isDrink) tags.Add(sub = "meads");
                break;
            case Type.OneHandedWeapon or Type.TwoHandedWeapon or Type.TwoHandedWeaponLeft or Type.Bow:
                sub = WeaponClass(s);
                main = sub == "pickaxes" ? "tools" : "weapons";
                if (sub != null) tags.Add(sub);
                tags.Add(type == Type.OneHandedWeapon ? "1h" : "2h");
                if (sub == "pickaxes" || s.m_damages.m_chop > 0 && sub is "axes" or "battleaxes") tags.Add("tools");
                break;
            case Type.Shield:
                main = "weapons";
                sub = "shields";
                tags.Add(sub);
                if (s.m_timedBlockBonus <= 1f) tags.Add("tower");
                else if (s.m_timedBlockBonus >= 2f) tags.Add("buckler");
                break;
            case Type.Helmet or Type.Chest or Type.Legs or Type.Shoulder or Type.Hands or Type.Utility or Type.Trinket:
                main = "armor";
                sub = type switch { Type.Helmet => "helmet", Type.Chest => "chest", Type.Legs => "legs", Type.Shoulder => "cape", Type.Hands => "gloves", Type.Utility => "utility", _ => "trinket" };
                tags.Add(sub);
                break;
            case Type.Ammo or Type.AmmoNonEquipable:
                main = "ammo";
                sub = Sanitize(s.m_ammoType) is { Length: > 0 } ammo ? ammo : null;
                break;
            case Type.Tool or Type.Torch:
                main = "tools";
                break;
            case Type.Trophy:
                main = "trophies";
                break;
            case Type.Fish:
                main = "fish";
                break;
            case Type.Material:
                main = "materials";
                break;
            default:
                main = s.m_buildPieces ? "tools" : "misc";
                break;
        }
        if (s.m_buildPieces && !isFeast && main != "tools") tags.Add("tools");
        if (!string.IsNullOrEmpty(s.m_setName)) tags.Add(SetPrefix + Sanitize(s.m_setName));
        Finish(entry, main, sub);
    }

    public static void OfPiece(Entry entry, Piece piece, bool isSeed)
    {
        string sub = piece == null ? null : piece.m_category switch
        {
            Piece.PieceCategory.Crafting => "crafting",
            Piece.PieceCategory.BuildingWorkbench => "building",
            Piece.PieceCategory.BuildingStonecutter => "stone",
            Piece.PieceCategory.Furniture => "furniture",
            Piece.PieceCategory.Feasts or Piece.PieceCategory.Food or Piece.PieceCategory.Meads => "feasts",
            // Misc stavby (portál, vozík…) jen pod #pieces, "misc" je hlavní kategorie předmětů.
            _ => null,
        };
        if (isSeed) sub = "seeds";
        if (sub != null) entry.Tags.Add(sub);
        Finish(entry, "pieces", sub);
    }

    private static void Finish(Entry entry, string main, string sub)
    {
        entry.MainTag = main;
        entry.Subcategory = sub;
        entry.Tags.Add(main);
    }

    private static string FoodKind(ItemDrop.ItemData.SharedData s)
    {
        float health = s.m_food, stamina = s.m_foodStamina, eitr = s.m_foodEitr;
        if (eitr > 0 && eitr >= 0.5f * System.Math.Max(health, stamina)) return "eitr";
        if (health >= 1.5f * stamina) return "health";
        if (stamina >= 1.5f * health) return "stamina";
        return "balanced";
    }

    private static string WeaponClass(ItemDrop.ItemData.SharedData s) => s.m_skillType switch
    {
        Skill.Swords => "swords",
        Skill.Axes => s.m_animationState == Anim.TwoHandedAxe ? "battleaxes" : "axes",
        Skill.Clubs => s.m_itemType == Type.TwoHandedWeapon || s.m_animationState == Anim.TwoHandedClub ? "sledges" : "clubs",
        Skill.Spears => "spears",
        Skill.Polearms => "polearms",
        Skill.Knives => "knives",
        Skill.Unarmed => "fists",
        Skill.Bows => "bows",
        Skill.Crossbows => "crossbows",
        Skill.ElementalMagic or Skill.BloodMagic => "staffs",
        Skill.Pickaxes => "pickaxes",
        _ => s.m_itemType == Type.Bow ? "bows" : null,
    };
}
