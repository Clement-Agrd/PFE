using UnityEngine;

namespace Core.TowerDefense
{
    /// <summary>Un objectif que les ennemis cherchent à atteindre (base, cristal...).</summary>
    public sealed class NavTarget : MonoBehaviour
    {
        public static NavTarget Main { get; private set; }
        private void Awake() { if (Main == null) Main = this; }
    }
}