using Core.PauseMenu;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using static Core.PauseMenu.PauseUiKit;

namespace Core.BarracksSystem
{
    [DisallowMultipleComponent]
    public sealed class BarracksConfirmationUI : MonoBehaviour
    {
        private GameObject _canvas;
        private UnityEngine.UI.Button _noButton;
        private GameObject _previousSelection;
        private Barracks _owner;
        private BarracksFlagPlacement _placement;
        private GameObject _interactor;

        public bool IsOpen { get; private set; }

        public void Show(Barracks owner, BarracksFlagPlacement placement, GameObject interactor)
        {
            if (IsOpen || !isActiveAndEnabled || owner == null || placement == null || interactor == null)
                return;

            if (_canvas == null) BuildUi();
            _owner = owner;
            _placement = placement;
            _interactor = interactor;
            IsOpen = true;
            GameFreeze.RequestPause(this);
            GameFreeze.RequestCursorUnlock(this);
            _canvas.SetActive(true);
            if (EventSystem.current != null)
            {
                _previousSelection = EventSystem.current.currentSelectedGameObject;
                EventSystem.current.SetSelectedGameObject(_noButton.gameObject);
            }
        }

        public void Confirm()
        {
            if (!IsOpen) return;
            var owner = _owner;
            var placement = _placement;
            var interactor = _interactor;
            Cancel();
            if (owner != null && owner.isActiveAndEnabled && placement != null && placement.isActiveAndEnabled && interactor != null)
                placement.BeginPlacement(interactor);
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_canvas != null) _canvas.SetActive(false);
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_previousSelection);
            _previousSelection = null;
            _owner = null;
            _placement = null;
            _interactor = null;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_owner == null || !_owner.isActiveAndEnabled || _interactor == null || Input.GetKeyDown(KeyCode.Escape))
                Cancel();
        }

        private void OnDisable() => Cancel();

        private void OnDestroy()
        {
            Cancel();
            if (_canvas != null) Destroy(_canvas);
        }

        private void BuildUi()
        {
            _canvas = new GameObject("BarracksConfirmationCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var dim = NewImage("Backdrop", _canvas.transform, Dim);
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var card = NewImage("Confirmation", dim.transform, Card, 20);
            Anchor(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580, 260));
            var title = NewText("Question", card.transform, "Déplacer les troupes ?", 34, TextMain, TextAlignmentOptions.Center, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 65), new Vector2(520, 60));
            var help = NewText("Description", card.transform, "Choisir une nouvelle position de rassemblement.", 21, TextMuted, TextAlignmentOptions.Center);
            Anchor(help.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(520, 50));
            var row = NewRect("Choices", card.transform);
            Anchor(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(480, 60));
            var layout = HStack(row.gameObject, 20);
            layout.childForceExpandWidth = true;
            var yes = NewButton(row, "Oui", Confirm);
            yes.name = "YesButton";
            var no = NewButton(row, "Non", Cancel);
            no.name = "NoButton";
            _noButton = no.GetComponent<UnityEngine.UI.Button>();
            _canvas.SetActive(false);
        }
    }
}
