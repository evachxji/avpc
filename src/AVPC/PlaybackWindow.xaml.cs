using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Playback;

namespace AVPC;

public partial class PlaybackWindow : Window
{
    public PlaybackWindow()
    {
        InitializeComponent();
        AppWindow.Title = "AVPC 正式输出";
        Clear();
    }

    public void ShowVideo(MediaPlayer player)
    {
        VideoView.SetMediaPlayer(player);
        VideoView.Visibility = Visibility.Visible;
        ImageView.Visibility = Visibility.Collapsed;
        FadeOverlay.Opacity = 0;
    }

    public void ShowImage(string path)
    {
        VideoView.SetMediaPlayer(null);
        VideoView.Visibility = Visibility.Collapsed;
        ImageView.Source = new BitmapImage(new Uri(path));
        ImageView.Visibility = Visibility.Visible;
        FadeOverlay.Opacity = 0;
    }

    public void ResetFade() => FadeOverlay.Opacity = 0;

    public void BeginFade(TimeSpan duration)
    {
        FadeOverlay.Opacity = 0;
        FadeToBlack(FadeOverlay, duration);
    }

    public void HoldLastFrame() => FadeOverlay.Opacity = 0;

    public void ShowBlackAfterFade() => FadeOverlay.Opacity = 1;

    public void Clear()
    {
        VideoView.SetMediaPlayer(null);
        VideoView.Visibility = Visibility.Visible;
        ImageView.Source = null;
        FadeOverlay.Opacity = 1;
    }

    public static void FadeToBlack(Grid overlay, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            From = overlay.Opacity,
            To = 1,
            Duration = new Duration(duration)
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, overlay);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}