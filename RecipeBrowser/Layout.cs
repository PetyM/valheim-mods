using UnityEngine;

namespace RecipeBrowser;

/// <summary>Výchozí rozložení (pruh dole). Uživatelská přesunutí a velikosti ukládá UiConfig a mají přednost.</summary>
internal sealed record Layout(float Cell, float SlotSize, int Rows, Rect Results, Rect Bookmarks, Rect Window)
{
    private const float Margin = 12, ResultsShare = 0.62f, MaxWindowHeight = 1300, WindowWidth = 680;

    public static Layout Fit(InventoryGui gui, RectTransform root, int rows)
    {
        float scale = LocalSize(gui.m_playerGrid.m_gridRoot, root);
        float cell = gui.m_playerGrid.m_elementSpace * scale;
        float slot = ((RectTransform)gui.m_playerGrid.m_elementPrefab.transform).rect.width * scale;
        var screen = root.rect;
        float height = ItemPanel.Height(rows, cell);
        float cut = screen.xMin + Margin + (screen.width - Margin * 2) * ResultsShare;
        var results = Rect.MinMaxRect(screen.xMin + Margin, screen.yMin + Margin, cut - Margin / 2, screen.yMin + Margin + height);
        var bookmarks = Rect.MinMaxRect(cut + Margin / 2, screen.yMin + Margin, screen.xMax - Margin, screen.yMin + Margin + height);
        float windowBottom = results.yMax + Margin;
        var window = Rect.MinMaxRect(screen.center.x - WindowWidth / 2, windowBottom, screen.center.x + WindowWidth / 2,
            Mathf.Min(screen.yMax - Margin, windowBottom + MaxWindowHeight));
        return new Layout(cell, slot, rows, results, bookmarks, window);
    }

    private static float LocalSize(RectTransform vanilla, RectTransform root)
    {
        var camera = Ui.CameraOf(vanilla);
        var a = Ui.ScreenToLocal(root, RectTransformUtility.WorldToScreenPoint(camera, vanilla.TransformPoint(Vector3.zero)));
        var b = Ui.ScreenToLocal(root, RectTransformUtility.WorldToScreenPoint(camera, vanilla.TransformPoint(Vector3.right)));
        return Vector2.Distance(a, b);
    }
}
