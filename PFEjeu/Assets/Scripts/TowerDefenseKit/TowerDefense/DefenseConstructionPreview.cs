using Core.BarracksSystem;
using Core.Village;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core.TowerDefense
{
    internal sealed class DefenseConstructionPreview : System.IDisposable
    {
        private GameObject _root;
        private Material _buildingMaterial;
        private Material _rangeMaterial;

        public void Show(Transform anchor, DefenseBuildingDefinition definition)
        {
            _root = new GameObject("DefenseBuildPreview");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_root, anchor.gameObject.scene);
            _root.transform.SetPositionAndRotation(anchor.position + anchor.rotation * definition.placementOffset, anchor.rotation);
            _root.transform.localScale = definition.prefab.transform.localScale;

            var prefab = definition.prefab;
            var tower = prefab.GetComponent<Tower>();
            var barracks = prefab.GetComponent<Barracks>();
            var towerProfile = tower != null ? tower.BaseProfile : null;
            var barracksProfile = barracks != null ? barracks.Data : null;
            var building = prefab.GetComponent<Building>();
            var model = towerProfile != null ? ModelAt(towerProfile.models, towerProfile.MinLevel - 1) :
                barracksProfile != null ? ModelAt(barracksProfile.models, 0) :
                building != null && building.Definition != null ? building.Definition.GetLevelData(1)?.visualPrefab : null;
            var modelAnchor = tower != null ? tower.ModelAnchor : barracks != null ? barracks.ModelAnchor : prefab.transform;

            _buildingMaterial ??= MakeMaterial(new Color(.51f, .86f, 1f, .48f), 3001);
            if (model != null)
            {
                var visual = NewChild("Modèle", _root.transform);
                visual.localPosition = prefab.transform.InverseTransformPoint(modelAnchor.position);
                visual.localRotation = Quaternion.Inverse(prefab.transform.rotation) * modelAnchor.rotation * model.transform.localRotation;
                visual.localScale = model.transform.localScale;
                CopyGeometry(model.transform, visual);
            }
            else CopyGeometry(prefab.transform, _root.transform);

            if (towerProfile != null && towerProfile.DetectionRange > 0f)
                AddRange(towerProfile.DetectionRange);
        }

        private static GameObject ModelAt(GameObject[] models, int index)
            => models != null && index >= 0 && index < models.Length ? models[index] : null;

        private void CopyGeometry(Transform source, Transform destination)
        {
            if (source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null &&
                source.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
            {
                destination.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var previewRenderer = destination.gameObject.AddComponent<MeshRenderer>();
                var materials = new Material[filter.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = _buildingMaterial;
                previewRenderer.sharedMaterials = materials;
                previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
                previewRenderer.receiveShadows = false;
            }

            foreach (Transform child in source)
            {
                if (!child.gameObject.activeSelf) continue;
                var copy = NewChild(child.name, destination);
                copy.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
                copy.localScale = child.localScale;
                CopyGeometry(child, copy);
            }
        }

        private void AddRange(float radius)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Portée de la tour";
            sphere.SetActive(false);
            var collider = sphere.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            sphere.transform.position = _root.transform.position;
            sphere.transform.localScale = Vector3.one * (radius * 2f);
            sphere.transform.SetParent(_root.transform, true);
            _rangeMaterial ??= MakeMaterial(new Color(.25f, .70f, 1f, .12f), 3000);
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _rangeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            sphere.SetActive(true);
        }

        private static Transform NewChild(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Material MakeMaterial(Color color, int queue)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = queue;
            return material;
        }

        public void Clear()
        {
            if (_root == null) return;
            _root.SetActive(false);
            Object.Destroy(_root);
            _root = null;
        }

        public void Dispose()
        {
            Clear();
            if (_buildingMaterial != null) Object.Destroy(_buildingMaterial);
            if (_rangeMaterial != null) Object.Destroy(_rangeMaterial);
        }
    }
}
