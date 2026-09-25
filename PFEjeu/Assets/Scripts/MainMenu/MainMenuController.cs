using Core.TweenSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Core.MainMenu
{
    /// <summary>
    /// Actions du menu principal (Jouer / Quitter) + petite entrée en fondu des
    /// boutons pour un menu qui donne moins l'impression d'être statique.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private string gameSceneName = "Max";

        [Header("Entrée")]
        [SerializeField] private CanvasGroup menuGroup;
        [SerializeField, Min(0f)] private float fadeInDuration = 0.5f;

        private void Start()
        {
            if (menuGroup == null) return;

            menuGroup.alpha = 0f;
            menuGroup.transform.KillTweens();
            menuGroup.transform.localScale = Vector3.one * 0.92f;
            menuGroup.TweenFade(1f, fadeInDuration);
            menuGroup.transform.TweenScale(1f, fadeInDuration).SetEase(Ease.OutBack);
        }

        public void Play() => SceneManager.LoadScene(gameSceneName);

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
