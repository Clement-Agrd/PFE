using System.Collections.Generic;
using UnityEngine;
using Core.InteractionSystem;

namespace Core.DisintegrationVFX
{
    /// <summary>
    /// Station de la zone FX : interagir désintègre les cibles de test (cycle des styles),
    /// puis les remet debout. Mode Showcase : boucle automatique.
    /// </summary>
    public sealed class DisintegrationStation : InteractableBase
    {
        [Header("Cibles")]
        [SerializeField] private List<DisintegrationController> targets = new List<DisintegrationController>();
        [SerializeField] private List<DisintegrationStyle> styles = new List<DisintegrationStyle>();

        [Header("Showcase")]
        [SerializeField] private bool loop;
        [SerializeField, Range(1f, 10f)] private float loopDelay = 6f;
        [SerializeField, Range(0f, 1f)] private float staggerPerTarget = 0.35f;

        private int _styleIndex;
        private float _nextAt;
        private bool _running;

        public override string Prompt => "Désintégrer les cibles";
        public List<DisintegrationController> Targets => targets;
        public List<DisintegrationStyle> Styles => styles;
        public bool Loop { get => loop; set => loop = value; }

        public string StyleName
        {
            get
            {
                if (styles.Count == 0 || styles[Mod(_styleIndex)] == null) return "Défaut";
                string n = styles[Mod(_styleIndex)].name;
                return n.StartsWith("Style_") ? n.Substring(6) : n;
            }
        }

        public void SelectStyle(int delta)
        {
            if (styles.Count == 0) return;
            _styleIndex = Mod(_styleIndex + delta);
        }

        private int Mod(int i) => styles.Count == 0 ? 0 : ((i % styles.Count) + styles.Count) % styles.Count;

        public override bool CanInteract(GameObject interactor) => isActiveAndEnabled;

        public override void Interact(GameObject interactor) => Trigger();

        public void Trigger()
        {
            float delay = 0f;
            DisintegrationStyle st = styles.Count > 0 ? styles[Mod(_styleIndex)] : null;
            for (int i = 0; i < targets.Count; i++)
            {
                DisintegrationController t = targets[i];
                if (t == null) continue;
                t.Reset();
                if (st != null) t.Style = st;
                StartCoroutine(Fire(t, delay));
                delay += staggerPerTarget;
            }

            if (loop) _styleIndex++;
            _nextAt = Time.time + loopDelay + delay;
        }

        private System.Collections.IEnumerator Fire(DisintegrationController t, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (t != null)
                t.Disintegrate();
        }

        private void Update()
        {
            if (loop && Time.time >= _nextAt)
                Trigger();
        }
    }
}
