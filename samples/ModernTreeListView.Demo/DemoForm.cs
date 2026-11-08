using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ModernTreeListView.Demo;

// ==================== DEMO MODEL ====================

/// <summary>
/// Immutable record used for both the in-memory org chart and the huge lazily-generated dataset.
/// Demonstrates the control's first-class support for C# records via <c>ReplaceModel</c>.
/// </summary>
public record OrgItem(
    string Id,
    string Name,
    string Type,            // "Department" | "Team" | "Person" | "Contractor"
    string? Role = null,
    int Headcount = 0,
    DateTime? LastActive = null,
    bool Active = true,
    List<OrgItem>? Children = null)
{
    /// <summary>Child items; always non-null (defaults to an empty list).</summary>
    public List<OrgItem> Children { get; init; } = Children ?? [];
}

/// <summary>
/// Demonstrates the full capability of <see cref="ModernTreeListView{TModel}"/>:
/// fluent columns with formatters/alignment, lazy children loading (105,000+ nodes on demand),
/// in-place editing (default + custom editors + validation), sorting, filtering, multi-selection,
/// checkboxes, per-node icons, tree lines, themes, auto-fit columns, and programmatic API
/// (SelectModel, Expand/CollapseAll, ExpandSubtree, BeginEdit, ReplaceModel, Reload).
/// </summary>
public sealed class DemoForm : Form
{
    private readonly ModernTreeListView<OrgItem> _tree;
    private readonly ToolStrip _toolStrip;
    private readonly TextBox _filterBox;
    private readonly Label _statusLabel;

    private List<OrgItem> _org;
    private bool _bigDataMode;
    private bool _darkMode;
    private bool _iconsEnabled = true;
    private int _addCounter;

    private const string ReadyText =
        "Ready • F2/Enter = edit • Tab = next cell • Double-click = edit • Click header = sort • " +
        "Drag header edge = resize • Double-click edge = auto-fit • Type = search • +/-/* = expand/collapse • " +
        "Space = check • Ctrl+A = select all • F5 = rebuild";

    // ==================== ICONS (drawn in code, no resources needed) ====================

    private static readonly Dictionary<string, Image> Icons = new()
    {
        ["Department"] = CreateFolderIcon(Color.FromArgb(244, 180, 76)),
        ["Team"] = CreateTeamIcon(Color.FromArgb(32, 162, 152)),
        ["Person"] = CreatePersonIcon(Color.FromArgb(96, 116, 145)),
        ["Contractor"] = CreatePersonIcon(Color.FromArgb(173, 126, 188))
    };

    /// <summary>Builds the demo UI and wires up every feature of the control.</summary>
    public DemoForm()
    {
        Text = "ModernTreeListView — Full Capability Demo (.NET 8+)";
        Size = new Size(1280, 820);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;

        _org = CreateSampleData();

        // ==================== THE CONTROL ====================
        _tree = new ModernTreeListView<OrgItem>
        {
            Dock = DockStyle.Fill,
            RowHeight = 28,
            HeaderHeight = 36,
            ShowAlternatingRows = true,
            ShowGridLines = false,
            ShowTreeLines = true,
            MultiSelect = true,
            AutoFillLastColumn = true
        };

        ConfigureColumns();
        ConfigureEditing();
        ConfigureOrgMode();
        _tree.SetIconGetter(GetIcon);

        // ==================== CHROME ====================
        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            BorderStyle = BorderStyle.FixedSingle,
            AutoEllipsis = true,
            Text = ReadyText
        };

