using System;
using System.Collections.Generic;
using UnityEngine;
using Core.WaveSystem;
using Core.PoolingSystem;
using Core.HealthSystem;
using Core.ShopSystem;
using Core.LootSystem;
using Core.InventorySystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Un ennemi de tower defense : suit le chemin (waypoints) jusqu'à la base.
    /// Ses stats viennent d'une EnemyDefinition (PV, vitesse, dégâts, or, drop) —
    /// ou, si aucune fiche n'est assignée, des champs réglés ici (fallback).
    /// - Tué (via Health) → +or au joueur + roll du drop dans l'inventaire.
    /// - Arrivé à la base → dégâts à la base.
    /// Dans les deux cas : crie Defeated (la vague le décompte) + retour au pool.
    /// </summary>
    [RequireComponent(typeof(PooledObject))]
    public sealed class TowerDefenseEnemy : MonoBehaviour, IWaveEnemy
    {
        [Header("Fiche (optionnelle : surcharge les champs ci-dessous)")]
        [SerializeField] private EnemyDefinition definition;

        [Header("Fallback si pas de fiche")]
        [SerializeField, Min(0.1f)] private float speed = 3f;
        [SerializeField, Min(1)] private int baseDamage = 1;
        [SerializeField, Min(0)] private int goldReward = 5;
        [SerializeField] private LootTable lootTable;

        [Header("Déplacement")]
        [SerializeField] private bool faceMovement = true;
        [SerializeField] private float reachThreshold = 0.05f;

        public event Action<IWaveEnemy> Defeated;
        /// <summary>Émis à la mort avec le butin obtenu (pour spawn de pickups, UI...).</summary>
        public event Action<IReadOnlyList<LootResult>> OnDropped;

        // Références du niveau, fournies au spawn.
        private WaypointPath _path;
        private PlayerBase _base;
        private Wallet _wallet;
        private InventoryHolder _inventory;

        private Health _health;

        // Valeurs effectives (fiche ou fallback).
        private float _speed;
        private int _baseDamage;
        private int _goldReward;
        private LootTable _loot;

        private int _index;
        private bool _finished;

        private void Awake() => TryGetComponent(out _health);

        private void OnEnable()
        {
            if (_health != null) _health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            if (_health != null) _health.OnDeath -= HandleDeath;
        }

        /// <summary>Appelé par le TowerDefenseLevel à chaque apparition. Réinitialise l'ennemi.</summary>
        public void Initialize(WaypointPath path, PlayerBase playerBase, Wallet wallet, InventoryHolder inventory)
        {
            _path = path;
            _base = playerBase;
            _wallet = wallet;
            _inventory = inventory;

            // Résout les stats depuis la fiche, sinon les champs locaux.
            if (definition != null)
            {
                _speed = definition.Speed;
                _baseDamage = definition.BaseDamage;
                _goldReward = definition.GoldReward;
                _loot = definition.LootTable;
                if (_health != null) _health.SetMaxHealth(definition.MaxHealth);
            }
            else
            {
                _speed = speed;
                _baseDamage = baseDamage;
                _goldReward = goldReward;
                _loot = lootTable;
            }

            _index = 0;
            _finished = false;

            if (_path != null && _path.Count > 0)
                transform.position = _path.GetPoint(0);

            // L'ennemi vient peut-être du pool (déjà "mort") : on le remet à fond et vivant.
            if (_health != null) _health.Revive();
        }

        private void Update()
        {
            if (_finished || _path == null || _index >= _path.Count) return;

            Vector3 target = _path.GetPoint(_index);

            if (faceMovement)
            {
                Vector3 dir = target - transform.position;
                if (dir.sqrMagnitude > 0.0001f)
                    transform.forward = dir.normalized; // 2D : remplace par une rotation Z
            }

            transform.position = Vector3.MoveTowards(transform.position, target, _speed * Time.deltaTime);

            if ((transform.position - target).sqrMagnitude <= reachThreshold * reachThreshold)
            {
                _index++;
                if (_index >= _path.Count)
                    ReachBase();
            }
        }

        // Tué par les tours (via Health).
        private void HandleDeath()
        {
            if (_finished) return;
            _finished = true;

            if (_wallet != null) _wallet.Add(_goldReward);
            RollDrops();
            Defeat();
        }

        // Arrivé au bout du chemin.
        private void ReachBase()
        {
            if (_finished) return;
            _finished = true;

            if (_base != null) _base.TakeDamage(_baseDamage);
            Defeat();
        }

        private void RollDrops()
        {
            if (_loot == null) return;

            List<LootResult> results = _loot.Roll();
            if (results.Count == 0) return;

            if (_inventory != null && _inventory.Inventory != null)
            {
                foreach (LootResult r in results)
                    _inventory.Inventory.Add(r.Item, r.Amount);
            }

            OnDropped?.Invoke(results);
        }

        // Point commun : décompte pour la vague + retour au pool.
        private void Defeat()
        {
            Defeated?.Invoke(this);

            if (TryGetComponent(out PooledObject pooled)) pooled.Release();
            else Destroy(gameObject);
        }
    }
}
