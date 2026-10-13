using System.Collections;
using UnityEngine;
using Core.StatsSystem;
using Core.HealthSystem;

namespace Core.InventorySystem
{
    /// <summary>
    /// Consomme les objets de catégorie Consumable de l'inventaire du joueur : soin instantané,
    /// buffs de stats temporaires et régénération de vie sur la durée. Partagé par la barre de
    /// nourriture et l'inventaire (clic droit). À poser sur le joueur ; les références vides
    /// sont retrouvées sur le même GameObject.
    /// </summary>
    public sealed class ConsumableUser : MonoBehaviour
    {
        [SerializeField] private InventoryHolder inventoryHolder;
        [SerializeField] private EntityStats playerStats;
        [SerializeField] private Health playerHealth;

        /// <summary>Retrouve le ConsumableUser du porteur d'inventaire, en l'ajoutant s'il manque.</summary>
        public static ConsumableUser For(InventoryHolder holder)
        {
            if (holder == null) return null;
            return holder.TryGetComponent(out ConsumableUser user) ? user : holder.gameObject.AddComponent<ConsumableUser>();
        }

        private void Awake()
        {
            if (inventoryHolder == null) inventoryHolder = GetComponent<InventoryHolder>();
            if (playerStats == null) playerStats = GetComponent<EntityStats>();
            if (playerHealth == null) playerHealth = GetComponent<Health>();
        }

        /// <summary>Vrai si la case contient un consommable utilisable.</summary>
        public bool CanConsume(int slotIndex)
        {
            Inventory inv = inventoryHolder != null ? inventoryHolder.Inventory : null;
            if (inv == null || slotIndex < 0 || slotIndex >= inv.Capacity) return false;
            ItemStack stack = inv.GetSlot(slotIndex);
            return stack != null && !stack.IsEmpty && stack.Definition.IsConsumable;
        }

        /// <summary>Consomme UNE unité de la case. Retourne false si rien n'a été consommé.</summary>
        public bool Consume(int slotIndex)
        {
            if (!CanConsume(slotIndex)) return false;

            Inventory inv = inventoryHolder.Inventory;
            ItemDefinition item = inv.GetSlot(slotIndex).Definition;

            if (item.HealAmount > 0 && playerHealth != null)
                playerHealth.Heal(item.HealAmount);

            if (playerStats != null)
                foreach (ConsumableStatEffect effect in item.StatEffects)
                    StartCoroutine(RunBuff(effect));

            if (playerHealth != null && item.RegenPercentPerSecond > 0f && item.RegenDuration > 0f)
                StartCoroutine(RunRegen(item));

            inv.RemoveAt(slotIndex, 1);
            return true;
        }

        private IEnumerator RunBuff(ConsumableStatEffect effect)
        {
            // Jeton unique à CET effet : deux plats identiques ne s'annulent pas entre eux,
            // et chaque effet expire à sa propre durée.
            object source = new object();
            playerStats.AddModifier(effect.type, effect.value, effect.modifierType, source);
            yield return new WaitForSeconds(effect.duration);
            playerStats.RemoveModifiersFromSource(source);
        }

        private IEnumerator RunRegen(ItemDefinition item)
        {
            var wait = new WaitForSeconds(1f);
            float remaining = item.RegenDuration;
            while (remaining > 0f)
            {
                yield return wait;
                remaining -= 1f;
                int amount = Mathf.RoundToInt(playerHealth.MaxHealth * item.RegenPercentPerSecond / 100f);
                if (amount > 0) playerHealth.Heal(amount);
            }
        }
    }
}
