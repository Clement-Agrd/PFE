using UnityEngine;
using UnityEngine.InputSystem;

using Core.WaveSystem;

namespace Core.TowerDefense
{
    /// <summary>
    /// Lanceur de vagues pour la zone de test des tours.
    ///
    /// - startOnPlay : lance la vague au démarrage de la scène.
    /// - restartWhenCleared : relance la vague dès qu'elle est nettoyée
    ///   (ennemi qui tourne en boucle, remplacé s'il est tué).
    /// - startKey : lance la prochaine vague à la demande.
    /// </summary>
    public sealed class TowerTestWaveLauncher :
        MonoBehaviour
    {
        [SerializeField]
        private WaveSpawner spawner;

        [SerializeField]
        private bool startOnPlay;

        [SerializeField]
        private bool restartWhenCleared;

        [Tooltip("Touche pour lancer la vague (None = désactivé).")]
        [SerializeField]
        private Key startKey =
            Key.None;


        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.OnWaveCompleted +=
                    HandleWaveCompleted;
            }
        }


        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.OnWaveCompleted -=
                    HandleWaveCompleted;
            }
        }


        private void Start()
        {
            if (startOnPlay)
            {
                StartWave();
            }
        }


        private void Update()
        {
            if (startKey != Key.None &&
                Keyboard.current != null &&
                Keyboard.current[startKey]
                    .wasPressedThisFrame)
            {
                StartWave();
            }
        }


        /// <summary>
        /// Utilisable depuis un bouton UI ou le menu contextuel.
        /// </summary>
        [ContextMenu("Start Wave")]
        public void StartWave()
        {
            if (spawner != null)
            {
                spawner.StartNextWave();
            }
        }


        private void HandleWaveCompleted(
            int wave)
        {
            if (restartWhenCleared)
            {
                // Le spawner n'est libéré qu'à la fin de la coroutine.
                Invoke(
                    nameof(StartWave),
                    0.1f
                );
            }
        }
    }
}
