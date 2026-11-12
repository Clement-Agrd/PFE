using System;
using UnityEngine;
using static Core.PauseMenu.PauseUiKit;

namespace Core.TowerDefense
{
    public sealed class TrainingTeleport : TrainingPanel
    {
        [Serializable]
        private struct Stop
        {
            public string label;
            public Transform arrival;
        }

        [SerializeField] private Stop[] destinations;
        public override string Prompt => "Ouvrir les destinations";

        protected override void BuildPanel(Transform card, GameObject actor)
        {
            Layout(NewText("Title", card, "Téléportation", 34, Accent).gameObject, height: 54);
            foreach (var stop in destinations)
            {
                if (stop.arrival == null) continue;
                Layout(NewButton(card, stop.label, () =>
                {
                    var controller = actor.GetComponent<CharacterController>();
                    bool wasEnabled = controller != null && controller.enabled;
                    if (wasEnabled) controller.enabled = false;
                    actor.transform.SetPositionAndRotation(stop.arrival.position, stop.arrival.rotation);
                    if (wasEnabled) controller.enabled = true;
                    Close();
                }).gameObject, height: 58);
            }
        }
    }
}
