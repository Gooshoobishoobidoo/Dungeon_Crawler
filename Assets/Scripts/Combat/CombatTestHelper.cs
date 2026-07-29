using UnityEngine;
using UnityEngine.InputSystem;

public class CombatTestHelper : MonoBehaviour
{
    [Header("Test Settings")]
    public float moveRange = 3f; // how far characters will randomly move

    private void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            AssignRandomActions();
            CombatManager.Instance.OnPlanningComplete();
        }
    }

    private void AssignRandomActions()
    {
        // Assign a random move destination to every character
        var allCharacters = new System.Collections.Generic.List<Character>();
        allCharacters.AddRange(CombatManager.Instance.playerCharacters);
        allCharacters.AddRange(CombatManager.Instance.enemyCharacters);

        foreach (Character c in allCharacters)
        {
            if (c.isDead) continue;

            // Pick a random point near the character's current position
            Vector3 randomOffset = new Vector3(
                Random.Range(-moveRange, moveRange),
                0f,
                Random.Range(-moveRange, moveRange)
            );

            c.plannedAction = new PlannedAction
            {
                moveDestination = c.transform.position + randomOffset,
                ability = null,
                abilityTarget = Vector3.zero,
                targetCharacter = null
            };

            Debug.Log($"{c.data.characterName} will move to {c.plannedAction.moveDestination}");
        }
    }
}