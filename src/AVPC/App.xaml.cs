using AVPC.Services;
using Microsoft.UI.Xaml;

namespace AVPC;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (sender, args) => Diagnostics.LogException("应用未处理异常", args.Exception);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var displays = DisplayService.GetDisplays();
            var consoleDisplay = displays.Count > 1 ? displays[1] : displays[0];
            var window = new MainWindow();
            DisplayService.PlaceConsole(window, consoleDisplay);
            window.Activate();
            window.InitializeDisplays();
            await window.InitializeWorkspaceAsync();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "AVPC-startup.log"), ex.ToString());
            throw;
        }
    }
}
