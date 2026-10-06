using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Core.Village.Exploration;

namespace Core.Village.UI
{
    /// <summary>
    /// Section "Expéditions" de l'onglet Village : dit d'un coup d'œil s'il y a
    /// une expédition en cours (ou un retour qui attend la prochaine défense),
    /// puis liste chacune. Lecture seule — le lancement reste dans
    /// ExplorationPanelUI. Se met à jour à l'ouverture de l'onglet et à chaque
    /// changement d'état du poste d'expédition.
    /// </summary>
    public sealed class ExpeditionStatusUI : MonoBehaviour
    {
        [SerializeField] private ExplorationManager manager;
        [SerializeField] private TMP_Text summaryLabel;
        [SerializeField] private GameObject emptyCard;
        [SerializeField] private Transform rowsParent;
        [SerializeField] private ExpeditionStatusRowUI rowPrefab;

        [Header("Couleurs")]
        [SerializeField] private Color idleColor = new(0.62f, 0.66f, 0.72f);
        [SerializeField] private Color activeColor = new(0.95f, 0.78f, 0.35f);
        [SerializeField] private Color successColor = new(0.55f, 0.88f, 0.5f);
        [SerializeField] private Color failColor = new(0.92f, 0.45f, 0.42f);

        private readonly List<ExpeditionStatusRowUI> _rows = new();

        private void OnEnable()
        {
            if (manager != null) manager.OnStateChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (manager != null) manager.OnStateChanged -= Refresh;
        }

        public void Refresh()
        {
            if (rowsParent == null || rowPrefab == null) return;

            int count = 0;
            int active = 0;

            if (manager != null)
            {
                foreach (var (hero, mission, cyclesRemaining) in manager.GetActiveMissions())
                {
                    Row(count++).Set(Title(hero, mission), $"{cyclesRemaining} cycle(s) restant(s)", activeColor);
                    active++;
                }

                foreach (var (hero, mission, success) in manager.GetPendingRewards())
                {
                    Row(count++).Set(Title(hero, mission),
                        success ? "De retour — butin à la prochaine défense" : "Blessé — butin perdu",
                        success ? successColor : failColor);
                }
            }

            for (int i = 0; i < _rows.Count; i++) _rows[i].gameObject.SetActive(i < count);
            if (emptyCard != null) emptyCard.SetActive(count == 0);

            if (summaryLabel != null)
            {
                summaryLabel.text = active > 0 ? $"{active} en cours"
                    : count > 0 ? "Retour en attente"
                    : "Aucune expédition";
                summaryLabel.color = count == 0 ? idleColor : activeColor;
            }
        }

        private static string Title(HeroDefinition hero, ExplorationMissionDefinition mission)
            => $"{hero.DisplayName} ({hero.Class}) → {mission.DisplayName}";

        private ExpeditionStatusRowUI Row(int index)
        {
            while (_rows.Count <= index) _rows.Add(Instantiate(rowPrefab, rowsParent));
            return _rows[index];
        }
    }
}
