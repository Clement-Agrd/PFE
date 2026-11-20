using UnityEngine;
using UnityEngine.InputSystem;

namespace DamageFx
{
    /// <summary>
    /// Banc d'essai de lisibilité : une horde de cibles reçoit des coups en rafale (normaux, crits, feu, glace, poison en ticks,
    /// magie, soins, coups sur la tour et sur un boss). Espace = change l'intensité de la rafale. Utilise uniquement DamageNumbers.Show.
    /// </summary>
    public sealed class DamageFxStressTest : MonoBehaviour
    {
        [SerializeField] private Transform[] targets;
        [SerializeField] private Transform tower;
        [SerializeField] private Transform boss;
        [SerializeField] private DamageNumbersManager manager;

        [Header("Rafale")]
        [SerializeField, Min(1f)] private float hitsPerSecond = 70f;
        [SerializeField, Range(0f, 1f)] private float critChance = 0.06f;
        [SerializeField, Min(0f)] private float healsPerSecond = 3f;
        [SerializeField, Min(0f)] private float towerHitsPerSecond = 2.5f;
        [SerializeField, Min(0f)] private float bossHitsPerSecond = 2f;
        [SerializeField, Min(0)] private int poisonedTargets = 6;
        [SerializeField, Min(0.05f)] private float poisonTick = 0.25f;
        [SerializeField, Min(1f)] private float targetMaxHealth = 220f;

        private static readonly float[] Intensities = { 25f, 70f, 200f };
        private int intensityIndex = 1;
        private float hitAcc, healAcc, towerAcc, bossAcc, poisonAcc;

        private void Start()
        {
            hitsPerSecond = Intensities[intensityIndex];
            if (manager == null) manager = FindFirstObjectByType<DamageNumbersManager>();
        }

        private void Update()
        {
            if (targets == null || targets.Length == 0) return;
            float dt = Time.deltaTime;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
            {
                intensityIndex = (intensityIndex + 1) % Intensities.Length;
                hitsPerSecond = Intensities[intensityIndex];
            }

            hitAcc += hitsPerSecond * dt;
            while (hitAcc >= 1f) { hitAcc -= 1f; RandomHit(); }

            healAcc += healsPerSecond * dt;
            while (healAcc >= 1f) { healAcc -= 1f; Hit(RandomTarget(), Random.Range(8f, 25f), DamageFxKind.Heal, false); }

            if (tower != null)
            {
                towerAcc += towerHitsPerSecond * dt;
                while (towerAcc >= 1f) { towerAcc -= 1f; Hit(tower, Random.Range(15f, 70f), DamageFxKind.TowerHit, false, 800f); }
            }

            if (boss != null)
            {
                bossAcc += bossHitsPerSecond * dt;
                while (bossAcc >= 1f) { bossAcc -= 1f; Hit(boss, Random.Range(20f, 90f), DamageFxKind.Physical, Random.value < 0.15f, 3000f, true); }
            }

            // Dégâts sur la durée : quelques cibles empoisonnées reçoivent de petits ticks réguliers.
            poisonAcc += dt;
            if (poisonAcc >= poisonTick)
            {
                poisonAcc -= poisonTick;
                int n = Mathf.Min(poisonedTargets, targets.Length);
                for (int i = 0; i < n; i++) Hit(targets[i], Random.Range(1f, 4f), DamageFxKind.Poison, false);
            }
        }

        private void RandomHit()
        {
            float r = Random.value;
            DamageFxKind kind = r < 0.5f ? DamageFxKind.Physical : r < 0.68f ? DamageFxKind.Fire : r < 0.84f ? DamageFxKind.Ice
                              : r < 0.92f ? DamageFxKind.Magic : DamageFxKind.Poison;
            bool crit = Random.value < critChance;
            float amount = crit ? Random.Range(90f, 170f) : (kind == DamageFxKind.Poison ? Random.Range(1f, 5f) : Random.Range(3f, 55f));
            Hit(RandomTarget(), amount, kind, crit);
        }

        private Transform RandomTarget() => targets[Random.Range(0, targets.Length)];

        private void Hit(Transform t, float amount, DamageFxKind kind, bool crit, float maxHp = 0f, bool isBoss = false)
        {
            // Point d'impact : torse de la cible + petit écart (tous les coups ne touchent pas pile le centre).
            Vector3 point = t.position + Vector3.up * 1.1f + Random.insideUnitSphere * 0.25f;
            DamageNumbers.Show(amount, point, kind, crit, t, maxHp > 0f ? maxHp : targetMaxHealth, isBoss);
        }

        private void OnGUI()
        {
            if (manager == null) return;
            GUI.Label(new Rect(12, 8, 700, 24), $"DamageFx — chiffres actifs : {manager.ActiveCount}   |   rafale : {hitsPerSecond:0} coups/s   |   Espace : changer l'intensité");
        }
    }
}
