using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Core.TowerDefense;
using Core.TrapSystem;
using static Core.PauseMenu.PauseUiKit;

namespace Core.PauseMenu
{
    /// <summary>
    /// Menu pause (Échap / Start) : Reprendre, Options, Bestiaire, Menu principal, Quitter.
    /// Toute l'interface est construite en code au lancement (aucun prefab à brancher) et animée en
    /// temps non mis à l'échelle. Le jeu est figé via GameFreeze tant que le menu est ouvert.
    /// Échap n'ouvre le menu que si aucune autre fenêtre n'était ouverte à la frame précédente
    /// (sinon la même touche qui ferme une fenêtre ouvrirait aussitôt la pause).
    /// </summary>
    public sealed class PauseMenuUI : MonoBehaviour
    {
        private enum View { None, Main, Options, Bestiary }

        [Header("Données")]
        [SerializeField] private List<EnemyDefinition> enemies = new();
        [Tooltip("Asset d'actions du joueur : sert à lister les commandes dans les options.")]
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private string mainMenuScene = "MainMenu";
        [Tooltip("Pour le blocage pendant le placement d'un piège (optionnel, retrouvé sinon).")]
        [SerializeField] private TrapPlacer trapPlacer;

        private GameObject _canvasGo;
        private CanvasGroup _root;
        private RectTransform _mainView, _mainColumn;
        private CanvasGroup _mainGroup;
        private readonly PauseSettingsView _options = new();
        private readonly PauseBestiaryView _bestiary = new();
        private GameObject _confirm;
        private TMP_Text _confirmText;
        private Action _confirmAction;
        private TMP_Text _fpsLabel;

        private View _view = View.None;
        private bool _owned;
        private bool _blockedLastFrame;
        private float _fpsTimer;
        private int _fpsFrames;

        public bool IsOpen => _view != View.None;

#if UNITY_EDITOR
        private void Reset() => CollectFromProject();

