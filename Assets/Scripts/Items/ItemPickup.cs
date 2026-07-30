using UnityEngine;

// A world item pickup. Deliberately doesn't auto-collect on proximity (OnTriggerEnter) - that's
// the walk-over pattern RestRoomTransition uses, and this project explicitly chose click-to-
// interact for items instead. ExplorationController clicks this to target it, then calls
// Collect() once the party is actually close enough.
[RequireComponent(typeof(Collider))]
public class ItemPickup : MonoBehaviour
{
    public ItemData item;

    public void Collect()
    {
        if (DungeonManager.Instance == null || item == null) return;

        if (item.itemType == ItemType.Currency)
        {
            DungeonManager.Instance.currency += item.currencyValue;
        }
        else
        {
            Character receiver = NearestLivingPartyMember();
            receiver?.inventory.Add(item);
        }

        Destroy(gameObject);
    }

    private Character NearestLivingPartyMember()
    {
        Character nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (Character member in DungeonManager.Instance.party)
        {
            if (member.isDead) continue;
            float distance = Vector3.Distance(transform.position, member.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = member;
            }
        }

        return nearest;
    }
}
