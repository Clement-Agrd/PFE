using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// Construit une table de correspondance fiable entre "distance parcourue sur la spline" et
/// "paramètre t normalisé (0-1)", en échantillonnant la courbe à résolution fixe.
///
/// Remplace SplineUtility.GetPointAtLinearDistance, qui devient imprécis (voire non-monotone,
/// provoquant des allers-retours visuels) sur une spline avec peu de knots très espacés :
/// sa table interne n'a pas assez de résolution pour capturer correctement l'arc entre deux knots.
///
/// Usage : une seule instance construite par régénération (pas par point), puis interrogée
/// autant de fois que nécessaire via DistanceToT.
/// </summary>
public sealed class SplineDistanceMap
{
    private readonly float[] _tValues;
    private readonly float[] _cumulativeDistances;

    /// <summary>Longueur totale de la spline, mesurée avec la même résolution que la table.</summary>
    public float TotalLength { get; }

    public SplineDistanceMap(Spline spline, int resolution)
    {
        resolution = Mathf.Max(resolution, 8);

        _tValues = new float[resolution + 1];
        _cumulativeDistances = new float[resolution + 1];

        float3 previousPos = SplineUtility.EvaluatePosition(spline, 0f);
        _tValues[0] = 0f;
        _cumulativeDistances[0] = 0f;

        for (int i = 1; i <= resolution; i++)
        {
            float t = i / (float)resolution;
            float3 pos = SplineUtility.EvaluatePosition(spline, t);

            float segmentDistance = math.distance(previousPos, pos);
            _cumulativeDistances[i] = _cumulativeDistances[i - 1] + segmentDistance;
            _tValues[i] = t;

            previousPos = pos;
        }

        TotalLength = _cumulativeDistances[resolution];
    }

    /// <summary>
    /// Convertit une distance linéaire (clampée entre 0 et TotalLength) en paramètre t normalisé,
    /// en interpolant entre les deux échantillons encadrants. Monotone par construction.
    /// </summary>
    public float DistanceToT(float distance)
    {
        distance = Mathf.Clamp(distance, 0f, TotalLength);

        int lo = 0;
        int hi = _cumulativeDistances.Length - 1;

        // Recherche binaire du segment [lo, hi] de la table qui encadre cette distance.
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (_cumulativeDistances[mid] < distance) lo = mid;
            else hi = mid;
        }

        float distLo = _cumulativeDistances[lo];
        float distHi = _cumulativeDistances[hi];
        float segmentLength = distHi - distLo;
        float alpha = segmentLength > 0.0001f ? (distance - distLo) / segmentLength : 0f;

        return Mathf.Lerp(_tValues[lo], _tValues[hi], alpha);
    }
}