        [ContextMenu("Remplir depuis le projet")]
        private void CollectFromProject()
        {
            enemies.Clear();
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:EnemyDefinition"))
                enemies.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)));
            if (controls == null)
            {
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("PlayerControls t:InputActionAsset"))
                    controls = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private void Start()
        {
            if (trapPlacer == null) trapPlacer = FindAnyObjectByType<TrapPlacer>();
            BuildUi();
            BestiaryData.Changed += HandleBestiaryChanged;
            SetViewImmediate(View.None);
        }

        private void OnDestroy()
        {
            BestiaryData.Changed -= HandleBestiaryChanged;
            ReleaseFreeze();
            if (_canvasGo != null) Destroy(_canvasGo);
        }

        private void HandleBestiaryChanged()
        {
            if (_view == View.Bestiary) _bestiary.Refresh();
        }

        #region Entrées

        private void Update()
        {
            UpdateFps();

            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            bool pressed = escape || start;

            bool blockedByOthers = !_owned && (GameFreeze.IsPaused || GameFreeze.IsCursorUnlocked
                                               || (trapPlacer != null && (trapPlacer.IsWheelOpen || trapPlacer.IsPlacing)));

            if (pressed)
            {
                if (IsOpen) HandleBack();
                else if (!blockedByOthers && !_blockedLastFrame) Open();
            }

            _blockedLastFrame = blockedByOthers;
            AnimateViews();
        }

        private void HandleBack()
        {
            if (_confirm != null && _confirm.activeSelf) { _confirm.SetActive(false); return; }
            if (_view == View.Main) Close();
            else GoTo(View.Main);
        }

        #endregion

        #region Navigation

        public void Open()
        {
            if (IsOpen) return;
            if (!_owned)
            {
                _owned = true;
                GameFreeze.RequestPause(this);
                GameFreeze.RequestCursorUnlock(this);
            }
            _mainColumn.anchoredPosition = new Vector2(-70f, 0f);
            GoTo(View.Main);
        }

        public void Close()
        {
            _confirm.SetActive(false);
            GoTo(View.None);
            ReleaseFreeze();
            GameSettings.Save();
        }

        private void ReleaseFreeze()
        {
            if (!_owned) return;
            _owned = false;
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }

        private void GoTo(View view)
        {
            _view = view;
            if (view == View.Options) _options.OnShown();
            if (view == View.Bestiary) _bestiary.OnShown();
        }

        private void SetViewImmediate(View view)
        {
            _view = view;
            ApplyAlpha(_root, view != View.None ? 1f : 0f, true);
            ApplyAlpha(_mainGroup, view == View.Main ? 1f : 0f, true);
            ApplyAlpha(_options.Group, view == View.Options ? 1f : 0f, true);
            ApplyAlpha(_bestiary.Group, view == View.Bestiary ? 1f : 0f, true);
        }

        private void AnimateViews()
        {
            float step = Time.unscaledDeltaTime * 7f;
            ApplyAlpha(_root, _view != View.None ? 1f : 0f, false, step);
            ApplyAlpha(_mainGroup, _view == View.Main ? 1f : 0f, false, step);
            ApplyAlpha(_options.Group, _view == View.Options ? 1f : 0f, false, step);
            ApplyAlpha(_bestiary.Group, _view == View.Bestiary ? 1f : 0f, false, step);

            // Léger glissement d'entrée de la colonne principale.
            if (_mainColumn != null)
                _mainColumn.anchoredPosition = Vector2.Lerp(_mainColumn.anchoredPosition, Vector2.zero, Time.unscaledDeltaTime * 9f);
        }

        private static void ApplyAlpha(CanvasGroup group, float target, bool instant, float step = 1f)
        {
            if (group == null) return;
            group.alpha = instant ? target : Mathf.MoveTowards(group.alpha, target, step);
            bool visible = group.alpha > 0.001f;
            if (group.gameObject.activeSelf != visible) group.gameObject.SetActive(visible);
            group.interactable = target > 0.5f;
            group.blocksRaycasts = target > 0.5f;
        }

        #endregion

        #region Construction

        private void BuildUi()
        {
            _canvasGo = new GameObject("PauseMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Compteur de FPS : hors du groupe animé pour rester visible hors pause.
            _fpsLabel = NewText("Fps", _canvasGo.transform, "", 26f, Good, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            Anchor(_fpsLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(300f, 40f));

            RectTransform layer = NewRect("Layer", _canvasGo.transform);
            Stretch(layer);
            var layerGroup = layer.gameObject.AddComponent<CanvasGroup>();
            // Le groupe racine du canvas pilote le fondu global ; la couche reçoit les clics.
            _root = layerGroup;

            Image dim = NewImage("Dim", layer, Dim);
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            Image vignette = NewImage("Vignette", layer, Color.white);
            vignette.sprite = Vignette();
            Stretch(vignette.rectTransform);

            BuildMain(layer);
            _options.Build(layer, () => GoTo(View.Main), controls);
            _bestiary.Build(layer, enemies, () => GoTo(View.Main));
            BuildConfirm(layer);
        }

        private void BuildMain(Transform parent)
        {
            _mainView = NewRect("MainView", parent);
            Stretch(_mainView);
            _mainGroup = _mainView.gameObject.AddComponent<CanvasGroup>();

            _mainColumn = NewRect("Column", _mainView);
            Anchor(_mainColumn, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1f, 1f));
            // Décalage horizontal appliqué via le conteneur, la colonne reste ancrée à gauche.
            RectTransform column = NewRect("Content", _mainColumn);
            Anchor(column, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(190f, 0f), new Vector2(560f, 780f));
            VStack(column.gameObject, 16f, align: TextAnchor.MiddleLeft);

            TMP_Text title = NewText("Title", column, "PAUSE", 128f, TextMain, TextAlignmentOptions.Left, FontStyles.Bold);
            title.characterSpacing = 16f;
            Layout(title.gameObject, height: 150f);

            Image accent = NewImage("Accent", column, Accent, 2);
            Layout(accent.gameObject, 140f, 6f);

            TMP_Text sub = NewText("Subtitle", column, "Le monde retient son souffle.", 30f, TextMuted, TextAlignmentOptions.Left, FontStyles.Italic);
            Layout(sub.gameObject, height: 56f);

            AddSpacer(column, 18f);

            AddMainButton(column, "Reprendre", Close, ButtonStyle.Primary);
            AddMainButton(column, "Options", () => GoTo(View.Options), ButtonStyle.Secondary);
            AddMainButton(column, "Bestiaire", () => GoTo(View.Bestiary), ButtonStyle.Secondary);
            AddMainButton(column, "Menu principal", () => Ask("Retourner au menu principal ?\nLa progression non sauvegardée sera perdue.", ReturnToMainMenu), ButtonStyle.Secondary);
            AddMainButton(column, "Quitter le jeu", () => Ask("Quitter le jeu ?", Quit), ButtonStyle.Danger);

            TMP_Text hint = NewText("Hint", _mainView, "Échap : Reprendre", 24f, TextMuted, TextAlignmentOptions.BottomLeft);
            Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(190f, 48f), new Vector2(500f, 36f));

            TMP_Text version = NewText("Version", _mainView, "v" + Application.version, 22f, TextMuted, TextAlignmentOptions.BottomRight);
            Anchor(version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 48f), new Vector2(300f, 32f));
        }

        private static void AddSpacer(Transform parent, float height)
        {
            RectTransform s = NewRect("Spacer", parent);
            Layout(s.gameObject, height: height);
        }

        private static void AddMainButton(Transform parent, string label, Action onClick, ButtonStyle style)
        {
            PauseButton b = NewButton(parent, label, onClick, style, 34f, 18);
            Layout(b.gameObject, 520f, 86f);
        }

        private void BuildConfirm(Transform parent)
        {
            _confirm = NewRect("Confirm", parent).gameObject;
            Stretch((RectTransform)_confirm.transform);
            Image shade = _confirm.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.6f);

            RectTransform box = NewRect("Box", _confirm.transform);
            Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 360f));
            var bg = box.gameObject.AddComponent<Image>();
            bg.sprite = Rounded(26);
            bg.type = Image.Type.Sliced;
            bg.color = Card;
            box.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            VStack(box.gameObject, 28f, new RectOffset(48, 48, 44, 40), TextAnchor.MiddleCenter);

            _confirmText = NewText("Text", box, "", 34f, TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            Layout(_confirmText.gameObject, height: 150f);

            RectTransform row = NewRect("Buttons", box);
            Layout(row.gameObject, height: 76f);
            HStack(row.gameObject, 20f, align: TextAnchor.MiddleCenter);
            PauseButton no = NewButton(row, "Annuler", () => _confirm.SetActive(false), ButtonStyle.Secondary, 30f);
            no.Label.alignment = TextAlignmentOptions.Center;
            Layout(no.gameObject, 280f, 76f);
            PauseButton yes = NewButton(row, "Confirmer", () => { _confirm.SetActive(false); _confirmAction?.Invoke(); }, ButtonStyle.Primary, 30f);
            yes.Label.alignment = TextAlignmentOptions.Center;
            Layout(yes.gameObject, 280f, 76f);

            _confirm.SetActive(false);
        }

        private void Ask(string message, Action onConfirm)
        {
            _confirmText.text = message;
            _confirmAction = onConfirm;
            _confirm.SetActive(true);
            _confirm.transform.SetAsLastSibling();
        }

        #endregion

        #region Actions

        private void ReturnToMainMenu()
        {
            Close();
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuScene);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void UpdateFps()
        {
            if (_fpsLabel == null) return;
            bool show = GameSettings.ShowFps;
            if (_fpsLabel.gameObject.activeSelf != show) _fpsLabel.gameObject.SetActive(show);
            if (!show) return;

            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer < 0.5f) return;
            _fpsLabel.text = $"{Mathf.RoundToInt(_fpsFrames / _fpsTimer)} FPS";
            _fpsFrames = 0;
            _fpsTimer = 0f;
        }

        #endregion
    }
}
