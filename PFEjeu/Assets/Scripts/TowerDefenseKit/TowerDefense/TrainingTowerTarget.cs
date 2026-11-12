using System;
using Core.HealthSystem;
using Core.StatsSystem;
using TMPro;
using UnityEngine;
using static Core.PauseMenu.PauseUiKit;
using Stat = Core.StatsSystem.EnumStats.StatTypes;

namespace Core.TowerDefense
{
    [RequireComponent(typeof(EntityStats), typeof(Health))]
    public sealed class TrainingTowerTarget : TrainingPanel
    {
        [SerializeField] private int maxHealth = 1500;
        [SerializeField] private int physicalArmor = 50;
        [SerializeField] private int magicalArmor = 50;
        [SerializeField] private int healingPerSecond = 250;
        [SerializeField] private TextMeshPro readout;
        private EntityStats _stats;
        private Health _health;
        private float _healBudget;
        private int _lastDamage;
        private long _totalDamage;

        public override string Prompt => "Régler la cible d’essai";

        private void Awake()
        {
            _stats = GetComponent<EntityStats>();
            _health = GetComponent<Health>();
        }

        private void Start()
        {
            _health.SetMaxHealth(maxHealth, true);
            _stats.SetBaseStat(Stat.DefensePhysic, physicalArmor);
            _stats.SetBaseStat(Stat.DefenseMagic, magicalArmor);
            RefreshReadout();
        }

        private void OnEnable() => _health.OnDamageResolved += RecordDamage;

        protected override void OnDisable()
        {
            _health.OnDamageResolved -= RecordDamage;
            base.OnDisable();
        }

        protected override void Update()
        {
            base.Update();
            if (_health.IsDead)
            {
                _health.Revive();
                RefreshReadout();
            }
            if (_health.CurrentHealth >= _health.MaxHealth)
            {
                _healBudget = 0f;
                return;
            }
            _healBudget += healingPerSecond * Time.deltaTime;
            int amount = Mathf.FloorToInt(_healBudget);
            if (amount < 1) return;
            _healBudget -= amount;
            _health.Heal(amount, showNumber: false);
            RefreshReadout();
        }

        private void RecordDamage(DamageInfo info, int amount)
        {
            _lastDamage = amount;
            _totalDamage += amount;
            RefreshReadout();
        }

        private void RefreshReadout()
        {
            if (readout != null)
                readout.text = $"Cible d’essai\nPV {_health.CurrentHealth} / {_health.MaxHealth}\nDernier coup : {_lastDamage}";
        }

        protected override void BuildPanel(Transform card, GameObject actor)
        {
            Layout(NewText("Title", card, "Cible d’essai", 34, Accent).gameObject, height: 55);
            var damage = NewText("Damage", card, $"Dernier coup : {_lastDamage}   ·   Cumul : {_totalDamage}", 23, TextMain);
            Layout(damage.gameObject, height: 45);
            AddSetting(card, "PV max", 100, 5000, maxHealth, value =>
            {
                maxHealth = value;
                _health.SetMaxHealth(value, true);
            });
            AddSetting(card, "Armure physique", 0, 500, physicalArmor, value =>
            {
                physicalArmor = value;
                _stats.SetBaseStat(Stat.DefensePhysic, value);
            });
            AddSetting(card, "Armure magique", 0, 500, magicalArmor, value =>
            {
                magicalArmor = value;
                _stats.SetBaseStat(Stat.DefenseMagic, value);
            });
            AddSetting(card, "Régénération (PV/s)", 1, 2000, healingPerSecond, value => healingPerSecond = value);
            Layout(NewButton(card, "Remettre les compteurs à zéro", () =>
            {
                _lastDamage = 0;
                _totalDamage = 0;
                _health.Revive();
                damage.text = "Dernier coup : 0   ·   Cumul : 0";
                RefreshReadout();
            }).gameObject, height: 55);
        }

        private void AddSetting(Transform card, string title, int min, int max, int current, Action<int> apply)
        {
            var label = NewText(title, card, $"{title} : {current}", 22, TextMain);
            Layout(label.gameObject, height: 30);
            var slider = NewSlider(card, min, max, current, value =>
            {
                int rounded = Mathf.RoundToInt(value);
                label.text = $"{title} : {rounded}";
                apply(rounded);
                RefreshReadout();
            });
            slider.wholeNumbers = true;
            Layout(slider.gameObject, height: 36);
        }
    }
}
