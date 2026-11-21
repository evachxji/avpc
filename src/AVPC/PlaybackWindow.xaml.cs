using AVPC.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Playback;

namespace AVPC;

public partial class PlaybackWindow : Window, IPlaybackSurface
{
    private Storyboard? _fadeStoryboard;

    public PlaybackWindow()
    {
        InitializeComponent();
        AppWindow.Title = "AVPC 正式输出";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AVPC.ico"));
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

    public void ResetFade()
    {
        _fadeStoryboard?.Stop();
        _fadeStoryboard = null;
        FadeOverlay.Opacity = 0;
    }

    public void BeginFade(TimeSpan duration)
    {
        ResetFade();
        _fadeStoryboard = FadeToBlack(FadeOverlay, duration);
    }

    public void PauseFade() => _fadeStoryboard?.Pause();

    public void ResumeFade() => _fadeStoryboard?.Resume();

    public void HoldLastFrame() => FadeOverlay.Opacity = 0;

    public void ShowBlackAfterFade()
    {
        _fadeStoryboard?.Stop();
        _fadeStoryboard = null;
        FadeOverlay.Opacity = 1;
    }

    public void Clear()
    {
        _fadeStoryboard?.Stop();
        _fadeStoryboard = null;
        VideoView.SetMediaPlayer(null);
        VideoView.Visibility = Visibility.Visible;
        ImageView.Source = null;
        FadeOverlay.Opacity = 1;
    }

    public static Storyboard FadeToBlack(Grid overlay, TimeSpan duration)
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
        return storyboard;
    }
}
