using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Shows ActiveCharacter's queued actions in order, each with a remove button. Self-refreshing
// like InventoryBarUI (watches plannedAction.queue.Count instead of inventory.Count), since
// the queue can change from more than just this UI - removing entries, or (in combat) actions
// resolving one by one during execution.
public class QueueDisplayUI : MonoBehaviour
{
    private readonly List<GameObject> entries = new List<GameObject>();
    private Character shownCharacter;
    private int lastKnownCount = -1;

    public void Build()
    {
        var rect = gameObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0);
        rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 160);

        var layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Show(Character character)
    {
        shownCharacter = character;
        Rebuild();
    }

    public void Hide()
    {
        shownCharacter = null;
        Clear();
        lastKnownCount = -1;
    }

    private void Update()
    {
        if (shownCharacter == null) return;

        // plannedAction itself goes null at the start of every new turn (StartPlanningPhase) -
        // treating that as "0 queued" (rather than bailing out before ever reaching Rebuild)
        // is what actually clears last turn's now-stale entries instead of leaving them on
        // screen until something else happens to call Show() again.
        int currentCount = shownCharacter.plannedAction?.queue.Count ?? 0;
        if (currentCount != lastKnownCount) Rebuild();
    }

    private void Rebuild()
    {
        Clear();
        if (shownCharacter?.plannedAction == null)
        {
            // Without this, lastKnownCount stays stuck at whatever it was before plannedAction
            // went null (every new turn) instead of resetting to 0 - if the new turn's first
            // queued count then happens to match that stale value (e.g. always 1, if a character
            // typically queues exactly one action a turn), Update()'s change-detection never
            // fires and the display stays empty until something else calls Show() again.
            lastKnownCount = 0;
            return;
        }

        foreach (QueuedAction queuedAction in shownCharacter.plannedAction.queue)
        {
            entries.Add(BuildEntry(shownCharacter, queuedAction));
        }

        lastKnownCount = shownCharacter.plannedAction.queue.Count;
    }

    private GameObject BuildEntry(Character character, QueuedAction queuedAction)
    {
        var entryGO = new GameObject($"Queued_{queuedAction.DisplayName}");
        entryGO.transform.SetParent(transform, false);
        entryGO.AddComponent<RectTransform>().sizeDelta = new Vector2(150, 40);

        var image = entryGO.AddComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(entryGO.transform, false);
        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = new Vector2(0.75f, 1);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 12;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = queuedAction.DisplayName;

        UIButtonFactory.Build(entryGO.transform, "X",
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(35, 40),
            new Color(0.5f, 0.15f, 0.15f, 0.9f),
            () => PlanningController.Instance?.RemoveQueuedAction(character, queuedAction));

        return entryGO;
    }

    private void Clear()
    {
        foreach (GameObject go in entries) Destroy(go);
        entries.Clear();
    }
}
