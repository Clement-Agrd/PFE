using System.Collections;
using UnityEngine;
using BuildVFX;
using UpgradeVFX;

namespace VFXCycle
{
    /// Enchaîne en boucle : construction du niveau 1 (BuildVFX) puis amélioration 1 → 2 → 3 (UpgradeVFX).
    /// Chaque niveau est un enfant actif/inactif de la racine, tous au même pivot.
    public class BuildUpgradeCycle : MonoBehaviour
    {
        public GameObject[] levels;
        public BuildSite build;
        public UpgradeSite upgrade;
        public float startDelay = 1f, pauseBetween = 2.5f, pauseAtEnd = 4f;

        IEnumerator Start()
        {
            yield return new WaitForSeconds(startDelay);
            while (true)
            {
                for (int i = 0; i < levels.Length; i++) levels[i].SetActive(i == 0);
                build.Play();
                yield return null;
                while (build.IsPlaying) yield return null;
                for (int i = 0; i < levels.Length - 1; i++)
                {
                    yield return new WaitForSeconds(pauseBetween);
                    upgrade.stateA = levels[i];
                    upgrade.stateB = levels[i + 1];
                    upgrade.SetTier(i + 2);
                    upgrade.Play();
                    yield return null;
                    while (upgrade.IsPlaying) yield return null;
                }
                yield return new WaitForSeconds(pauseAtEnd);
            }
        }
    }
}
