using UnityEngine;

namespace BuildVFX
{
    /// Relance BuildSite.Play() en boucle, avec une pause entre deux constructions.
    [RequireComponent(typeof(BuildSite))]
    public class BuildSiteLoop : MonoBehaviour
    {
        public float startDelay = 1f, pauseBetween = 3.5f;
        BuildSite site;
        float next;

        void OnEnable() { site = GetComponent<BuildSite>(); next = Time.time + startDelay; }

        void Update()
        {
            if (site.IsPlaying) { next = Time.time + pauseBetween; return; }
            if (Time.time >= next) { site.Play(); next = float.MaxValue; }
        }
    }
}
