using TMPro;
using UnityEngine;

namespace RecipeBrowser;

internal sealed class Tooltip(GameObject prefab, RectTransform root)
{
    private const float Delay = 0.3f;

    private GameObject shown;
    private Entry entry;
    private float hoveredSince;

    public void Update(Entry hovered)
    {
        if (hovered != entry)
        {
            Hide();
            entry = hovered;
            hoveredSince = Time.time;
        }
        if (entry == null || !prefab) return;
        if (!shown && Time.time - hoveredSince >= Delay) Show();
        if (!shown) return;
        shown.transform.position = Input.mousePosition;
        Utils.ClampUIToScreen((RectTransform)shown.transform.GetChild(0));
    }

    public void Hide()
    {
        if (shown) Object.Destroy(shown);
        shown = null;
    }

    private void Show()
    {
        shown = Object.Instantiate(prefab, root);
        SetText("Topic", entry.Name);
        SetText("Text", entry.Tooltip);
        shown.transform.SetAsLastSibling();
        shown.SetActive(true);
    }

    private void SetText(string child, string text)
    {
        var target = Utils.FindChild(shown.transform, child);
        if (target) target.GetComponent<TMP_Text>().text = Localization.instance.Localize(text);
    }
}
