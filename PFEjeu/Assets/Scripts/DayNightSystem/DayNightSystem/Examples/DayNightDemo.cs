using UnityEngine;

namespace Core.DayNightSystem.Examples
{
    /// <summary>
    /// Démo : P = pause/reprise, N = minuit, M = midi. Logge les changements
    /// d'heure, de phase et de jour. Pose ce composant à côté d'un DayNightCycle.
    /// </summary>
    [RequireComponent(typeof(DayNightCycle))]
    public sealed class DayNightDemo : MonoBehaviour
    {
        private DayNightCycle _cycle;

        private void Awake() => _cycle = GetComponent<DayNightCycle>();

        private void OnEnable()
        {
            _cycle.OnHourChanged += HandleHour;
            _cycle.OnPhaseChanged += HandlePhase;
            _cycle.OnDayPassed += HandleDay;
        }

        private void OnDisable()
        {
            _cycle.OnHourChanged -= HandleHour;
            _cycle.OnPhaseChanged -= HandlePhase;
            _cycle.OnDayPassed -= HandleDay;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.P)) _cycle.Paused = !_cycle.Paused;
            if (Input.GetKeyDown(KeyCode.N)) _cycle.SetTime(0f);   // minuit
            if (Input.GetKeyDown(KeyCode.M)) _cycle.SetTime(12f);  // midi
        }

        private void HandleHour(int hour) => Debug.Log($"[Cycle] Il est {hour}h00.");
        private void HandlePhase(DayPhase phase) => Debug.Log($"[Cycle] Phase : {phase}");
        private void HandleDay(int day) => Debug.Log($"[Cycle] Nouveau jour : {day}");
    }
}
