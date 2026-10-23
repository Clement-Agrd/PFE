using UnityEngine;

namespace IceVFX
{
    /// Marqueur posé par IcePool sur les instances recyclées (mémorise leur prefab d'origine).
    [DisallowMultipleComponent]
    public class IcePooled : MonoBehaviour
    {
        [HideInInspector] public GameObject prefab;
        [HideInInspector] public bool inPool;
    }
}
