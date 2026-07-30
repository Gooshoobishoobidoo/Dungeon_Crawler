using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Row of item buttons for one character's inventory. Reused by both PlanningController
// (combat: selecting an item sets it as the planned action) and ExplorationController
// (selecting an item uses it immediately) - the onUse callback is what differs between them.
// Structurally like AbilityBarUI, but simpler: items have no cost/cooldown to grey out against.
public class InventoryBarUI : MonoBehaviour
{
    private readonly List<GameObject> entries = new List<GameObject>();
    private Character shownCharacter;
    private Action<Character, ItemData> onUse;
    private int lastKnownCount = -1;

    public void Build()
    {
        var rect = gameObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0);
        rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 90);

        var layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Show(Character character, Action<Character, ItemData> onUse)
    {
        shownCharacter = character;
        this.onUse = onUse;
        Rebuild();
    }

    public void Hide()
    {
        shownCharacter = null;
        onUse = null;
        Clear();
        lastKnownCount = -1;
    }

    // Item use doesn't always happen at the moment it's selected - in combat it's deferred to
    // ExecuteCharacterAction, which can run well after Show() was last called. Re-checking the
    // count each frame catches that (and any other) case an already-open panel goes stale,
    // rather than requiring every caller to remember to re-call Show() after using an item.
    private void Update()
    {
        if (shownCharacter == null) return;
        if (shownCharacter.inventory.Count != lastKnownCount) Rebuild();
    }

    private void Rebuild()
    {
        Clear();
        if (shownCharacter?.inventory == null) return;

        Character character = shownCharacter;
        foreach (ItemData item in character.inventory)
        {
            if (item == null) continue;

            Button button = UIButtonFactory.Build(transform, $"{item.itemName}\n({item.useTime:F1}s)",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(120, 50),
                new Color(0.25f, 0.3f, 0.2f, 0.9f), () => onUse?.Invoke(character, item));

            entries.Add(button.gameObject);
        }

        lastKnownCount = character.inventory.Count;
    }

    private void Clear()
    {
        foreach (GameObject go in entries) Destroy(go);
        entries.Clear();
    }
}
