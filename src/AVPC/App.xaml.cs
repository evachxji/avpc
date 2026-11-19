using AVPC.Services;
using Microsoft.UI.Xaml;

namespace AVPC;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var displays = DisplayService.GetDisplays();
        var consoleDisplay = displays.Count > 1 ? displays[1] : displays[0];
        var window = new MainWindow();
        DisplayService.PlaceConsole(window, consoleDisplay);
        window.Activate();
        window.InitializeDisplays();
    }
}

