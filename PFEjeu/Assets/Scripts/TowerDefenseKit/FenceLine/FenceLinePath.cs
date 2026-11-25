using System.Collections.Generic;
using UnityEngine;

namespace Core.TowerDefense.Fences
{
    /// <summary>
    /// Conteneur de groupes de barrieres (traces independants). Aucune logique de jeu : edition et generation
    /// vivent dans l'assembly Editor (FenceLinePathEditor / FenceGenerator). Les points sont en coordonnees
    /// MONDE : le GameObject porteur doit rester a l'identite (position 0, rotation 0, echelle 1).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FenceLinePath : MonoBehaviour
    {
        public const string GeneratedRootName = "Fences_Generated";

        [SerializeField] private List<FenceGroup> groups = new List<FenceGroup>();
        [SerializeField, HideInInspector] private Transform generatedRoot;

        public IReadOnlyList<FenceGroup> Groups => groups;
        public Transform GeneratedRoot => generatedRoot;

        public FenceGroup Group(int index) => groups[index];

        public void AddGroup(FenceGroup group) => groups.Add(group);
        public void InsertGroup(int index, FenceGroup group) => groups.Insert(index, group);
        public void RemoveGroupAt(int index) => groups.RemoveAt(index);

        public bool SwapGroups(int a, int b)
        {
            if (a < 0 || b < 0 || a >= groups.Count || b >= groups.Count) return false;
            (groups[a], groups[b]) = (groups[b], groups[a]);
            return true;
        }

        public void SetGeneratedRoot(Transform root) => generatedRoot = root;
    }
}
