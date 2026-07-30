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

    [Header("Currency")]
    public int currencyValue; // only used if itemType == Currency

    [Header("Visuals")]
    public Sprite icon;
}
