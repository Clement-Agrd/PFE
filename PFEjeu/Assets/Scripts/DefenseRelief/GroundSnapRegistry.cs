using System;
using System.Collections.Generic;
using UnityEngine;

namespace Defense.Relief
{
    /// <summary>
    /// Memorise, pour des objets de scene, leur hauteur par rapport au sol d'origine
    /// afin de pouvoir les reposer sur le terrain apres tout nouveau sculpt.
    /// Composant de scene uniquement : aucun prefab n'est modifie.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GroundSnapRegistry : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private Transform target;
            [Tooltip("Y de l'objet moins hauteur du terrain d'origine.")]
            [SerializeField] private float offset;

            public Entry(Transform target, float offset)
            {
                this.target = target;
                this.offset = offset;
            }

            public Transform Target => target;
            public float Offset => offset;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public void Register(Transform target, float offset)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Target != target) continue;
                entries[i] = new Entry(target, offset);
                return;
            }
            entries.Add(new Entry(target, offset));
        }

        public bool IsRegistered(Transform target)
        {
            foreach (Entry entry in entries)
            {
                if (entry.Target == target) return true;
            }
            return false;
        }

        /// <summary>Repose chaque objet : Y = hauteur du terrain + offset d'origine.</summary>
        public int Snap(Terrain terrain)
        {
            int moved = 0;
            Vector3 origin = terrain.transform.position;
            foreach (Entry entry in entries)
            {
                Transform t = entry.Target;
                if (t == null) continue;
                Vector3 p = t.position;
                float y = terrain.SampleHeight(p) + origin.y + entry.Offset;
                if (Mathf.Abs(y - p.y) < 0.0005f) continue;
                t.position = new Vector3(p.x, y, p.z);
                moved++;
            }
            return moved;
        }
    }
}
