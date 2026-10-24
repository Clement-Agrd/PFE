using UnityEditor;
using UnityEngine;

namespace DustVFX.EditorTools
{
    public static class DustMenu
    {
        [MenuItem("Tools/Dust VFX/Build Surfaces + Settings")]
        public static void BuildSurfaces()
        {
            DustSurfaceBuilder.BuildAll();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Dust VFX/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            DustSceneBuilder.Build();
        }
    }
}
