using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Procedural row of clickable party portraits. Built once at Start from
// CombatManager.Instance.playerCharacters; Refresh() updates stats/ready state each frame.
public class PartyBarUI : MonoBehaviour
{
    private class Entry
    {
        public Character character;
        public Image background;
        public Text statsText;
    }

    private readonly List<Entry> entries = new List<Entry>();

    public void Build()
    {
        var rect = gameObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 0);
        rect.pivot = new Vector2(0, 0);
        rect.anchoredPosition = new Vector2(20, 20);

        var layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        List<Character> characters = CombatManager.Instance != null ? CombatManager.Instance.playerCharacters : null;
        if (characters == null) return;

        foreach (Character c in characters)
            entries.Add(BuildEntry(c));
    }

    private Entry BuildEntry(Character c)
    {
        var entryGO = new GameObject($"Portrait_{c.data.characterName}");
        entryGO.transform.SetParent(transform, false);
        entryGO.AddComponent<RectTransform>().sizeDelta = new Vector2(140, 90);

        var background = entryGO.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        var button = entryGO.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => PlanningController.Instance?.SelectCharacter(c));

        var nameGO = new GameObject("Name");
        nameGO.transform.SetParent(entryGO.transform, false);
        var nameRect = nameGO.AddComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.65f);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;
        var nameText = nameGO.AddComponent<Text>();
        nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameText.fontSize = 16;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.color = Color.white;
        nameText.text = c.data.characterName;

        var statsGO = new GameObject("Stats");
        statsGO.transform.SetParent(entryGO.transform, false);
        var statsRect = statsGO.AddComponent<RectTransform>();
        statsRect.anchorMin = new Vector2(0, 0);
        statsRect.anchorMax = new Vector2(1, 0.65f);
        statsRect.offsetMin = Vector2.zero;
        statsRect.offsetMax = Vector2.zero;
        var statsText = statsGO.AddComponent<Text>();
        statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statsText.fontSize = 13;
        statsText.alignment = TextAnchor.MiddleCenter;
        statsText.color = Color.white;

        return new Entry { character = c, background = background, statsText = statsText };
    }

    public void Refresh()
    {
        foreach (Entry e in entries)
        {
            if (e.character == null) continue;

            e.statsText.text = $"HP {e.character.currentHealth}/{e.character.data.maxHealth}\n" +
                                $"MP {e.character.currentMana}/{e.character.data.maxMana}  " +
                                $"SP {e.character.currentStamina}/{e.character.data.maxStamina}";

            bool isActive = PlanningController.Instance != null && PlanningController.Instance.ActiveCharacter == e.character;
            bool isReady = e.character.plannedAction != null;

            e.background.color = isActive ? new Color(0.25f, 0.35f, 0.55f, 0.9f)
                : isReady ? new Color(0.15f, 0.35f, 0.15f, 0.9f)
                : new Color(0.1f, 0.1f, 0.1f, 0.85f);
        }
    }
}
