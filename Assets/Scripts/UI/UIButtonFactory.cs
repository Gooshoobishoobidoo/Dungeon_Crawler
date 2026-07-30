using UnityEngine;
using UnityEngine.UI;

// Shared procedural button construction, used anywhere UI is built at runtime instead of
// authored as a prefab (see PlanningController for why: keeps everything in reviewable C#
// instead of hand-edited Canvas/prefab YAML).
public static class UIButtonFactory
{
    public static Button Build(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, Color color,
        UnityEngine.Events.UnityAction onClick)
    {
        var buttonGO = new GameObject($"{text}Button");
        buttonGO.transform.SetParent(parent, false);

        var rect = buttonGO.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        var image = buttonGO.AddComponent<Image>();
        image.color = color;

        var button = buttonGO.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(buttonGO.transform, false);
        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = text;

        return button;
    }
}
