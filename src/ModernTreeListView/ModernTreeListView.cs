using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ModernTreeListView;

/// <summary>
/// A modern, high-quality hybrid Tree + ListView control for WinForms (.NET 9+).
/// Supports hierarchical data, columns, lazy loading for large datasets, excellent in-place editing,
/// clickable column sorting, multi-selection, checkboxes, filtering, type-ahead search, per-node icons,
/// tree connector lines, hover highlighting, and light/dark themes.
/// </summary>
/// <typeparam name="TModel">The type of the data model for each node. For best results with immutable records,
/// prefer using <see cref="ReplaceModel"/> after creating an updated instance (see remarks on SetCellValueSetter).</typeparam>
public sealed class ModernTreeListView<TModel> : Control where TModel : notnull
{
    // Layout constants - modern defaults
    private const int DefaultRowHeight = 26;
    private const int DefaultHeaderHeight = 32;
    private const int IndentSize = 18;
    private const int ExpanderSize = 11;
    private const int CheckBoxSize = 14;
    private const int IconSize = 16;
    private const int CellPadding = 6;
    private const int ExpanderMargin = 3;
    private const int ResizeGripWidth = 6;

    // State
    private readonly List<TreeListColumn<TModel>> _columns = [];
    private readonly List<TreeNode> _rootNodes = [];
    private readonly List<VisibleRow> _visibleRows = [];

    private Func<TModel, IEnumerable<TModel>>? _childrenGetter;
    private Func<TModel, bool>? _hasChildrenGetter;
    private Action<TModel, TreeListColumn<TModel>, object?>? _setCellValue;
    private Func<TModel, Image?>? _iconGetter;

    // Selection state (multi-select aware; _selectedNode is the focused node)
    private readonly HashSet<TreeNode> _selectedNodes = [];
    private TreeNode? _selectedNode;
    private int _selectedIndex = -1;
    private int _anchorIndex = -1;
    private TreeNode? _anchorNode;
    private bool _multiSelect = true;

    private int _rowHeight = DefaultRowHeight;
    private int _headerHeight = DefaultHeaderHeight;

    private int _vOffset;
    private int _hOffset;

    private VScrollBar _vScrollBar = null!;
    private HScrollBar _hScrollBar = null!;

    // Hover / tooltip state
    private int _hoverRowIndex = -1;
    private ToolTip _toolTip = null!;
    private string _currentToolTipText = string.Empty;

    // Column resizing state
    private int _resizingColumnIndex = -1;
    private int _resizeStartX;
    private int _resizeStartWidth;

    // Hint for which column to start editing on F2/Enter (remembers last interacted column)
    private int _currentEditColumnHint;

    // Sorting state
    private int _sortColumnIndex = -1;
    private SortOrder _sortOrder = SortOrder.None;

    // Filtering state
    private Func<TModel, bool>? _filter;
    private Dictionary<TreeNode, bool>? _filterMatch;

    // Type-ahead search state
    private string _typeAheadPrefix = string.Empty;
    private long _typeAheadLastTick;
    private const int TypeAheadResetMs = 1000;

    // Editing state - excellent in-place editing support
    private Control? _activeEditor;
    private int _editingRowIndex = -1;
    private int _editingColIndex = -1;
    private TreeNode? _editingNode;
    private object? _editingOriginalValue;
    private bool _inEndEdit;
    private bool _suppressFocusCommit; // true while a DateTimePicker dropdown is open
    private bool _handlingMouseDown;
    private bool _updatingScrollbars;

    // Display options
    private bool _showCheckBoxes;
    private bool _showTreeLines;
    private bool _autoFillLastColumn;

    // Theme
    private TreeListTheme _theme = null!;

