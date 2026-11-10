using UnityEngine;

namespace Core.GroundFog
{
    /// <summary>
    /// À poser sur le joueur : le brouillard au sol s'écarte autour de lui.
    /// Le trou est irrégulier (contour déformé par du bruit) et laisse une traîne derrière le joueur
    /// quand il avance, pour un effet de sillage naturel plutôt qu'un rond parfait.
    /// Publie les données via des variables globales shader (pas de singleton, pas d'allocation par frame).
    /// </summary>
    [ExecuteAlways]
    public sealed class GroundFogClearer : MonoBehaviour
    {
        [Tooltip("Rayon (m) totalement dégagé autour du joueur.")]
        [SerializeField, Min(0f)] private float clearRadius = 1.0f;

        [Tooltip("Distance (m) sur laquelle le brouillard revient progressivement.")]
        [SerializeField, Min(0.01f)] private float softness = 1.5f;

        [Tooltip("Irrégularité du contour (0 = cercle parfait, 1 = très déchiré).")]
        [SerializeField, Range(0f, 1f)] private float edgeIrregularity = 0.6f;

        [Tooltip("Vitesse à laquelle la traîne rattrape le joueur (petit = sillage long, 0 = pas de traîne).")]
        [SerializeField, Range(0f, 10f)] private float trailFollowSpeed = 2.5f;

        private static readonly int ClearId = Shader.PropertyToID("_GroundFogClear");
        private static readonly int SoftId = Shader.PropertyToID("_GroundFogClearSoft");
        private static readonly int TrailId = Shader.PropertyToID("_GroundFogTrail");
        private static readonly int WarpId = Shader.PropertyToID("_GroundFogClearWarp");

        private Vector3 _trail;
        private bool _trailInit;

        private void OnEnable() => _trailInit = false;

        private void LateUpdate()
        {
            Vector3 p = transform.position;

            if (!_trailInit || trailFollowSpeed <= 0f)
            {
                _trail = p;
                _trailInit = true;
            }
            else
            {
                // Suivi exponentiel indépendant du framerate.
                float k = 1f - Mathf.Exp(-trailFollowSpeed * Time.deltaTime);
                _trail = Vector3.Lerp(_trail, p, k);
            }

            Shader.SetGlobalVector(ClearId, new Vector4(p.x, p.y, p.z, clearRadius));
            Shader.SetGlobalVector(TrailId, new Vector4(_trail.x, _trail.y, _trail.z, 0f));
            Shader.SetGlobalFloat(SoftId, softness);
            Shader.SetGlobalFloat(WarpId, edgeIrregularity);
        }

        private void OnDisable()
        {
            // Rayon 0 = effet désactivé dans le shader.
            Shader.SetGlobalVector(ClearId, Vector4.zero);
        }
    }
}
