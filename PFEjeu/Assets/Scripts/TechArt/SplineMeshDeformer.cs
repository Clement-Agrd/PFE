using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Axe local du mesh source qui correspond à sa longueur (le sens qui doit suivre la spline).
/// Y est toujours considéré comme "haut" quel que soit ce choix.
/// </summary>
public enum SplineLengthAxis
{
    X,
    Z
}

/// <summary>
/// Déforme un mesh "droit" pour qu'il suive la courbure d'une Spline, façon Spline Mesh Component (Unreal).
///
/// Convention attendue sur le mesh source :
/// - Axe de longueur = X ou Z local, selon SplineLengthAxis (Y = toujours haut)
/// - Les deux autres axes locaux = décalage perpendiculaire (largeur / hauteur de la section), conservés tels quels
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
    public static Mesh BendMeshAlongSpline(Mesh sourceMesh, Spline spline, SplineDistanceMap distanceMap,
        float distanceStart, float distanceEnd, SplineLengthAxis lengthAxis)
    {
        if (sourceMesh == null || spline == null || distanceMap == null) return null;

        Mesh bent = Object.Instantiate(sourceMesh);
        bent.name = sourceMesh.name + "_Bent";
        bent.hideFlags = HideFlags.DontSave; // évite que ça pollue les assets sauvegardés

        Vector3[] vertices = bent.vertices;
        Vector3[] normals = bent.normals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;

        // Étendue du mesh source sur son axe de longueur, sert à normaliser chaque vertex en 0-1.
        Bounds sourceBounds = sourceMesh.bounds;
        float lengthMin = lengthAxis == SplineLengthAxis.X ? sourceBounds.min.x : sourceBounds.min.z;
        float lengthMax = lengthAxis == SplineLengthAxis.X ? sourceBounds.max.x : sourceBounds.max.z;
        float lengthRange = Mathf.Max(lengthMax - lengthMin, 0.0001f);

        float segmentLength = distanceEnd - distanceStart;

        for (int i = 0; i < vertices.Length; i++)
        {
            // Remap dans un espace canonique (X=droite, Y=haut, Z=longueur) quel que soit l'axe d'origine du mesh.
            Vector3 canonical = ToCanonicalSpace(vertices[i], lengthAxis);

            float alpha = Mathf.Clamp01((canonical.z - lengthMin) / lengthRange);
            float distanceAlongSpline = distanceStart + alpha * segmentLength;

            float t = distanceMap.DistanceToT(distanceAlongSpline);
            SplineUtility.Evaluate(spline, t, out float3 splinePos, out float3 tangent, out float3 upVector);

            // Repère local à ce point de la spline (tangente = avant, up = haut).
            Quaternion frame = Quaternion.LookRotation((Vector3)tangent, (Vector3)upVector);

            // X/Y canoniques = décalage perpendiculaire (largeur/hauteur), le Z canonique (longueur)
            // est déjà "absorbé" par la distance le long de la courbe, donc ignoré ici.
            Vector3 perpendicularOffset = frame * new Vector3(canonical.x, canonical.y, 0f);
            vertices[i] = (Vector3)splinePos + perpendicularOffset;

            if (hasNormals)
            {
                Vector3 canonicalNormal = ToCanonicalSpace(normals[i], lengthAxis);
                normals[i] = frame * canonicalNormal;
            }
        }

        bent.vertices = vertices;
        if (hasNormals) bent.normals = normals;
        bent.RecalculateBounds();
        // Volontairement pas de RecalculateNormals() : on a fait tourner les normales d'origine
        // avec le même repère que les vertices, plus précis et moins cher qu'un recalcul auto.

        return bent;
    }

    /// <summary>
    /// Fait tourner un vecteur (position ou normale) de -90° autour de Y quand l'axe de longueur
    /// du mesh source est X, pour le ramener dans l'espace canonique (X=droite, Y=haut, Z=longueur).
    /// Une VRAIE rotation (pas un simple échange de composantes, qui serait une réflexion et
    /// inverserait le mesh).
    /// </summary>
    private static Vector3 ToCanonicalSpace(Vector3 v, SplineLengthAxis axis)
    {
        return axis == SplineLengthAxis.X ? new Vector3(-v.z, v.y, v.x) : v;
    }
}
