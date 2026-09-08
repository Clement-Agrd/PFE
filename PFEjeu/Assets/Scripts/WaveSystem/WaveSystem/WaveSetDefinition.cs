using System.Collections.Generic;
using UnityEngine;

namespace Core.WaveSystem
{
    /// <summary>
    /// Séquence ordonnée de vagues (le déroulé d'un niveau), avec option endless :
    /// une fois la dernière vague finie, on reboucle en augmentant la difficulté.
    /// Création : Assets > Create > Waves > Wave Set
    /// </summary>
    [CreateAssetMenu(fileName = "WaveSet", menuName = "Waves/Wave Set")]
    public sealed class WaveSetDefinition : ScriptableObject
    {
        [SerializeField] private List<WaveDefinition> waves = new();

        [Header("Endless")]
        [SerializeField] private bool loopEndless;

        [Tooltip("Nombre d'ennemis ajouté par boucle : 0.5 = +50 % à chaque tour complet.")]
        [SerializeField, Min(0f)] private float countScalePerLoop = 0.5f;

        public IReadOnlyList<WaveDefinition> Waves => waves;
        public bool LoopEndless => loopEndless;
        public float CountScalePerLoop => countScalePerLoop;
    }
}
