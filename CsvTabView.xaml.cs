using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CsvViewer;

public partial class CsvTabView : UserControl
{
    private static readonly string SettingsFile =
        Path.Combine(AppContext.BaseDirectory, "ui_settings.json");

    // ── Shared UI settings (widths + visibility) ──────────────────────────
    private sealed class UiSettings
    {
        public Dictionary<string, double> ColumnWidths  { get; set; } = [];
        public List<string>               HiddenColumns { get; set; } = ["BeerkDat", "LejDat", "LejIdo"];
    }

    private static UiSettings _settings = LoadSettings();

    private static UiSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var s = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsFile));
                if (s is not null) return s;
            }
        }
        catch { }
        return new UiSettings();
    }

    private static void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(_settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // ── Row colour (shared) ───────────────────────────────────────────────
    private static readonly string RowColorFile =
        Path.Combine(AppContext.BaseDirectory, "row_color.json");

    private static Color _primaryRowColor = LoadPrimaryColorFromFile();
    private static Color _altRowColor     = DeriveAltColor(_primaryRowColor);

    // ── Instance state ────────────────────────────────────────────────────
    private readonly MainViewModel _vm = new();
    private TextBox?         _lastFocusedFilter;
    private bool             _initialized;
    private bool             _closingHandlerAttached;
    private bool             _widthsSubscribed;
    private bool             _windowClosing;
    private DispatcherTimer? _saveDebounce;

    public CsvTabView()
    {
        InitializeComponent();
        _initialized = true;
        DataContext = _vm;

        AddHandler(GotFocusEvent, new RoutedEventHandler((s, e) =>
        {
            if (e.OriginalSource is TextBox tb) _lastFocusedFilter = tb;
        }));

        dataGrid.CurrentCellChanged += DataGrid_CurrentCellChanged;

        Loaded   += OnViewLoaded;
        Unloaded += OnViewUnloaded;
    }

    public Task LoadTableAsync(string tableName, string dbPath)
    {
        if (_isEditMode) ExitEditMode();
        return _vm.LoadTableAsync(tableName, dbPath);
    }

    // ── View lifecycle ────────────────────────────────────────────────────

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        ApplyColumnWidths();
        ApplyColumnVisibility();
        SyncColumnSelectorCheckboxes();
        ApplyRowColors();

        if (!_closingHandlerAttached && Window.GetWindow(this) is Window w)
        {
            w.Closing += (s, args) =>
            {
                _windowClosing = true;
                _saveDebounce?.Stop();
                if (IsLoaded) SaveColumnWidths();
            };
            _closingHandlerAttached = true;
        }

        if (!_widthsSubscribed)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var dpd = DependencyPropertyDescriptor
                    .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
                foreach (var col in dataGrid.Columns)
                    dpd.AddValueChanged(col, OnColumnWidthChanged);
                _widthsSubscribed = true;
            });
        }
    }

    private void OnViewUnloaded(object sender, RoutedEventArgs e)
    {
        _saveDebounce?.Stop();
        if (!_windowClosing) SaveColumnWidths();
    }

    // ── Column width persistence ──────────────────────────────────────────

    private void ApplyColumnWidths()
    {
        foreach (var col in dataGrid.Columns.OfType<DataGridBoundColumn>())
        {
            var key = BindingPath(col);
            if (key is not null && _settings.ColumnWidths.TryGetValue(key, out double w) && w > 0)
                col.Width = new DataGridLength(w);
        }
    }

    private void SaveColumnWidths()
    {
        try
        {
            bool anyInvalid = false;
            foreach (var col in dataGrid.Columns.OfType<DataGridBoundColumn>())
            {
                if (col.ActualWidth <= 0) { anyInvalid = true; break; }
                var key = BindingPath(col);
                if (key is not null)
                    _settings.ColumnWidths[key] = col.ActualWidth;
            }
            if (!anyInvalid) SaveSettings();
        }
        catch { }
    }

    private void OnColumnWidthChanged(object? sender, EventArgs e)
    {
        if (_windowClosing) return;
        if (_saveDebounce is null)
        {
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveDebounce.Tick += (s, _) => { _saveDebounce.Stop(); SaveColumnWidths(); };
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    // ── Column visibility ─────────────────────────────────────────────────

    private void ApplyColumnVisibility()
    {
        foreach (var col in dataGrid.Columns.OfType<DataGridBoundColumn>())
        {
            var key = BindingPath(col);
            if (key is not null)
                col.Visibility = _settings.HiddenColumns.Contains(key)
                    ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void SyncColumnSelectorCheckboxes()
    {
        foreach (CheckBox cb in colSelectorPanel.Children.OfType<CheckBox>())
        {
            if (cb.Tag is string key)
                cb.IsChecked = !_settings.HiddenColumns.Contains(key);
        }
    }

    private void BtnColumns_Click(object sender, RoutedEventArgs e)
        => colPopup.IsOpen = !colPopup.IsOpen;

    private void ColVis_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        if (sender is not CheckBox cb || cb.Tag is not string key) return;

        var col = dataGrid.Columns.OfType<DataGridBoundColumn>()
            .FirstOrDefault(c => BindingPath(c) == key);
        if (col is null) return;

        bool visible = cb.IsChecked == true;
        col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (visible) _settings.HiddenColumns.Remove(key);
        else if (!_settings.HiddenColumns.Contains(key)) _settings.HiddenColumns.Add(key);

        SaveSettings();
    }

    private static string? BindingPath(DataGridBoundColumn col)
        => (col.Binding as Binding)?.Path.Path;

    // ── Row colour ────────────────────────────────────────────────────────

    private static Color LoadPrimaryColorFromFile()
    {
        Color def = Color.FromRgb(0xFE, 0xFC, 0xF8);
        try
        {
            if (!File.Exists(RowColorFile)) return def;
            string? hex = JsonSerializer.Deserialize<string>(File.ReadAllText(RowColorFile));
            if (hex is { Length: 7 } && hex[0] == '#')
                return Color.FromRgb(
                    Convert.ToByte(hex[1..3], 16),
                    Convert.ToByte(hex[3..5], 16),
                    Convert.ToByte(hex[5..7], 16));
        }
        catch { }
        return def;
    }

    private static void SaveRowColor()
    {
        try
        {
            string hex = $"#{_primaryRowColor.R:X2}{_primaryRowColor.G:X2}{_primaryRowColor.B:X2}";
            Directory.CreateDirectory(Path.GetDirectoryName(RowColorFile)!);
            File.WriteAllText(RowColorFile, JsonSerializer.Serialize(hex));
        }
        catch { }
    }

    private static Color DeriveAltColor(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0, s = 0, h = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if      (max == r) h = ((g - b) / d + (g < b ? 6.0 : 0.0)) / 6.0;
            else if (max == g) h = ((b - r) / d + 2.0) / 6.0;
            else               h = ((r - g) / d + 4.0) / 6.0;
        }
        l = Math.Max(0, l - 0.05);
        if (s == 0) { byte v = (byte)(l * 255); return Color.FromRgb(v, v, v); }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        return Color.FromRgb(
            (byte)(Hue2Rgb(p, q, h + 1.0 / 3) * 255),
            (byte)(Hue2Rgb(p, q, h)            * 255),
            (byte)(Hue2Rgb(p, q, h - 1.0 / 3) * 255));
    }

    private static double Hue2Rgb(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private void ApplyRowColors()
    {
        dataGrid.RowBackground            = new SolidColorBrush(_primaryRowColor);
        dataGrid.AlternatingRowBackground = new SolidColorBrush(_altRowColor);
        rowColorSwatch.Background         = new SolidColorBrush(_primaryRowColor);
    }

    private void BtnRowColor_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(Window.GetWindow(this)).Handle;
        int init = _primaryRowColor.R | (_primaryRowColor.G << 8) | (_primaryRowColor.B << 16);
        var cc = new CHOOSECOLOR
        {
            lStructSize  = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CHOOSECOLOR>(),
            hwndOwner    = hwnd,
            rgbResult    = init,
            lpCustColors = _custColorsPin.AddrOfPinnedObject(),
            Flags        = 0x03,
        };
        if (!ChooseColor(ref cc)) return;
        int rgb = cc.rgbResult;
        _primaryRowColor = Color.FromRgb((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF));
        _altRowColor     = DeriveAltColor(_primaryRowColor);
        ApplyRowColors();
        SaveRowColor();
    }

    [System.Runtime.InteropServices.DllImport("comdlg32.dll")]
    private static extern bool ChooseColor(ref CHOOSECOLOR cc);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CHOOSECOLOR
    {
        public uint   lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int    rgbResult;
        public IntPtr lpCustColors;
        public uint   Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    private static readonly int[] _custColorArr = new int[16];
    private static readonly System.Runtime.InteropServices.GCHandle _custColorsPin =
        System.Runtime.InteropServices.GCHandle.Alloc(_custColorArr,
            System.Runtime.InteropServices.GCHandleType.Pinned);

    // ── Toolbar buttons ───────────────────────────────────────────────────

    private void BtnClear_Click(object sender, RoutedEventArgs e)     => _vm.ClearAllFilters();
    private void BtnClearSort_Click(object sender, RoutedEventArgs e) => _vm.ClearSortOnly();

    private void BtnWrapStart_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFocusedFilter is null) return;
        var t = _lastFocusedFilter.Text;
        _lastFocusedFilter.Text = t.StartsWith('(') ? t[1..] : "(" + t;
        _lastFocusedFilter.Focus();
    }

    private void BtnWrapEnd_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFocusedFilter is null) return;
        var t = _lastFocusedFilter.Text;
        _lastFocusedFilter.Text = t.EndsWith(')') ? t[..^1] : t + ")";
        _lastFocusedFilter.Focus();
    }

    private void BtnWrapBoth_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFocusedFilter is null) return;
        var t = _lastFocusedFilter.Text;
        if (t.StartsWith('(') && t.EndsWith(')'))
            _lastFocusedFilter.Text = t[1..^1];
        else
        {
            if (!t.StartsWith('(')) t = "(" + t;
            if (!t.EndsWith(')'))   t += ")";
            _lastFocusedFilter.Text = t;
        }
        _lastFocusedFilter.Focus();
    }

    // ── Column header click → sort ────────────────────────────────────────

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (sender is not DataGridColumnHeader { Column: not null } header) return;
        int col = dataGrid.Columns.IndexOf(header.Column) + 1;
        if (col >= 1 && col <= 11) DoSort(col);
    }

    // ── Filter box keyboard shortcuts ─────────────────────────────────────

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            e.Handled = true;
            DoClearAndStay();
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (sender is TextBox tb) tb.Text = "";
            dataGrid.Focus();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Down)
        {
            dataGrid.Focus();
            e.Handled = true;
        }
    }

    // ── DataGrid F-key sort ───────────────────────────────────────────────

    private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.F11)
        {
            e.Handled = true;
            if (dataGrid.CurrentColumn is not null && dataGrid.CurrentItem is not null)
                DoF11(dataGrid.CurrentColumn, dataGrid.CurrentItem);
            return;
        }

        if (chkAutoF11.IsChecked == true && (e.Key == Key.Left || e.Key == Key.Right))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (dataGrid.CurrentColumn is not null && dataGrid.CurrentItem is not null)
                    DoF11(dataGrid.CurrentColumn, dataGrid.CurrentItem);
            });
            return;
        }

        if (e.Key == Key.F12)
        {
            e.Handled = true;
            DoClearAndStay();
            return;
        }

        int col = e.Key switch
        {
            Key.F1 => 1, Key.F2 => 2, Key.F3 => 3, Key.F4 => 4,
            Key.F5 => 5, Key.F6 => 6, Key.F7 => 7, Key.F8 => 8,
            _ => 0
        };
        if (col > 0) { e.Handled = true; DoSort(col); }
    }

    // ── F12: clear all, cursor stays ──────────────────────────────────────

    private void DoClearAndStay()
    {
        object?         currentItem   = dataGrid.CurrentItem;
        DataGridColumn? currentColumn = dataGrid.CurrentColumn;

        _vm.ClearFiltersAndSort();

        if (currentItem is null) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            dataGrid.ScrollIntoView(currentItem);
            dataGrid.UpdateLayout();
            ScrollRowToCenter(currentItem);
            dataGrid.UpdateLayout();
            RestoreCellFocus(currentItem, currentColumn);
        });
    }

    private void DoF11(DataGridColumn column, object item)
    {
        int colIdx = dataGrid.Columns.IndexOf(column) + 1;
        if (colIdx < 1 || colIdx > 11) return;

        _vm.ClearFiltersOnly();
        _vm.SortByColumn(colIdx);

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            dataGrid.ScrollIntoView(item);
            dataGrid.UpdateLayout();
            ScrollRowToCenter(item);
            dataGrid.UpdateLayout();
            RestoreCellFocus(item, column);
        });
    }

    private void DoSort(int col)
    {
        object?         currentItem   = dataGrid.CurrentItem;
        DataGridColumn? currentColumn = dataGrid.CurrentColumn;

        _vm.SortByColumn(col);

        if (currentItem is null) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            dataGrid.ScrollIntoView(currentItem);
            dataGrid.UpdateLayout();
            ScrollRowToCenter(currentItem);
            dataGrid.UpdateLayout();
            RestoreCellFocus(currentItem, currentColumn);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void RestoreCellFocus(object item, DataGridColumn? column)
    {
        if (column is null) { dataGrid.Focus(); return; }
        var row = dataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        if (row is null) { dataGrid.Focus(); return; }
        row.ApplyTemplate();
        var presenter = FindChild<DataGridCellsPresenter>(row);
        if (presenter is null) { dataGrid.Focus(); return; }
        int colIdx = dataGrid.Columns.IndexOf(column);
        var cell = presenter.ItemContainerGenerator.ContainerFromIndex(colIdx) as DataGridCell;
        if (cell is null) { dataGrid.Focus(); return; }
        cell.Focus();
        cell.IsSelected = true;
    }

    private void ScrollRowToCenter(object item)
    {
        var sv = FindChild<ScrollViewer>(dataGrid);
        if (sv is null) return;
        int index = 0; bool found = false;
        foreach (var it in _vm.RowsView)
        {
            if (ReferenceEquals(it, item)) { found = true; break; }
            index++;
        }
        if (!found) return;
        sv.ScrollToVerticalOffset(Math.Max(0, index - sv.ViewportHeight / 2.0));
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var hit = FindChild<T>(VisualTreeHelper.GetChild(parent, i));
            if (hit is not null) return hit;
        }
        return null;
    }

    // ── Edit mode ─────────────────────────────────────────────────────────

    private record CsvRowSnapshot(long RowId, int Disc, int Track, string Artist, string Title,
        string Duration, string Info, string Album, string CdCim, string BeerkDat, string LejDat, string LejIdo);

    private Dictionary<long, CsvRowSnapshot>? _editSnapshot;
    private bool _isEditMode;

    private void DataGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (!_isEditMode || dataGrid.CurrentItem is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_isEditMode && dataGrid.CurrentItem is not null)
                dataGrid.BeginEdit();
        });
    }

    private void ChkEdit_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        bool entering = chkEdit.IsChecked == true;
        if (entering)
        {
            _editSnapshot = TakeSnapshot();
            _isEditMode   = true;
            dataGrid.IsReadOnly    = false;
            btnSave.Visibility   = Visibility.Visible;
            btnCancel.Visibility = Visibility.Visible;
        }
        else
        {
            ExitEditMode();
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Biztosan menteni szeretnéd a módosításokat az adatbázisba?",
            "Mentés megerősítése",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        await SaveChangesAsync();
        ExitEditMode();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Biztosan elveted az összes módosítást és visszaállítod az eredeti adatokat?",
            "Szerkesztés visszavonása",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        dataGrid.CancelEdit();
        RevertToSnapshot();
        ExitEditMode();
    }

    private void ExitEditMode()
    {
        _isEditMode          = false;
        _editSnapshot        = null;
        dataGrid.IsReadOnly  = true;
        btnSave.Visibility   = Visibility.Collapsed;
        btnCancel.Visibility = Visibility.Collapsed;
        chkEdit.IsChecked    = false;
    }

    private Dictionary<long, CsvRowSnapshot> TakeSnapshot()
        => _vm.GetAllRows().ToDictionary(r => r.RowId, r => new CsvRowSnapshot(
            r.RowId, r.Disc, r.Track, r.Artist, r.Title,
            r.Duration, r.Info, r.Album, r.CdCim, r.BeerkDat, r.LejDat, r.LejIdo));

    private void RevertToSnapshot()
    {
        if (_editSnapshot is null) return;
        foreach (var row in _vm.GetAllRows())
        {
            if (!_editSnapshot.TryGetValue(row.RowId, out var snap)) continue;
            row.Disc     = snap.Disc;
            row.Track    = snap.Track;
            row.Artist   = snap.Artist;
            row.Title    = snap.Title;
            row.Duration = snap.Duration;
            row.Info     = snap.Info;
            row.Album    = snap.Album;
            row.CdCim    = snap.CdCim;
            row.BeerkDat = snap.BeerkDat;
            row.LejDat   = snap.LejDat;
            row.LejIdo   = snap.LejIdo;
        }
    }

    private async Task SaveChangesAsync()
    {
        if (_editSnapshot is null || _vm.CurrentTable is null || _vm.CurrentDbPath is null) return;
        dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        dataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var dirty = _vm.GetAllRows()
            .Where(r => _editSnapshot.TryGetValue(r.RowId, out var snap) && IsDirty(r, snap))
            .ToList();

        if (dirty.Count == 0) return;

        string table = _vm.CurrentTable;
        string db    = _vm.CurrentDbPath;
        await Task.Run(() => WriteToDb(dirty, table, db));
    }

    private static bool IsDirty(CsvRow r, CsvRowSnapshot s)
        => r.Disc != s.Disc || r.Track != s.Track || r.Artist != s.Artist ||
           r.Title != s.Title || r.Duration != s.Duration || r.Info != s.Info ||
           r.Album != s.Album || r.CdCim != s.CdCim || r.BeerkDat != s.BeerkDat ||
           r.LejDat != s.LejDat || r.LejIdo != s.LejIdo;

    private static void WriteToDb(List<CsvRow> dirty, string tableName, string dbPath)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE [{tableName}] SET
                CD_SORSZAM = @disc, TRACK = @track, ELOADO = @artist, SZAM_CIM = @title,
                SZAM_HOSSZ = @duration, STILUS = @info, KIADO = @album,
                BEERK_DAT = @beerk, CD_CIM = @cdcim, LEJ_DAT = @lejdat, LEJ_IDO = @lejido
            WHERE rowid = @rowid
            """;

        cmd.Parameters.Add("@disc",     Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@track",    Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@artist",   Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@title",    Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@duration", Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@info",     Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@album",    Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@beerk",    Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@cdcim",    Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@lejdat",   Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@lejido",   Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@rowid",    Microsoft.Data.Sqlite.SqliteType.Integer);

        foreach (var r in dirty)
        {
            cmd.Parameters["@disc"].Value     = r.Disc.ToString();
            cmd.Parameters["@track"].Value    = r.Track.ToString();
            cmd.Parameters["@artist"].Value   = r.Artist;
            cmd.Parameters["@title"].Value    = r.Title;
            cmd.Parameters["@duration"].Value = r.Duration;
            cmd.Parameters["@info"].Value     = r.Info;
            cmd.Parameters["@album"].Value    = r.Album;
            cmd.Parameters["@beerk"].Value    = r.BeerkDat;
            cmd.Parameters["@cdcim"].Value    = r.CdCim;
            cmd.Parameters["@lejdat"].Value   = r.LejDat;
            cmd.Parameters["@lejido"].Value   = r.LejIdo;
            cmd.Parameters["@rowid"].Value    = r.RowId;
            cmd.ExecuteNonQuery();
        }
    }
}
