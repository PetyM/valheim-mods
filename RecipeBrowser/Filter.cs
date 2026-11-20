using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Type = ItemDrop.ItemData.ItemType;

namespace RecipeBrowser;

/// <summary>Filter for unobtainable entries (SPEC 1.1). Every rejection is counted by reason and, if enabled in config, logged.</summary>
internal sealed class Filter
{
    private static readonly Regex CheatOrTest = new("(Cheat|Test)(?![a-z])");
    private readonly System.Func<string, bool> ignored = DataConfig.Ignored();
    private readonly Dictionary<string, int> counts = new();

    /// <summary>Reason to reject an item, null = it passes.</summary>
    public string RejectItem(string prefab, ItemDrop.ItemData.SharedData shared, bool vanilla) =>
        shared.m_itemType is Type.Customization or Type.Attach_Atgeir ? "type " + shared.m_itemType
        : RejectName(prefab) ?? Dlc(shared.m_dlc) ?? (vanilla && Missing(shared.m_name) ? "missing translation " + shared.m_name : null)
          ?? (vanilla && Unreleased(prefab) ? "unreleased or unused vanilla" : null);

    // Vanilla 1.0.21 ships unfinished Deep North content and a few unused duplicates. They have recipes
    // (made from each other), so the source/use rule does not catch them.
    private static readonly HashSet<string> UnreleasedNames = new()
    {
        "OrbFrostFire", "OrbThunderBlood", "HelmetCrownofValheim", "CrownJewel", "FrozenKingDrop", "Shovel",
        "StaffOrbofAhri", "StaffOrbofAhriUncooked", "StaffThunderBlood", "StaffThunderbloodUncooked", "KnifeVoid",
        "TrophyBlob_Frost", "TrophyForestTroll", "TrophyDraugrFem",
    };

    private static bool Unreleased(string prefab) =>
        UnreleasedNames.Contains(prefab) || prefab.EndsWith("_FrostFire") || prefab.EndsWith("_BloodLightning");

    /// <summary>Reason to reject a piece, null = it passes.</summary>
    public string RejectPiece(Piece piece, bool vanilla)
    {
        if (piece.m_repairPiece || piece.m_removePiece) return "repair/remove tool";
        if (piece.GetComponent<Feast>()) return "feast (treated as food)";
        if (!piece.m_resources.Any(r => r.m_resItem && r.m_amount > 0) && !piece.GetComponent<Plant>()) return "no resources";
        return RejectName(piece.name) ?? Dlc(piece.m_dlc) ?? (vanilla && Missing(piece.m_name) ? "missing translation " + piece.m_name : null);
    }

    private string RejectName(string name)
    {
        if (name.StartsWith("SP_") || name.StartsWith("FW_")) return "prefab SP_/FW_";
        string lower = name.ToLowerInvariant();
        if (lower.EndsWith("_attach") || lower.EndsWith("_nochest") || lower.EndsWith("_ragdoll")) return "technical variant";
        if (CheatOrTest.IsMatch(name) || lower.Contains("_test")) return "cheat/test";
        return ignored(name) ? "ignore list" : null;
    }

    private static string Dlc(string dlc) =>
        !string.IsNullOrEmpty(dlc) && DLCMan.instance && !DLCMan.instance.IsDLCInstalled(dlc) ? "DLC " + dlc : null;

    /// <summary>Token '$x' without a translation: Localization returns "[x]".</summary>
    public static bool Missing(string token) =>
        !string.IsNullOrEmpty(token) && token[0] == '$' && Localization.instance.Localize(token) == "[" + token.Substring(1) + "]";

    /// <summary>Entry name: the translation, or for mods without one a cleaned-up prefab name ("SwordBlackmetal_TW" → "Sword Blackmetal TW").</summary>
    public static string DisplayName(string token, string prefab) =>
        string.IsNullOrEmpty(token) || Missing(token) ? CleanName(prefab) : Localization.instance.Localize(token);

    public static string CleanName(string prefab) =>
        Regex.Replace(Regex.Replace(prefab.Replace('_', ' '), "(?<=[a-z])(?=[A-Z0-9])", " "), @"\s+", " ").Trim();

    public void Hide(string prefab, string name, string reason)
    {
        int slash = reason.IndexOf(' ');
        string group = slash > 0 && reason.StartsWith("missing") ? "missing translation" : reason.StartsWith("DLC") ? "DLC" : reason;
        counts[group] = counts.TryGetValue(group, out var n) ? n + 1 : 1;
        if (DataConfig.LogFilter?.Value == true) DataConfig.Log.LogInfo($"Hidden {prefab} ({name}): {reason}");
    }

    public string Summary() => string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}"));
}
