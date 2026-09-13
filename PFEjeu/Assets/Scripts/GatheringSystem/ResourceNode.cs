using System;
using System.Collections.Generic;
using UnityEngine;

using Core.HealthSystem;
using Core.InventorySystem;
using Core.LootSystem;

namespace Core.GatheringSystem
{
    public enum GatheringToolType
    {
        Axe,
        Pickaxe
    }


    /// <summary>
    /// Rend une entité récoltable.
    ///
    /// Exemple :
    /// Tree  -> Axe
    /// Rock  -> Pickaxe
    ///
    /// Le Health continue de gérer les PV.
    /// ResourceNode décide uniquement quels coups sont acceptés
    /// et distribue le loot à la destruction.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class ResourceNode :
        MonoBehaviour,
        IDamageFilter
    {
        [Header("Resource")]

        [SerializeField]
        private GatheringToolType requiredTool;


        [Header("Loot")]

        [SerializeField]
        private LootDropper lootDropper;

        [Tooltip(
            "Cherche l'InventoryHolder sur le joueur ayant détruit la ressource."
        )]
        [SerializeField]
        private bool addLootToSourceInventory = true;


        [Header("Destruction")]

        [Tooltip(
            "Détruit le GameObject après récolte. " +
            "Désactive ceci plus tard si tu ajoutes du respawn."
        )]
        [SerializeField]
        private bool destroyAfterHarvest = true;

        [SerializeField, Min(0f)]
        private float destroyDelay = 0.1f;


        private Health _health;

        private GameObject _lastValidSource;

        private bool _harvested;


        public GatheringToolType RequiredTool =>
            requiredTool;


        public event Action<ResourceNode>
            OnHarvested;

        public event Action<ItemDefinition, int>
            OnInventoryOverflow;


        // ============================================================
        // UNITY
        // ============================================================

        private void Awake()
        {
            _health =
                GetComponent<Health>();


            if (lootDropper == null)
            {
                lootDropper =
                    GetComponent<LootDropper>();
            }
        }


        private void OnEnable()
        {
            if (_health == null)
                return;


            _health.OnDamaged +=
                OnDamaged;

            _health.OnDeath +=
                OnDeath;
        }


        private void OnDisable()
        {
            if (_health == null)
                return;


            _health.OnDamaged -=
                OnDamaged;

            _health.OnDeath -=
                OnDeath;
        }


        // ============================================================
        // DAMAGE FILTER
        // ============================================================

        public bool CanTakeDamage(
            in DamageInfo info)
        {
            bool correctTool =
                requiredTool switch
                {
                    GatheringToolType.Axe =>
                        info.SourceKind ==
                        DamageSourceKind.Axe,

                    GatheringToolType.Pickaxe =>
                        info.SourceKind ==
                        DamageSourceKind.Pickaxe,

                    _ => false
                };


            return correctTool;
        }


        private void OnDamaged(
            DamageInfo info)
        {
            // Comme Health applique le filtre avant d'émettre OnDamaged,
            // on sait ici que l'outil était valide.
            if (info.Source != null)
            {
                _lastValidSource =
                    info.Source;
            }
        }


        // ============================================================
        // HARVEST
        // ============================================================

        private void OnDeath()
        {
            if (_harvested)
                return;


            _harvested = true;


            Harvest();


            OnHarvested?.Invoke(
                this
            );


            if (destroyAfterHarvest)
            {
                Destroy(
                    gameObject,
                    destroyDelay
                );
            }
        }


        private void Harvest()
        {
            if (lootDropper == null)
            {
                Debug.LogWarning(
                    $"[{nameof(ResourceNode)}] " +
                    $"{name} n'a pas de LootDropper.",
                    this
                );

                return;
            }


            IReadOnlyList<LootResult> loot =
                lootDropper.Drop();


            if (!addLootToSourceInventory)
                return;


            InventoryHolder inventoryHolder =
                FindSourceInventory();


            if (inventoryHolder == null ||
                inventoryHolder.Inventory == null)
            {
                Debug.LogWarning(
                    $"[{nameof(ResourceNode)}] " +
                    $"Aucun InventoryHolder trouvé pour récupérer le loot de {name}.",
                    this
                );

                return;
            }


            for (int i = 0;
                 i < loot.Count;
                 i++)
            {
                LootResult result =
                    loot[i];


                if (result.Item == null ||
                    result.Amount <= 0)
                {
                    continue;
                }


                int remaining =
                    inventoryHolder.Inventory.Add(
                        result.Item,
                        result.Amount
                    );


                // Inventory.Add renvoie la quantité
                // qui n'a pas pu être ajoutée.
                if (remaining > 0)
                {
                    OnInventoryOverflow?.Invoke(
                        result.Item,
                        remaining
                    );


                    Debug.LogWarning(
                        $"Inventaire plein : " +
                        $"{remaining} x {result.Item.name} n'ont pas pu être ajoutés."
                    );
                }
            }
        }


        private InventoryHolder FindSourceInventory()
        {
            if (_lastValidSource == null)
                return null;


            InventoryHolder holder =
                _lastValidSource
                    .GetComponent<InventoryHolder>();


            if (holder != null)
                return holder;


            holder =
                _lastValidSource
                    .GetComponentInParent<InventoryHolder>();


            if (holder != null)
                return holder;


            holder =
                _lastValidSource
                    .GetComponentInChildren<InventoryHolder>();


            return holder;
        }
    }
}