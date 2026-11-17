using UnityEngine;
using Core.InteractionSystem;

namespace Core.VillageEvents
{
    /// <summary>
    /// Bouton de test (cube) à poser dans la zone village : l'interaction (E) fait apparaître un événement
    /// de village sans avoir à jouer une vague. Par défaut il tire un événement admissible selon l'état
    /// actuel du village (comme après une vague) ; sinon n'importe quelle fiche du catalogue.
    /// </summary>
    public sealed class VillageEventTestButton : InteractableBase
    {
        [SerializeField] private VillageEventManager manager;
        [Tooltip("Vrai : ignore les conditions et les cooldowns, tire n'importe quelle fiche du catalogue.")]
        [SerializeField] private bool ignoreConditions;

        private void Awake()
        {
            if (manager == null) manager = FindFirstObjectByType<VillageEventManager>();
        }

        public override string Prompt => ignoreConditions ? "tirer un événement (toutes fiches)" : "déclencher un événement de village";

        public override bool CanInteract(GameObject interactor)
            => base.CanInteract(interactor) && manager != null && !manager.IsEventOpen;

        public override void Interact(GameObject interactor)
        {
            if (manager == null) return;
            manager.DebugPresentRandom(ignoreConditions);
        }
    }
}
