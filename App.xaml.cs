using System.Globalization;
using System.Windows;

namespace CsvViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var hu = new CultureInfo("hu-HU");
        Thread.CurrentThread.CurrentCulture   = hu;
        Thread.CurrentThread.CurrentUICulture = hu;
        CultureInfo.DefaultThreadCurrentCulture   = hu;
        CultureInfo.DefaultThreadCurrentUICulture = hu;
        base.OnStartup(e);
    }
}
