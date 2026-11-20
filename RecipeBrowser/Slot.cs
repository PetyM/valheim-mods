using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecipeBrowser;

internal sealed class Slot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private static readonly Color Background = new(0, 0, 0, 0.45f);

    public static Slot Hovered { get; private set; }
    public Entry Entry { get; private set; }
    public bool InBookmarks { get; private set; }

    private Image icon;
    private Text amount;
    private System.Action<Entry, View> open;
    private System.Action<Entry, Entry> reorder;
    private GameObject ghost;

    /// <param name="reorder">Přetažení na jiný slot téže mřížky: (přesouvaná, cílová nebo null = na konec).</param>
    public static Slot Create(Transform parent, float x, float y, float size, bool inBookmarks, System.Action<Entry, View> open, System.Action<Entry, Entry> reorder = null)
    {
        var go = Ui.Rect("Slot", parent, x, y, size, size);
        go.AddComponent<Image>().color = Background;
        var slot = go.AddComponent<Slot>();
        slot.InBookmarks = inBookmarks;
        slot.open = open;
        slot.reorder = reorder;
        var iconGo = Ui.Rect("Icon", go.transform, 2, 2, size - 4, size - 4);
        slot.icon = iconGo.AddComponent<Image>();
        slot.icon.raycastTarget = false;
        slot.amount = Ui.Label(go.transform, "", 0, size - 18, size - 2, 18, 14, TextAnchor.LowerRight);
        slot.Show(null);
        return slot;
    }

    public void Show(Entry entry, int count = 0, Color? countColour = null)
    {
        amount.color = countColour ?? Color.white;
        Entry = entry;
        icon.sprite = entry?.Icon;
        icon.enabled = entry?.Icon;
        amount.text = count > 1 ? count.ToString() : "";
        gameObject.SetActive(entry != null);
    }

    public void OnPointerEnter(PointerEventData eventData) => Hovered = this;
    public void OnPointerExit(PointerEventData eventData) => Unhover();

    private void OnDisable()
    {
        Unhover();
        DropGhost();
    }

    private void Unhover()
    {
        if (Hovered == this) Hovered = null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Entry == null || eventData.dragging) return;
        if (eventData.button == PointerEventData.InputButton.Left) open(Entry, View.Recipes);
        else if (eventData.button == PointerEventData.InputButton.Right) open(Entry, View.Usages);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (reorder == null || Entry == null || eventData.button != PointerEventData.InputButton.Left) return;
        var canvas = GetComponentInParent<Canvas>().rootCanvas;
        ghost = new GameObject("DragGhost", typeof(RectTransform));
        ghost.transform.SetParent(canvas.transform, false);
        ((RectTransform)ghost.transform).sizeDelta = ((RectTransform)transform).sizeDelta;
        var image = ghost.AddComponent<Image>();
        image.sprite = Entry.Icon;
        image.color = new Color(1, 1, 1, 0.7f);
        image.raycastTarget = false;
        ghost.transform.position = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (ghost) ghost.transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!ghost) return;
        DropGhost();
        var hit = eventData.pointerCurrentRaycast.gameObject;
        if (!hit) return;
        var target = hit.GetComponentInParent<Slot>();
        if (target && target.reorder == reorder && target != this) reorder(Entry, target.Entry);
        else if (!target && hit.transform == transform.parent) reorder(Entry, null);
    }

    private void DropGhost()
    {
        if (ghost) Destroy(ghost);
        ghost = null;
    }
}

internal sealed class ScrollArea : MonoBehaviour, IScrollHandler
{
    public System.Action<int> Scrolled;
    public void OnScroll(PointerEventData eventData) => Scrolled?.Invoke(eventData.scrollDelta.y > 0 ? -1 : 1);
}
