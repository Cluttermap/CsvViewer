using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using Microsoft.Data.Sqlite;

namespace CsvViewer;

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo HuCulture = new("hu-HU");
    private static readonly CompareInfo HuCompare = HuCulture.CompareInfo;

    private readonly ObservableCollection<CsvRow> _rows = [];
    public  ICollectionView RowsView { get; }

    // ── Filter properties ────────────────────────────────────────────────────
    private string _fDisc = "", _fTrack = "", _fArtist = "", _fTitle = "",
                   _fDuration = "", _fInfo = "", _fAlbum = "",
                   _fCdCim = "", _fBeerkDat = "", _fLejDat = "", _fLejIdo = "";

    public string FilterDisc     { get => _fDisc;     set { _fDisc     = value; OnPropertyChanged(); Refresh(); } }
    public string FilterTrack    { get => _fTrack;    set { _fTrack    = value; OnPropertyChanged(); Refresh(); } }
    public string FilterArtist   { get => _fArtist;   set { _fArtist   = value; OnPropertyChanged(); Refresh(); } }
    public string FilterTitle    { get => _fTitle;    set { _fTitle    = value; OnPropertyChanged(); Refresh(); } }
    public string FilterDuration { get => _fDuration; set { _fDuration = value; OnPropertyChanged(); Refresh(); } }
    public string FilterInfo     { get => _fInfo;     set { _fInfo     = value; OnPropertyChanged(); Refresh(); } }
    public string FilterAlbum    { get => _fAlbum;    set { _fAlbum    = value; OnPropertyChanged(); Refresh(); } }
    public string FilterCdCim    { get => _fCdCim;    set { _fCdCim    = value; OnPropertyChanged(); Refresh(); } }
    public string FilterBeerkDat { get => _fBeerkDat; set { _fBeerkDat = value; OnPropertyChanged(); Refresh(); } }
    public string FilterLejDat   { get => _fLejDat;   set { _fLejDat   = value; OnPropertyChanged(); Refresh(); } }
    public string FilterLejIdo   { get => _fLejIdo;   set { _fLejIdo   = value; OnPropertyChanged(); Refresh(); } }

    // ── Sort indicators (SI1–SI11) ────────────────────────────────────────
    private readonly string[] _si = new string[12];
    public string SI1  => _si[1];  public string SI2  => _si[2];  public string SI3  => _si[3];
    public string SI4  => _si[4];  public string SI5  => _si[5];  public string SI6  => _si[6];
    public string SI7  => _si[7];  public string SI8  => _si[8];  public string SI9  => _si[9];
    public string SI10 => _si[10]; public string SI11 => _si[11];

    private string _si1Sub = "";
    private string _si2Sub = "";
    public string SI1Sub => _si1Sub;
    public string SI2Sub => _si2Sub;

    // ── Status / loading ─────────────────────────────────────────────────
    private string _statusText = "Kész";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; OnPropertyChanged(); } }

    // ── Sort state ────────────────────────────────────────────────────────
    private int               _sortCol;
    private ListSortDirection _sortDir = ListSortDirection.Ascending;

    private static readonly string[] ColNames =
        ["", "Lemez", "Track", "Előadó", "Cím", "Hossz", "Stílus", "Kiadó", "CD Cím", "Beérk.", "Lej.d", "Lej.i"];

    public MainViewModel()
    {
        RowsView = CollectionViewSource.GetDefaultView(_rows);
        RowsView.Filter = FilterPredicate;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public string? CurrentTable  { get; private set; }
    public string? CurrentDbPath { get; private set; }

    public IEnumerable<CsvRow> GetAllRows() => _rows;

    public void RemoveRow(CsvRow row)
    {
        _rows.Remove(row);
        StatusText = $"{CountFiltered()} / {_rows.Count} sor";
    }

    public void AddRow(CsvRow row)
    {
        _rows.Add(row);
        StatusText = $"{CountFiltered()} / {_rows.Count} sor";
    }

    public void LoadEmptyRows(int count, string tableName, string dbPath)
    {
        CurrentTable  = tableName;
        CurrentDbPath = dbPath;
        _rows.Clear();
        for (int i = 0; i < count; i++) _rows.Add(new CsvRow { Track = i + 1 });

        _fDisc = _fTrack = _fArtist = _fTitle = _fDuration = _fInfo = _fAlbum =
            _fCdCim = _fBeerkDat = _fLejDat = _fLejIdo = "";
        RaiseAllFilterProps();

        RowsView.SortDescriptions.Clear();
        if (RowsView is ListCollectionView lcv) lcv.CustomSort = null;
        Array.Fill(_si, "");
        RaiseAllSortIndicators();
        _sortCol = 0;

        RowsView.Refresh();
        StatusText = $"{count} üres sor — {tableName}";
    }

    public async Task LoadTableAsync(string tableName, string dbPath)
    {
        IsLoading  = true;
        StatusText = "Betöltés...";
        CurrentTable  = tableName;
        CurrentDbPath = dbPath;

        var parsed = await Task.Run(() => ReadFromDb(tableName, dbPath));

        _rows.Clear();
        foreach (var r in parsed) _rows.Add(r);

        _fDisc = _fTrack = _fArtist = _fTitle = _fDuration = _fInfo = _fAlbum =
            _fCdCim = _fBeerkDat = _fLejDat = _fLejIdo = "";
        RaiseAllFilterProps();

        ResetToDefaultSort();
        IsLoading  = false;
        StatusText = $"Betöltve {_rows.Count} sor — {tableName}";
    }

    public async Task RefreshTableAsync(string tableName, string dbPath)
    {
        IsLoading = true;

        var fDisc = _fDisc; var fTrack = _fTrack; var fArtist = _fArtist;
        var fTitle = _fTitle; var fDuration = _fDuration; var fInfo = _fInfo;
        var fAlbum = _fAlbum; var fCdCim = _fCdCim; var fBeerkDat = _fBeerkDat;
        var fLejDat = _fLejDat; var fLejIdo = _fLejIdo;
        int savedSortCol = _sortCol;
        var savedSortDir = _sortDir;

        CurrentTable  = tableName;
        CurrentDbPath = dbPath;
        var parsed = await Task.Run(() => ReadFromDb(tableName, dbPath));

        _rows.Clear();
        foreach (var r in parsed) _rows.Add(r);

        _fDisc = fDisc; _fTrack = fTrack; _fArtist = fArtist;
        _fTitle = fTitle; _fDuration = fDuration; _fInfo = fInfo;
        _fAlbum = fAlbum; _fCdCim = fCdCim; _fBeerkDat = fBeerkDat;
        _fLejDat = fLejDat; _fLejIdo = fLejIdo;
        RaiseAllFilterProps();

        if (savedSortCol > 0)
        {
            _sortCol = savedSortCol;
            _sortDir = savedSortDir;
            if (RowsView is ListCollectionView lcv)
                lcv.CustomSort = new HuComparer(savedSortCol, savedSortDir);
            Array.Fill(_si, "");
            _si[savedSortCol] = savedSortDir == ListSortDirection.Ascending ? " ▲" : " ▼";
            _si1Sub = savedSortCol >= 3 ? " ▲" : "";
            _si2Sub = (savedSortCol == 1 || savedSortCol >= 3) ? " ▲" : "";
            OnPropertyChanged(nameof(SI1Sub));
            OnPropertyChanged(nameof(SI2Sub));
            RaiseAllSortIndicators();
        }
        else
        {
            ResetToDefaultSort();
        }

        RowsView.Refresh();
        IsLoading = false;
        StatusText = $"{CountFiltered()} / {_rows.Count} sor";
    }

    public void ClearAllFilters()
    {
        FilterDisc = FilterTrack = FilterArtist = FilterTitle =
            FilterDuration = FilterInfo = FilterAlbum = FilterCdCim =
            FilterBeerkDat = FilterLejDat = FilterLejIdo = "";
    }

    public void ClearSortOnly() => ResetToDefaultSort();

    public void ClearFiltersOnly()
    {
        _fDisc = _fTrack = _fArtist = _fTitle = _fDuration = _fInfo = _fAlbum =
            _fCdCim = _fBeerkDat = _fLejDat = _fLejIdo = "";
        RaiseAllFilterProps();
        RowsView.Refresh();
    }

    public void ClearFiltersAndSort()
    {
        _fDisc = _fTrack = _fArtist = _fTitle = _fDuration = _fInfo = _fAlbum =
            _fCdCim = _fBeerkDat = _fLejDat = _fLejIdo = "";
        RaiseAllFilterProps();
        ResetToDefaultSort();
    }

    private void ResetToDefaultSort()
    {
        _sortCol = 0; // force SortByColumn(1) to treat Disc as a fresh column → ascending
        SortByColumn(1);
    }

    public void SortByColumn(int col)
    {
        if (_sortCol == col)
            _sortDir = _sortDir == ListSortDirection.Ascending
                      ? ListSortDirection.Descending : ListSortDirection.Ascending;
        else
        {
            _sortCol = col;
            _sortDir = ListSortDirection.Ascending;
        }

        if (RowsView is ListCollectionView lcv)
            lcv.CustomSort = new HuComparer(col, _sortDir);
        else
        {
            string prop = col switch
            {
                1  => "Disc",    2  => "Track",    3  => "Artist",
                4  => "Title",   5  => "Duration", 6  => "Info",
                7  => "Album",   8  => "CdCim",    9  => "BeerkDat",
                10 => "LejDat",  _  => "LejIdo"
            };
            RowsView.SortDescriptions.Clear();
            RowsView.SortDescriptions.Add(new SortDescription(prop, _sortDir));
        }

        Array.Fill(_si, "");
        _si[col] = _sortDir == ListSortDirection.Ascending ? " ▲" : " ▼";
        _si1Sub = col >= 3 ? " ▲" : "";
        _si2Sub = (col == 1 || col >= 3) ? " ▲" : "";
        OnPropertyChanged(nameof(SI1Sub));
        OnPropertyChanged(nameof(SI2Sub));
        RaiseAllSortIndicators();

        StatusText = $"{CountFiltered()} / {_rows.Count} sor  —  rendezve: {ColNames[col]}{_si[col]}";
    }

    // ── Filter predicate ──────────────────────────────────────────────────

    private bool FilterPredicate(object obj)
    {
        if (obj is not CsvRow r) return false;
        return MatchNumeric(r.Disc,                        _fDisc)
            && MatchNumeric(r.Track,                       _fTrack)
            && Match(r.Artist,    _fArtist)
            && Match(r.Title,     _fTitle)
            && MatchNumeric(ParseNumericValue(r.Duration), _fDuration)
            && Match(r.Info,      _fInfo)
            && Match(r.Album,     _fAlbum)
            && Match(r.CdCim,     _fCdCim)
            && Match(r.BeerkDat,  _fBeerkDat)
            && Match(r.LejDat,    _fLejDat)
            && Match(r.LejIdo,    _fLejIdo);
    }

    // Numeric filter: supports  5  |  <5  |  >5  |  >3<7  (between, exclusive)
    // For Duration the cell value is already parsed to seconds via ParseNumericValue.
    private static bool MatchNumeric(double cellVal, string filter)
    {
        filter = filter.Trim();
        if (filter.Length == 0) return true;
        if (double.IsNaN(cellVal)) return false;

        if (filter.StartsWith('>'))
        {
            // between: >LO<HI
            int ltIdx = filter.IndexOf('<', 1);
            if (ltIdx > 1)
            {
                double lo = ParseNumericValue(filter[1..ltIdx]);
                double hi = ParseNumericValue(filter[(ltIdx + 1)..]);
                if (!double.IsNaN(lo) && !double.IsNaN(hi))
                    return cellVal > lo && cellVal < hi;
            }
            double gt = ParseNumericValue(filter[1..]);
            return !double.IsNaN(gt) && cellVal > gt;
        }

        if (filter.StartsWith('<'))
        {
            double lt = ParseNumericValue(filter[1..]);
            return !double.IsNaN(lt) && cellVal < lt;
        }

        double exact = ParseNumericValue(filter);
        return !double.IsNaN(exact) && Math.Abs(cellVal - exact) < 0.001;
    }

    internal static double ParseNumericValue(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return double.NaN;
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                CultureInfo.InvariantCulture, out double d)) return d;
        var p = s.Split(':');
        if (p.Length == 2 && int.TryParse(p[0], out int m) && int.TryParse(p[1], out int sc))
            return m * 60 + sc;
        if (p.Length == 3 && int.TryParse(p[0], out int h) && int.TryParse(p[1], out int mm) && int.TryParse(p[2], out int ss))
            return h * 3600 + mm * 60 + ss;
        return double.NaN;
    }

    private static bool Match(string cell, string filter)
    {
        filter = filter.Trim();
        if (filter.Length == 0) return true;

        bool begins = filter.StartsWith('(');
        bool ends   = filter.EndsWith(')');
        string raw  = filter.TrimStart('(').TrimEnd(')');
        if (raw.Length == 0) return true;

        if (begins && ends)
            return HuCompare.IndexOf(cell, raw, CompareOptions.IgnoreCase) >= 0;
        if (begins)
            return HuCompare.IsPrefix(cell, raw, CompareOptions.IgnoreCase);
        if (ends)
            return HuCompare.IsSuffix(cell, raw, CompareOptions.IgnoreCase);

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.All(p => HuCompare.IndexOf(cell, p, CompareOptions.IgnoreCase) >= 0);
    }

    // ── SQLite reader ──────────────────────────────────────────────────────

    private static List<CsvRow> ReadFromDb(string tableName, string dbPath)
    {
        var rows = new List<CsvRow>();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT rowid, CD_SORSZAM, TRACK, ELOADO, SZAM_CIM, SZAM_HOSSZ, " +
            $"STILUS, KIADO, BEERK_DAT, CD_CIM, LEJ_DAT, LEJ_IDO FROM [{tableName}]";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CsvRow
            {
                RowId    = reader.GetInt64(0),
                Disc     = int.TryParse(reader[1] as string, out int d) ? d : 0,
                Track    = int.TryParse(reader[2] as string, out int t) ? t : 0,
                Artist   = reader[3]  as string ?? "",
                Title    = reader[4]  as string ?? "",
                Duration = reader[5]  as string ?? "",
                Info     = reader[6]  as string ?? "",
                Album    = reader[7]  as string ?? "",
                BeerkDat = reader[8]  as string ?? "",
                CdCim    = reader[9]  as string ?? "",
                LejDat   = reader[10] as string ?? "",
                LejIdo   = reader[11] as string ?? "",
            });
        }
        return rows;
    }

    private int CountFiltered()
    {
        int n = 0;
        foreach (var _ in RowsView) n++;
        return n;
    }

    public void RefreshView() => Refresh();

    private void Refresh()
    {
        if (RowsView is System.Windows.Data.ListCollectionView { IsEditingItem: true } or
                        System.Windows.Data.ListCollectionView { IsAddingNew:    true })
            return;
        RowsView.Refresh();
        StatusText = $"{CountFiltered()} / {_rows.Count} sor";
    }

    private void RaiseAllFilterProps()
    {
        OnPropertyChanged(nameof(FilterDisc));    OnPropertyChanged(nameof(FilterTrack));
        OnPropertyChanged(nameof(FilterArtist));  OnPropertyChanged(nameof(FilterTitle));
        OnPropertyChanged(nameof(FilterDuration));OnPropertyChanged(nameof(FilterInfo));
        OnPropertyChanged(nameof(FilterAlbum));   OnPropertyChanged(nameof(FilterCdCim));
        OnPropertyChanged(nameof(FilterBeerkDat));OnPropertyChanged(nameof(FilterLejDat));
        OnPropertyChanged(nameof(FilterLejIdo));
    }

    private void RaiseAllSortIndicators()
    {
        for (int i = 1; i <= 11; i++) OnPropertyChanged($"SI{i}");
    }

    // ── Hungarian comparer ────────────────────────────────────────────────

    private sealed class HuComparer(int col, ListSortDirection dir) : System.Collections.IComparer
    {
        private readonly int _dir = dir == ListSortDirection.Ascending ? 1 : -1;

        public int Compare(object? x, object? y)
        {
            if (x is not CsvRow rx || y is not CsvRow ry) return 0;
            int primary = col switch
            {
                1  => rx.Disc.CompareTo(ry.Disc) switch { 0 => rx.Track.CompareTo(ry.Track), var d => d },
                2  => rx.Track.CompareTo(ry.Track),
                3  => HuCompare.Compare(rx.Artist,   ry.Artist,   CompareOptions.IgnoreCase),
                4  => HuCompare.Compare(rx.Title,    ry.Title,    CompareOptions.IgnoreCase),
                5  => CompareNumeric(rx.Duration, ry.Duration),
                6  => HuCompare.Compare(rx.Info,     ry.Info,     CompareOptions.IgnoreCase),
                7  => HuCompare.Compare(rx.Album,    ry.Album,    CompareOptions.IgnoreCase),
                8  => HuCompare.Compare(rx.CdCim,    ry.CdCim,    CompareOptions.IgnoreCase),
                9  => string.Compare(rx.BeerkDat, ry.BeerkDat, StringComparison.Ordinal),
                10 => string.Compare(rx.LejDat,   ry.LejDat,   StringComparison.Ordinal),
                _  => string.Compare(rx.LejIdo,   ry.LejIdo,   StringComparison.Ordinal),
            };
            if (primary != 0) return primary * _dir;
            // Disc+Track tiebreaker is always ascending regardless of primary sort direction
            return col >= 3 ? DiscTrack(rx, ry) : 0;
        }

        private static int DiscTrack(CsvRow rx, CsvRow ry)
            => rx.Disc.CompareTo(ry.Disc) switch { 0 => rx.Track.CompareTo(ry.Track), var d => d };

        private static int CompareNumeric(string a, string b)
        {
            double da = ParseNumericValue(a), db = ParseNumericValue(b);
            if (!double.IsNaN(da) && !double.IsNaN(db)) return da.CompareTo(db);
            return string.Compare(a, b, StringComparison.Ordinal);
        }
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
