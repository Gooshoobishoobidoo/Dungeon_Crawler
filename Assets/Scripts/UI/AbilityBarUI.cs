using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Procedural row of ability buttons for whichever character PlanningController has active, plus
// three pseudo-actions appended after the real abilities: Rest, Focus, and Do Nothing. Rest/Focus
// don't queue immediately - clicking either collapses the whole row into a single -/+ duration
// stepper with a live "will restore ~X" preview (stepperFor tracks which one, null = normal view);
// Confirm queues it and rebuilds back to the normal ability row. Do Nothing queues straight away,
// no stepper needed. Rebuilt on every Show() and every stepper enter/exit rather than mutating
// entries in place - simplest way to swap between two structurally different button layouts.
public class AbilityBarUI : MonoBehaviour
{
    private const float DurationStep = 0.5f;
    private const float MinDuration = 0.5f;
    private const float MaxDuration = 10f;

    private class Entry
    {
        public GameObject go;
        public Button button;
        public Ability ability; // null for pseudo-actions/stepper controls - Update() skips those
        public Text label;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private Character shownCharacter;

    private QueuedActionType? stepperFor;
    private float stepperDuration = MinDuration;
    private Text stepperDurationText;
    private Text stepperPreviewText;

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
        stepperFor = null;
        Rebuild();
    }

    private void Rebuild()
    {
        Clear();
        if (shownCharacter == null) return;

        if (stepperFor.HasValue)
        {
            BuildStepperRow(stepperFor.Value);
            return;
        }

        if (shownCharacter.data?.abilities != null)
        {
            foreach (Ability ability in shownCharacter.data.abilities)
            {
                if (ability == null) continue;
                entries.Add(BuildAbilityEntry(shownCharacter, ability));
            }
        }

        entries.Add(BuildPseudoButton("Rest", () => EnterStepper(QueuedActionType.Rest)));
        entries.Add(BuildPseudoButton("Focus", () => EnterStepper(QueuedActionType.Focus)));
        entries.Add(BuildPseudoButton("Do Nothing", () => PlanningController.Instance?.TryQueuePass(shownCharacter)));
    }

    private void EnterStepper(QueuedActionType type)
    {
        stepperFor = type;
        stepperDuration = MinDuration;
        Rebuild();
    }

    private void ConfirmStepper()
    {
        if (stepperFor == QueuedActionType.Rest)
            PlanningController.Instance?.TryQueueRest(shownCharacter, stepperDuration);
        else if (stepperFor == QueuedActionType.Focus)
            PlanningController.Instance?.TryQueueFocus(shownCharacter, stepperDuration);

        stepperFor = null;
        Rebuild();
    }

    private void CancelStepper()
    {
        stepperFor = null;
        Rebuild();
    }

    private void BuildStepperRow(QueuedActionType type)
    {
        string label = type == QueuedActionType.Rest ? "Rest" : "Focus";

        entries.Add(new Entry { go = BuildLabel(label, new Vector2(70, 50)).gameObject });
        entries.Add(BuildPseudoButton("-", () => AdjustStepperDuration(-DurationStep)));

        stepperDurationText = BuildLabel($"{stepperDuration:F1}s", new Vector2(60, 50));
        entries.Add(new Entry { go = stepperDurationText.gameObject });

        entries.Add(BuildPseudoButton("+", () => AdjustStepperDuration(DurationStep)));

        stepperPreviewText = BuildLabel("+0", new Vector2(90, 50));
        entries.Add(new Entry { go = stepperPreviewText.gameObject });

        entries.Add(BuildPseudoButton($"Confirm {label}", ConfirmStepper));
        entries.Add(BuildPseudoButton("Cancel", CancelStepper));

        RefreshStepperTexts();
    }

    private void AdjustStepperDuration(float delta)
    {
        stepperDuration = Mathf.Clamp(stepperDuration + delta, MinDuration, MaxDuration);
        RefreshStepperTexts();
    }

    private void RefreshStepperTexts()
    {
        if (!stepperFor.HasValue || stepperDurationText == null || stepperPreviewText == null) return;

        stepperDurationText.text = $"{stepperDuration:F1}s";

        bool isRest = stepperFor.Value == QueuedActionType.Rest;
        float rate = isRest ? shownCharacter.data.restRegenPerSecond : shownCharacter.data.focusRegenPerSecond;
        int current = isRest ? shownCharacter.currentStamina : shownCharacter.currentMana;
        int max = isRest ? shownCharacter.data.maxStamina : shownCharacter.data.maxMana;
        string unit = isRest ? "SP" : "MP";

        int potential = Mathf.FloorToInt(rate * stepperDuration);
        int preview = Mathf.Clamp(potential, 0, Mathf.Max(0, max - current));
        stepperPreviewText.text = $"+{preview} {unit}";
    }

    private Entry BuildAbilityEntry(Character character, Ability ability)
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

    private Entry BuildPseudoButton(string text, UnityEngine.Events.UnityAction onClick)
    {
        Button button = UIButtonFactory.Build(transform, text, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
            new Vector2(110, 50), new Color(0.25f, 0.25f, 0.2f, 0.9f), onClick);
        return new Entry { go = button.gameObject, button = button, ability = null, label = null };
    }

    private Text BuildLabel(string text, Vector2 sizeDelta)
    {
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(transform, false);
        labelGO.AddComponent<RectTransform>().sizeDelta = sizeDelta;

        var label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 13;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = text;

        return label;
    }

    private void Clear()
    {
        foreach (Entry e in entries) Destroy(e.go);
        entries.Clear();
        stepperDurationText = null;
        stepperPreviewText = null;
    }

    private void Update()
    {
        if (shownCharacter == null) return;

        if (stepperFor.HasValue)
        {
            RefreshStepperTexts();
            return;
        }

        foreach (Entry e in entries)
        {
            if (e.ability == null) continue;

            bool onCooldown = shownCharacter.IsAbilityOnCooldown(e.ability);
            e.button.interactable = !onCooldown
                && shownCharacter.currentMana >= e.ability.manaCost
                && shownCharacter.currentStamina >= e.ability.staminaCost;

            e.label.text = onCooldown
                ? $"{e.ability.abilityName}\n(CD {Mathf.CeilToInt(shownCharacter.GetAbilityCooldown(e.ability))})"
                : $"{e.ability.abilityName}\n({e.ability.manaCost} MP / {e.ability.staminaCost} SP)";
        }
    }
}
