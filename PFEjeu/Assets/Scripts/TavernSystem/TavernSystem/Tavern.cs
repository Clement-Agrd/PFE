using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.ShopSystem;
using Core.StatsSystem;
using Core.HealthSystem;

namespace Core.TavernSystem
{
    /// <summary>
    /// Une taverne : le joueur commande un plat/boisson de la carte (Wallet),
    /// ce qui applique ses effets de stats (EntityStats.AddModifier) et sa
    /// régénération éventuelle pendant sa durée, puis retire tout proprement à
    /// la fin. Plusieurs commandes du même plat peuvent être actives en même
    /// temps sans s'annuler entre elles (chacune a son propre "jeton" source).
    /// </summary>
    public sealed class Tavern : MonoBehaviour
    {
        [SerializeField] private TavernMenuDefinition menu;
        [SerializeField] private Wallet wallet;
        [SerializeField] private EntityStats playerStats;
        [SerializeField] private Health playerHealth;

        public IReadOnlyList<TavernDishDefinition> Menu => menu != null ? menu.Dishes : Array.Empty<TavernDishDefinition>();
        public Wallet Wallet => wallet;

        public event Action<TavernDishDefinition> OnOrdered;
        public event Action<string> OnOrderFailed;

        /// <summary>Tente de commander ce plat. Débite le Wallet et lance ses effets si possible.</summary>
        public bool Order(TavernDishDefinition dish)
        {
            if (dish == null) { Fail("Plat invalide."); return false; }
            if (playerStats == null) { Fail("Aucune statistique à améliorer."); return false; }
            if (wallet == null || !wallet.CanAfford(dish.Cost)) { Fail("Pas assez d'or."); return false; }

            wallet.Spend(dish.Cost);
            StartCoroutine(RunDish(dish));

            OnOrdered?.Invoke(dish);
            return true;
        }

        private IEnumerator RunDish(TavernDishDefinition dish)
        {
            // Jeton unique à CETTE commande : une deuxième commande du même plat
            // ne retirera pas les modificateurs de la première en expirant.
            object source = new object();

            foreach (TavernStatEffect effect in dish.StatEffects)
                playerStats.AddModifier(effect.type, effect.value, effect.modifierType, source);

            Coroutine regen = null;
            if (dish.HealPercentPerSecond > 0f && playerHealth != null)
                regen = StartCoroutine(RegenTick(dish.HealPercentPerSecond));

            yield return new WaitForSeconds(dish.Duration);

            playerStats.RemoveModifiersFromSource(source);
            if (regen != null) StopCoroutine(regen);
        }

        private IEnumerator RegenTick(float percentPerSecond)
        {
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;
                int amount = Mathf.RoundToInt(playerHealth.MaxHealth * percentPerSecond / 100f);
                if (amount > 0) playerHealth.Heal(amount);
            }
        }

        private void Fail(string reason) => OnOrderFailed?.Invoke(reason);
    }
}
