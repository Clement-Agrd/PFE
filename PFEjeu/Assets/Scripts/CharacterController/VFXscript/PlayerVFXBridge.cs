using UnityEngine;

namespace ProfessionalTPS
{
    /// <summary>
    /// Centralise les effets visuels du joueur.
    ///
    /// Le gameplay ne connaît pas directement les prefabs VFX.
    /// </summary>
    public sealed class PlayerVFXBridge : MonoBehaviour
    {
        [Header("Sword")]

        [SerializeField]
        private GameObject slashPrefab;

        [SerializeField]
        private GameObject impactPrefab;


        [Header("Slash Settings")]

        [Tooltip(
            "Point où le VFX de slash est créé."
        )]
        [SerializeField]
        private Transform slashOrigin;

        [SerializeField]
        private Vector3 slashPositionOffset;

        [SerializeField]
        private Vector3 slashRotationOffset;


        [Header("Impact Settings")]

        [Tooltip(
            "Petit décalage évitant que le VFX soit exactement dans la surface."
        )]
        [SerializeField, Min(0f)]
        private float impactSurfaceOffset = 0.02f;


        // ============================================================
        // SLASH
        // ============================================================

        public void PlaySlash()
        {
            if (slashPrefab == null)
                return;


            Transform origin =
                slashOrigin != null
                    ? slashOrigin
                    : transform;


            Vector3 position =
                origin.position +
                origin.TransformVector(
                    slashPositionOffset
                );


            Quaternion rotation =
                origin.rotation *
                Quaternion.Euler(
                    slashRotationOffset
                );


            Instantiate(
                slashPrefab,
                position,
                rotation
            );
        }


        // ============================================================
        // IMPACT
        // ============================================================

        public void PlayImpact(
            Vector3 position,
            Vector3 surfaceNormal)
        {
            if (impactPrefab == null)
                return;


            Vector3 normal =
                surfaceNormal.sqrMagnitude >
                0.001f
                    ? surfaceNormal.normalized
                    : Vector3.up;


            Vector3 spawnPosition =
                position +
                normal *
                impactSurfaceOffset;


            Quaternion rotation =
                Quaternion.LookRotation(
                    normal
                );


            Instantiate(
                impactPrefab,
                spawnPosition,
                rotation
            );
        }
    }
}