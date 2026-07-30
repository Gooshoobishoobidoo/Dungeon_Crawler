using UnityEngine;

public enum ItemType
{
    Consumable,
    Currency
}

[CreateAssetMenu(fileName = "NewItem", menuName = "DungeonCrawler/Item")]
public class ItemData : ScriptableObject
{
    [Header("Identity")]
    public string itemName;
    public string description;
    public ItemType itemType;

    [Header("Consumable Effect")]
    public int healAmount;
    public int manaAmount;
    public int staminaAmount;
    public float useTime; // seconds before the effect lands - the balancing cost instead of mana/stamina

    [Header("Currency")]
    public int currencyValue; // only used if itemType == Currency

    [Header("Visuals")]
    public Sprite icon;

    // Single source of truth for "what this item actually does" - called from both the
    // exploration (Character.UseItem) and combat (CombatManager.ExecuteCharacterAction) paths.
    public void ApplyTo(Character character)
    {
        if (healAmount > 0) character.Heal(healAmount);
        if (manaAmount > 0) character.RestoreMana(manaAmount);
        if (staminaAmount > 0) character.RestoreStamina(staminaAmount);
    }
}
