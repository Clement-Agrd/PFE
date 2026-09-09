using UnityEngine;
using Core.DamageNumberSystem;

public class SimpleDamageSpawner : MonoBehaviour
{
    public static SimpleDamageSpawner Instance { get; private set; }

    [SerializeField] private GameObject prefab;
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, 0f);

    private void Awake() => Instance = this;

    public void Show(Vector3 worldPos, int amount, string text, Color color, bool isCrit = false)
    {
        if (prefab == null)
        {
            Debug.LogError("[SimpleDamageSpawner] Prefab non assigné !", this);
            return;
        }

        GameObject go = Instantiate(prefab, worldPos + offset, Quaternion.identity);

        if (go.TryGetComponent(out DamageNumber damageNumber))
        {
            damageNumber.Initialize(amount, text, color, isCrit);
        }
        else
        {
            Debug.LogError("[SimpleDamageSpawner] Le composant 'DamageNumber' est introuvable sur le Prefab !", go);
        }
    }
}