using UnityEngine;

public enum AbilityType
{
    Skillshot,      // aimed in a direction
    AreaOfEffect,   // placed at a ground position
    UnitTarget,     // targets a specific character
    Self            // instant, no aiming needed
}

[CreateAssetMenu(fileName = "NewAbility", menuName = "DungeonCrawler/Ability")]
public class Ability : ScriptableObject
{
    [Header("Identity")]
    public string abilityName;
    public string description;
    public AbilityType abilityType;

    [Header("Stats")]
    public int manaCost;
    public int staminaCost;
    public float cooldown;      // seconds before this specific ability is usable again after use
    public float range;         // max distance the ability can reach
    public int damage;
    public float aoeRadius;     // only used if AbilityType is AreaOfEffect

    [Header("Visuals")]
    public GameObject vfxPrefab;
    public Sprite icon;
}