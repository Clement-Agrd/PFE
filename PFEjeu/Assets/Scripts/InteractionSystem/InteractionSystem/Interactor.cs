using System;
using UnityEngine;

namespace Core.InteractionSystem
{
    /// <summary>
    /// À poser sur le joueur : détecte les interactables à portée, met en avant le
    /// plus proche, et déclenche l'interaction à l'appui de la touche.
    /// Détection par OverlapSphereNonAlloc (pas de trigger à configurer, zéro alloc).
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float radius = 2f;
        [SerializeField] private LayerMask interactableMask = ~0;
        [SerializeField] private KeyCode interactKey = KeyCode.E;
        [Tooltip("Taille du buffer de détection (nombre max de colliders considérés).")]
        [SerializeField, Min(4)] private int maxDetected = 16;

        private Collider[] _buffer;
        private IInteractable _current;

        public IInteractable Current => _current;
        public KeyCode InteractKey => interactKey;

        /// <summary>Émis quand la cible en vue change (null = plus rien à portée).</summary>
        public event Action<IInteractable> OnFocusChanged;

        private void Awake() => _buffer = new Collider[maxDetected];

        private void Update()
        {
            IInteractable best = FindBest();

            if (!ReferenceEquals(best, _current))
            {
                _current = best;
                OnFocusChanged?.Invoke(_current);
            }

            if (_current != null && Input.GetKeyDown(interactKey))
                _current.Interact(gameObject);
        }

        private IInteractable FindBest()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, _buffer, interactableMask);

            IInteractable best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                // L'interactable peut être sur le collider ou un parent.
                var interactable = _buffer[i].GetComponentInParent<IInteractable>();
                if (interactable == null || !interactable.CanInteract(gameObject)) continue;

                float sqr = (_buffer[i].transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = interactable;
                }
            }

            return best;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
