using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecipeBrowser;

/// <summary>
/// Přesouvatelný a zvětšovatelný panel. Rect je v lokálních souřadnicích plátna (y nahoru), panel má pivot vlevo nahoře.
/// Fit přichytí požadovaný obdélník (celá políčka, celé karty) a rozvrhne obsah.
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

    /// <summary>Úchyt v pravém dolním rohu. Musí se po každém Place posunout, proto ho drží rodič s ukotvením do rohu.</summary>
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

/// <summary>Tažení myší, pozice předává v lokálních souřadnicích plátna.</summary>
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

/// <summary>Levý a pravý klik (Button reaguje jen na levý).</summary>
internal sealed class Clicks : MonoBehaviour, IPointerClickHandler
{
    public Action Left, Right;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) Left?.Invoke();
        else if (e.button == PointerEventData.InputButton.Right) Right?.Invoke();
    }
}

/// <summary>Reaguje už na stisk (dřív, než pole hledání ztratí fokus a našeptávač zmizí) a hlídá najetí myší.</summary>
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
