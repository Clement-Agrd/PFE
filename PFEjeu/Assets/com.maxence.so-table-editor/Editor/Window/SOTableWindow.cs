using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Maxence.SOTable.Editor
{
    /// <summary>
    /// Editor window hosting the table. Owns the model and the view, draws the toolbar
    /// and the status bar, and reloads when the project changes.
    /// Rebuilds are deferred to the next Layout event to keep IMGUI layout consistent.
    /// Menu entries are in <see cref="SOTableMenuItems"/>.
    /// </summary>
    public sealed class SOTableWindow : EditorWindow
    {
        #region Constants

        private const float TypeButtonMinWidth = 160f;
        private const float TypeButtonMaxWidth = 260f;
        private const float SearchFieldMinWidth = 120f;
        private const float SearchFieldMaxWidth = 320f;
        private const string CsvExtension = "csv";
        private static readonly Vector2 WindowMinSize = new Vector2(420f, 220f);

        #endregion

        #region Serialized State (survives domain reloads)

        [SerializeField] private string _selectedTypeName;
        [SerializeField] private string _searchQuery = string.Empty;
        [SerializeField] private bool _includeDerivedTypes = true;
        [SerializeField] private MultiColumnHeaderState _headerState;
        [SerializeField] private string _headerStateTypeName;
        [SerializeField] private AdvancedDropdownState _typeDropdownState;

        #endregion

        #region Runtime State

        private TableModel _model;
        private TableView _view;
        private SearchField _searchField;
        private Type _selectedType;
        private GUIContent _typeButtonContent;
        private string _emptyMessage = string.Empty;

        private bool _rebuildRequested;
        private bool _rebuildKeepsViewState;

        private readonly List<int> _exportColumns = new List<int>();
        private readonly HashSet<Object> _selectionToRestore = new HashSet<Object>();

        private string _statusText = string.Empty;
        private int _statusVisibleCount = -1;
        private int _statusTotalCount = -1;
        private int _statusSelectedCount = -1;

        #endregion

        #region Public API

        public static void Open()
        {
            GetWindow<SOTableWindow>(SOTableStyles.WindowTitle);
        }

        /// <summary>Opens the window directly on <paramref name="type"/>.</summary>
        public static void OpenForType(Type type)
        {
            if (!ScriptableObjectAssetDatabase.IsEditableType(type))
            {
                Debug.LogWarning($"[SO Table Editor] {type} cannot be displayed (abstract, generic or built-in type).");
                return;
            }

            SOTableWindow window = GetWindow<SOTableWindow>(SOTableStyles.WindowTitle);
            window.SetSelectedType(type);
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            titleContent = new GUIContent(SOTableStyles.WindowTitle);
            minSize = WindowMinSize;

            _searchField = new SearchField();
            _typeDropdownState ??= new AdvancedDropdownState();

            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += Repaint;

            // After a domain reload (script change), restore the previous type and rebuild everything.
            // Deferred to the first Layout event: the AssetDatabase may still be importing during OnEnable.
            _selectedType = ResolveType(_selectedTypeName);
            RefreshTypeButtonContent();
            RequestRebuild(false);
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            Undo.undoRedoPerformed -= Repaint;

            DetachView();
            _model?.Dispose();
            _model = null;
        }

        private void OnProjectChanged()
        {
            // Assets created, deleted, renamed or saved: reload, but keep columns and selection.
            RequestRebuild(true);
        }

        #endregion

        #region Table Lifecycle

        private void SetSelectedType(Type type)
        {
            _selectedType = type;
            _selectedTypeName = type != null ? type.AssemblyQualifiedName : null;
            RefreshTypeButtonContent();
            RequestRebuild(false);
        }

        private void RequestRebuild(bool keepViewState)
        {
            // Any request that resets the view wins over requests that keep it.
            _rebuildKeepsViewState = _rebuildRequested ? _rebuildKeepsViewState && keepViewState : keepViewState;
            _rebuildRequested = true;
            Repaint();
        }

        private void ProcessPendingRebuild()
        {
            if (!_rebuildRequested || Event.current.type != EventType.Layout)
            {
                return;
            }

            _rebuildRequested = false;
            RebuildTable(_rebuildKeepsViewState);
        }

        private void RebuildTable(bool keepViewState)
        {
            if (_selectedType == null)
            {
                DetachView();
                _model?.Dispose();
                _model = null;
                return;
            }

            _selectionToRestore.Clear();
            if (keepViewState && _view != null)
            {
                _selectionToRestore.UnionWith(_view.RowSelection.GetSelectedAssets());
            }

            if (_model == null)
            {
                _model = new TableModel();
                _model.SetFilter(_searchQuery);
            }

            _model.Load(_selectedType, _includeDerivedTypes);

            bool columnsChanged = _view == null || _view.HeaderColumnCount != _model.Columns.Count + 1;
            if (!keepViewState || columnsChanged)
            {
                CreateView();
            }

            _view.RowSelection.SelectAssets(_selectionToRestore);
            _emptyMessage = string.Format(SOTableStyles.NoAssetFoundFormat, _selectedType.Name);
            Repaint();
        }

        private void CreateView()
        {
            MultiColumnHeaderState newState = TableView.CreateHeaderState(_model.Columns);

            // Keep widths, visibility and sort of the same type (e.g. after a domain reload).
            bool isSameType = _headerStateTypeName == _selectedTypeName;
            if (isSameType && _headerState != null && MultiColumnHeaderState.CanOverwriteSerializedFields(_headerState, newState))
            {
                MultiColumnHeaderState.OverwriteSerializedFields(_headerState, newState);
            }

            _headerState = newState;
            _headerStateTypeName = _selectedTypeName;

            DetachView();
            _view = new TableView(_model, _headerState);
            _view.SortChanged += OnSortChanged;
            _view.RowSelection.Changed += Repaint;

            if (_view.TryGetSort(out int dataColumnIndex, out bool ascending))
            {
                _model.SetSort(dataColumnIndex, ascending);
            }
            else
            {
                _model.ClearSort();
            }
        }

        private void DetachView()
        {
            if (_view == null)
            {
                return;
            }

            _view.SortChanged -= OnSortChanged;
            _view.RowSelection.Changed -= Repaint;
            _view = null;
        }

        private void OnSortChanged(int dataColumnIndex, bool ascending)
        {
            _model.SetSort(dataColumnIndex, ascending);
            Repaint();
        }

        private static Type ResolveType(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
            {
                return null;
            }

            Type type = Type.GetType(assemblyQualifiedName);
            return ScriptableObjectAssetDatabase.IsEditableType(type) ? type : null;
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            ProcessPendingRebuild();
            DrawToolbar();

            if (_selectedType == null || _model == null || _view == null)
            {
                EditorGUILayout.HelpBox(SOTableStyles.NoTypeSelectedMessage, MessageType.Info);
                return;
            }

            if (_model.AllRows.Count == 0)
            {
                EditorGUILayout.HelpBox(_emptyMessage, MessageType.Info);
                return;
            }

            Rect tableRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _view.OnGUI(tableRect);

            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawTypeSelector();

                using (new EditorGUI.DisabledScope(_selectedType == null))
                {
                    bool includeDerived = GUILayout.Toggle(_includeDerivedTypes, SOTableStyles.IncludeDerivedContent, EditorStyles.toolbarButton);
                    if (includeDerived != _includeDerivedTypes)
                    {
                        _includeDerivedTypes = includeDerived;
                        RequestRebuild(true);
                    }

                    if (GUILayout.Button(SOTableStyles.CreateAssetContent, EditorStyles.toolbarButton))
                    {
                        CreateAssetForSelectedType();
                    }

                    if (GUILayout.Button(SOTableStyles.RefreshContent, EditorStyles.toolbarButton))
                    {
                        RequestRebuild(true);
                    }

                    if (GUILayout.Button(SOTableStyles.ExportCsvContent, EditorStyles.toolbarButton))
                    {
                        ExportCsv();
                    }
                }

                GUILayout.FlexibleSpace();
                DrawSearchField();
            }
        }

        private void DrawTypeSelector()
        {
            Rect buttonRect = GUILayoutUtility.GetRect(
                _typeButtonContent,
                EditorStyles.toolbarDropDown,
                GUILayout.MinWidth(TypeButtonMinWidth),
                GUILayout.MaxWidth(TypeButtonMaxWidth));

            if (EditorGUI.DropdownButton(buttonRect, _typeButtonContent, FocusType.Passive, EditorStyles.toolbarDropDown))
            {
                List<Type> types = ScriptableObjectAssetDatabase.FindTypesWithAssets();
                var dropdown = new ScriptableObjectTypeDropdown(_typeDropdownState, types, SetSelectedType);
                dropdown.Show(buttonRect);
            }
        }

        private void DrawSearchField()
        {
            string newQuery = _searchField.OnToolbarGUI(
                _searchQuery,
                GUILayout.MinWidth(SearchFieldMinWidth),
                GUILayout.MaxWidth(SearchFieldMaxWidth));

            if (newQuery == _searchQuery)
            {
                return;
            }

            _searchQuery = newQuery;
            if (_model != null && _view != null)
            {
                _model.SetFilter(_searchQuery);
                _view.RowSelection.RemoveHiddenRows();
            }
        }

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(GetStatusText(), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(SOTableStyles.HelpHintContent, EditorStyles.miniLabel);
            }
        }

        /// <summary>Rebuilds the status string only when a count changes (no allocation per frame).</summary>
        private string GetStatusText()
        {
            int visibleCount = _model.VisibleRows.Count;
            int totalCount = _model.AllRows.Count;
            int selectedCount = _view.RowSelection.Count;

            if (visibleCount != _statusVisibleCount || totalCount != _statusTotalCount || selectedCount != _statusSelectedCount)
            {
                _statusVisibleCount = visibleCount;
                _statusTotalCount = totalCount;
                _statusSelectedCount = selectedCount;
                _statusText = string.Format(SOTableStyles.StatusFormat, visibleCount, totalCount, selectedCount);
            }

            return _statusText;
        }

        private void RefreshTypeButtonContent()
        {
            _typeButtonContent = _selectedType != null
                ? new GUIContent(_selectedType.Name, _selectedType.FullName)
                : SOTableStyles.SelectTypeContent;
        }

        #endregion

        #region Toolbar Actions

        private void CreateAssetForSelectedType()
        {
            string folder = _model != null && _model.AllRows.Count > 0
                ? RowActions.GetAssetFolder(_model.AllRows[0].Asset)
                : null;

            // The table reloads through EditorApplication.projectChanged.
            RowActions.CreateAsset(_selectedType, folder);
        }

        private void ExportCsv()
        {
            if (_model == null || _view == null)
            {
                return;
            }

            string path = EditorUtility.SaveFilePanel(SOTableStyles.ExportDialogTitle, string.Empty, _selectedType.Name + "." + CsvExtension, CsvExtension);
            if (!string.IsNullOrEmpty(path))
            {
                _view.GetVisibleDataColumns(_exportColumns);
                CsvExporter.Export(path, _model, _exportColumns);
                EditorUtility.RevealInFinder(path);
            }

            // A modal dialog breaks the current IMGUI layout pass: exit it cleanly.
            GUIUtility.ExitGUI();
        }

        #endregion
    }
}
