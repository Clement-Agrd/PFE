using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Core
{
    /// <summary>Keyboard/controller focus, modal ownership and scrolling for all runtime uGUI screens.</summary>
    [DefaultExecutionOrder(-2000)]
    public sealed class UiNavigation : MonoBehaviour
    {
        private static UiNavigation _instance;
        private static int _cancelFrame = -1, _usedFrame = -1;
        private InputAction _move, _submit, _cancel;
        private EventSystem _events;
        private UiNavigationScope _scope;
        private readonly List<Selectable> _controls = new();
        private readonly Dictionary<UiNavigationScope, FocusMemory> _memory = new();
        private Selectable[] _buffer = new Selectable[128];
        private readonly Vector3[] _corners = new Vector3[4];
        private Transform _controlRoot;
        private float _refreshAt, _repeatAt;
        private Vector2 _lastMove;
        private Canvas _focusCanvas;
        private RectTransform _frame;
        private UnityEngine.UI.Image[] _edges;
        private bool _mainMenu;
        private sealed class FocusMemory { public GameObject Target; public string Name; public bool Requested; }

        public static bool CancelConsumedThisFrame => _cancelFrame == Time.frameCount;
        public static bool HasUiFocus => TopScope() != null;
        public static bool HasModalFocus => TopScope() is { Modal: true, Automatic: false };
        public static bool BlocksGameplayInput => HasUiFocus || _usedFrame == Time.frameCount;
        public static Transform CurrentScopeRoot => TopScope()?.transform;
        public static void ConsumeCancel() { _cancelFrame = _usedFrame = Time.frameCount; }
        public static bool IsTopScopeOwner(Component owner) => owner == null || TopScope()?.Owner == owner;
        public static void Refresh() { if (_instance != null) _instance._refreshAt = 0; }

        public static void RememberSelection(GameObject target)
        {
            if (_instance == null || target == null) return;
            var scope = target.GetComponentInParent<UiNavigationScope>();
            if (scope == null) return;
            _instance._memory[scope] = new FocusMemory { Target = target, Name = target.name, Requested = true };
            if (TopScope() == scope && _instance._events != null)
                _instance._events.SetSelectedGameObject(target);
            Refresh();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var root = new GameObject("UI Keyboard and Controller Navigation");
            DontDestroyOnLoad(root); root.AddComponent<UiNavigation>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this; _cancelFrame = _usedFrame = -1;
            _move = new InputAction("Navigate UI", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/z").With("Up", "<Keyboard>/w").With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/s").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/q").With("Left", "<Keyboard>/a").With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/d").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick"); _move.AddBinding("<Gamepad>/dpad");
            _submit = new InputAction("Validate UI", InputActionType.Button);
            foreach (var binding in new[] { "<Keyboard>/space", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Gamepad>/buttonSouth" }) _submit.AddBinding(binding);
            _cancel = new InputAction("Back UI", InputActionType.Button);
            _cancel.AddBinding("<Keyboard>/escape"); _cancel.AddBinding("<Gamepad>/buttonEast");
            _move.Enable(); _submit.Enable(); _cancel.Enable();
            SceneManager.sceneLoaded += SceneLoaded;
            _mainMenu = FindAnyObjectByType<Core.MainMenu.MainMenuController>() != null;
        }

        private void SceneLoaded(Scene _, LoadSceneMode mode)
        {
            _events = null; _scope = null; _controlRoot = null; _controls.Clear(); _memory.Clear();
            _mainMenu = FindAnyObjectByType<Core.MainMenu.MainMenuController>() != null; _refreshAt = 0;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            _move?.Dispose(); _submit?.Dispose(); _cancel?.Dispose();
            if (_instance == this) _instance = null;
        }

        private static UiNavigationScope TopScope()
        {
            UiNavigationScope top = null;
            foreach (var scope in UiNavigationScope.Scopes)
            {
                if (scope == null || !scope.Available) continue;
                if (top == null || (top.Automatic && !scope.Automatic) || (scope.Automatic == top.Automatic && ((scope.Modal && !top.Modal) || (scope.Modal == top.Modal && scope.Order > top.Order)))) top = scope;
            }
            return top;
        }

        private void Update()
        {
            ResolveEventSystem();
            if (_events == null) return;
            var next = TopScope();
            if (next == null && (GameFreeze.IsCursorUnlocked || _mainMenu)) next = RegisterLegacyCanvas();
            if (next != _scope)
            {
                Remember(); _scope = next; _controlRoot = null; _refreshAt = 0; _lastMove = Vector2.zero;
            }
            if (_scope == null) { _frame?.gameObject.SetActive(false); return; }
            if (Time.unscaledTime >= _refreshAt || !ValidSelected()) RefreshControls();
            var selected = _events.currentSelectedGameObject;
            if (_cancel.WasPressedThisFrame() && Time.frameCount != _scope.OpenedFrame)
            {
                ConsumeCancel();
                var data = new BaseEventData(_events);
                if (selected != null) ExecuteEvents.ExecuteHierarchy(selected, data, ExecuteEvents.cancelHandler);
                if (!data.used) _scope.Cancel?.Invoke();
                Refresh(); return;
            }
            if (_controls.Count == 0) return;
            if (selected != null && selected.TryGetComponent<TMPro.TMP_InputField>(out var field) && field.isFocused) return;
            Vector2 axis = _move.ReadValue<Vector2>();
            if (axis.sqrMagnitude < .36f) _lastMove = Vector2.zero;
            else
            {
                Vector2 direction = Mathf.Abs(axis.x) > Mathf.Abs(axis.y)
                    ? new Vector2(Mathf.Sign(axis.x), 0) : new Vector2(0, Mathf.Sign(axis.y));
                if (direction != _lastMove || Time.unscaledTime >= _repeatAt)
                {
                    bool first = direction != _lastMove; _lastMove = direction;
                    _repeatAt = Time.unscaledTime + (first ? .32f : .11f); _usedFrame = Time.frameCount;
                    Move(direction);
                }
            }
            if (_submit.WasPressedThisFrame() && Time.frameCount != _scope.OpenedFrame)
            {
                _usedFrame = Time.frameCount;
                selected = _events.currentSelectedGameObject;
                if (selected != null) ExecuteEvents.Execute(selected, new BaseEventData(_events), ExecuteEvents.submitHandler);
                Refresh();
            }
            Remember();
        }

        private void ResolveEventSystem()
        {
            if (EventSystem.current == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                go.AddComponent<InputSystemUIInputModule>();
            }
            _events = EventSystem.current;
            // Keep pointer/touch processing; navigation is dispatched once by this controller.
            if (_events != null) _events.sendNavigationEvents = false;
        }

        private UiNavigationScope RegisterLegacyCanvas()
        {
            int count = GetSelectables(); Canvas best = null;
            for (int i = 0; i < count; i++)
            {
                var selectable = _buffer[i]; if (!Usable(selectable)) continue;
                var canvas = selectable.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.WorldSpace && (best == null || canvas.sortingOrder > best.sortingOrder)) best = canvas;
            }
            if (best == null) return null;
            var scope = UiNavigationScope.Register(best.transform, BackFromLegacyCanvas, null,
                () => GameFreeze.IsCursorUnlocked || _mainMenu, false);
            scope.Automatic = true;
            return scope;
        }

        private void BackFromLegacyCanvas()
        {
            foreach (var control in _controls)
            {
                if (control is not UnityEngine.UI.Button button) continue;
                var text = button.GetComponentInChildren<TMPro.TMP_Text>();
                if (text == null) continue;
                string label = text.text.Trim();
                if (label == "Fermer" || label == "Retour" || label == "Annuler" || label == "Reprendre") { button.onClick.Invoke(); return; }
            }
        }

        private int GetSelectables()
        {
            if (_buffer.Length < Selectable.allSelectableCount) _buffer = new Selectable[Mathf.NextPowerOfTwo(Selectable.allSelectableCount)];
            return Selectable.AllSelectablesNoAlloc(_buffer);
        }

        private static bool Usable(Selectable control)
        {
            if (control == null || !control.IsActive() || !control.IsInteractable()) return false;
            for (Transform t = control.transform; t != null; t = t.parent)
            {
                if (!t.TryGetComponent<CanvasGroup>(out var group)) continue;
                if (group.alpha < .02f || !group.interactable) return false;
                if (group.ignoreParentGroups) break;
            }
            var rect = control.transform as RectTransform;
            return rect != null && rect.rect.width > .1f && rect.rect.height > .1f;
        }

        private bool ValidSelected()
        {
            var selected = _events.currentSelectedGameObject;
            return selected != null && selected.transform.IsChildOf(_scope.transform) &&
                selected.TryGetComponent<Selectable>(out var selectable) && Usable(selectable) &&
                (_controlRoot == null || selected.transform.IsChildOf(_controlRoot));
        }

        private void RefreshControls()
        {
            _refreshAt = Time.unscaledTime + .1f; _controls.Clear();
            _controlRoot = _scope.transform;
            // TMP dropdowns create a higher-order nested canvas: contain focus inside it.
            var canvases = _scope.GetComponentsInChildren<Canvas>(); Canvas popup = null;
            var ownCanvas = _scope.GetComponentInParent<Canvas>();
            foreach (var canvas in canvases)
                if (canvas.overrideSorting && canvas != ownCanvas && canvas.sortingOrder > (popup != null ? popup.sortingOrder : ownCanvas != null ? ownCanvas.sortingOrder : 0)) popup = canvas;
            if (popup != null) _controlRoot = popup.transform;
            int count = GetSelectables();
            for (int i = 0; i < count; i++)
            {
                var control = _buffer[i];
                if (!Usable(control) || !control.transform.IsChildOf(_controlRoot)) continue;
                if (control.navigation.mode == Navigation.Mode.None) { var nav = control.navigation; nav.mode = Navigation.Mode.Automatic; control.navigation = nav; }
                _controls.Add(control);
            }
            _controls.Sort((a, b) => CompareHierarchy(a.transform, b.transform));
            if ((!ValidSelected() || (_memory.TryGetValue(_scope, out var request) && request.Requested)) && _controls.Count > 0)
            {
                Selectable choice = null;
                if (_memory.TryGetValue(_scope, out var memory))
                    foreach (var control in _controls)
                        if (control.gameObject == memory.Target || control.name == memory.Name) { choice = control; break; }
                if (choice != null && memory != null) memory.Requested = false;
                Select(choice != null ? choice : _controls[0]);
            }
        }

        private static int CompareHierarchy(Transform a, Transform b)
        {
            if (a == b) return 0;
            if (a.parent == b.parent) return a.GetSiblingIndex().CompareTo(b.GetSiblingIndex());
            int da = 0, db = 0; for (var t = a; t != null; t = t.parent) da++; for (var t = b; t != null; t = t.parent) db++;
            var x = a; var y = b;
            while (da > db) { x = x.parent; da--; if (x == b) return 1; }
            while (db > da) { y = y.parent; db--; if (y == a) return -1; }
            while (x.parent != y.parent) { x = x.parent; y = y.parent; }
            return x.GetSiblingIndex().CompareTo(y.GetSiblingIndex());
        }

        private void Remember()
        {
            if (_scope == null || _events == null || _events.currentSelectedGameObject == null) return;
            var target = _events.currentSelectedGameObject;
            if (!target.transform.IsChildOf(_scope.transform)) return;
            if (!_memory.TryGetValue(_scope, out var memory)) _memory[_scope] = memory = new FocusMemory();
            if (memory.Requested) return;
            memory.Target = target; memory.Name = target.name;
        }

        private void Select(Selectable control)
        {
            _events.SetSelectedGameObject(control.gameObject);
            ScrollTo(control.transform as RectTransform); Remember();
        }

        private void Move(Vector2 direction)
        {
            if (!ValidSelected()) { RefreshControls(); return; }
            var current = _events.currentSelectedGameObject.GetComponent<Selectable>();
            bool sliderAxis = current is UnityEngine.UI.Slider slider &&
                ((slider.direction == UnityEngine.UI.Slider.Direction.LeftToRight || slider.direction == UnityEngine.UI.Slider.Direction.RightToLeft) == (direction.x != 0));
            bool scrollAxis = current is Scrollbar bar && ((bar.direction == Scrollbar.Direction.LeftToRight || bar.direction == Scrollbar.Direction.RightToLeft) == (direction.x != 0));
            if (sliderAxis || scrollAxis)
            {
                var data = new AxisEventData(_events) { moveVector = direction, moveDir = Direction(direction) };
                ExecuteEvents.Execute(current.gameObject, data, ExecuteEvents.moveHandler); return;
            }
            Vector2 origin = Center(current.transform as RectTransform); Selectable best = null; float bestScore = 0;
            foreach (var candidate in _controls)
            {
                if (candidate == current || !Usable(candidate)) continue;
                Rect candidateRect = ScreenRect(candidate.transform as RectTransform);
                Vector2 centerDelta = candidateRect.center - origin;
                if (Vector2.Dot(direction, centerDelta) <= .5f) continue;
                Vector2 delta = new Vector2(Mathf.Clamp(origin.x,candidateRect.xMin,candidateRect.xMax),Mathf.Clamp(origin.y,candidateRect.yMin,candidateRect.yMax)) - origin;
                if (delta.sqrMagnitude < .01f) delta = centerDelta;
                float dot = Vector2.Dot(direction, delta); if (dot <= .5f) continue;
                float perpendicular = direction.x == 0 ? Mathf.Abs(delta.x) : Mathf.Abs(delta.y);
                float score = dot / (delta.sqrMagnitude + perpendicular * perpendicular * 2f);
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            if (best != null) Select(best);
        }

        private static MoveDirection Direction(Vector2 direction) => direction.x < 0 ? MoveDirection.Left : direction.x > 0 ? MoveDirection.Right : direction.y > 0 ? MoveDirection.Up : MoveDirection.Down;
        private static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(CameraFor(rect), rect.TransformPoint(rect.rect.center));
        private static Camera CameraFor(Transform t) { var canvas = t.GetComponentInParent<Canvas>(); return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null; }

        private Rect ScreenRect(RectTransform rect)
        {
            rect.GetWorldCorners(_corners);var camera=CameraFor(rect);
            Vector2 min=new(float.PositiveInfinity,float.PositiveInfinity),max=new(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var corner in _corners){var p=RectTransformUtility.WorldToScreenPoint(camera,corner);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }

        private static void ScrollTo(RectTransform selected)
        {
            Canvas.ForceUpdateCanvases();
            for (Transform t = selected.parent; t != null; t = t.parent)
            {
                if (!t.TryGetComponent<ScrollRect>(out var scroll) || scroll.content == null || !selected.IsChildOf(scroll.content)) continue;
                var viewport = scroll.viewport != null ? scroll.viewport : t as RectTransform;
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, selected);
                Vector2 offset = Vector2.zero;
                if (scroll.vertical) offset.y = bounds.min.y < viewport.rect.yMin ? viewport.rect.yMin - bounds.min.y : bounds.max.y > viewport.rect.yMax ? viewport.rect.yMax - bounds.max.y : 0;
                if (scroll.horizontal) offset.x = bounds.min.x < viewport.rect.xMin ? viewport.rect.xMin - bounds.min.x : bounds.max.x > viewport.rect.xMax ? viewport.rect.xMax - bounds.max.x : 0;
                scroll.StopMovement(); scroll.content.anchoredPosition += offset;
            }
        }

        private void LateUpdate()
        {
            if (_scope == null || _events == null || !ValidSelected()) { _frame?.gameObject.SetActive(false); return; }
            if (_frame == null) BuildFocusFrame();
            var selected = _events.currentSelectedGameObject.transform as RectTransform;
            selected.GetWorldCorners(_corners); Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = new(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in _corners) { Vector2 p = RectTransformUtility.WorldToScreenPoint(CameraFor(selected), corner); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            _frame.anchoredPosition = min - Vector2.one * 2; _frame.sizeDelta = max - min + Vector2.one * 4;
            _frame.gameObject.SetActive(true);
        }

        private void BuildFocusFrame()
        {
            var go = new GameObject("Keyboard and Controller Focus", typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(transform, false);
            _focusCanvas = go.GetComponent<Canvas>(); _focusCanvas.renderMode = RenderMode.ScreenSpaceOverlay; _focusCanvas.sortingOrder = 32760;
            var frame = new GameObject("Focus", typeof(RectTransform)); frame.transform.SetParent(go.transform, false); _frame = frame.GetComponent<RectTransform>();
            _frame.anchorMin = _frame.anchorMax = _frame.pivot = Vector2.zero;
            _edges = new UnityEngine.UI.Image[4];
            for (int i = 0; i < 4; i++)
            {
                var edge = new GameObject("Edge" + i, typeof(RectTransform), typeof(UnityEngine.UI.Image)); edge.transform.SetParent(_frame, false);
                var image = edge.GetComponent<UnityEngine.UI.Image>(); image.color = new Color(1f, .81f, .32f, .95f); image.raycastTarget = false; _edges[i] = image;
                var rt = image.rectTransform;
                rt.anchorMin = i == 0 ? Vector2.zero : i == 1 ? Vector2.up : i == 2 ? Vector2.zero : Vector2.right;
                rt.anchorMax = i == 0 ? Vector2.right : i == 1 ? Vector2.one : i == 2 ? Vector2.up : Vector2.one;
                rt.pivot = i == 0 ? new Vector2(.5f,0) : i == 1 ? new Vector2(.5f,1) : i == 2 ? new Vector2(0,.5f) : new Vector2(1,.5f);
                rt.anchoredPosition = Vector2.zero; rt.sizeDelta = i < 2 ? new Vector2(0,3) : new Vector2(3,0);
            }
        }
    }
}
