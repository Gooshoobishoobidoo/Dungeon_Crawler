using System.Collections.Generic;
using UnityEngine;

public enum CharacterClass
{
    Warrior,
    Mage,
    Rogue,
    Cleric
}

[CreateAssetMenu(fileName = "NewCharacter", menuName = "DungeonCrawler/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("Identity")]
    public string characterName;
    public CharacterClass characterClass;

    [Header("Core Stats")]
    public int maxHealth;
    public int maxMana;
    public int maxStamina;
    public float speed;       // affects action stagger timing in execution phase
    public int initiative;    // breaks ties in ordering
    public float moveStaminaCostPerUnit; // combat's queued Move spends this much stamina per unit of distance travelled

    [Header("Combat Stats")]
    public int attackPower;
    public int defense;
    public float critChance;  // 0.0 - 1.0

    [Header("Regen")]
    public float manaRegenPerSecond;    // passive, ticks whenever real time is passing (see Character.Update)
    public float staminaRegenPerSecond; // passive, same gating as above
    public float focusRegenPerSecond;   // boosted rate while actively using the Focus queued action
    public float restRegenPerSecond;    // boosted rate while actively using the Rest queued action

    [Header("AI")]
    public float chaseLeashDistance; // enemy AI only - how far a queued Move/ability aim can shift to track a dodging target, capped at this distance. 0 = no chase.

    [Header("Abilities")]
    public List<Ability> abilities;

    [Header("Visuals")]
    public GameObject characterPrefab;
    public Sprite portrait;
}