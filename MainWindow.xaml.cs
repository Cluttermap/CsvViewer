using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace CsvViewer;

public partial class MainWindow : Window
{
    private static readonly string DefaultDbPath =
        Path.Combine(AppContext.BaseDirectory, "zene_adatbazis.db");

    private static readonly string[] DefaultTables = ["torony2", "torony", "sziszi", "csurka"];

    private readonly List<(string Name, CsvTabView View)> _dbs = [];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(DefaultDbPath)) return;
        foreach (string table in DefaultTables)
            await AddTab(table, DefaultDbPath);
        if (_dbs.Count > 0)
            dbSelector.SelectedIndex = 0;
    }

    private void DbSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        int idx = dbSelector.SelectedIndex;
        contentArea.Content = idx >= 0 ? _dbs[idx].View : null;
    }

    private async void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter           = "SQLite adatbázis (*.db)|*.db|Minden fájl (*.*)|*.*",
            Title            = "Adatbázis megnyitása",
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var table in GetTableNames(dlg.FileName))
            await AddTab(table, dlg.FileName);
    }

    private void BtnCloseDb_Click(object sender, RoutedEventArgs e)
    {
        int idx = dbSelector.SelectedIndex;
        if (idx < 0) return;
        _dbs.RemoveAt(idx);
        dbSelector.Items.RemoveAt(idx);
        if (_dbs.Count > 0)
            dbSelector.SelectedIndex = Math.Min(idx, _dbs.Count - 1);
        else
            contentArea.Content = null;
    }

    private async Task AddTab(string tableName, string dbPath)
    {
        var view = new CsvTabView();
        _dbs.Add((tableName, view));
        dbSelector.Items.Add(tableName);
        dbSelector.SelectedIndex = _dbs.Count - 1;
        await view.LoadTableAsync(tableName, dbPath);
    }

    private static List<string> GetTableNames(string dbPath)
    {
        var tables = new List<string>();
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }
        catch { }
        return tables;
    }
}