    // Modern visual configuration (clean, minimal)
    // These are hidden from the WinForms designer because this is a generic control
    // primarily configured via code / fluent API. Change values programmatically
    // or use ApplyTheme() with a TreeListTheme preset.
    private Color _headerBackColor;
    private Color _headerForeColor;
    private Color _rowBackColor;
    private Color _alternatingRowBackColor;
    private Color _selectionBackColor;
    private Color _selectionForeColor;
    private Color _gridLineColor;
    private Color _expanderColor;
    private Color _treeLineColor;
    private Color _hoverBackColor;
    private Color _editorBackColor;
    private Color _editorForeColor;
    private Color _focusCueColor;
    private bool _showAlternatingRows = true;
    private bool _showGridLines;
    private bool _fullRowSelect = true;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HeaderBackColor { get => _headerBackColor; set => SetColor(ref _headerBackColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HeaderForeColor { get => _headerForeColor; set => SetColor(ref _headerForeColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color RowBackColor { get => _rowBackColor; set => SetColor(ref _rowBackColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AlternatingRowBackColor { get => _alternatingRowBackColor; set => SetColor(ref _alternatingRowBackColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SelectionBackColor { get => _selectionBackColor; set => SetColor(ref _selectionBackColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SelectionForeColor { get => _selectionForeColor; set => SetColor(ref _selectionForeColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GridLineColor { get => _gridLineColor; set => SetColor(ref _gridLineColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ExpanderColor { get => _expanderColor; set => SetColor(ref _expanderColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TreeLineColor { get => _treeLineColor; set => SetColor(ref _treeLineColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverBackColor { get => _hoverBackColor; set => SetColor(ref _hoverBackColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color EditorBackColor { get => _editorBackColor; set => SetColor(ref _editorBackColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color EditorForeColor { get => _editorForeColor; set => SetColor(ref _editorForeColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FocusCueColor { get => _focusCueColor; set => SetColor(ref _focusCueColor, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowAlternatingRows { get => _showAlternatingRows; set => SetVisualFlag(ref _showAlternatingRows, value); }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGridLines { get => _showGridLines; set => SetVisualFlag(ref _showGridLines, value); }

    /// <summary>
    /// When true (the default), the selection highlight covers the entire row.
    /// When false, only the tree column is highlighted.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool FullRowSelect { get => _fullRowSelect; set => SetVisualFlag(ref _fullRowSelect, value); }

    /// <summary>
    /// When true, draws connector lines between parent and child rows in the tree column.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowTreeLines
    {
        get => _showTreeLines;
        set { if (_showTreeLines == value) return; _showTreeLines = value; Invalidate(); }
    }

    /// <summary>
    /// When true, shows a checkbox for each row in the tree column.
    /// Use Space to toggle all selected rows, or click the checkbox directly.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowCheckBoxes
    {
        get => _showCheckBoxes;
        set { if (_showCheckBoxes == value) return; _showCheckBoxes = value; Invalidate(); }
    }

    /// <summary>
    /// When true, the last column stretches to fill any remaining client width
    /// (it never shrinks below its configured width).
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoFillLastColumn
    {
        get => _autoFillLastColumn;
        set { if (_autoFillLastColumn == value) return; _autoFillLastColumn = value; UpdateScrollbars(); Invalidate(); }
    }

    /// <summary>
    /// Enables multi-selection via Ctrl+Click (toggle), Shift+Click (range) and Shift+Arrow keys.
    /// When disabled, selection collapses to the focused row. Default: true.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool MultiSelect
    {
        get => _multiSelect;
        set
        {
            if (_multiSelect == value) return;
            _multiSelect = value;
            if (!value && _selectedNodes.Count > 1)
            {
                _selectedNodes.Clear();
                if (_selectedNode != null) _selectedNodes.Add(_selectedNode);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Gets or sets the active theme. Setting this applies all theme colors at once (same as <see cref="ApplyTheme"/>).
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TreeListTheme Theme
    {
        get => _theme;
        set => ApplyTheme(value);
    }

    // Public events
    public event EventHandler<CellEditEventArgs<TModel>>? CellEditCommitted;
    public event EventHandler<CellEditEventArgs<TModel>>? CellEditCanceled;
    public event EventHandler? SelectionChanged;
    public event EventHandler<TreeNodeEventArgs<TModel>>? NodeExpanded;
    public event EventHandler<TreeNodeEventArgs<TModel>>? NodeCollapsed;

    /// <summary>
    /// Raised when the user changes the sort column or direction (including clearing the sort).
    /// </summary>
    public event EventHandler? ColumnSortChanged;

    /// <summary>
    /// Raised whenever a node's checked state changes (checkbox click, Space key, or <see cref="SetChecked"/>).
    /// </summary>
    public event EventHandler<TreeNodeEventArgs<TModel>>? CheckedChanged;

    public ModernTreeListView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);

        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        TabStop = true;

        _toolTip = new ToolTip { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 8000 };

        ApplyTheme(TreeListTheme.Light);
        InitializeScrollBars();
    }

    private void InitializeScrollBars()
    {
        _vScrollBar = new VScrollBar
        {
            Visible = false,
            Width = SystemInformation.VerticalScrollBarWidth,
            Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
        };
        _hScrollBar = new HScrollBar
        {
            Visible = false,
            Height = SystemInformation.HorizontalScrollBarHeight,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        Controls.Add(_vScrollBar);
        Controls.Add(_hScrollBar);

        _vScrollBar.Scroll += OnVScroll;
        _hScrollBar.Scroll += OnHScroll;
    }

    private void OnVScroll(object? sender, ScrollEventArgs e)
    {
        // WinForms raises Scroll before it assigns ScrollBar.Value, so Value is still
        // the previous position here. NewValue is the position being applied.
        CommitEditIfActive();
        _vOffset = e.NewValue;
        _hoverRowIndex = -1;
        if (_activeEditor != null)
            PositionActiveEditor();
        Invalidate();
    }

    private void OnHScroll(object? sender, ScrollEventArgs e)
    {
        CommitEditIfActive();
        _hOffset = e.NewValue;
        if (_activeEditor != null)
            PositionActiveEditor();
        Invalidate();
    }

    private void SetColor(ref Color field, Color value)
    {
        if (field == value) return;
        field = value;
        Invalidate();
    }

    private void SetVisualFlag(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        Invalidate();
    }

    // ==================== FLUENT API ====================

    /// <summary>
    /// Adds a column with fluent configuration support.
    /// </summary>
    public ModernTreeListView<TModel> AddColumn(
        string title,
        Func<TModel, object?> getter,
        int width = 140,
        Action<TreeListColumn<TModel>>? configure = null)
    {
        var column = new TreeListColumn<TModel>(title, getter, width);
        configure?.Invoke(column);
        _columns.Add(column);
        UpdateScrollbars();
        Invalidate();
        return this;
    }

    /// <summary>
    /// Clears all columns.
    /// </summary>
    public ModernTreeListView<TModel> ClearColumns()
    {
        _columns.Clear();
        UpdateScrollbars();
        Invalidate();
        return this;
    }

    /// <summary>
    /// Sets the root models (top level nodes). Children are loaded on demand via the registered children getter.
    /// This is the primary entry point for populating the control.
    /// </summary>
    public ModernTreeListView<TModel> SetRoots(IEnumerable<TModel> roots)
    {
        LoadRoots(roots);
        return this;
    }

    /// <summary>
    /// Sets the delegate used to retrieve children for any model (enables lazy loading / virtual trees).
    /// </summary>
    public ModernTreeListView<TModel> SetChildrenGetter(Func<TModel, IEnumerable<TModel>> getter)
    {
        _childrenGetter = getter;
        // Note: does not auto-reload existing expanded nodes; call Rebuild() if needed
        return this;
    }

    /// <summary>
    /// Optional: provides a fast path to know whether a node has children without enumerating (large/virtual datasets).
    /// </summary>
    public ModernTreeListView<TModel> SetHasChildrenGetter(Func<TModel, bool> getter)
    {
        _hasChildrenGetter = getter;
        return this;
    }

    /// <summary>
    /// Optional: provides a 16x16 icon for each node, drawn in the tree column before the text.
    /// Return null for nodes without an icon. The control does not take ownership of the images.
    /// </summary>
    public ModernTreeListView<TModel> SetIconGetter(Func<TModel, Image?> getter)
    {
        _iconGetter = getter;
        Invalidate();
        return this;
    }

    /// <summary>
    /// Sets the action invoked when the user commits an in-place edit.
    /// The action is responsible for writing the value back to the model (or data source).
    /// </summary>
    /// <remarks>
    /// <para><b>Records / immutable models:</b> Because records are immutable, the setter cannot mutate the instance.
    /// Preferred pattern: create a new record (e.g. <c>model with { Name = (string)newValue }</c>),
    /// update your source collections so that future <see cref="Reload"/> / children getters see consistent data,
    /// then call <see cref="ReplaceModel"/> to swap the reference inside the control's tree nodes while preserving
    /// expand/selection state. See the demo for a complete example.</para>
    /// <para>For mutable POCOs you can mutate directly and call <see cref="RefreshObject"/> or <see cref="Rebuild"/> as needed.</para>
    /// </remarks>
    public ModernTreeListView<TModel> SetCellValueSetter(Action<TModel, TreeListColumn<TModel>, object?> setter)
    {
        _setCellValue = setter;
        return this;
    }

    /// <summary>
    /// Applies a row filter. A node remains visible when it matches the predicate or any of its descendants do;
    /// ancestors of matches are automatically expanded so matches become visible.
    /// </summary>
    /// <remarks>
    /// Filtering needs to evaluate descendants, so children are loaded on demand for the whole tree.
    /// For very large virtual trees consider filtering at the data source instead.
    /// </remarks>
    public ModernTreeListView<TModel> SetFilter(Func<TModel, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        CancelEdit();
        _filter = predicate;
        RecomputeFilterCache();
        AutoExpandFilterMatches();
        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
        return this;
    }

    /// <summary>
    /// Removes any active filter. The expansion state created by the filter is kept.
    /// </summary>
    public void ClearFilter()
    {
        if (_filter == null) return;

        CancelEdit();
        _filter = null;
        _filterMatch = null;
        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Rebuilds the visible row list from the current expanded state.
    /// Call this after you have mutated the underlying data (add/remove children, reordered items, etc.)
    /// while the control's nodes are already loaded. Does not reload children from getters.
    /// </summary>
    public void Rebuild()
    {
        CancelEdit();
        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Fully reloads the tree from the current root models using the registered children getter.
    /// All expanded state is lost; nodes will be re-expanded on demand when the user expands them again.
    /// Use when your root collection or children source has changed structurally.
    /// </summary>
    public void Reload()
    {
        CancelEdit();
        var currentRoots = _rootNodes.Select(n => n.Model).ToList();
        LoadRoots(currentRoots);
    }

    /// <summary>
    /// Replaces a model instance in the tree with a different instance (primary use case: immutable records).
    /// The node keeps its parent, children, expanded state and position in the tree. Selection is preserved.
    /// After calling this you typically also want to update the same logical item inside your own source collections
    /// so that <see cref="Reload"/> and future children enumeration remain consistent.
    /// </summary>
    /// <param name="oldModel">The model reference currently held by a tree node.</param>
    /// <param name="newModel">The new model instance that should take its place for display and future operations.</param>
    public void ReplaceModel(TModel oldModel, TModel newModel)
    {
        var node = FindNode(oldModel);
        if (node == null) return;

        node.ReplaceModelReference(newModel);

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Invalidates and redraws the row for the specified model if it is currently visible.
    /// Useful after external changes to a mutable model's display properties when you do not want a full <see cref="Rebuild"/>.
    /// For immutable records, prefer <see cref="ReplaceModel"/> followed by this (or just ReplaceModel).
    /// </summary>
    public void RefreshObject(TModel model)
    {
        var node = FindNode(model);
        if (node == null) return;

        int idx = _visibleRows.FindIndex(vr => vr.Node == node);
        if (idx >= 0)
        {
            InvalidateRow(idx);
        }
    }

    /// <summary>
    /// Sorts the tree by the specified column using the given direction.
    /// Sorting is applied to the siblings of each level (per-parent), is stable, and respects the current expanded state.
    /// </summary>
    /// <param name="columnIndex">The column to sort by.</param>
    /// <param name="order">The desired sort direction. Use <see cref="SortOrder.None"/> to clear sorting for this column.</param>
    public void Sort(int columnIndex, SortOrder order)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count || order == SortOrder.None)
        {
            ClearSortInternal();
            return;
        }

        _sortColumnIndex = columnIndex;
        _sortOrder = order;

        ColumnSortChanged?.Invoke(this, EventArgs.Empty);
        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Clears any active column sort and restores the original sibling insertion order.
    /// </summary>
    public void ClearSort()
    {
        ClearSortInternal();
    }

    private void ClearSortInternal()
    {
        if (_sortColumnIndex == -1 && _sortOrder == SortOrder.None) return;

        _sortColumnIndex = -1;
        _sortOrder = SortOrder.None;

        ColumnSortChanged?.Invoke(this, EventArgs.Empty);
        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Applies the given theme: copies all theme colors onto the control's individual color properties.
    /// Individual properties remain settable afterwards for fine-tuning.
    /// </summary>
    public void ApplyTheme(TreeListTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        _theme = theme;

        BackColor = theme.BackColor;
        ForeColor = theme.ForeColor;
        HeaderBackColor = theme.HeaderBackColor;
        HeaderForeColor = theme.HeaderForeColor;
        RowBackColor = theme.RowBackColor;
        AlternatingRowBackColor = theme.AlternatingRowBackColor;
        SelectionBackColor = theme.SelectionBackColor;
        SelectionForeColor = theme.SelectionForeColor;
        GridLineColor = theme.GridLineColor;
        ExpanderColor = theme.ExpanderColor;
        TreeLineColor = theme.TreeLineColor;
        HoverBackColor = theme.HoverBackColor;
        EditorBackColor = theme.EditorBackColor;
        EditorForeColor = theme.EditorForeColor;
        FocusCueColor = theme.FocusCueColor;

        if (_toolTip != null)
        {
            _toolTip.BackColor = theme.RowBackColor;
            _toolTip.ForeColor = theme.ForeColor;
        }

        Invalidate();
    }

    // ==================== PROPERTIES ====================

    public IReadOnlyList<TreeListColumn<TModel>> Columns => _columns;

    /// <summary>
    /// The model of the focused row, or default when nothing is selected.
    /// With multi-selection enabled, use <see cref="SelectedModels"/> for the full set.
    /// </summary>
    public TModel? SelectedModel => _selectedNode is { } n ? n.Model : default(TModel)!;

    /// <summary>
    /// All currently selected models, in visible row order.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<TModel> SelectedModels
    {
        get
        {
            if (_selectedNodes.Count == 0) return [];
            var list = new List<TModel>(_selectedNodes.Count);
            foreach (var vr in _visibleRows)
            {
                if (_selectedNodes.Contains(vr.Node))
                    list.Add(vr.Node.Model);
            }
            return list;
        }
    }

    /// <summary>
    /// All currently checked models (requires <see cref="ShowCheckBoxes"/> or programmatic <see cref="SetChecked"/> calls).
    /// Only loaded nodes are considered.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<TModel> CheckedModels
    {
        get
        {
            var list = new List<TModel>();
            void Walk(TreeNode n)
            {
                if (n.IsChecked) list.Add(n.Model);
                if (n.ChildrenLoaded)
                {
                    foreach (var c in n.Children) Walk(c);
                }
            }
            foreach (var root in _rootNodes) Walk(root);
            return list;
        }
    }

    public int SelectedRowIndex => _selectedIndex;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int RowHeight
    {
        get => _rowHeight;
        set
        {
            _rowHeight = Math.Max(16, value);
            UpdateScrollbars();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HeaderHeight
    {
        get => _headerHeight;
        set
        {
            _headerHeight = Math.Max(18, value);
            UpdateScrollbars();
            Invalidate();
        }
    }

    /// <summary>
    /// Gets the zero-based index of the column currently used for sorting, or null if no column sort is active.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? SortColumn => _sortColumnIndex >= 0 ? _sortColumnIndex : null;

    /// <summary>
    /// Gets the current sort direction. When <see cref="SortColumn"/> is null this is <see cref="SortOrder.None"/>.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SortOrder SortOrder => _sortOrder;

    // ==================== DATA LOADING ====================

    private void LoadRoots(IEnumerable<TModel> roots)
    {
        CancelEdit();
        bool hadSelection = _selectedNode != null || _selectedNodes.Count > 0;
        _rootNodes.Clear();
        _visibleRows.Clear();
        _selectedNodes.Clear();
        _selectedNode = null;
        _selectedIndex = -1;
        SetAnchor(-1);
        _hoverRowIndex = -1;

        if (roots != null)
        {
            int idx = 0;
            foreach (var model in roots)
            {
                _rootNodes.Add(new TreeNode(model, parent: null) { OriginalIndex = idx++ });
            }
        }

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
        if (hadSelection)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildVisibleRows()
    {
        _visibleRows.Clear();
        _hoverRowIndex = -1;

        if (_filter != null)
            RecomputeFilterCache();
        else
            _filterMatch = null;

        var lineage = new List<bool>();
        var orderedRoots = OrderSiblings(_rootNodes).Where(IsNodeVisibleUnderFilter).ToList();
        for (int i = 0; i < orderedRoots.Count; i++)
        {
            lineage.Add(i < orderedRoots.Count - 1);
            AppendVisible(orderedRoots[i], level: 0, lineage);
            lineage.RemoveAt(lineage.Count - 1);
        }

        // Prune selection: only nodes that are still visible remain selected.
        var focusedBefore = _selectedNode;
        int selectedCountBefore = _selectedNodes.Count;
        if (_selectedNodes.Count > 0)
        {
            var visibleSet = new HashSet<TreeNode>();
            foreach (var vr in _visibleRows) visibleSet.Add(vr.Node);
            _selectedNodes.RemoveWhere(n => !visibleSet.Contains(n));
        }

        if (_selectedNode != null)
        {
            _selectedIndex = _visibleRows.FindIndex(vr => vr.Node == _selectedNode);
            if (_selectedIndex < 0)
            {
                // Focused node vanished; fall back to the first remaining selected node (if any)
                int fallback = _visibleRows.FindIndex(vr => _selectedNodes.Contains(vr.Node));
                _selectedNode = fallback >= 0 ? _visibleRows[fallback].Node : null;
                _selectedIndex = fallback;
            }
        }
        else
        {
            _selectedIndex = -1;
        }

        // The anchor is a node, not a row number: sorting and filtering move rows.
        // A hidden anchor keeps its node so the range origin returns when it is visible again.
        if (_anchorNode != null)
            _anchorIndex = _visibleRows.FindIndex(vr => vr.Node == _anchorNode);

        if (focusedBefore != _selectedNode || selectedCountBefore != _selectedNodes.Count)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AppendVisible(TreeNode node, int level, List<bool> lineage)
    {
        _visibleRows.Add(new VisibleRow(node, level, lineage.ToArray()));

        if (!node.IsExpanded) return;

        node.EnsureChildrenLoaded(_childrenGetter);
        var children = OrderSiblings(node.Children).Where(IsNodeVisibleUnderFilter).ToList();
        for (int i = 0; i < children.Count; i++)
        {
            lineage.Add(i < children.Count - 1);
            AppendVisible(children[i], level + 1, lineage);
            lineage.RemoveAt(lineage.Count - 1);
        }
    }

    private bool IsNodeVisibleUnderFilter(TreeNode node)
        => _filterMatch == null || (_filterMatch.TryGetValue(node, out var match) && match);

    private IEnumerable<TreeNode> OrderSiblings(List<TreeNode> nodes)
    {
        if (_sortOrder == SortOrder.None || _sortColumnIndex < 0 || _sortColumnIndex >= _columns.Count || nodes.Count <= 1)
            return nodes;

        var col = _columns[_sortColumnIndex];

        return _sortOrder == SortOrder.Ascending
            ? nodes.OrderBy(n => col.Getter(n.Model), SortValueComparer.Instance).ThenBy(n => n.OriginalIndex)
            : nodes.OrderByDescending(n => col.Getter(n.Model), SortValueComparer.Instance).ThenBy(n => n.OriginalIndex);
    }

    private bool NodeHasChildren(TreeNode node)
    {
        if (node.ChildrenLoaded)
        {
            if (_filterMatch != null)
                return node.Children.Any(IsNodeVisibleUnderFilter);
            return node.Children.Count > 0;
        }

        if (_hasChildrenGetter != null)
            return _hasChildrenGetter(node.Model);

        // Optimistic: allow expand attempt (getter will decide at load time)
        return _childrenGetter != null;
    }

    // ==================== FILTERING ====================

    private void RecomputeFilterCache()
    {
        _filterMatch = new Dictionary<TreeNode, bool>();
        foreach (var root in _rootNodes)
        {
            ComputeFilterMatch(root);
        }
    }

    private bool ComputeFilterMatch(TreeNode node)
    {
        bool match;
        try { match = _filter!(node.Model); }
        catch { match = false; }

        node.EnsureChildrenLoaded(_childrenGetter);
        foreach (var child in node.Children)
        {
            if (ComputeFilterMatch(child))
                match = true;
        }

        _filterMatch![node] = match;
        return match;
    }

    private void AutoExpandFilterMatches()
    {
        if (_filterMatch == null) return;

        void Walk(TreeNode node)
        {
            if (!node.ChildrenLoaded) return;
            bool anyChildMatches = false;
            foreach (var child in node.Children)
            {
                if (_filterMatch.TryGetValue(child, out var m) && m)
                    anyChildMatches = true;
                Walk(child);
            }
            if (anyChildMatches)
                node.IsExpanded = true;
        }

        foreach (var root in _rootNodes) Walk(root);
    }

    // ==================== SCROLLING ====================

    private int GetRowsViewportHeight()
    {
        int height = Math.Max(0, ClientSize.Height - _headerHeight);
        if (_hScrollBar is { Visible: true })
            height = Math.Max(0, height - _hScrollBar.Height);
        return height;
    }

    private int GetColumnsViewportWidth()
    {
        int width = ClientSize.Width;
        if (_vScrollBar is { Visible: true })
            width = Math.Max(0, width - _vScrollBar.Width);
        return width;
    }

    private int MeasureContentWidth(int viewportWidth)
    {
        if (_columns.Count == 0) return 0;

        int fixedCount = _autoFillLastColumn ? _columns.Count - 1 : _columns.Count;
        int others = 0;
        for (int c = 0; c < fixedCount; c++)
            others += _columns[c].Width;

        if (!_autoFillLastColumn)
            return others;

        return others + Math.Max(_columns[^1].Width, viewportWidth - others);
    }

    private int GetColumnWidth(int columnIndex)
    {
        var col = _columns[columnIndex];
        if (_autoFillLastColumn && columnIndex == _columns.Count - 1)
        {
            int others = 0;
            for (int c = 0; c < _columns.Count - 1; c++)
                others += _columns[c].Width;

            int avail = GetColumnsViewportWidth() - others;
            return Math.Max(col.Width, avail);
        }
        return col.Width;
    }

    private int GetTotalColumnsWidth()
    {
        int total = 0;
        for (int c = 0; c < _columns.Count; c++)
            total += GetColumnWidth(c);
        return total;
    }

    private void UpdateScrollbars()
    {
        if (_updatingScrollbars || _vScrollBar == null || _hScrollBar == null) return;
        _updatingScrollbars = true;
        try
        {
            UpdateScrollbarsCore();
        }
        finally
        {
            _updatingScrollbars = false;
        }
    }

    private void UpdateScrollbarsCore()
    {

        int clientWidth = ClientSize.Width;
        int clientHeight = ClientSize.Height;
        int header = _headerHeight;
        int vBarW = _vScrollBar.Width;
        int hBarH = _hScrollBar.Height;
        int totalHeight = _visibleRows.Count * _rowHeight;

        // Decide which bars are needed before showing them. A horizontal bar steals
        // row space (and can force the vertical bar); a vertical bar steals column space.
        int rowsHeight = Math.Max(0, clientHeight - header);
        int colsWidth = clientWidth;

        bool needV = totalHeight > rowsHeight && rowsHeight > 0;
        if (needV)
            colsWidth = Math.Max(0, clientWidth - vBarW);

        int totalWidth = MeasureContentWidth(colsWidth);
        bool needH = totalWidth > colsWidth && colsWidth > 0;
        if (needH)
        {
            rowsHeight = Math.Max(0, rowsHeight - hBarH);
            if (!needV && totalHeight > rowsHeight && rowsHeight > 0)
            {
                needV = true;
                colsWidth = Math.Max(0, clientWidth - vBarW);
                totalWidth = MeasureContentWidth(colsWidth);
                needH = totalWidth > colsWidth && colsWidth > 0;
            }
        }

        _vScrollBar.Visible = needV;
        if (needV)
        {
            _vScrollBar.Left = clientWidth - vBarW;
            _vScrollBar.Top = header;
            _vScrollBar.Height = Math.Max(0, rowsHeight);

            int large = Math.Max(_rowHeight, Math.Max(1, rowsHeight));
            _vScrollBar.LargeChange = large;
            _vScrollBar.SmallChange = _rowHeight;

            int maxVal = Math.Max(0, totalHeight - rowsHeight);
            _vScrollBar.Maximum = maxVal + large - 1;
            _vScrollBar.Value = Math.Min(_vOffset, maxVal);
            _vOffset = _vScrollBar.Value;
        }
        else
        {
            _vOffset = 0;
        }

        _hScrollBar.Visible = needH;
        if (needH)
        {
            _hScrollBar.Left = 0;
            _hScrollBar.Top = clientHeight - hBarH;
            _hScrollBar.Width = Math.Max(0, colsWidth);

            int large = Math.Max(10, Math.Max(1, colsWidth / 3));
            _hScrollBar.LargeChange = large;
            _hScrollBar.SmallChange = 20;

            int maxVal = Math.Max(0, totalWidth - colsWidth);
            _hScrollBar.Maximum = maxVal + large - 1;
            _hScrollBar.Value = Math.Min(_hOffset, maxVal);
            _hOffset = _hScrollBar.Value;
        }
        else
        {
            _hOffset = 0;
        }
    }

    private void EnsureRowVisible(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _visibleRows.Count) return;

        int rowTop = rowIndex * _rowHeight;
        int rowBottom = rowTop + _rowHeight;
        int viewTop = _vOffset;
        int viewHeight = GetRowsViewportHeight();
        int viewBottom = viewTop + viewHeight;

        if (rowTop < viewTop)
        {
            _vOffset = rowTop;
        }
        else if (rowBottom > viewBottom)
        {
            _vOffset = Math.Max(0, rowBottom - viewHeight);
        }

        if (_vScrollBar.Visible)
        {
            int maxVal = Math.Max(0, (_visibleRows.Count * _rowHeight) - viewHeight);
            _vScrollBar.Value = Math.Clamp(_vOffset, 0, maxVal);
            _vOffset = _vScrollBar.Value;
        }

        Invalidate();
    }

    // ==================== HIT TESTING ====================

    private readonly record struct HitTestResult(
        int RowIndex,
        int ColumnIndex,
        bool IsExpander,
        bool IsCheckBox,
        bool IsHeader,
        bool IsValid);

    private HitTestResult HitTest(int x, int y)
    {
        if (y < 0) return default;

        if (y < _headerHeight)
        {
            // Header
            int colX = -_hOffset;
            for (int c = 0; c < _columns.Count; c++)
            {
                int w = GetColumnWidth(c);
                if (x >= colX && x < colX + w)
                {
                    return new HitTestResult(-1, c, false, false, true, true);
                }
                colX += w;
            }
            return new HitTestResult(-1, -1, false, false, true, true);
        }

        int firstRow = _vOffset / _rowHeight;
        int rowIndex = firstRow + (y - _headerHeight + (_vOffset % _rowHeight)) / _rowHeight;

        if (rowIndex < 0 || rowIndex >= _visibleRows.Count)
            return default;

        var vrow = _visibleRows[rowIndex];

        // Compute column
        int cellX = -_hOffset;
        int colIndex = -1;
        bool isExpander = false;
        bool isCheckBox = false;

        for (int c = 0; c < _columns.Count; c++)
        {
            int w = GetColumnWidth(c);
            if (x >= cellX && x < cellX + w)
            {
                colIndex = c;
                if (c == 0)
                {
                    int rowTop = _headerHeight + (rowIndex * _rowHeight) - _vOffset;
                    var cellRect = new Rectangle(cellX, rowTop, w, _rowHeight);
                    var layout = GetTreeCellLayout(vrow, cellRect);

                    if (layout.HasChildren)
                    {
                        var expRect = layout.ExpanderRect;
                        expRect.Inflate(3, 3);
                        if (expRect.Contains(x, y)) isExpander = true;
                    }

                    if (!isExpander && _showCheckBoxes)
                    {
                        var cbRect = layout.CheckBoxRect;
                        cbRect.Inflate(2, 2);
                        if (cbRect.Contains(x, y)) isCheckBox = true;
                    }
                }
                break;
            }
            cellX += w;
        }

        return new HitTestResult(rowIndex, colIndex, isExpander, isCheckBox, false, true);
    }

    // ==================== SELECTION ====================

    private void SelectSingle(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _visibleRows.Count)
        {
            ClearSelection();
            return;
        }

        var node = _visibleRows[rowIndex].Node;
        SetAnchor(rowIndex);

        bool unchanged = _selectedNode == node && _selectedIndex == rowIndex &&
                         _selectedNodes.Count == 1 && _selectedNodes.Contains(node);
        if (unchanged) return;

        _selectedNodes.Clear();
        _selectedNodes.Add(node);
        _selectedNode = node;
        _selectedIndex = rowIndex;
        _currentEditColumnHint = 0;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void ToggleRowSelection(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _visibleRows.Count) return;

        var node = _visibleRows[rowIndex].Node;
        SetAnchor(rowIndex);

        if (_selectedNodes.Add(node))
        {
            _selectedNode = node;
            _selectedIndex = rowIndex;
        }
        else
        {
            _selectedNodes.Remove(node);
            if (_selectedNode == node)
            {
                int fallback = _visibleRows.FindIndex(vr => _selectedNodes.Contains(vr.Node));
                _selectedNode = fallback >= 0 ? _visibleRows[fallback].Node : null;
                _selectedIndex = fallback;
            }
        }

        _currentEditColumnHint = 0;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void SelectRange(int anchorIndex, int focusIndex)
    {
        if (_visibleRows.Count == 0) return;

        anchorIndex = Math.Clamp(anchorIndex, 0, _visibleRows.Count - 1);
        focusIndex = Math.Clamp(focusIndex, 0, _visibleRows.Count - 1);

        int lo = Math.Min(anchorIndex, focusIndex);
        int hi = Math.Max(anchorIndex, focusIndex);

        var newSet = new HashSet<TreeNode>();
        for (int i = lo; i <= hi; i++)
            newSet.Add(_visibleRows[i].Node);

        var newFocused = _visibleRows[focusIndex].Node;
        bool changed = _selectedNode != newFocused || !_selectedNodes.SetEquals(newSet);

        _selectedNodes.Clear();
        _selectedNodes.UnionWith(newSet);
        _selectedNode = newFocused;
        _selectedIndex = focusIndex;
        SetAnchor(anchorIndex);
        _currentEditColumnHint = 0;

        if (changed)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void SelectAllRows()
    {
        if (_visibleRows.Count == 0) return;

        _selectedNodes.Clear();
        foreach (var vr in _visibleRows)
            _selectedNodes.Add(vr.Node);

        if (_selectedNode == null || _selectedIndex < 0)
        {
            _selectedNode = _visibleRows[0].Node;
            _selectedIndex = 0;
        }
        if (_anchorIndex < 0) SetAnchor(0);

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void MoveFocusTo(int rowIndex, bool extendRange)
    {
        if (rowIndex < 0 || rowIndex >= _visibleRows.Count) return;

        if (extendRange && _multiSelect && _anchorIndex >= 0)
            SelectRange(_anchorIndex, rowIndex);
        else
            SelectSingle(rowIndex);

        EnsureRowVisible(rowIndex);
    }

    /// <summary>
    /// Selects the given model (expands all ancestor nodes as needed so the item becomes visible and selected).
    /// </summary>
    public void SelectModel(TModel model)
    {
        // Find the node in the currently visible tree (depth-first search)
        var node = FindNode(model);
        if (node == null) return;

        // Ensure all ancestors are expanded so the node becomes visible
        ExpandAncestors(node);

        RebuildVisibleRows();
        UpdateScrollbars();

        int idx = _visibleRows.FindIndex(vr => vr.Node == node);
        if (idx >= 0)
        {
            SelectSingle(idx);
            EnsureRowVisible(idx);
        }
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        if (_selectedNode == null && _selectedNodes.Count == 0) return;
        _selectedNodes.Clear();
        _selectedNode = null;
        _selectedIndex = -1;
        SetAnchor(-1);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void SetAnchor(int rowIndex)
    {
        if (rowIndex >= 0 && rowIndex < _visibleRows.Count)
        {
            _anchorIndex = rowIndex;
            _anchorNode = _visibleRows[rowIndex].Node;
        }
        else
        {
            _anchorIndex = -1;
            _anchorNode = null;
        }
    }

    private TreeNode? FindNode(TModel model)
    {
        TreeNode? equalMatch = null;
        foreach (var root in _rootNodes)
        {
            var byReference = FindNodeRecursive(root, model, ref equalMatch);
            if (byReference != null) return byReference;
        }
        return equalMatch;
    }

    /// <summary>
    /// Reference types match the exact instance first, so two records with the same values
    /// stay independently addressable. A value-equal copy is still found when no instance matches.
    /// Value types compare with <see cref="EqualityComparer{T}"/>.
    /// </summary>
    private TreeNode? FindNodeRecursive(TreeNode node, TModel model, ref TreeNode? equalMatch)
    {
        if (typeof(TModel).IsValueType)
        {
            if (EqualityComparer<TModel>.Default.Equals(node.Model, model))
                return node;
        }
        else if (ReferenceEquals(node.Model, model))
        {
            return node;
        }
        else if (equalMatch == null && EqualityComparer<TModel>.Default.Equals(node.Model, model))
        {
            equalMatch = node;
        }

        // Only search loaded children (avoids forcing load of entire tree)
        if (node.ChildrenLoaded)
        {
            foreach (var child in node.Children)
            {
                var byReference = FindNodeRecursive(child, model, ref equalMatch);
                if (byReference != null) return byReference;
            }
        }
        return null;
    }

    private void ExpandAncestors(TreeNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (!current.IsExpanded)
            {
                current.EnsureChildrenLoaded(_childrenGetter);
                current.IsExpanded = true;
                NodeExpanded?.Invoke(this, new TreeNodeEventArgs<TModel>(current.Model, current));
            }
            current = current.Parent;
        }
    }

    // ==================== CHECKBOXES ====================

    /// <summary>
    /// Sets the checked state of the node for the given model. Raises <see cref="CheckedChanged"/> when the state changes.
    /// </summary>
    public void SetChecked(TModel model, bool isChecked)
    {
        var node = FindNode(model);
        if (node == null) return;

        SetCheckedCore(node, isChecked);
        int idx = _visibleRows.FindIndex(vr => vr.Node == node);
        if (idx >= 0) InvalidateRow(idx);
    }

    private void SetCheckedCore(TreeNode node, bool isChecked)
    {
        if (node.IsChecked == isChecked) return;
        node.IsChecked = isChecked;
        CheckedChanged?.Invoke(this, new TreeNodeEventArgs<TModel>(node.Model, node));
    }

    private void ToggleCheckedForSelection()
    {
        if (_selectedNodes.Count == 0) return;

        bool target = !(_selectedNode ?? _selectedNodes.First()).IsChecked;
        foreach (var node in _selectedNodes)
            SetCheckedCore(node, target);

        Invalidate();
    }

    // ==================== EXPAND / COLLAPSE ====================

    private void ToggleExpand(TreeNode node)
    {
        if (!NodeHasChildren(node)) return;

        if (!node.IsExpanded)
        {
            node.EnsureChildrenLoaded(_childrenGetter);
            node.IsExpanded = true;
            NodeExpanded?.Invoke(this, new TreeNodeEventArgs<TModel>(node.Model, node));
        }
        else
        {
            node.IsExpanded = false;
            NodeCollapsed?.Invoke(this, new TreeNodeEventArgs<TModel>(node.Model, node));
        }

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Expands the node for the given model (loads children on demand if necessary).
    /// </summary>
    public void Expand(TModel model)
    {
        var node = FindNode(model);
        if (node == null || node.IsExpanded) return;

        ExpandAncestors(node);
        node.EnsureChildrenLoaded(_childrenGetter);
        node.IsExpanded = true;
        NodeExpanded?.Invoke(this, new TreeNodeEventArgs<TModel>(node.Model, node));

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Collapses the node for the given model.
    /// </summary>
    public void Collapse(TModel model)
    {
        var node = FindNode(model);
        if (node == null || !node.IsExpanded) return;

        node.IsExpanded = false;
        NodeCollapsed?.Invoke(this, new TreeNodeEventArgs<TModel>(node.Model, node));

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Expands every node in the tree, loading children on demand as needed.
    /// Per-node <see cref="NodeExpanded"/> events are not raised for bulk expansion.
    /// </summary>
    public void ExpandAll()
    {
        CancelEdit();
        foreach (var root in _rootNodes)
            ExpandNodeRecursive(root);

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Collapses every loaded node in the tree.
    /// Per-node <see cref="NodeCollapsed"/> events are not raised for bulk collapse.
    /// </summary>
    public void CollapseAll()
    {
        CancelEdit();
        foreach (var root in _rootNodes)
            CollapseNodeRecursive(root);

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    /// <summary>
    /// Expands the node for the given model and its entire subtree (loads children on demand).
    /// Bound to the * key for the selected row.
    /// </summary>
    public void ExpandSubtree(TModel model)
    {
        var node = FindNode(model);
        if (node == null) return;

        CancelEdit();
        ExpandAncestors(node);
        ExpandNodeRecursive(node);

        RebuildVisibleRows();
        UpdateScrollbars();
        Invalidate();
    }

    private void ExpandNodeRecursive(TreeNode node)
    {
        node.EnsureChildrenLoaded(_childrenGetter);
        if (node.Children.Count == 0) return;

        node.IsExpanded = true;
        foreach (var child in node.Children)
            ExpandNodeRecursive(child);
    }

    private static void CollapseNodeRecursive(TreeNode node)
    {
        node.IsExpanded = false;
        if (node.ChildrenLoaded)
        {
            foreach (var child in node.Children)
                CollapseNodeRecursive(child);
        }
    }

    // ==================== IN-PLACE EDITING (Excellent support) ====================

    /// <summary>
    /// Begins in-place editing for the cell at the given visible row and column index.
    /// The default editor is a TextBox; DateTime and bool values get a DateTimePicker or CheckBox.
    /// Null cells use <see cref="TreeListColumn{TModel}.DataType"/> to choose the same editors.
    /// Columns can supply a custom editor via <see cref="TreeListColumn{TModel}.EditorFactory"/>.
    /// Commit with Enter or by losing focus; cancel with Escape. Tab / Shift+Tab move to the next / previous editable cell.
    /// </summary>
    public void BeginEdit(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _visibleRows.Count) return;
        if (columnIndex < 0 || columnIndex >= _columns.Count) return;

        var column = _columns[columnIndex];
        if (!column.IsEditable) return;

        // Keep a value already typed into another cell.
        if (!CommitEdit(restoreFocus: false)) return;

        var vrow = _visibleRows[rowIndex];
        var node = vrow.Node;

        _editingRowIndex = rowIndex;
        _editingColIndex = columnIndex;
        _editingNode = node;

        object? currentValue = column.Getter(node.Model);
        _editingOriginalValue = currentValue;

        var editor = column.EditorFactory != null
            ? column.EditorFactory(node.Model, currentValue)
            : CreateDefaultEditor(column, currentValue);
        if (editor == null) { EndEdit(); return; }

        editor.Font = Font;
        editor.Tag = new EditTag(column, node, currentValue);

        _activeEditor = editor;
        _suppressFocusCommit = false;
        Controls.Add(editor);
        editor.BringToFront();
        PositionActiveEditor();
        editor.Focus();

        if (editor is TextBox tb)
            tb.SelectAll();

        if (editor is DateTimePicker dtp)
        {
            dtp.DropDown += Editor_DropDown;
            dtp.CloseUp += Editor_CloseUp;
            // Checked is unreliable until the picker has a handle. Re-apply the null state.
            if (dtp.ShowCheckBox && _editingOriginalValue is not DateTime)
                dtp.Checked = false;
        }

        editor.PreviewKeyDown += Editor_PreviewKeyDown;
        editor.KeyDown += Editor_KeyDown;
        editor.LostFocus += Editor_LostFocus;

        Invalidate(); // in case we want to highlight editing cell
    }

    private void PositionActiveEditor()
    {
        if (_activeEditor == null || _editingRowIndex < 0 || _editingColIndex < 0) return;
        if (_editingRowIndex >= _visibleRows.Count) return;

        var cellRect = GetCellRectangle(_editingRowIndex, _editingColIndex);
        if (cellRect.IsEmpty) return;

        if (_editingColIndex == 0)
        {
            // Do not cover the expander / checkbox / icon area of the tree cell
            var layout = GetTreeCellLayout(_visibleRows[_editingRowIndex], cellRect);
            int delta = Math.Max(0, layout.ContentLeft - cellRect.Left);
            cellRect.X += delta;
            cellRect.Width = Math.Max(20, cellRect.Width - delta);
        }

        // Inset slightly for modern look
        cellRect.Inflate(-1, -1);
        if (cellRect.Width < 20) cellRect.Width = 20;
        _activeEditor.Bounds = cellRect;
    }

    private Control CreateDefaultEditor(TreeListColumn<TModel> column, object? currentValue)
    {
        // DateTime and DateTime? -> DateTimePicker. Null values need TreeListColumn.DataType,
        // because a null cell does not carry a runtime type. Nullable dates can be cleared.
        // bool / bool? -> CheckBox. Everything else -> TextBox.
        // Keyboard: arrows, Home, End etc. work inside the editors because we only intercept Enter/Escape/Tab.

        if (IsDateTimeLike(currentValue, column.DataType))
        {
            bool nullable = column.DataType != null && Nullable.GetUnderlyingType(column.DataType) == typeof(DateTime);
            var dtp = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                ShowCheckBox = nullable,
                CalendarMonthBackground = EditorBackColor,
                CalendarForeColor = EditorForeColor
            };
            if (currentValue is DateTime dtVal)
            {
                dtp.Value = dtVal;
                dtp.Checked = true;
            }
            else
            {
                dtp.Value = DateTime.Today;
                dtp.Checked = false;
            }
            return dtp;
        }

        if (IsBoolLike(currentValue, column.DataType))
        {
            bool nullable = column.DataType != null && Nullable.GetUnderlyingType(column.DataType) == typeof(bool);
            var cb = new CheckBox
            {
                BackColor = EditorBackColor,
                ForeColor = EditorForeColor,
                CheckAlign = ContentAlignment.MiddleCenter,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                ThreeState = nullable
            };
            if (currentValue is bool boolVal)
                cb.Checked = boolVal;
            else if (nullable)
                cb.CheckState = CheckState.Indeterminate;
            return cb;
        }

        // Default: TextBox (strings, numbers, GUIDs, etc.)
        var text = currentValue?.ToString() ?? string.Empty;
        var align = column.Alignment;
        if (align == HorizontalAlignment.Left && (IsNumericType(currentValue) || IsNumericType(column.DataType)))
            align = HorizontalAlignment.Right;

        return new TextBox
        {
            Text = text,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = EditorBackColor,
            ForeColor = EditorForeColor,
            Padding = new Padding(2),
            TextAlign = align
        };
    }

    private static bool IsDateTimeLike(object? value, Type? dataType)
    {
        if (value is DateTime) return true;
        if (dataType == null) return false;
        var t = Nullable.GetUnderlyingType(dataType) ?? dataType;
        return t == typeof(DateTime);
    }

    private static bool IsBoolLike(object? value, Type? dataType)
    {
        if (value is bool) return true;
        if (dataType == null) return false;
        var t = Nullable.GetUnderlyingType(dataType) ?? dataType;
        return t == typeof(bool);
    }

    private static bool IsNumericType(object? value) => value != null && IsNumericType(value.GetType());

    private static bool IsNumericType(Type? type)
    {
        if (type == null) return false;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(sbyte) || type == typeof(byte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    private void Editor_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        // Tab is normally swallowed as a dialog/navigation key before KeyDown fires.
        // Declaring it an input key lets Editor_KeyDown handle Tab / Shift+Tab cell navigation.
        if (e.KeyCode == Keys.Tab)
            e.IsInputKey = true;
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CommitEdit();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CancelEdit();
        }
        else if (e.KeyCode == Keys.Tab)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            // Capture position before commit resets editing state
            int row = _editingRowIndex;
            int col = _editingColIndex;
            int direction = e.Shift ? -1 : +1;

            if (CommitEdit())
                MoveToAdjacentEditableCell(row, col, direction);
        }
    }

    private void Editor_DropDown(object? sender, EventArgs e)
    {
        // The DateTimePicker's calendar dropdown takes focus; do not treat that as "editing finished".
        _suppressFocusCommit = true;
    }

    private void Editor_CloseUp(object? sender, EventArgs e)
    {
        _suppressFocusCommit = false;
    }

    private void Editor_LostFocus(object? sender, EventArgs e)
    {
        if (_suppressFocusCommit || _inEndEdit || _activeEditor == null || _handlingMouseDown) return;

        var editor = _activeEditor;
        // Focus moving into a child of a composite editor also raises LostFocus.
        // Wait until the message pump settles, then commit only if focus really left.
        if (editor.HasChildren && IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new MethodInvoker(() =>
            {
                if (IsDisposed || _activeEditor != editor || editor.IsDisposed || editor.ContainsFocus)
                    return;
                CommitEdit(restoreFocus: false);
            }));
            return;
        }

        // Do not take focus back; the user clicked some other control (or the form is closing).
        CommitEdit(restoreFocus: false);
    }

    /// <summary>
    /// Applies the open editor. Returns false when a <see cref="CellEditCommitted"/> handler
    /// sets <see cref="CellEditEventArgs{TModel}.Cancel"/>; the editor stays open so the value
    /// can be corrected. Does not raise <see cref="CellEditCanceled"/> in that case.
    /// </summary>
    private bool CommitEdit(bool restoreFocus = true)
    {
        if (_inEndEdit) return false;
        if (_activeEditor == null || _editingNode == null || _editingColIndex < 0)
        {
            if (_activeEditor != null)
                EndEdit(restoreFocus);
            return true;
        }

        _inEndEdit = true;
        bool accepted = false;
        try
        {
            var column = _columns[_editingColIndex];
            var model = _editingNode.Model;
            object? newValue = ExtractValueFromEditor(_activeEditor, column);

            var args = new CellEditEventArgs<TModel>(
                model,
                column,
                newValue,
                _editingOriginalValue,
                _editingRowIndex,
                _editingColIndex);

            CellEditCommitted?.Invoke(this, args);

            if (args.Cancel)
                return false;

            // Apply via registered setter if present (user is responsible for updating model)
            _setCellValue?.Invoke(model, column, args.ProposedValue);
            InvalidateRow(_editingRowIndex);
            accepted = true;
            return true;
        }
        finally
        {
            if (accepted)
                EndEdit(restoreFocus);
            else if (_activeEditor != null && !_activeEditor.IsDisposed)
                _activeEditor.Focus();

            _inEndEdit = false;
        }
    }

    private bool CommitEditIfActive(bool restoreFocus = true)
    {
        if (_activeEditor == null) return true;
        return CommitEdit(restoreFocus);
    }

    private void CancelEdit(bool restoreFocus = true)
    {
        if (_inEndEdit) return;
        if (_activeEditor == null) return;

        _inEndEdit = true;
        try
        {
            if (_editingNode != null && _editingColIndex >= 0 && _editingColIndex < _columns.Count)
            {
                var args = new CellEditEventArgs<TModel>(
                    _editingNode.Model,
                    _columns[_editingColIndex],
                    null,
                    _editingOriginalValue,
                    _editingRowIndex,
                    _editingColIndex)
                { Cancel = true };

                CellEditCanceled?.Invoke(this, args);
            }
        }
        finally
        {
            EndEdit(restoreFocus);
            _inEndEdit = false;
        }
    }

    private void EndEdit(bool restoreFocus = true)
    {
        if (_activeEditor != null)
        {
            var editor = _activeEditor;
            editor.LostFocus -= Editor_LostFocus;
            editor.KeyDown -= Editor_KeyDown;
            editor.PreviewKeyDown -= Editor_PreviewKeyDown;

            if (editor is DateTimePicker dtp)
            {
                dtp.DropDown -= Editor_DropDown;
                dtp.CloseUp -= Editor_CloseUp;
            }

            _activeEditor = null;

            if (!editor.IsDisposed)
            {
                if (Controls.Contains(editor))
                    Controls.Remove(editor);
                editor.Dispose();
            }
        }

        _editingRowIndex = -1;
        _editingColIndex = -1;
        _editingNode = null;
        _editingOriginalValue = null;
        _suppressFocusCommit = false;

        if (restoreFocus && IsHandleCreated && !IsDisposed && !Disposing)
            Focus();
        Invalidate();
    }

    private object? ExtractValueFromEditor(Control editor, TreeListColumn<TModel> column)
    {
        if (column.EditorValueExtractor != null)
            return column.EditorValueExtractor(editor);

        // For default TextBox we return the string. Setter can parse.
        return editor switch
        {
            TextBox tb => tb.Text,
            CheckBox cb => cb.ThreeState && cb.CheckState == CheckState.Indeterminate ? null : cb.Checked,
            DateTimePicker dtp => dtp.ShowCheckBox && !dtp.Checked ? null : dtp.Value,
            ComboBox cmb => cmb.SelectedItem ?? cmb.Text,
            _ => editor.Text
        };
    }

    private int FirstEditableColumn()
    {
        for (int c = 0; c < _columns.Count; c++)
        {
            if (_columns[c].IsEditable) return c;
        }
        return -1;
    }

    private void MoveToAdjacentEditableCell(int fromRow, int fromCol, int direction)
    {
        if (fromRow < 0 || fromCol < 0 || _columns.Count == 0) return;

        int r = fromRow;
        int c = fromCol;
        int guard = _columns.Count * Math.Max(1, _visibleRows.Count);

        while (guard-- > 0)
        {
            c += direction;
            if (c >= _columns.Count) { c = 0; r++; }
            else if (c < 0) { c = _columns.Count - 1; r--; }

            if (r < 0 || r >= _visibleRows.Count) return;

            if (_columns[c].IsEditable)
            {
                SelectSingle(r);
                EnsureRowVisible(r);
                BeginEdit(r, c);
                return;
            }
        }
    }

    private void InvalidateRow(int rowIndex)
    {
        if (rowIndex < 0) return;
        int y = _headerHeight + (rowIndex * _rowHeight) - _vOffset;
        Invalidate(new Rectangle(0, y, ClientSize.Width, _rowHeight));
    }

    // ==================== GEOMETRY HELPERS ====================

    private Rectangle GetCellRectangle(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 || columnIndex >= _columns.Count)
            return Rectangle.Empty;

        int colX = -_hOffset;
        for (int c = 0; c < columnIndex; c++)
            colX += GetColumnWidth(c);

        int colWidth = GetColumnWidth(columnIndex);

        int rowY = _headerHeight + (rowIndex * _rowHeight) - _vOffset;

        return new Rectangle(colX, rowY, colWidth, _rowHeight);
    }

    private int GetColumnStartX(int columnIndex)
    {
        int x = -_hOffset;
        for (int c = 0; c < columnIndex; c++)
            x += GetColumnWidth(c);
        return x;
    }

    private readonly record struct TreeCellLayout(
        bool HasChildren,
        Rectangle ExpanderRect,
        Rectangle CheckBoxRect,
        Rectangle IconRect,
        Image? Icon,
        int ContentLeft);

    private TreeCellLayout GetTreeCellLayout(VisibleRow vrow, Rectangle cellRect)
    {
        bool hasChildren = NodeHasChildren(vrow.Node);
        int x = cellRect.Left + (vrow.Level * IndentSize) + ExpanderMargin;

        Rectangle expanderRect = Rectangle.Empty;
        if (hasChildren)
        {
            expanderRect = new Rectangle(
                x,
                cellRect.Top + (cellRect.Height - ExpanderSize) / 2,
                ExpanderSize,
                ExpanderSize);
        }
        x += hasChildren ? ExpanderSize + 4 : 4;

        Rectangle checkRect = Rectangle.Empty;
        if (_showCheckBoxes)
        {
            checkRect = new Rectangle(
                x,
                cellRect.Top + (cellRect.Height - CheckBoxSize) / 2,
                CheckBoxSize,
                CheckBoxSize);
            x += CheckBoxSize + 5;
        }

        Image? icon = null;
        Rectangle iconRect = Rectangle.Empty;
        if (_iconGetter != null)
        {
            icon = _iconGetter(vrow.Node.Model);
            if (icon != null)
            {
                iconRect = new Rectangle(
                    x,
                    cellRect.Top + (cellRect.Height - IconSize) / 2,
                    IconSize,
                    IconSize);
                x += IconSize + 4;
            }
        }

        return new TreeCellLayout(hasChildren, expanderRect, checkRect, iconRect, icon, x);
    }

    // ==================== COLUMN AUTO-FIT ====================

    /// <summary>
    /// Resizes the column so its header and all currently realized rows fit without truncation.
    /// Also triggered by double-clicking a column divider in the header.
    /// </summary>
    public void AutoFitColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count) return;

        var col = _columns[columnIndex];

        // Header text + room for the sort glyph
        int max = TextRenderer.MeasureText(col.Title, Font).Width + CellPadding * 2 + 18;

        foreach (var vrow in _visibleRows)
        {
            string text = GetDisplayText(vrow.Node.Model, col);
            if (text.Length == 0) continue;

            int w = TextRenderer.MeasureText(text, Font).Width + CellPadding * 2;
            if (columnIndex == 0)
            {
                w += (vrow.Level * IndentSize) + ExpanderMargin + ExpanderSize + 4;
                if (_showCheckBoxes) w += CheckBoxSize + 5;
                if (_iconGetter != null) w += IconSize + 4;
            }
            if (w > max) max = w;
        }

        col.Width = Math.Max(col.MinWidth, max);
        UpdateScrollbars();
        Invalidate();
    }

    // ==================== PAINTING (Modern clean look) ====================

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        int width = ClientSize.Width;
        int height = ClientSize.Height;

        // 1. Header
        DrawHeader(g, width);

        // 2. Rows
        DrawRows(g, width, height);

        // 3. Borders / finishing
        using var borderPen = new Pen(GridLineColor);
        g.DrawLine(borderPen, 0, _headerHeight - 1, width, _headerHeight - 1);

        // Focus cue on whole control when focused (subtle)
        if (Focused && _selectedIndex >= 0)
        {
            using var focusPen = new Pen(FocusCueColor) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focusPen, 0, 0, width - 1, height - 1);
        }
    }

    private void DrawHeader(Graphics g, int clientWidth)
    {
        var headerRect = new Rectangle(0, 0, clientWidth, _headerHeight);

        using var headerBrush = new SolidBrush(HeaderBackColor);
        g.FillRectangle(headerBrush, headerRect);

        using var linePen = new Pen(GridLineColor);

        int x = -_hOffset;

        for (int c = 0; c < _columns.Count; c++)
        {
            var col = _columns[c];
            int colWidth = GetColumnWidth(c);
            var colRect = new Rectangle(x, 0, colWidth, _headerHeight);

            if (colRect.Right > 0 && colRect.Left < clientWidth)
            {
                // Column separator (subtle)
                g.DrawLine(linePen, colRect.Right - 1, 4, colRect.Right - 1, _headerHeight - 5);

                // Title + optional sort indicator
                bool isSortedCol = (_sortColumnIndex == c && _sortOrder != SortOrder.None);
                string sortGlyph = isSortedCol
                    ? (_sortOrder == SortOrder.Ascending ? "▲" : "▼")
                    : "";

                int textRightPadding = isSortedCol ? 18 : CellPadding;
                var textRect = new Rectangle(
                    colRect.Left + CellPadding,
                    colRect.Top,
                    Math.Max(4, colRect.Width - CellPadding - textRightPadding),
                    colRect.Height);

                TextRenderer.DrawText(
                    g,
                    col.Title,
                    Font,
                    textRect,
                    HeaderForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);

                if (isSortedCol)
                {
                    // Draw sort indicator on the right side of the header cell (subtle, modern)
                    var glyphRect = new Rectangle(
                        colRect.Right - 16,
                        colRect.Top,
                        14,
                        colRect.Height);

                    TextRenderer.DrawText(
                        g,
                        sortGlyph,
                        Font,
                        glyphRect,
                        HeaderForeColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.PreserveGraphicsClipping);
                }
            }

            x += colWidth;
        }

        // Right edge line if needed
        g.DrawLine(linePen, 0, _headerHeight - 1, clientWidth, _headerHeight - 1);
    }

    private void DrawRows(Graphics g, int clientWidth, int clientHeight)
    {
        if (_visibleRows.Count == 0) return;

        int firstRow = Math.Max(0, _vOffset / _rowHeight);
        int rowPixelY = _headerHeight - (_vOffset % _rowHeight);

        using var gridPen = new Pen(GridLineColor);

        for (int r = firstRow; r < _visibleRows.Count && rowPixelY < clientHeight; r++)
        {
            var vrow = _visibleRows[r];
            bool rowSelected = _selectedNodes.Contains(vrow.Node);
            bool paintFullSelection = rowSelected && FullRowSelect;
            bool isHover = (r == _hoverRowIndex) && !rowSelected;
            bool isAlt = ShowAlternatingRows && (r % 2 == 1);

            var rowRect = new Rectangle(0, rowPixelY, clientWidth, _rowHeight);

            // Row background (full-row selection > hover > alternating > normal)
            Color bg = paintFullSelection ? SelectionBackColor :
                       isHover ? HoverBackColor :
                       isAlt ? AlternatingRowBackColor : RowBackColor;
            using (var b = new SolidBrush(bg))
            {
                g.FillRectangle(b, rowRect);
            }

            // Draw cells
            int cellX = -_hOffset;
            for (int c = 0; c < _columns.Count; c++)
            {
                var col = _columns[c];
                int colW = GetColumnWidth(c);
                var cellRect = new Rectangle(cellX, rowPixelY, colW, _rowHeight);
                bool cellSelected = rowSelected && (FullRowSelect || c == 0);

                if (cellRect.Right > 0 && cellRect.Left < clientWidth)
                {
                    if (cellSelected && !FullRowSelect)
                    {
                        using var sel = new SolidBrush(SelectionBackColor);
                        g.FillRectangle(sel, cellRect);
                    }

                    if (c == 0)
                    {
                        DrawTreeCell(g, cellRect, vrow, col, cellSelected);
                    }
                    else
                    {
                        DrawDataCell(g, cellRect, vrow, col, cellSelected);
                    }
                }

                // Vertical grid line
                if (ShowGridLines)
                {
                    g.DrawLine(gridPen, cellRect.Right - 1, rowPixelY + 2, cellRect.Right - 1, rowPixelY + _rowHeight - 3);
                }

                cellX += colW;
            }

            // Bottom grid line for row
            if (ShowGridLines)
            {
                g.DrawLine(gridPen, 0, rowPixelY + _rowHeight - 1, clientWidth, rowPixelY + _rowHeight - 1);
            }

            rowPixelY += _rowHeight;
        }
    }

    private void DrawTreeCell(Graphics g, Rectangle cellRect, VisibleRow vrow, TreeListColumn<TModel> column, bool isSelected)
    {
        var node = vrow.Node;
        var layout = GetTreeCellLayout(vrow, cellRect);

        if (_showTreeLines && vrow.Level > 0)
        {
            DrawTreeLines(g, cellRect, vrow, layout);
        }

        if (layout.HasChildren)
        {
            DrawModernExpander(g, layout.ExpanderRect, node.IsExpanded, isSelected);
        }

        if (_showCheckBoxes)
        {
            DrawCheckBox(g, layout.CheckBoxRect, node.IsChecked, isSelected);
        }

        if (layout.Icon != null)
        {
            g.DrawImage(layout.Icon, layout.IconRect);
        }

        // Text
        string text = GetDisplayText(node.Model, column);
        var textColor = isSelected ? SelectionForeColor : ForeColor;

        var textRect = new Rectangle(
            layout.ContentLeft,
            cellRect.Top,
            Math.Max(4, cellRect.Right - layout.ContentLeft - CellPadding),
            cellRect.Height);

        TextRenderer.DrawText(
            g,
            text,
            Font,
            textRect,
            textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
    }

    private void DrawTreeLines(Graphics g, Rectangle cellRect, VisibleRow vrow, TreeCellLayout layout)
    {
        using var pen = new Pen(TreeLineColor);
        int midY = cellRect.Top + cellRect.Height / 2;

        // Pass-through vertical lines for ancestors that have following siblings
        // (level 0 is skipped: no root-level connector lines for a cleaner look)
        for (int i = 1; i < vrow.Level; i++)
        {
            if (vrow.AncestorsHaveNext[i])
            {
                int lx = cellRect.Left + (i * IndentSize) + ExpanderMargin + ExpanderSize / 2;
                g.DrawLine(pen, lx, cellRect.Top, lx, cellRect.Bottom);
            }
        }

        // This node's own connector elbow
        int ex = cellRect.Left + (vrow.Level * IndentSize) + ExpanderMargin + ExpanderSize / 2;
        g.DrawLine(pen, ex, cellRect.Top, ex, midY);
        if (vrow.AncestorsHaveNext[vrow.Level])
        {
            g.DrawLine(pen, ex, midY, ex, cellRect.Bottom);
        }

        // Horizontal stub to the content for leaf nodes (parents have the chevron at the junction)
        if (!layout.HasChildren)
        {
            g.DrawLine(pen, ex, midY, ex + ExpanderSize / 2 + 3, midY);
        }
    }

    private void DrawDataCell(Graphics g, Rectangle cellRect, VisibleRow vrow, TreeListColumn<TModel> column, bool isSelected)
    {
        string text = GetDisplayText(vrow.Node.Model, column);
        var textColor = isSelected ? SelectionForeColor : ForeColor;

        // Respect column alignment (simple mapping)
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;
        if (column.Alignment == HorizontalAlignment.Right)
            flags |= TextFormatFlags.Right;
        else if (column.Alignment == HorizontalAlignment.Center)
            flags |= TextFormatFlags.HorizontalCenter;
        else
            flags |= TextFormatFlags.Left;

        var textRect = new Rectangle(
            cellRect.Left + CellPadding,
            cellRect.Top,
            Math.Max(4, cellRect.Width - CellPadding * 2),
            cellRect.Height);

        TextRenderer.DrawText(g, text, Font, textRect, textColor, flags);
    }

    private void DrawModernExpander(Graphics g, Rectangle rect, bool expanded, bool selected)
    {
        // Clean modern chevron/triangle style (no box)
        var color = selected ? SelectionForeColor : ExpanderColor;
        using var pen = new Pen(color, 1.6f);

        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        int sz = 3;

        if (expanded)
        {
            // Down chevron
            g.DrawLine(pen, cx - sz, cy - 1, cx, cy + sz - 1);
            g.DrawLine(pen, cx, cy + sz - 1, cx + sz, cy - 1);
        }
        else
        {
            // Right chevron
            g.DrawLine(pen, cx - 1, cy - sz, cx + sz - 1, cy);
            g.DrawLine(pen, cx + sz - 1, cy, cx - 1, cy + sz);
        }
    }

    private void DrawCheckBox(Graphics g, Rectangle rect, bool isChecked, bool isSelected)
    {
        Color borderColor = isSelected ? SelectionForeColor : ExpanderColor;

        using (var fill = new SolidBrush(isSelected ? Color.FromArgb(40, 255, 255, 255) : RowBackColor))
        {
            g.FillRectangle(fill, rect);
        }
        using (var pen = new Pen(borderColor, 1.2f))
        {
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
        }

        if (isChecked)
        {
            Color markColor = isSelected ? SelectionForeColor : SelectionBackColor;
            using var markPen = new Pen(markColor, 1.8f);
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLine(markPen, rect.X + 3, rect.Y + rect.Height / 2, rect.X + rect.Width / 2 - 1, rect.Y + rect.Height - 4);
            g.DrawLine(markPen, rect.X + rect.Width / 2 - 1, rect.Y + rect.Height - 4, rect.X + rect.Width - 3, rect.Y + 3);
            g.SmoothingMode = oldMode;
        }
    }

    private string GetDisplayText(TModel model, TreeListColumn<TModel> column)
    {
        object? value = column.Getter(model);
        if (column.Formatter != null)
            return column.Formatter(value) ?? string.Empty;
        return value?.ToString() ?? string.Empty;
    }

    // ==================== INPUT HANDLING ====================

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // LostFocus commits clicks that leave the tree. This flag tells it to leave a click
        // on the tree itself to the logic below, so a veto is raised once.
        _handlingMouseDown = true;
        try
        {
            Focus();
            if (_activeEditor != null && !_suppressFocusCommit && !CommitEdit(restoreFocus: false))
                return;
            HandleMouseDown(e);
        }
        finally
        {
            _handlingMouseDown = false;
        }
    }

    private void HandleMouseDown(MouseEventArgs e)
    {

        var hit = HitTest(e.X, e.Y);
        // The second click of a double-click repeats MouseDown. Expander, checkbox, and header
        // actions must run once; OnMouseDoubleClick handles divider auto-fit and cell edit.
        bool repeatedClick = e.Clicks > 1;

        if (hit.IsHeader)
        {
            if (!repeatedClick)
                HandleHeaderMouseDown(e, hit);
            return;
        }

        if (hit.RowIndex >= 0 && hit.IsValid)
        {
            var vrow = _visibleRows[hit.RowIndex];

            if (hit.IsExpander)
            {
                if (!repeatedClick)
                    ToggleExpand(vrow.Node);
                return;
            }

            if (hit.IsCheckBox)
            {
                if (!repeatedClick)
                {
                    SetCheckedCore(vrow.Node, !vrow.Node.IsChecked);
                    InvalidateRow(hit.RowIndex);
                }
                return;
            }

            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;

            if (e.Button == MouseButtons.Right)
            {
                // Right-click selects the row only when it is not already part of the selection
                // (so context menus can operate on a multi-selection)
                if (!_selectedNodes.Contains(vrow.Node))
                    SelectSingle(hit.RowIndex);
            }
            else if (_multiSelect && shift && _anchorIndex >= 0)
            {
                SelectRange(_anchorIndex, hit.RowIndex);
            }
            else if (_multiSelect && ctrl)
            {
                ToggleRowSelection(hit.RowIndex);
            }
            else
            {
                SelectSingle(hit.RowIndex);
            }

            // For single click on data cell we just select (excellent editing uses F2 / double-click)
            _currentEditColumnHint = hit.ColumnIndex >= 0 ? hit.ColumnIndex : 0;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (e.Y >= 0 && e.Y < _headerHeight)
        {
            // Double-click on a column divider auto-fits that column
            int x = -_hOffset;
            for (int c = 0; c < _columns.Count; c++)
            {
                x += GetColumnWidth(c);
                if (Math.Abs(e.X - x) <= ResizeGripWidth)
                {
                    _resizingColumnIndex = -1;
                    Capture = false;
                    Cursor = Cursors.Default;
                    AutoFitColumn(c);
                    return;
                }
            }
            return;
        }

        var hit = HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0 && !hit.IsExpander && !hit.IsCheckBox && hit.IsValid)
        {
            int col = hit.ColumnIndex >= 0 ? hit.ColumnIndex : 0;
            BeginEdit(hit.RowIndex, col);
        }
    }

    private void HandleHeaderMouseDown(MouseEventArgs e, HitTestResult hit)
    {
        if (e.Button != MouseButtons.Left || hit.ColumnIndex < 0) return;

        // Check for column resize grip
        int colRight = GetColumnStartX(hit.ColumnIndex) + GetColumnWidth(hit.ColumnIndex);

        if (Math.Abs(e.X - colRight) <= ResizeGripWidth)
        {
            _resizingColumnIndex = hit.ColumnIndex;
            _resizeStartX = e.X;
            // Store the configured width. The displayed width of an auto-filled last
            // column is stretched, and writing that back would inflate Width permanently.
            _resizeStartWidth = _columns[hit.ColumnIndex].Width;
            Capture = true;
            Cursor = Cursors.VSplit;
        }
        else
        {
            // Header click (not on grip) -> toggle sort for this column
            ToggleSortOnColumn(hit.ColumnIndex);
        }
    }

    private void ToggleSortOnColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count) return;

        SortOrder nextOrder;

        if (_sortColumnIndex != columnIndex)
        {
            // Different column: start with ascending
            nextOrder = SortOrder.Ascending;
        }
        else
        {
            // Same column: cycle Asc -> Desc -> None
            nextOrder = _sortOrder switch
            {
                SortOrder.None => SortOrder.Ascending,
                SortOrder.Ascending => SortOrder.Descending,
                _ => SortOrder.None
            };
        }

        Sort(columnIndex, nextOrder);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_resizingColumnIndex >= 0)
        {
            var col = _columns[_resizingColumnIndex];
            int delta = e.X - _resizeStartX;
            col.Width = Math.Max(col.MinWidth, _resizeStartWidth + delta);
            UpdateScrollbars();
            Invalidate();
            return;
        }

        // Update cursor for resize affordance in header
        if (e.Y < _headerHeight)
        {
            SetHoverRow(-1);
            UpdateToolTip(default);

            var hit = HitTest(e.X, e.Y);
            if (hit.ColumnIndex >= 0)
            {
                int colRight = GetColumnStartX(hit.ColumnIndex) + GetColumnWidth(hit.ColumnIndex);
                if (Math.Abs(e.X - colRight) <= 5)
                {
                    Cursor = Cursors.VSplit;
                    return;
                }
            }

            Cursor = Cursors.Default;
            return;
        }

        Cursor = Cursors.Default;

        var rowHit = HitTest(e.X, e.Y);
        SetHoverRow(!rowHit.IsHeader && rowHit.IsValid ? rowHit.RowIndex : -1);
        UpdateToolTip(rowHit);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoverRow(-1);
        UpdateToolTip(default);
    }

    private void SetHoverRow(int rowIndex)
    {
        if (rowIndex == _hoverRowIndex) return;
        int old = _hoverRowIndex;
        _hoverRowIndex = rowIndex;
        if (old >= 0) InvalidateRow(old);
        if (rowIndex >= 0) InvalidateRow(rowIndex);
    }

    private void UpdateToolTip(HitTestResult hit)
    {
        string text = string.Empty;

        if (hit.IsValid && !hit.IsHeader && hit.RowIndex >= 0 && hit.RowIndex < _visibleRows.Count &&
            hit.ColumnIndex >= 0 && hit.ColumnIndex < _columns.Count)
        {
            var vrow = _visibleRows[hit.RowIndex];
            var column = _columns[hit.ColumnIndex];
            string display = GetDisplayText(vrow.Node.Model, column);

            if (display.Length > 0)
            {
                var cellRect = GetCellRectangle(hit.RowIndex, hit.ColumnIndex);
                int available;
                if (hit.ColumnIndex == 0)
                {
                    var layout = GetTreeCellLayout(vrow, cellRect);
                    available = cellRect.Right - layout.ContentLeft - CellPadding;
                }
                else
                {
                    available = cellRect.Width - CellPadding * 2;
                }

                int needed = TextRenderer.MeasureText(display, Font).Width;
                if (needed > available)
                    text = display;
            }
        }

        if (text != _currentToolTipText)
        {
            _currentToolTipText = text;
            _toolTip.SetToolTip(this, text.Length == 0 ? null : text);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_resizingColumnIndex >= 0)
        {
            _resizingColumnIndex = -1;
            Capture = false;
            Cursor = Cursors.Default;
            UpdateScrollbars();
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        // Shift + wheel scrolls horizontally
        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
        {
            if (_hScrollBar.Visible)
            {
                CommitEditIfActive();
                int viewWidth = Math.Max(1, GetColumnsViewportWidth());
                int maxVal = Math.Max(0, GetTotalColumnsWidth() - viewWidth);
                _hOffset = Math.Clamp(_hOffset - Math.Sign(e.Delta) * 48, 0, maxVal);
                _hScrollBar.Value = Math.Min(_hOffset, maxVal);
                _hoverRowIndex = -1;
                if (_activeEditor != null)
                    PositionActiveEditor();
                Invalidate();
            }
            return;
        }

        if (_vScrollBar.Visible)
        {
            CommitEditIfActive();
            int viewHeight = Math.Max(1, GetRowsViewportHeight());
            int newVal = _vOffset - (e.Delta / 2); // natural feel
            int maxVal = Math.Max(0, (_visibleRows.Count * _rowHeight) - viewHeight);
            _vOffset = Math.Clamp(newVal, 0, maxVal);

            if (_vScrollBar.Visible)
            {
                _vScrollBar.Value = _vOffset;
            }

            _hoverRowIndex = -1;
            if (_activeEditor != null)
                PositionActiveEditor();
            Invalidate();
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        // Navigation keys must reach OnKeyDown instead of being treated as dialog keys
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.Home:
            case Keys.End:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Enter:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_visibleRows.Count == 0) return;

        switch (e.KeyCode)
        {
            case Keys.F2:
            case Keys.Enter:
                if (_selectedIndex >= 0)
                {
                    int col = _currentEditColumnHint >= 0 && _currentEditColumnHint < _columns.Count && _columns[_currentEditColumnHint].IsEditable
                        ? _currentEditColumnHint
                        : FirstEditableColumn();
                    if (col >= 0)
                        BeginEdit(_selectedIndex, col);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                break;

            case Keys.Up:
                if (_selectedIndex > 0)
                    MoveFocusTo(_selectedIndex - 1, e.Shift);
                else if (_selectedIndex < 0)
                    MoveFocusTo(0, false);
                e.Handled = true;
                break;

            case Keys.Down:
                if (_selectedIndex >= 0 && _selectedIndex < _visibleRows.Count - 1)
                    MoveFocusTo(_selectedIndex + 1, e.Shift);
                else if (_selectedIndex == -1)
                    MoveFocusTo(0, false);
                e.Handled = true;
                break;

            case Keys.Left:
                if (_selectedIndex >= 0)
                {
                    var node = _visibleRows[_selectedIndex].Node;
                    if (node.IsExpanded)
                    {
                        ToggleExpand(node);
                    }
                    else if (node.Parent != null)
                    {
                        // Go to parent
                        int parentIdx = _visibleRows.FindIndex(vr => vr.Node == node.Parent);
                        if (parentIdx >= 0)
                        {
                            SelectSingle(parentIdx);
                            EnsureRowVisible(parentIdx);
                        }
                    }
                }
                e.Handled = true;
                break;

            case Keys.Right:
                if (_selectedIndex >= 0)
                {
                    var node = _visibleRows[_selectedIndex].Node;
                    if (!node.IsExpanded && NodeHasChildren(node))
                    {
                        ToggleExpand(node);
                    }
                    else if (node.IsExpanded && node.Children.Count > 0)
                    {
                        // Go to first child
                        int childIdx = _visibleRows.FindIndex(vr => vr.Node.Parent == node);
                        if (childIdx >= 0)
                        {
                            SelectSingle(childIdx);
                            EnsureRowVisible(childIdx);
                        }
                    }
                }
                e.Handled = true;
                break;

            case Keys.PageUp:
                {
                    int page = Math.Max(1, (ClientSize.Height - _headerHeight) / _rowHeight);
                    int newIdx = Math.Max(0, _selectedIndex - page);
                    MoveFocusTo(newIdx, e.Shift);
                    e.Handled = true;
                }
                break;

            case Keys.PageDown:
                {
                    int page = Math.Max(1, (ClientSize.Height - _headerHeight) / _rowHeight);
                    int newIdx = Math.Min(_visibleRows.Count - 1, _selectedIndex + page);
                    MoveFocusTo(newIdx, e.Shift);
                    e.Handled = true;
                }
                break;

            case Keys.Home:
                MoveFocusTo(0, e.Shift);
                e.Handled = true;
                break;

            case Keys.End:
                MoveFocusTo(_visibleRows.Count - 1, e.Shift);
                e.Handled = true;
                break;

            case Keys.Multiply:
            case Keys.D8 when e.Shift && !e.Control && !e.Alt: // main-keyboard *
                if (_selectedIndex >= 0)
                {
                    ExpandSubtree(_visibleRows[_selectedIndex].Node.Model);
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Add:
            case Keys.Oemplus: // main-keyboard = / +
                if (_selectedIndex >= 0)
                {
                    var node = _visibleRows[_selectedIndex].Node;
                    if (!node.IsExpanded && NodeHasChildren(node))
                        ToggleExpand(node);
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Subtract:
            case Keys.OemMinus: // main-keyboard -
                if (_selectedIndex >= 0)
                {
                    var node = _visibleRows[_selectedIndex].Node;
                    if (node.IsExpanded)
                        ToggleExpand(node);
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            case Keys.Space:
                if (_showCheckBoxes && _selectedNodes.Count > 0)
                {
                    ToggleCheckedForSelection();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                break;

            case Keys.A:
                if (e.Control && _multiSelect)
                {
                    SelectAllRows();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                break;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);

        // Type-ahead search on the tree (first) column
        if (e.Handled || _columns.Count == 0 || _visibleRows.Count == 0) return;

        char ch = e.KeyChar;
        if (char.IsControl(ch) || !char.IsLetterOrDigit(ch)) return;

        long now = Environment.TickCount64;
        if (now - _typeAheadLastTick > TypeAheadResetMs)
            _typeAheadPrefix = string.Empty;
        _typeAheadLastTick = now;
        _typeAheadPrefix += ch;

        // A fresh single-char prefix searches from the next row; a growing prefix re-matches the current row first
        int start = _typeAheadPrefix.Length == 1 ? _selectedIndex + 1 : Math.Max(0, _selectedIndex);
        if (start < 0) start = 0;

        var firstColumn = _columns[0];
        for (int offset = 0; offset < _visibleRows.Count; offset++)
        {
            int idx = (start + offset) % _visibleRows.Count;
            string text = GetDisplayText(_visibleRows[idx].Node.Model, firstColumn);
            if (text.StartsWith(_typeAheadPrefix, StringComparison.CurrentCultureIgnoreCase))
            {
                SelectSingle(idx);
                EnsureRowVisible(idx);
                break;
            }
        }

        e.Handled = true;
    }

    // ==================== LAYOUT & RESIZE ====================

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollbars();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        UpdateScrollbars();
    }

    // ==================== FOCUS ====================

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    // ==================== CLEANUP ====================

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Unhook before the editor is disposed. Disposing it here would raise LostFocus
            // and try to commit while this control is going away. base.Dispose releases children.
            if (_activeEditor != null)
            {
                _activeEditor.LostFocus -= Editor_LostFocus;
                _activeEditor.KeyDown -= Editor_KeyDown;
                _activeEditor.PreviewKeyDown -= Editor_PreviewKeyDown;
                if (_activeEditor is DateTimePicker dtp)
                {
                    dtp.DropDown -= Editor_DropDown;
                    dtp.CloseUp -= Editor_CloseUp;
                }
                _activeEditor = null;
            }
            _toolTip?.Dispose();
            // Scrollbars are disposed by Controls collection
        }
        base.Dispose(disposing);
    }

    // ==================== INTERNAL TYPES ====================

    private sealed class TreeNode
    {
        public TModel Model { get; private set; }
        public TreeNode? Parent { get; }
        public List<TreeNode> Children { get; } = [];
        public bool IsExpanded { get; set; }
        public bool IsChecked { get; set; }
        public bool ChildrenLoaded { get; private set; }

        /// <summary>
        /// Stable sibling index assigned at load time. Used as a tie-breaker for stable sorting.
        /// </summary>
        public int OriginalIndex { get; set; }

        public TreeNode(TModel model, TreeNode? parent)
        {
            Model = model;
            Parent = parent;
        }

        public void EnsureChildrenLoaded(Func<TModel, IEnumerable<TModel>>? getter)
        {
            if (ChildrenLoaded || getter is null) return;

            Children.Clear();
            var children = getter(Model);
            if (children != null)
            {
                int idx = 0;
                foreach (var child in children)
                {
                    var n = new TreeNode(child, this);
                    n.OriginalIndex = idx++;
                    Children.Add(n);
                }
            }
            ChildrenLoaded = true;
        }

        /// <summary>
        /// Internal helper for ReplaceModel (immutable record support).
        /// </summary>
        internal void ReplaceModelReference(TModel newModel)
        {
            Model = newModel;
        }
    }

    /// <summary>
    /// Robust value comparer for sorting heterogeneous column data (strings, numbers, dates, nulls).
    /// </summary>
    private sealed class SortValueComparer : IComparer<object?>
    {
        public static readonly SortValueComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            // Fast path for identical runtime types that implement IComparable
            if (x.GetType() == y.GetType() && x is IComparable cx)
            {
                try { return cx.CompareTo(y); }
                catch { /* fall through */ }
            }

            // Try IComparable on either side (different numeric types, DateTime vs string, etc.)
            if (x is IComparable cx2)
            {
                try { return cx2.CompareTo(y); } catch { }
            }
            if (y is IComparable cy2)
            {
                try { return -cy2.CompareTo(x); } catch { }
            }

            // Fallback: culture-insensitive string compare of ToString representations
            return string.Compare(x.ToString(), y.ToString(), StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>
    /// A realized (visible) row. AncestorsHaveNext[i] indicates whether the chain node at depth i
    /// (i == Level is the row's own node) has a following sibling - used to draw tree connector lines.
    /// </summary>
    private readonly record struct VisibleRow(TreeNode Node, int Level, bool[] AncestorsHaveNext);

    // Helper tag for editors (extensibility point)
    private sealed record EditTag(TreeListColumn<TModel> Column, TreeNode Node, object? OriginalValue);
}

/// <summary>
/// Defines a column in the ModernTreeListView.
/// </summary>
public sealed class TreeListColumn<TModel>
{
    public string Title { get; set; }
    public int Width { get; set; }
    public Func<TModel, object?> Getter { get; set; }
    public Func<object?, string>? Formatter { get; set; }
    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>
    /// Optional minimum width when the user resizes the column.
    /// </summary>
    public int MinWidth { get; set; } = 36;

    /// <summary>
    /// Whether cells in this column can be edited in place. Default: true.
    /// </summary>
    public bool IsEditable { get; set; } = true;

    /// <summary>
    /// Optional runtime type of the cell value. Used to choose a default editor when the
    /// current value is null, which has no runtime type of its own.
    /// <see cref="DateTime"/> and <see cref="Nullable{T}"/> of <see cref="DateTime"/> use a
    /// <see cref="DateTimePicker"/> (nullable dates show a checkbox and can be cleared).
    /// <see cref="bool"/> and <c>bool?</c> use a <see cref="CheckBox"/>.
    /// </summary>
    public Type? DataType { get; set; }

    /// <summary>
    /// Optional factory creating a custom in-place editor for this column.
    /// Receives the model and the current cell value; return the (unparented) editor control.
    /// Pair with <see cref="EditorValueExtractor"/> to read the value back on commit.
    /// </summary>
    public Func<TModel, object?, Control>? EditorFactory { get; set; }

    /// <summary>
    /// Optional delegate extracting the committed value from the editor control.
    /// When null, built-in extraction is used (TextBox.Text, CheckBox.Checked, DateTimePicker.Value, ...).
    /// </summary>
    public Func<Control, object?>? EditorValueExtractor { get; set; }

    internal TreeListColumn(string title, Func<TModel, object?> getter, int width)
    {
        Title = title;
        Getter = getter;
        Width = Math.Max(width, MinWidth);
    }
}

/// <summary>
/// A bundle of all visual colors used by <see cref="ModernTreeListView{TModel}"/>.
/// Apply via <c>ApplyTheme</c> or the <c>Theme</c> property; use <see cref="Light"/> / <see cref="Dark"/> presets as starting points.
/// </summary>
public sealed class TreeListTheme
{
    public Color BackColor { get; set; }
    public Color ForeColor { get; set; }
    public Color HeaderBackColor { get; set; }
    public Color HeaderForeColor { get; set; }
    public Color RowBackColor { get; set; }
    public Color AlternatingRowBackColor { get; set; }
    public Color SelectionBackColor { get; set; }
    public Color SelectionForeColor { get; set; }
    public Color GridLineColor { get; set; }
    public Color ExpanderColor { get; set; }
    public Color TreeLineColor { get; set; }
    public Color HoverBackColor { get; set; }
    public Color EditorBackColor { get; set; }
    public Color EditorForeColor { get; set; }
    public Color FocusCueColor { get; set; }

    /// <summary>
    /// Clean light preset (the control's defaults).
    /// </summary>
    public static TreeListTheme Light => new()
    {
        BackColor = Color.White,
        ForeColor = Color.FromArgb(33, 37, 41),
        HeaderBackColor = Color.FromArgb(247, 248, 250),
        HeaderForeColor = Color.FromArgb(52, 58, 64),
        RowBackColor = Color.White,
        AlternatingRowBackColor = Color.FromArgb(250, 251, 252),
        SelectionBackColor = Color.FromArgb(0, 120, 212),
        SelectionForeColor = Color.White,
        GridLineColor = Color.FromArgb(234, 236, 239),
        ExpanderColor = Color.FromArgb(108, 117, 125),
        TreeLineColor = Color.FromArgb(206, 212, 218),
        HoverBackColor = Color.FromArgb(241, 243, 245),
        EditorBackColor = Color.White,
        EditorForeColor = Color.FromArgb(33, 37, 41),
        FocusCueColor = Color.FromArgb(100, 0, 120, 212)
    };

    /// <summary>
    /// Modern dark preset (VS Code-like palette).
    /// </summary>
    public static TreeListTheme Dark => new()
    {
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.FromArgb(232, 232, 232),
        HeaderBackColor = Color.FromArgb(45, 45, 48),
        HeaderForeColor = Color.FromArgb(208, 212, 217),
        RowBackColor = Color.FromArgb(37, 37, 38),
        AlternatingRowBackColor = Color.FromArgb(42, 42, 43),
        SelectionBackColor = Color.FromArgb(10, 93, 171),
        SelectionForeColor = Color.White,
        GridLineColor = Color.FromArgb(63, 65, 68),
        ExpanderColor = Color.FromArgb(160, 166, 173),
        TreeLineColor = Color.FromArgb(74, 77, 82),
        HoverBackColor = Color.FromArgb(51, 52, 55),
        EditorBackColor = Color.FromArgb(45, 45, 48),
        EditorForeColor = Color.FromArgb(232, 232, 232),
        FocusCueColor = Color.FromArgb(100, 86, 156, 214)
    };
}

/// <summary>
/// Event arguments for cell edit commit/cancel.
/// </summary>
public sealed class CellEditEventArgs<TModel> : EventArgs
{
    public TModel Model { get; }
    public TreeListColumn<TModel> Column { get; }
    public object? ProposedValue { get; set; }
    public object? OriginalValue { get; }
    public int RowIndex { get; }
    public int ColumnIndex { get; }
    public bool Cancel { get; set; }

    public CellEditEventArgs(
        TModel model,
        TreeListColumn<TModel> column,
        object? proposedValue,
        object? originalValue,
        int rowIndex,
        int columnIndex)
    {
        Model = model;
        Column = column;
        ProposedValue = proposedValue;
        OriginalValue = originalValue;
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
    }
}

/// <summary>
/// Event args for tree node expand/collapse/check notifications.
/// </summary>
public sealed class TreeNodeEventArgs<TModel> : EventArgs
{
    public TModel Model { get; }
    internal object? Node { get; } // internal for future use

    public TreeNodeEventArgs(TModel model, object? node)
    {
        Model = model;
        Node = node;
    }
}

/// <summary>
/// Specifies the sort direction for a column in the tree list view.
/// </summary>
public enum SortOrder
{
    /// <summary>No sorting applied (original sibling order is used).</summary>
    None,
    /// <summary>Sort in ascending order.</summary>
    Ascending,
    /// <summary>Sort in descending order.</summary>
    Descending
}
