using System.Windows;

namespace CsvViewer;

public record PendingChange(
    string Muvelet,
    string Disc, string Track, string Artist, string Title, string Duration,
    string Info, string Album, string CdCim, string BeerkDat, string LejDat, string LejIdo);

public partial class ConfirmSaveWindow : Window
{
    public ConfirmSaveWindow(IReadOnlyList<PendingChange> changes)
    {
        InitializeComponent();

        changesGrid.ItemsSource = changes;

        int mods = changes.Count(c => c.Muvelet == "Módosítás");
        int dels = changes.Count(c => c.Muvelet == "Törlés");

        var parts = new List<string>();
        if (mods > 0) parts.Add($"{mods} módosítás");
        if (dels > 0) parts.Add($"{dels} törlés");
        txtSummary.Text = string.Join("  ·  ", parts);
        txtStatus.Text  = $"Összesen: {changes.Count} változás";
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void BtnCancel_Click(object sender, RoutedEventArgs e)  => Close();
}
