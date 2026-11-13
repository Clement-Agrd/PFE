
using System.Collections.Generic;
using UnityEngine;
using Core.StatsSystem;
using Core.HealthSystem;

public class EtatManager : MonoBehaviour
{
    [Header("Components")]
    private EntityStats entityStats;
    private Health health;

    [Header("Active States")]
    private readonly List<Etat> etatsActifs = new();
    private readonly Dictionary<SOEtat, GameObject> _effects = new();

    private void Awake()
    {
        entityStats = GetComponentInParent<EntityStats>();
        health = GetComponent<Health>();
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            if (etatsActifs.Count > 0 || _effects.Count > 0) ClearStates();
            return;
        }

        for (int i = etatsActifs.Count - 1; i >= 0; i--)
        {
            Etat etat = etatsActifs[i];

            etat.UpdateEtat(Time.deltaTime);
            if (health != null && health.IsDead)
            {
                ClearStates();
                return;
            }

            if (etat.IsFinished)
            {
                etat.RemoveEtat();
                etatsActifs.RemoveAt(i);
                if (_effects.TryGetValue(etat.EtatData, out var effect) && effect != null)
                    SetVisualState(effect, false);
            }
        }
    }

    private void OnDisable() => ClearStates();

    private void ClearStates()
    {
        foreach (var etat in etatsActifs) etat.RemoveEtat();
        etatsActifs.Clear();
        foreach (var effect in _effects.Values)
        {
            if (effect == null) continue;
            effect.SetActive(false);
            Destroy(effect);
        }
        _effects.Clear();
    }

    public static void ApplyTo(Health target, SOEtat data)
    {
        if (data == null || target == null || target.IsDead || !target.isActiveAndEnabled) return;
        if (!target.TryGetComponent<EtatManager>(out var manager))
            manager = target.gameObject.AddComponent<EtatManager>();
        manager.AddEtat(data);
    }

    public void AddEtat(SOEtat etatData)
    {
        if (etatData == null || !isActiveAndEnabled || (health != null && health.IsDead)) return;

        // Vérifier si cet état est déjà actif
        foreach (Etat etat in etatsActifs)
        {
            if (etat.EtatData == etatData)
            {
                etat.ResetDuration();
                return;
            }
        }

        // Créer un nouvel état
        Etat nouvelEtat = new Etat(etatData, entityStats, health);

        nouvelEtat.ApplyStatModifier();

        etatsActifs.Add(nouvelEtat);
        if (etatData.VisualEffect == null) return;
        if (!_effects.TryGetValue(etatData, out var visual) || visual == null)
        {
            visual = Instantiate(etatData.VisualEffect, transform);
            _effects[etatData] = visual;
        }
        SetVisualState(visual, true);
    }

    private static void SetVisualState(GameObject effect, bool active)
    {
        if (effect.TryGetComponent<BurnFx.BurnTargetFx>(out var burn))
        {
            if (active) burn.BeginBurn();
            else burn.EndBurn();
        }
        else if (effect.TryGetComponent<FreezeFx.FrozenTargetFx>(out var frozen))
        {
            if (active) frozen.BeginFreeze();
            else frozen.EndFreeze();
        }
        else if (effect.TryGetComponent<HealFx.HealTargetFx>(out var heal))
        {
            if (active)
            {
                heal.Pulse();
                heal.BeginHeal();
            }
            else heal.EndHeal();
        }
        else effect.SetActive(active);
    }
}
