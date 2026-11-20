using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace RecipeBrowser;

internal static class Ui
{
    public static readonly Color Orange = new(1f, 0.63f, 0.24f);
    public static readonly Color Muted = new(0.75f, 0.75f, 0.75f);
    public static readonly Color Enough = new(0.6f, 0.9f, 0.6f);
    public static readonly Color Missing = new(1f, 0.45f, 0.45f);

    public static Camera CameraOf(Transform transform)
    {
        var canvas = transform.GetComponentInParent<Canvas>().rootCanvas;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    public static Vector2 ScreenToLocal(RectTransform root, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, CameraOf(root), out var local);
        return local;
    }

    public static void PlaceLocal(RectTransform rect, RectTransform root, Vector2 local, Vector2 pivot)
    {
        rect.anchorMin = rect.anchorMax = root.pivot;
        rect.pivot = pivot;
        rect.anchoredPosition = local;
    }

    public static RectTransform Place(GameObject go, Transform parent, float x, float y, float width, float height)
    {
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(width, height);
        return rt;
    }

    public static GameObject Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Place(go, parent, x, y, width, height);
        return go;
    }

    public static Text Label(Transform parent, string text, float x, float y, float width, float height, int size,
        TextAnchor anchor = TextAnchor.MiddleLeft, Color? colour = null)
    {
        var go = Rect("Label", parent, x, y, width, height);
        var label = go.AddComponent<Text>();
        label.font = GUIManager.Instance.AveriaSerifBold;
        label.fontSize = size;
        label.alignment = anchor;
        label.color = colour ?? Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;
        label.text = text;
        go.AddComponent<Outline>().effectColor = Color.black;
        return label;
    }

    public static Button Button(Transform parent, string text, float x, float y, float width, float height, System.Action onClick)
    {
        var go = GUIManager.Instance.CreateButton(text, parent, Vector2.zero, Vector2.zero, Vector2.zero, width, height);
        Place(go, parent, x, y, width, height);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());
        return button;
    }
}
