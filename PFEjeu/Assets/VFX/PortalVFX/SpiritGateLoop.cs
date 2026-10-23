using System.Collections;
using UnityEngine;

namespace SpiritVFX
{
    /// Ouvre et ferme le portail en boucle : éveil, état stable, fermeture, pause.
    [RequireComponent(typeof(SpiritGate))]
    public class SpiritGateLoop : MonoBehaviour
    {
        public float startDelay = 1f, openDuration = 9f, closedDuration = 2.5f;
        SpiritGate gate;

        IEnumerator Start()
        {
            gate = GetComponent<SpiritGate>();
            yield return new WaitForSeconds(startDelay);
            while (true)
            {
                gate.Open();
                yield return new WaitUntil(() => gate.IsOpen);
                yield return new WaitForSeconds(openDuration);
                gate.Close();
                yield return new WaitUntil(() => gate.State == SpiritState.Closed);
                yield return new WaitForSeconds(closedDuration);
            }
        }
    }
}
