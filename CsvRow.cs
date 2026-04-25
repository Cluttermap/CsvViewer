using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CsvViewer;

public class CsvRow : INotifyPropertyChanged
{
    public long RowId { get; set; }

    private int _disc;
    public int Disc { get => _disc; set { _disc = value; OnPropertyChanged(); } }

    private int _track;
    public int Track { get => _track; set { _track = value; OnPropertyChanged(); } }

    private string _artist = "";
    public string Artist { get => _artist; set { _artist = value; OnPropertyChanged(); } }

    private string _title = "";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    private string _duration = "";
    public string Duration { get => _duration; set { _duration = value; OnPropertyChanged(); } }

    private string _info = "";
    public string Info { get => _info; set { _info = value; OnPropertyChanged(); } }

    private string _album = "";
    public string Album { get => _album; set { _album = value; OnPropertyChanged(); } }

    private string _cdCim = "";
    public string CdCim { get => _cdCim; set { _cdCim = value; OnPropertyChanged(); } }

    private string _beerkDat = "";
    public string BeerkDat { get => _beerkDat; set { _beerkDat = value; OnPropertyChanged(); } }

    private string _lejDat = "";
    public string LejDat { get => _lejDat; set { _lejDat = value; OnPropertyChanged(); } }

    private string _lejIdo = "";
    public string LejIdo { get => _lejIdo; set { _lejIdo = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
