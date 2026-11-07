using System;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Gere le TerrainData de BASE de P1 (copie du terrain avant la premiere application).
    /// Alphamaps, arbres et details sont toujours relus depuis cette base : l'outil est idempotent.
    /// </summary>
    public static class MountainSnapshot
    {
        public static TerrainData EnsureBase(MountainProfile profile, TerrainData live)
        {
            if (profile.BaseSnapshot != null) return profile.BaseSnapshot;

            var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(MountainProfile.SnapshotPath);
            if (existing == null)
            {
                string livePath = AssetDatabase.GetAssetPath(live);
                if (!AssetDatabase.CopyAsset(livePath, MountainProfile.SnapshotPath))
                    throw new InvalidOperationException("Impossible de copier le TerrainData vers " + MountainProfile.SnapshotPath);
                existing = AssetDatabase.LoadAssetAtPath<TerrainData>(MountainProfile.SnapshotPath);
            }

            profile.SetBaseSnapshot(existing);
            return existing;
        }

        /// <summary>Remet alphamaps, arbres et details du terrain vivant a l'etat de BASE.</summary>
        public static void RestoreBase(TerrainData live, TerrainData baseData)
        {
            Undo.RegisterCompleteObjectUndo(live, "Mountain : restore base");

            int alphaRes = live.alphamapResolution;
            live.SetAlphamaps(0, 0, baseData.GetAlphamaps(0, 0, alphaRes, alphaRes));

            int detailRes = live.detailResolution;
            for (int layer = 0; layer < live.detailPrototypes.Length; layer++)
            {
                live.SetDetailLayer(0, 0, layer, baseData.GetDetailLayer(0, 0, detailRes, detailRes, layer));
            }

            live.SetTreeInstances(baseData.treeInstances, true);
            EditorUtility.SetDirty(live);
        }
    }
}
