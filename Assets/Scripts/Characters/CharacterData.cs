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

    [Header("Combat Stats")]
    public int attackPower;
    public int defense;
    public float critChance;  // 0.0 - 1.0

    [Header("Abilities")]
    public List<Ability> abilities;

    [Header("Visuals")]
    public GameObject characterPrefab;
    public Sprite portrait;
}