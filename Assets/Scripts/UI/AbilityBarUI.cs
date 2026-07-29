using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Procedural row of ability buttons for whichever character PlanningController has active.
// Rebuilt each time Show() is called with a new character; button affordability is kept
// up to date every frame since mana/stamina can change between selections.
public class AbilityBarUI : MonoBehaviour
{
    private class Entry
    {
        public GameObject go;
        public Button button;
        public Ability ability;
        public Text label;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private Character shownCharacter;

    public void Build()
    {
        var rect = gameObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0);
        rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 20);

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
        Clear();

        if (character?.data?.abilities == null) return;

        foreach (Ability ability in character.data.abilities)
        {
            if (ability == null) continue;
            entries.Add(BuildEntry(character, ability));
        }
    }

    private Entry BuildEntry(Character character, Ability ability)
    {
        var buttonGO = new GameObject($"Ability_{ability.abilityName}");
        buttonGO.transform.SetParent(transform, false);
        buttonGO.AddComponent<RectTransform>().sizeDelta = new Vector2(120, 50);

        var image = buttonGO.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);

        var button = buttonGO.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => PlanningController.Instance?.SelectAbility(ability));

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(buttonGO.transform, false);
        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 13;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = $"{ability.abilityName}\n({ability.manaCost} MP / {ability.staminaCost} SP)";

        return new Entry { go = buttonGO, button = button, ability = ability, label = label };
    }

    private void Clear()
    {
        foreach (Entry e in entries) Destroy(e.go);
        entries.Clear();
    }

    private void Update()
    {
        if (shownCharacter == null) return;

        foreach (Entry e in entries)
        {
            bool onCooldown = shownCharacter.currentCooldown > 0;
            e.button.interactable = !onCooldown
                && shownCharacter.currentMana >= e.ability.manaCost
                && shownCharacter.currentStamina >= e.ability.staminaCost;

            e.label.text = onCooldown
                ? $"{e.ability.abilityName}\n(CD {Mathf.CeilToInt(shownCharacter.currentCooldown)})"
                : $"{e.ability.abilityName}\n({e.ability.manaCost} MP / {e.ability.staminaCost} SP)";
        }
    }
}
