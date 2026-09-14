using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Déforme un mesh "droit" pour qu'il suive la courbure d'une Spline, façon Spline Mesh Component (Unreal).
///
/// Convention attendue sur le mesh source :
/// - Axe de longueur = Z local (de bounds.min.z à bounds.max.z)
/// - X / Y locaux = décalage perpendiculaire (largeur / hauteur de la section), conservés tels quels
///
/// Limite connue : la déformation ne fait que déplacer les vertices existants, elle n'en ajoute pas.
/// Un mesh avec peu de vertices sur sa longueur donnera une courbe "cassée" (corde tendue) sur un
/// segment qui couvre une grande portion de courbe — subdiviser le mesh dans le logiciel 3D si besoin.
/// </summary>
public static class SplineMeshDeformer
{
    /// <summary>
    /// Retourne une COPIE déformée du mesh source, jamais l'asset d'origine.
    /// L'appelant est responsable de détruire le mesh retourné quand il n'est plus utilisé
    /// (Object.Destroy en Play Mode, Object.DestroyImmediate en éditeur).
    /// </summary>
    public static Mesh BendMeshAlongSpline(Mesh sourceMesh, Spline spline, SplineDistanceMap distanceMap, float distanceStart, float distanceEnd)
    {
        if (sourceMesh == null || spline == null || distanceMap == null) return null;

        Mesh bent = Object.Instantiate(sourceMesh);
        bent.name = sourceMesh.name + "_Bent";
        bent.hideFlags = HideFlags.DontSave; // évite que ça pollue les assets sauvegardés

        Vector3[] vertices = bent.vertices;
        Vector3[] normals = bent.normals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;

        // Étendue du mesh source sur son axe de longueur (Z), sert à normaliser chaque vertex en 0-1.
        Bounds sourceBounds = sourceMesh.bounds;
        float zMin = sourceBounds.min.z;
        float zRange = Mathf.Max(sourceBounds.max.z - zMin, 0.0001f);

        float segmentLength = distanceEnd - distanceStart;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];

            // Position le long du segment (0 = début, 1 = fin), déduite du Z d'origine du vertex.
            float alpha = Mathf.Clamp01((v.z - zMin) / zRange);
            float distanceAlongSpline = distanceStart + alpha * segmentLength;

            float t = distanceMap.DistanceToT(distanceAlongSpline);
            SplineUtility.Evaluate(spline, t, out float3 splinePos, out float3 tangent, out float3 upVector);

            // Repère local à ce point de la spline (tangente = avant, up = haut).
            Quaternion frame = Quaternion.LookRotation((Vector3)tangent, (Vector3)upVector);

            // X/Y du vertex d'origine = décalage perpendiculaire (largeur/hauteur), on ignore son Z
            // puisqu'il est déjà "absorbé" par la distance le long de la courbe.
            Vector3 perpendicularOffset = frame * new Vector3(v.x, v.y, 0f);
            vertices[i] = (Vector3)splinePos + perpendicularOffset;

            if (hasNormals)
                normals[i] = frame * normals[i];
        }

        bent.vertices = vertices;
        if (hasNormals) bent.normals = normals;
        bent.RecalculateBounds();
        // Volontairement pas de RecalculateNormals() : on a fait tourner les normales d'origine
        // avec le même repère que les vertices, plus précis et moins cher qu'un recalcul auto.

        return bent;
    }
}