        _filterBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Filter (matches Name, Type or Role; ancestors of matches stay visible and auto-expand)…"
        };
        _filterBox.TextChanged += (_, _) => ApplyFilter();

        _toolStrip = CreateToolStrip();

        var container = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 0) };
        container.Controls.Add(_tree);

        Controls.Add(container);
        Controls.Add(_filterBox);
        Controls.Add(_toolStrip);
        Controls.Add(_statusLabel);

        WireEvents();
        ApplyChrome();

        // Initial state: open a couple of branches and select the CEO programmatically.
        _tree.Expand(_org[0]);
        _tree.Expand(_org[1]);
        _tree.SelectModel(_org[0].Children[0]);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                _tree.Rebuild();
                SetStatus("Rebuild() — visible rows recomputed from current expand state.");
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape && _filterBox.Focused)
            {
                _filterBox.Clear();
                _tree.Focus();
            }
        };
    }

    // ==================== COLUMNS ====================

    private void ConfigureColumns()
    {
        _tree
            // Tree column: expander + checkbox + icon + text all live here.
            .AddColumn("Name", m => m.Name, width: 300)

            // Custom in-place editor: a drop-down ComboBox with an explicit value extractor.
            .AddColumn("Type", m => m.Type, width: 110, configure: c =>
            {
                c.EditorFactory = (_, value) =>
                {
                    var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    cmb.Items.AddRange(["Department", "Team", "Person", "Contractor"]);
                    cmb.SelectedItem = value as string;
                    if (cmb.SelectedIndex < 0) cmb.SelectedIndex = 2;
                    return cmb;
                };
                c.EditorValueExtractor = editor => ((ComboBox)editor).SelectedItem;
            })

            .AddColumn("Role", m => m.Role, width: 190)

            // Numeric column: right-aligned, formatted, edited with the default TextBox (right-aligned automatically).
            .AddColumn("Headcount", m => m.Type is "Person" or "Contractor" ? null : m.Headcount, width: 95, configure: c =>
            {
                c.DataType = typeof(int);
                c.Alignment = HorizontalAlignment.Right;
                c.Formatter = v => v is int i ? i.ToString("N0") : "";
            })

            // DateTime? column: DataType keeps the picker when the cell is null (departments and teams).
            .AddColumn("Last Active", m => m.LastActive, width: 145, configure: c =>
            {
                c.DataType = typeof(DateTime?);
                c.Alignment = HorizontalAlignment.Right;
                c.Formatter = v => v is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm") : "";
            })

            // Bool column: gets a CheckBox editor automatically. AutoFillLastColumn stretches it.
            .AddColumn("Active", m => m.Active, width: 80, configure: c =>
            {
                c.DataType = typeof(bool);
                c.Alignment = HorizontalAlignment.Center;
                c.Formatter = v => v is bool b ? (b ? "Yes" : "No") : "";
            });
    }

    // ==================== EDITING (immutable record pattern + validation) ====================

    private void ConfigureEditing()
    {
        _tree.SetCellValueSetter((model, column, value) =>
        {
            OrgItem? updated = column.Title switch
            {
                "Name" when value is string s && !string.IsNullOrWhiteSpace(s)
                    => model with { Name = s.Trim() },
                "Type" when value is string t
                    => model with { Type = t },
                "Role"
                    => model with { Role = string.IsNullOrWhiteSpace(value?.ToString()) ? null : value!.ToString() },
                "Headcount" when int.TryParse(value?.ToString(), out int hc) && hc >= 0
                    => model with { Headcount = hc },
                "Last Active" when value is DateTime dt
                    => model with { LastActive = dt },
                "Last Active" when value is null
                    => model with { LastActive = null },
                "Active" when value is bool b
                    => model with { Active = b },
                _ => null
            };

            if (updated is null || updated == model) return;

            // Keep our own source collections consistent, then swap the instance inside the
            // control while preserving expansion, children and selection.
            ReplaceInSource(_org, model, updated);
            _tree.ReplaceModel(model, updated);
            SetStatus($"Edited \"{column.Title}\" of {updated.Name} (record replaced via ReplaceModel).");
        });

        // Validation hook: veto a commit before the setter runs.
        _tree.CellEditCommitted += (_, e) =>
        {
            if (e.Column.Title == "Name" && string.IsNullOrWhiteSpace(e.ProposedValue?.ToString()))
            {
                e.Cancel = true;
                SetStatus("Edit rejected — Name cannot be empty (vetoed in CellEditCommitted).");
            }
        };

        _tree.CellEditCanceled += (_, e) =>
            SetStatus($"Edit of \"{e.Column.Title}\" canceled — original value kept.");
    }

    // ==================== DATA MODES ====================

    /// <summary>In-memory org chart: children come from the record's own list.</summary>
    private void ConfigureOrgMode()
    {
        _bigDataMode = false;
        _tree
            .SetChildrenGetter(m => m.Children)
            .SetHasChildrenGetter(m => m.Children.Count > 0)
            .SetRoots(_org);
    }

    /// <summary>
    /// Huge dataset: 100 departments × 50 teams × 20 people = 105,100 nodes.
    /// Nothing is materialized up front — children are synthesized only when a node is
    /// first expanded, and SetHasChildrenGetter avoids enumerating just to draw expanders.
    /// </summary>
    private void ConfigureBigDataMode()
    {
        _bigDataMode = true;
        _tree
            .SetChildrenGetter(CreateSyntheticChildren)
            .SetHasChildrenGetter(m => m.Type != "Person")
            .SetRoots(Enumerable.Range(1, 100).Select(d =>
                new OrgItem($"D{d}", $"Department {d:000}", "Department", Headcount: 1000)));
    }

    private static IEnumerable<OrgItem> CreateSyntheticChildren(OrgItem parent)
    {
        string[] roles = ["Engineer", "Senior Engineer", "Designer", "Analyst", "QA", "Manager"];

        return parent.Type switch
        {
            "Department" => Enumerable.Range(1, 50).Select(t =>
                new OrgItem($"{parent.Id}-T{t}", $"Team {t:00}", "Team", Headcount: 20)),

            "Team" => Enumerable.Range(1, 20).Select(p =>
                new OrgItem(
                    $"{parent.Id}-P{p}",
                    $"Member {parent.Id}-{p:00}",
                    "Person",
                    Role: roles[(parent.Id.Length * 7 + p) % roles.Length],
                    LastActive: DateTime.Now.AddHours(-((p * 13) % 200)),
                    Active: p % 7 != 0)),

            _ => []
        };
    }

    // ==================== FILTERING ====================

    private void ApplyFilter()
    {
        string query = _filterBox.Text.Trim();
        if (query.Length == 0)
        {
            _tree.ClearFilter();
            SetStatus("Filter cleared (expansion state from the filter is kept).");
            return;
        }

        _tree.SetFilter(m =>
            m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.Type.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (m.Role?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        SetStatus(_bigDataMode
            ? $"Filter \"{query}\" applied — note: filtering evaluates descendants, so the full lazy tree was materialized."
            : $"Filter \"{query}\" applied — matches and their ancestors stay visible; ancestors auto-expanded.");
    }

    // ==================== TOOLBAR ====================

    private ToolStrip CreateToolStrip()
    {
        var strip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2)
        };

        // ----- Appearance -----
        var appearance = new ToolStripDropDownButton("Appearance");
        appearance.DropDownItems.Add(MakeToggle("Dark Theme", false, on =>
        {
            _darkMode = on;
            _tree.ApplyTheme(on ? TreeListTheme.Dark : TreeListTheme.Light);
            ApplyChrome();
            SetStatus($"ApplyTheme(TreeListTheme.{(on ? "Dark" : "Light")}) — every color is also individually settable.");
        }));
        appearance.DropDownItems.Add(MakeToggle("Tree Connector Lines", true, on => _tree.ShowTreeLines = on));
        appearance.DropDownItems.Add(MakeToggle("Grid Lines", false, on => { _tree.ShowGridLines = on; _tree.Invalidate(); }));
        appearance.DropDownItems.Add(MakeToggle("Alternating Row Shading", true, on => { _tree.ShowAlternatingRows = on; _tree.Invalidate(); }));
        appearance.DropDownItems.Add(MakeToggle("Row Icons", true, on =>
        {
            _iconsEnabled = on;
            _tree.SetIconGetter(GetIcon);
        }));
        appearance.DropDownItems.Add(MakeToggle("Auto-Fill Last Column", true, on => _tree.AutoFillLastColumn = on));
        appearance.DropDownItems.Add(MakeToggle("Compact Rows", false, on => _tree.RowHeight = on ? 22 : 28));
        appearance.DropDownItems.Add(new ToolStripSeparator());
        appearance.DropDownItems.Add(MakeAction("Auto-Fit All Columns", () =>
        {
            for (int i = 0; i < _tree.Columns.Count; i++)
                _tree.AutoFitColumn(i);
            SetStatus("AutoFitColumn() on every column (also: double-click a header divider).");
        }));
        strip.Items.Add(appearance);

        // ----- Tree / selection -----
        var tree = new ToolStripDropDownButton("Tree");
        tree.DropDownItems.Add(MakeAction("Expand All", ExpandAllGuarded));
        tree.DropDownItems.Add(MakeAction("Collapse All", () =>
        {
            _tree.CollapseAll();
            SetStatus("CollapseAll() — every loaded node collapsed.");
        }));
        tree.DropDownItems.Add(MakeAction("Expand Selected Subtree  (*)", () =>
        {
            if (_tree.SelectedModel is { } sel)
            {
                _tree.ExpandSubtree(sel);
                SetStatus($"ExpandSubtree({sel.Name}) — whole branch opened, loading children on demand.");
            }
            else SetStatus("Select a row first.");
        }));
        tree.DropDownItems.Add(new ToolStripSeparator());
        tree.DropDownItems.Add(MakeToggle("Multi-Select (Ctrl/Shift)", true, on =>
        {
            _tree.MultiSelect = on;
            SetStatus(on ? "Multi-select on: Ctrl+Click toggles, Shift+Click/arrows extend, Ctrl+A selects all." : "Multi-select off.");
        }));
        tree.DropDownItems.Add(MakeToggle("Checkboxes", false, on =>
        {
            _tree.ShowCheckBoxes = on;
            SetStatus(on ? "Checkboxes on: click the box or press Space (toggles every selected row at once)." : "Checkboxes off.");
        }));
        tree.DropDownItems.Add(MakeAction("Check Selected Rows (SetChecked)", () =>
        {
            var models = _tree.SelectedModels;
            if (models.Count == 0) { SetStatus("Select one or more rows first."); return; }
            _tree.ShowCheckBoxes = true;
            foreach (var m in models)
                _tree.SetChecked(m, true);
            SetStatus($"SetChecked(model, true) applied to {models.Count} row(s) programmatically.");
        }));
        tree.DropDownItems.Add(new ToolStripSeparator());
        tree.DropDownItems.Add(MakeAction("Select the CEO (SelectModel)", () =>
        {
            if (_bigDataMode) { SetStatus("Switch back to the org sample first (Data menu)."); return; }
            _tree.SelectModel(_org[0].Children[0]);
            SetStatus("SelectModel() — ancestors were expanded automatically and the row scrolled into view.");
        }));
        tree.DropDownItems.Add(MakeAction("Clear Selection", () => _tree.ClearSelection()));
        strip.Items.Add(tree);

        // ----- Data -----
        var data = new ToolStripDropDownButton("Data");
        data.DropDownItems.Add(MakeAction("Add Person Under Selected", AddPersonToSelected));
        data.DropDownItems.Add(MakeAction("Remove Selected", RemoveSelected));
        data.DropDownItems.Add(MakeAction("Rename Selected (BeginEdit)", () =>
        {
            if (_tree.SelectedRowIndex >= 0)
                _tree.BeginEdit(_tree.SelectedRowIndex, 0);
            else
                SetStatus("Select a row first.");
        }));
        data.DropDownItems.Add(new ToolStripSeparator());
        data.DropDownItems.Add(MakeAction("Reload (structural refresh)", () =>
        {
            _tree.Reload();
            SetStatus("Reload() — children re-queried from the getter; expansion state reset.");
        }));
        data.DropDownItems.Add(new ToolStripSeparator());
        data.DropDownItems.Add(MakeAction("Load Huge Dataset (105,100 lazy nodes)", () =>
        {
            ConfigureBigDataMode();
            SetStatus("105,100 nodes available — none loaded yet. Expand anything: children are synthesized on first expand.");
        }));
        data.DropDownItems.Add(MakeAction("Load Org Sample Data", () =>
        {
            ConfigureOrgMode();
            _tree.Expand(_org[0]);
            _tree.SelectModel(_org[0].Children[0]);
            SetStatus("Org sample restored.");
        }));
        strip.Items.Add(data);

        // ----- Sort -----
        var sort = new ToolStripDropDownButton("Sort");
        sort.DropDownItems.Add(MakeAction("By Name ↑", () => _tree.Sort(0, SortOrder.Ascending)));
        sort.DropDownItems.Add(MakeAction("By Headcount ↓", () => _tree.Sort(3, SortOrder.Descending)));
        sort.DropDownItems.Add(MakeAction("By Last Active ↓", () => _tree.Sort(4, SortOrder.Descending)));
        sort.DropDownItems.Add(new ToolStripSeparator());
        sort.DropDownItems.Add(MakeAction("Clear Sort", () => _tree.ClearSort()));
        strip.Items.Add(sort);

        strip.Items.Add(new ToolStripSeparator());

        // ----- Inspection -----
        strip.Items.Add(MakeButton("Selected…", () =>
        {
            var sel = _tree.SelectedModels;
            MessageBox.Show(
                $"SelectedModels: {sel.Count}\n\n" + string.Join("\n", sel.Take(15).Select(m => "• " + m.Name)) +
                (sel.Count > 15 ? "\n…" : ""),
                "Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }));
        strip.Items.Add(MakeButton("Checked…", () =>
        {
            var chk = _tree.CheckedModels;
            MessageBox.Show(
                $"CheckedModels: {chk.Count}\n\n" + string.Join("\n", chk.Take(15).Select(m => "• " + m.Name)) +
                (chk.Count > 15 ? "\n…" : ""),
                "Checked Items", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }));

        return strip;
    }

    private void ExpandAllGuarded()
    {
        if (_bigDataMode &&
            MessageBox.Show(
                "Expand All in the huge dataset materializes all 105,100 nodes. Continue?",
                "Expand All", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _tree.ExpandAll();
        SetStatus("ExpandAll() — every node expanded, children loaded on demand.");
    }

    // ==================== STRUCTURAL CHANGES (add / remove) ====================

    private void AddPersonToSelected()
    {
        if (_bigDataMode) { SetStatus("Structural editing is demoed on the org sample (Data → Load Org Sample Data)."); return; }

        var sel = _tree.SelectedModel;
        if (sel is null || sel.Type is "Person" or "Contractor")
        {
            SetStatus("Select a Department or Team to add a person under.");
            return;
        }

        var person = new OrgItem(
            Guid.NewGuid().ToString("N")[..8],
            $"New Member {++_addCounter}",
            "Person",
            Role: "Engineer",
            LastActive: DateTime.Now);

        sel.Children.Add(person);

        // Structural change in the source -> Reload, then re-open the path and select the new row.
        _tree.Reload();
        ExpandPathTo(person);
        _tree.SelectModel(person);
        SetStatus($"Added {person.Name} under {sel.Name}; Reload() + SelectModel() restored the view.");
    }

    private void RemoveSelected()
    {
        if (_bigDataMode) { SetStatus("Structural editing is demoed on the org sample (Data → Load Org Sample Data)."); return; }

        var sel = _tree.SelectedModel;
        if (sel is null) { SetStatus("Select a row to remove."); return; }

        var path = new List<OrgItem>();
        if (!TryFindPath(_org, sel, path)) return;

        var parent = path.Count > 1 ? path[^2] : null;
        if (parent != null) parent.Children.Remove(sel);
        else _org.Remove(sel);

        _tree.Reload();
        if (parent != null)
        {
            ExpandPathTo(parent);
            _tree.SelectModel(parent);
        }
        SetStatus($"Removed {sel.Name}" + (parent != null ? $" from {parent.Name}." : " (root)."));
    }

    private void ExpandPathTo(OrgItem target)
    {
        var path = new List<OrgItem>();
        if (!TryFindPath(_org, target, path)) return;
        foreach (var ancestor in path.Take(path.Count - 1))
            _tree.Expand(ancestor);
    }

    private static bool TryFindPath(List<OrgItem> roots, OrgItem target, List<OrgItem> path)
    {
        foreach (var item in roots)
        {
            path.Add(item);
            if (ReferenceEquals(item, target)) return true;
            if (TryFindPath(item.Children, target, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    private static bool ReplaceInSource(List<OrgItem> list, OrgItem oldItem, OrgItem newItem)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], oldItem))
            {
                list[i] = newItem;
                return true;
            }
            if (ReplaceInSource(list[i].Children, oldItem, newItem))
                return true;
        }
        return false;
    }

    // ==================== EVENTS ====================

    private void WireEvents()
    {
        _tree.SelectionChanged += (_, _) =>
        {
            var sel = _tree.SelectedModels;
            SetStatus(sel.Count switch
            {
                0 => "Nothing selected.",
                1 => $"Selected: {sel[0].Name}  •  {sel[0].Type}" + (sel[0].Role is { } r ? $"  •  {r}" : ""),
                _ => $"{sel.Count} rows selected (SelectedModels, in visible order)."
            });
        };

        _tree.NodeExpanded += (_, e) => SetStatus($"NodeExpanded: {e.Model.Name}");
        _tree.NodeCollapsed += (_, e) => SetStatus($"NodeCollapsed: {e.Model.Name}");

        _tree.CheckedChanged += (_, e) =>
            SetStatus($"CheckedChanged: {e.Model.Name} → total checked: {_tree.CheckedModels.Count}");

        _tree.ColumnSortChanged += (_, _) =>
            SetStatus(_tree.SortColumn is { } col
                ? $"ColumnSortChanged: \"{_tree.Columns[col].Title}\" {_tree.SortOrder} (stable, per sibling group; click again to cycle)."
                : "ColumnSortChanged: sort cleared — original sibling order restored.");
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    // ==================== THEME-AWARE CHROME ====================

    private void ApplyChrome()
    {
        var theme = _tree.Theme;
        BackColor = theme.BackColor;

        _toolStrip.BackColor = theme.HeaderBackColor;
        _toolStrip.ForeColor = theme.HeaderForeColor;
        foreach (ToolStripItem item in _toolStrip.Items)
            item.ForeColor = theme.HeaderForeColor;

        _statusLabel.BackColor = theme.HeaderBackColor;
        _statusLabel.ForeColor = theme.ForeColor;

        _filterBox.BackColor = theme.EditorBackColor;
        _filterBox.ForeColor = theme.EditorForeColor;
    }

    // ==================== TOOLSTRIP HELPERS ====================

    private static ToolStripMenuItem MakeToggle(string text, bool initial, Action<bool> apply)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = initial };
        item.CheckedChanged += (_, _) => apply(item.Checked);
        return item;
    }

    private static ToolStripMenuItem MakeAction(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripButton MakeButton(string text, Action action)
    {
        var btn = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btn.Click += (_, _) => action();
        return btn;
    }

    // ==================== ICON DRAWING ====================

    private Image? GetIcon(OrgItem m) =>
        _iconsEnabled && Icons.TryGetValue(m.Type, out var img) ? img : null;

    private static Image CreateFolderIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        using var darker = new SolidBrush(ControlPaint.Dark(color, 0.1f));
        g.FillRectangle(darker, 1, 3, 7, 4);
        g.FillRectangle(brush, 1, 5, 14, 9);
        return bmp;
    }

    private static Image CreateTeamIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var back = new SolidBrush(ControlPaint.Light(color, 0.6f));
        using var front = new SolidBrush(color);
        g.FillEllipse(back, 8, 2, 6, 6);
        g.FillEllipse(back, 7, 9, 8, 6);
        g.FillEllipse(front, 2, 3, 7, 7);
        g.FillEllipse(front, 1, 10, 9, 6);
        return bmp;
    }

    private static Image CreatePersonIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 5, 1, 6, 6);
        g.FillEllipse(brush, 2, 9, 12, 9);
        return bmp;
    }

    // ==================== SAMPLE DATA ====================

    private static List<OrgItem> CreateSampleData()
    {
        var now = DateTime.Now;

        var exec = new OrgItem("exec", "Executive", "Department", Headcount: 4,
            Children:
            [
                new OrgItem("ceo", "Dr. Elena Voss", "Person", Role: "CEO", LastActive: now.AddMinutes(-15)),
                new OrgItem("coo", "Marcus Bell", "Person", Role: "COO", LastActive: now.AddHours(-4)),
                new OrgItem("cfo", "Priya Nair", "Person", Role: "CFO", LastActive: now.AddHours(-26))
            ]);

        var engineering = new OrgItem("eng", "Engineering", "Department", Headcount: 87,
            Children:
            [
                new OrgItem("eng-plat", "Platform", "Team", Headcount: 32,
                    Children:
                    [
                        new OrgItem("p1", "Alex Rivera", "Person", Role: "Principal Engineer", LastActive: now.AddHours(-3)),
                        new OrgItem("p2", "Jordan Hale", "Person", Role: "Staff Engineer", LastActive: now.AddDays(-1)),
                        new OrgItem("p3", "Sam Patel", "Person", Role: "Senior Engineer", LastActive: now.AddHours(-9)),
                        new OrgItem("p4", "Nikola Saric", "Contractor", Role: "SRE (contract)", LastActive: now.AddDays(-12), Active: false)
                    ]),
                new OrgItem("eng-app", "Applications", "Team", Headcount: 41,
                    Children:
                    [
                        new OrgItem("a1", "Taylor Kim", "Person", Role: "Engineering Manager", LastActive: now.AddMinutes(-40)),
                        new OrgItem("a2", "Casey Brooks", "Person", Role: "Senior Engineer", LastActive: now.AddHours(-2)),
                        new OrgItem("a3", "Riley Quinn", "Person", Role: "Engineer", LastActive: now.AddDays(-2)),
                        new OrgItem("a4", "Morgan Ellis", "Person", Role: "Engineer", LastActive: now.AddHours(-11))
                    ]),
                new OrgItem("eng-qa", "Quality & Reliability", "Team", Headcount: 14,
                    Children:
                    [
                        new OrgItem("q1", "Drew Santos", "Person", Role: "QA Lead", LastActive: now.AddHours(-5)),
                        new OrgItem("q2", "Ines Fontaine", "Person", Role: "QA Engineer", LastActive: now.AddDays(-3))
                    ])
            ]);

        var design = new OrgItem("des", "Design", "Department", Headcount: 19,
            Children:
            [
                new OrgItem("des-prod", "Product Design", "Team", Headcount: 12,
                    Children:
                    [
                        new OrgItem("d1", "Jamie Torres", "Person", Role: "Design Director", LastActive: now.AddHours(-1)),
                        new OrgItem("d2", "Avery Lane", "Person", Role: "Senior Product Designer", LastActive: now.AddDays(-1))
                    ]),
                new OrgItem("des-brand", "Brand & Marketing", "Team", Headcount: 7)
            ]);

        var people = new OrgItem("hr", "People Operations", "Department", Headcount: 11,
            Children:
            [
                new OrgItem("hr-t1", "People Partners", "Team", Headcount: 5,
                    Children:
                    [
                        new OrgItem("h1", "Cameron West", "Person", Role: "People Partner", LastActive: now.AddHours(-7))
                    ]),
                new OrgItem("hr-recruit", "Talent Acquisition", "Team", Headcount: 6)
            ]);

        return [exec, engineering, design, people];
    }
}
