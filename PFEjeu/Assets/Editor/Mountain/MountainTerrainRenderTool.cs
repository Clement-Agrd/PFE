using Defense.Relief.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Defense.Mountain.EditorTools
{
    /// <summary>
    /// Optimisation du rendu du terrain (composant Terrain de la scene, avec Undo) : distance d'affichage des arbres
    /// ramenee a la taille reelle de la scene et instancing du terrain. N'agit ni sur l'eclairage ni sur les assets.
    /// </summary>
    public static class MountainTerrainRenderTool
    {
        [MenuItem("Tools/Defense/Mountain/Optimiser le rendu du terrain")]
        public static void Apply()
        {
            MountainProfile profile = MountainProfile.LoadOrCreate();
            Terrain terrain = DefenseReliefScene.Terrain;

            float oldDistance = terrain.treeDistance;
            bool oldInstanced = terrain.drawInstanced;

            Undo.RecordObject(terrain, "Mountain : rendu du terrain");
            terrain.treeDistance = profile.TerrainTreeDistance;
            terrain.drawInstanced = profile.TerrainDrawInstanced;
            EditorUtility.SetDirty(terrain);
            DefenseReliefScene.MarkSceneDirty();

            Debug.Log("[Mountain] Rendu du terrain : treeDistance " + oldDistance + " -> " + terrain.treeDistance +
                      " ; drawInstanced " + oldInstanced + " -> " + terrain.drawInstanced);
        }
    }
}
