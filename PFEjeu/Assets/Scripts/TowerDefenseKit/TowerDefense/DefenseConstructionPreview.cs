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
        private Material _rangeOutlineMaterial;
        private const int OutlineSamples = 160;
        private float _shownRadius;

        public void ShowRange(Transform anchor, float radius)
        {
            if (_root != null && _shownRadius == radius && _root.transform.position == anchor.position) return;
            Clear();
            if (radius <= 0f) return;

            _root = new GameObject("TowerRangePreview");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_root, anchor.gameObject.scene);
            _root.transform.position = anchor.position;
            _shownRadius = radius;
            AddRange(radius);
        }

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
            _rangeMaterial ??= MakeRangeMaterial();
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _rangeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            sphere.SetActive(true);
            AddGroundOutline(radius);
        }

        private void AddGroundOutline(float radius)
        {
            Vector3 center = _root.transform.position;
            var terrains = Terrain.activeTerrains;
            var outline = NewChild("Contour de portée au sol", _root.transform).gameObject.AddComponent<LineRenderer>();
            outline.useWorldSpace = true;
            outline.loop = true;
            outline.positionCount = OutlineSamples;
            outline.widthMultiplier = .24f;
            outline.numCornerVertices = 2;
            outline.shadowCastingMode = ShadowCastingMode.Off;
            outline.receiveShadows = false;
            _rangeOutlineMaterial ??= MakeMaterial(new Color(.035f, .53f, 1f, .98f), 3002);
            outline.sharedMaterial = _rangeOutlineMaterial;

            float radiusSquared = radius * radius;
            var points = new Vector3[OutlineSamples];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = 2f * Mathf.PI * i / points.Length;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float inside = 0f;
                float outside = radius;
                // On a slope the sphere meets the ground before its horizontal radius.
                for (int step = 0; step < 12; step++)
                {
                    float distance = (inside + outside) * .5f;
                    Vector3 sample = GroundPoint(center + direction * distance, terrains);
                    if ((sample - center).sqrMagnitude <= radiusSquared) inside = distance;
                    else outside = distance;
                }
                points[i] = GroundPoint(center + direction * inside, terrains) + Vector3.up * .12f;
            }
            outline.SetPositions(points);
        }

        private static Vector3 GroundPoint(Vector3 point, Terrain[] terrains)
        {
            foreach (var terrain in terrains)
            {
                Vector3 offset = point - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (offset.x < 0f || offset.z < 0f || offset.x > size.x || offset.z > size.z) continue;
                point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                break;
            }
            return point;
        }

        private static Transform NewChild(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Material MakeRangeMaterial()
        {
            var material = MakeMaterial(new Color(.24f, .66f, 1f, .24f), 3000);
            material.SetFloat("_Blend", 3f); // URP Multiply
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.EnableKeyword("_ALPHAMODULATE_ON");
            return material;
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
            if (_rangeOutlineMaterial != null) Object.Destroy(_rangeOutlineMaterial);
        }
    }
}
