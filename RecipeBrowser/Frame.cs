using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecipeBrowser;

/// <summary>
/// Movable and resizable panel. Rect is in local canvas coordinates (y up), the panel pivot is top left.
/// Fit snaps the requested rectangle (whole slots, whole cards) and lays out the content.
/// </summary>
internal sealed class Frame(RectTransform canvas, RectTransform target, Func<Rect, Rect> fit, Vector2 step)
{
    private const float HandleSize = 16;

    public Rect Rect { get; private set; }
    public Action<Rect> Saved { get; set; }

    public void Place(Rect desired)
    {
        var rect = fit(desired);
        var screen = canvas.rect;
        float x = Mathf.Clamp(rect.xMin, screen.xMin, Mathf.Max(screen.xMin, screen.xMax - rect.width));
        float y = Mathf.Clamp(rect.yMin, Mathf.Min(screen.yMin, screen.yMax - rect.height), screen.yMax - rect.height);
        Rect = new Rect(x, y, rect.width, rect.height);
        Ui.PlaceLocal(target, canvas, new Vector2(Rect.xMin, Rect.yMax), new Vector2(0, 1));
        target.sizeDelta = Rect.size;
    }

    public void AddMoveHandle(GameObject handle)
    {
        if (!handle.GetComponent<Graphic>()) handle.AddComponent<Image>().color = Color.clear;
        Vector2 offset = default;
        var drag = handle.AddComponent<Drag>();
        drag.Canvas = canvas;
        drag.Began = p => offset = new Vector2(Rect.xMin, Rect.yMax) - p;
        drag.Moved = p => Place(new Rect(p.x + offset.x, p.y + offset.y - Rect.height, Rect.width, Rect.height));
        drag.Ended = () => Saved?.Invoke(Rect);
    }

    /// <summary>Grip in the bottom right corner. Must move after every Place, so it sits in a parent anchored to the corner.</summary>
    public void AddResizeHandle()
    {
        var go = new GameObject("Resize", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(target, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-3, 3);
        rt.sizeDelta = new Vector2(HandleSize, HandleSize);
        go.AddComponent<Image>().color = new Color(Ui.Orange.r, Ui.Orange.g, Ui.Orange.b, 0.55f);
        var drag = go.AddComponent<Drag>();
        drag.Canvas = canvas;
        drag.Moved = p => Place(Rect.MinMaxRect(Rect.xMin, p.y - step.y / 2, p.x + step.x / 2, Rect.yMax));
        drag.Ended = () => Saved?.Invoke(Rect);
    }
}

/// <summary>Mouse dragging, reports positions in local canvas coordinates.</summary>
internal sealed class Drag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public RectTransform Canvas;
    public Action<Vector2> Began, Moved;
    public Action Ended;

    private Vector2 Local(PointerEventData e) => Ui.ScreenToLocal(Canvas, e.position);
    public void OnBeginDrag(PointerEventData e) => Began?.Invoke(Local(e));
    public void OnDrag(PointerEventData e) => Moved?.Invoke(Local(e));
    public void OnEndDrag(PointerEventData e) => Ended?.Invoke();
}

/// <summary>Left and right click (Button only reacts to left).</summary>
internal sealed class Clicks : MonoBehaviour, IPointerClickHandler
{
    public Action Left, Right;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) Left?.Invoke();
        else if (e.button == PointerEventData.InputButton.Right) Right?.Invoke();
    }
}

/// <summary>Reacts on press already (before the search field loses focus and the suggestions disappear) and tracks hover.</summary>
internal sealed class Press : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Action Pressed;
    public bool Hovered { get; private set; }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) Pressed?.Invoke();
    }

    public void OnPointerEnter(PointerEventData e) => Hovered = true;
    public void OnPointerExit(PointerEventData e) => Hovered = false;
    private void OnDisable() => Hovered = false;
}
