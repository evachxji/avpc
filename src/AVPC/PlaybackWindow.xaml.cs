using Microsoft.UI.Xaml;

namespace AVPC;

public partial class PlaybackWindow : Window
{
    public PlaybackWindow()
    {
        InitializeComponent();
        AppWindow.Title = "AVPC 正式输出";
    }
}
