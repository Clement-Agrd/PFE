
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
    private List<Etat> etatsActifs = new List<Etat>();

    private void Awake()
    {
        entityStats = GetComponent<EntityStats>();
        health = GetComponent<Health>();
    }

    private void Update()
    {
        for (int i = etatsActifs.Count - 1; i >= 0; i--)
        {
            Etat etat = etatsActifs[i];

            etat.UpdateEtat(Time.deltaTime);

            if (etat.IsFinished)
            {
                etat.RemoveEtat();
                etatsActifs.RemoveAt(i);
            }
        }
    }

    public void AddEtat(SOEtat etatData)
    {
        if (etatData == null) return;

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
    }
}
