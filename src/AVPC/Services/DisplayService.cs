using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace AVPC.Services;

public sealed record DisplayOption(DisplayArea Area, string Name);

public static class DisplayService
{
    public static List<DisplayArea> GetDisplays()
    {
        var areas = DisplayArea.FindAll();
        var displays = new List<DisplayArea>(areas.Count);
        for (var i = 0; i < areas.Count; i++)
            displays.Add(areas[i]);
        return displays;
    }

    public static List<DisplayOption> GetDisplayOptions() => GetDisplays()
        .Select((area, index) => new DisplayOption(area, $"{(area.IsPrimary ? "主屏" : "副屏")} {index + 1} · {area.WorkArea.Width}×{area.WorkArea.Height}"))
        .ToList();

    public static void PlaceConsole(Window window, DisplayArea display)
    {
        var area = display.WorkArea;
        const int width = 1480;
        const int height = 900;
        window.AppWindow.Resize(new SizeInt32(Math.Min(width, area.Width), Math.Min(height, area.Height)));
        window.AppWindow.Move(new PointInt32(
            area.X + Math.Max(0, (area.Width - window.AppWindow.Size.Width) / 2),
            area.Y + Math.Max(0, (area.Height - window.AppWindow.Size.Height) / 2)));
    }

    public static void ShowFullScreen(Window window, DisplayArea display)
    {
        var area = display.OuterBounds;
        window.AppWindow.Move(new PointInt32(area.X, area.Y));
        window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }
}

