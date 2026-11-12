using Core.InteractionSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static Core.PauseMenu.PauseUiKit;

namespace Core.TowerDefense
{
    public abstract class TrainingPanel : InteractableBase
    {
        private GameObject _canvas;

        public override bool CanInteract(GameObject interactor)
            => interactor != null && base.CanInteract(interactor) && !GameFreeze.IsPaused;

        public override void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return;
            _canvas = new GameObject(GetType().Name + "Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 570;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var backdrop = NewImage("Backdrop", _canvas.transform, Dim);
            Stretch(backdrop.rectTransform);
            var card = NewImage("Panel", backdrop.transform, Card, 16);
            Anchor(card.rectTransform, Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(690, 835));
            VStack(card.gameObject, 11, new RectOffset(26, 26, 25, 25));
            BuildPanel(card.transform, interactor);
            Layout(NewButton(card.transform, "Fermer", Close).gameObject, height: 55);
            GameFreeze.RequestPause(this);
            GameFreeze.RequestCursorUnlock(this);
        }

        protected abstract void BuildPanel(Transform card, GameObject actor);

        protected void Close()
        {
            if (_canvas != null)
            {
                _canvas.SetActive(false);
                Destroy(_canvas);
                _canvas = null;
            }
            GameFreeze.ReleasePause(this);
            GameFreeze.ReleaseCursorUnlock(this);
        }

        protected virtual void Update()
        {
            if (_canvas != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Close();
        }

        protected virtual void OnDisable() => Close();
    }
}
