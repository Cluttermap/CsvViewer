using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace CsvViewer;

public partial class UjSorWindow : Window
{
    // ── Column state persistence (separate from main window) ─────────────
    private static readonly string SettingsFile =
        Path.Combine(AppContext.BaseDirectory, "ui_settings_ujsor.json");

    private sealed class UjSorSettings
    {
        public Dictionary<string, double> ColumnWidths { get; set; } = [];
        public List<string>               ColumnOrder  { get; set; } = [];
    }

    private static UjSorSettings _colSettings = LoadColSettings();

    private static UjSorSettings LoadColSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var s = JsonSerializer.Deserialize<UjSorSettings>(File.ReadAllText(SettingsFile));
                if (s is not null) return s;
            }
        }
        catch { }
        return new UjSorSettings();
    }

    private void SaveColSettings()
    {
        try
        {
            _colSettings.ColumnWidths.Clear();
            foreach (var col in dataGrid.Columns.OfType<DataGridBoundColumn>())
            {
                var key = BindingPath(col);
                if (key is not null && col.ActualWidth > 0)
                    _colSettings.ColumnWidths[key] = col.ActualWidth;
            }
            _colSettings.ColumnOrder = dataGrid.Columns.OfType<DataGridBoundColumn>()
                .OrderBy(c => c.DisplayIndex)
                .Select(c => BindingPath(c))
                .Where(k => k is not null)
                .ToList()!;
            File.WriteAllText(SettingsFile,
                JsonSerializer.Serialize(_colSettings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void ApplyColSettings()
    {
        foreach (var col in dataGrid.Columns.OfType<DataGridBoundColumn>())
        {
            var key = BindingPath(col);
            if (key is not null && _colSettings.ColumnWidths.TryGetValue(key, out double w) && w > 0)
                col.Width = new DataGridLength(w);
        }
        if (_colSettings.ColumnOrder.Count > 0)
        {
            int next = 1; // index 0 is the delete column
            foreach (var key in _colSettings.ColumnOrder)
            {
                var col = dataGrid.Columns.OfType<DataGridBoundColumn>()
                    .FirstOrDefault(c => BindingPath(c) == key);
                if (col is not null) col.DisplayIndex = next++;
            }
        }
    }

    private static string? BindingPath(DataGridBoundColumn col)
        => (col.Binding as Binding)?.Path.Path;

    // ── Instance state ────────────────────────────────────────────────────
    private readonly MainViewModel _vm;
    private readonly string        _tableName;
    private readonly string        _dbPath;
    private CsvRow?                _watchedRow;   // the current trailing (unfilled) row

    public event Action? RowsSaved;

    public UjSorWindow(string tableName, string dbPath)
    {
        _tableName = tableName;
        _dbPath    = dbPath;
        _vm        = new MainViewModel();
        _vm.LoadEmptyRows(1, tableName, dbPath);  // start with one empty row

        InitializeComponent();
        Title       = $"Új sorok hozzáadása — {tableName}";
        DataContext = _vm;

        Loaded += (_, _) => ApplyColSettings();

        dataGrid.CurrentCellChanged += DataGrid_CurrentCellChanged;
        WatchLastRow();
    }

    // ── Dynamic row growth ────────────────────────────────────────────────
    // As soon as any string field on the trailing row is non-empty, a new
    // trailing row appears (Disc copied, Track incremented).

    private void WatchLastRow()
    {
        if (_watchedRow is not null)
            _watchedRow.PropertyChanged -= TrailingRow_PropertyChanged;

        _watchedRow = _vm.GetAllRows().LastOrDefault();

        if (_watchedRow is not null)
            _watchedRow.PropertyChanged += TrailingRow_PropertyChanged;
    }

    private void TrailingRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CsvRow row || !IsRowFilled(row)) return;

        // Row is now "filled" — stop watching it and append a fresh trailing row
        row.PropertyChanged -= TrailingRow_PropertyChanged;
        _watchedRow = null;

        var next = new CsvRow { Disc = row.Disc, Track = row.Track + 1 };
        _vm.AddRow(next);
        WatchLastRow();

        Dispatcher.BeginInvoke(DispatcherPriority.Background,
            () => dataGrid.ScrollIntoView(next));
    }

    // ── Auto-BeginEdit ────────────────────────────────────────────────────

    private void DataGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (dataGrid.CurrentItem is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (dataGrid.CurrentItem is not null)
                dataGrid.BeginEdit();
        });
    }

    // ── Filter box keyboard handling ──────────────────────────────────────

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
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

    // ── Save / Close ──────────────────────────────────────────────────────

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        dataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var toSave = _vm.GetAllRows().Where(IsRowFilled).ToList();
        if (toSave.Count == 0)
        {
            MessageBox.Show("Nincs kitöltött sor a mentéshez.",
                "Mentés", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Check for (Disc, Track) combinations already in the DB
        var pairs = toSave.Select(r => (r.Disc, r.Track)).ToHashSet();
        var dups  = await Task.Run(() => FindDuplicatePairs(pairs, _tableName, _dbPath));
        if (dups.Count > 0)
        {
            var list = string.Join("\n", dups.OrderBy(p => p.Disc).ThenBy(p => p.Track)
                           .Select(p => $"  Lemez: {p.Disc}, Track: {p.Track}"));
            MessageBox.Show(
                $"A következő Lemez+Track kombinációk már szerepelnek az adatbázisban:\n\n{list}\n\nKérjük javítsd ki, majd próbálj újra menteni.",
                "Duplikált sor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Biztosan mented a {toSave.Count} kitöltött sort az adatbázisba?",
            "Mentés megerősítése",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        // Validate numeric-only columns (Duration is stored as string)
        var invalidDuration = toSave
            .Where(r => !string.IsNullOrWhiteSpace(r.Duration) && !IsNumericValue(r.Duration))
            .ToList();
        if (invalidDuration.Count > 0)
        {
            MessageBox.Show(
                $"{invalidDuration.Count} sorban a 'Hossz' mezo nem ervenyes szam.\n" +
                $"Elfogadott formatumok: 225  vagy  3:45  vagy  1:03:45\n\nKerem javitsd ki, majd probalj ujra.",
                "Érvénytelen adat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        btnSave.IsEnabled        = false;
        saveOverlay.Visibility   = Visibility.Visible;

        await Task.Run(() => InsertToDb(toSave, _tableName, _dbPath));

        saveOverlay.Visibility   = Visibility.Collapsed;
        btnSave.IsEnabled        = true;
        RowsSaved?.Invoke();
        SaveColSettings();

        // Reset to one fresh trailing row
        if (_watchedRow is not null)
        {
            _watchedRow.PropertyChanged -= TrailingRow_PropertyChanged;
            _watchedRow = null;
        }
        _vm.LoadEmptyRows(1, _tableName, _dbPath);
        _vm.StatusText = $"{toSave.Count} sor mentve";
        WatchLastRow();
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not CsvRow row) return;
        if (_watchedRow == row)
        {
            _watchedRow.PropertyChanged -= TrailingRow_PropertyChanged;
            _watchedRow = null;
        }
        _vm.RemoveRow(row);
        WatchLastRow();
        if (!_vm.GetAllRows().Any())
        {
            _vm.AddRow(new CsvRow { Track = 1 });
            WatchLastRow();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnHelp_Click(object sender, RoutedEventArgs e)
    {
        var manual = System.IO.Path.Combine(AppContext.BaseDirectory, "kezikonyv.html");
        if (System.IO.File.Exists(manual))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(manual) { UseShellExecute = true });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        SaveColSettings();

        dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        dataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (!_vm.GetAllRows().Any(IsRowFilled)) return;

        var result = MessageBox.Show(
            "Van nem mentett adat. Biztosan bezárod az ablakot?",
            "Bezárás megerősítése",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // "Filled" = user typed something meaningful beyond the auto-filled Disc/Track.
    private static bool IsRowFilled(CsvRow r)
        => !string.IsNullOrWhiteSpace(r.Artist)   ||
           !string.IsNullOrWhiteSpace(r.Title)    ||
           !string.IsNullOrWhiteSpace(r.Duration) ||
           !string.IsNullOrWhiteSpace(r.Info)     ||
           !string.IsNullOrWhiteSpace(r.Album)    ||
           !string.IsNullOrWhiteSpace(r.CdCim)    ||
           !string.IsNullOrWhiteSpace(r.BeerkDat) ||
           !string.IsNullOrWhiteSpace(r.LejDat)   ||
           !string.IsNullOrWhiteSpace(r.LejIdo);

    private static bool IsNumericValue(string s)
    {
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _)) return true;
        var p = s.Split(':');
        if (p.Length == 2) return int.TryParse(p[0], out _) && int.TryParse(p[1], out _);
        if (p.Length == 3) return int.TryParse(p[0], out _) && int.TryParse(p[1], out _) && int.TryParse(p[2], out _);
        return false;
    }

    private static HashSet<(int Disc, int Track)> FindDuplicatePairs(
        HashSet<(int Disc, int Track)> pairs, string tableName, string dbPath)
    {
        var dups = new HashSet<(int, int)>();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT CD_SORSZAM, TRACK FROM [{tableName}]";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (int.TryParse(reader[0] as string, out int d) &&
                int.TryParse(reader[1] as string, out int t) &&
                pairs.Contains((d, t)))
                dups.Add((d, t));
        }
        return dups;
    }

    private static void InsertToDb(List<CsvRow> rows, string tableName, string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO [{tableName}]
                (CD_SORSZAM, TRACK, ELOADO, SZAM_CIM, SZAM_HOSSZ,
                 STILUS, KIADO, BEERK_DAT, CD_CIM, LEJ_DAT, LEJ_IDO)
            VALUES
                (@disc, @track, @artist, @title, @duration,
                 @info, @album, @beerk, @cdcim, @lejdat, @lejido)
            """;

        cmd.Parameters.Add("@disc",     SqliteType.Text);
        cmd.Parameters.Add("@track",    SqliteType.Text);
        cmd.Parameters.Add("@artist",   SqliteType.Text);
        cmd.Parameters.Add("@title",    SqliteType.Text);
        cmd.Parameters.Add("@duration", SqliteType.Text);
        cmd.Parameters.Add("@info",     SqliteType.Text);
        cmd.Parameters.Add("@album",    SqliteType.Text);
        cmd.Parameters.Add("@beerk",    SqliteType.Text);
        cmd.Parameters.Add("@cdcim",    SqliteType.Text);
        cmd.Parameters.Add("@lejdat",   SqliteType.Text);
        cmd.Parameters.Add("@lejido",   SqliteType.Text);

        foreach (var r in rows)
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
            cmd.ExecuteNonQuery();
        }
    }
}
