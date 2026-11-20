using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Type = ItemDrop.ItemData.ItemType;

namespace RecipeBrowser;

/// <summary>Filtr nedostupných položek (SPEC 1.1). Každé vyřazení se počítá podle důvodu, s configem i loguje.</summary>
internal sealed class Filter
{
    private static readonly Regex CheatOrTest = new("(Cheat|Test)(?![a-z])");
    private readonly System.Func<string, bool> ignored = DataConfig.Ignored();
    private readonly Dictionary<string, int> counts = new();

    /// <summary>Důvod vyřazení předmětu, null = projde.</summary>
    public string RejectItem(string prefab, ItemDrop.ItemData.SharedData shared, bool vanilla) =>
        shared.m_itemType is Type.Customization or Type.Attach_Atgeir ? "typ " + shared.m_itemType
        : RejectName(prefab) ?? Dlc(shared.m_dlc) ?? (vanilla && Missing(shared.m_name) ? "chybí překlad " + shared.m_name : null);

    /// <summary>Důvod vyřazení stavby, null = projde.</summary>
    public string RejectPiece(Piece piece, bool vanilla)
    {
        if (piece.m_repairPiece || piece.m_removePiece) return "nástroj oprava/odstranění";
        if (piece.GetComponent<Feast>()) return "hostina (bere se jako jídlo)";
        if (!piece.m_resources.Any(r => r.m_resItem && r.m_amount > 0) && !piece.GetComponent<Plant>()) return "bez surovin";
        return RejectName(piece.name) ?? Dlc(piece.m_dlc) ?? (vanilla && Missing(piece.m_name) ? "chybí překlad " + piece.m_name : null);
    }

    private string RejectName(string name)
    {
        if (name.StartsWith("SP_") || name.StartsWith("FW_")) return "prefab SP_/FW_";
        string lower = name.ToLowerInvariant();
        if (lower.EndsWith("_attach") || lower.EndsWith("_nochest") || lower.EndsWith("_ragdoll")) return "technická varianta";
        if (CheatOrTest.IsMatch(name) || lower.Contains("_test")) return "cheat/test";
        return ignored(name) ? "ignore list" : null;
    }

    private static string Dlc(string dlc) =>
        !string.IsNullOrEmpty(dlc) && DLCMan.instance && !DLCMan.instance.IsDLCInstalled(dlc) ? "DLC " + dlc : null;

    /// <summary>Token '$x' bez překladu: Localization vrátí "[x]".</summary>
    public static bool Missing(string token) =>
        !string.IsNullOrEmpty(token) && token[0] == '$' && Localization.instance.Localize(token) == "[" + token.Substring(1) + "]";

    /// <summary>Jméno položky: překlad, u módů bez překladu očištěné jméno prefabu ("SwordBlackmetal_TW" → "Sword Blackmetal TW").</summary>
    public static string DisplayName(string token, string prefab) =>
        string.IsNullOrEmpty(token) || Missing(token) ? CleanName(prefab) : Localization.instance.Localize(token);

    public static string CleanName(string prefab) =>
        Regex.Replace(Regex.Replace(prefab.Replace('_', ' '), "(?<=[a-z])(?=[A-Z0-9])", " "), @"\s+", " ").Trim();

    public void Hide(string prefab, string name, string reason)
    {
        int slash = reason.IndexOf(' ');
        string group = slash > 0 && reason.StartsWith("chybí") ? "chybí překlad" : reason.StartsWith("DLC") ? "DLC" : reason;
        counts[group] = counts.TryGetValue(group, out var n) ? n + 1 : 1;
        if (DataConfig.LogFilter?.Value == true) DataConfig.Log.LogInfo($"Skryto {prefab} ({name}): {reason}");
    }

    public string Summary() => string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}"));
}
