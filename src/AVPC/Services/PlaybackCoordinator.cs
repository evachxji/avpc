using AVPC.Core;
using Microsoft.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AVPC.Services;

public sealed class PlaybackCoordinator
{
    private readonly PlaybackWindow _output;
    private MediaPlayer? _videoPlayer;
    private MediaPlayer? _audioPlayer;
    private AudioPlaylist? _audioPlaylist;
    private DispatcherTimer? _imageTimer;
    private DispatcherTimer? _fadeTimer;
    private ImageSlideshow? _slideshow;
    private PlaybackScheme? _scheme;
    private bool _fadeStarted;

    public PlaybackCoordinator(PlaybackWindow output)
    {
        _output = output;
    }

    public event Action<string>? ErrorOccurred;

    public void Play(PlaybackScheme scheme)
    {
        Stop();
        _scheme = scheme;
        _output.ResetFade();

        if (scheme.Video is not null)
            PlayVideo(scheme);
        else
            PlayImages(scheme);

        if (scheme.Audios.Count > 0)
        {
            _audioPlaylist = new AudioPlaylist(scheme);
            PlayAudio();
        }
    }

    public void Stop()
    {
        _imageTimer?.Stop();
        _fadeTimer?.Stop();
        _imageTimer = null;
        _fadeTimer = null;
        _slideshow = null;
        _scheme = null;
        _fadeStarted = false;
        DisposePlayer(ref _videoPlayer);
        DisposePlayer(ref _audioPlayer);
        _audioPlaylist = null;
        _output.Clear();
    }

    private void PlayAudio()
    {
        if (_audioPlaylist is null) return;
        DisposePlayer(ref _audioPlayer);
        _audioPlayer = CreatePlayer(_audioPlaylist.Current, false, false);
        _audioPlayer.MediaEnded += OnAudioEnded;
        _audioPlayer.Play();
    }

    private void OnAudioEnded(MediaPlayer sender, object args)
    {
        if (_audioPlaylist?.MoveNext() is not null)
            PlayAudio();
        else
            DisposePlayer(ref _audioPlayer);
    }
    private void PlayVideo(PlaybackScheme scheme)
    {
        _videoPlayer = CreatePlayer(scheme.Video!, scheme.IsVideoLooping, scheme.Audios.Count > 0);
        _videoPlayer.MediaEnded += OnVideoEnded;
        _output.ShowVideo(_videoPlayer);
        if (!scheme.IsVideoLooping && scheme.EndBehavior == VideoEndBehavior.FadeToBlack)
        {
            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _fadeTimer.Tick += OnFadeTimerTick;
            _fadeTimer.Start();
        }
        _videoPlayer.Play();
    }

    private void PlayImages(PlaybackScheme scheme)
    {
        _slideshow = new ImageSlideshow(scheme);
        _output.ShowImage(_slideshow.Current.Path);
        if (scheme.Images.Count == 1) return;
        _imageTimer = new DispatcherTimer { Interval = _slideshow.Interval };
        _imageTimer.Tick += OnImageTick;
        _imageTimer.Start();
    }

    private MediaPlayer CreatePlayer(MediaAsset asset, bool loop, bool muted)
    {
        var player = new MediaPlayer
        {
            Source = MediaSource.CreateFromUri(new Uri(asset.Path)),
            IsLoopingEnabled = loop,
            IsMuted = muted,
            Volume = 1
        };
        player.MediaFailed += OnMediaFailed;
        return player;
    }

    private void OnImageTick(object? sender, object e)
    {
        if (_slideshow is not null)
            _output.ShowImage(_slideshow.MoveNext().Path);
    }

    private void OnFadeTimerTick(object? sender, object e)
    {
        if (_videoPlayer is null || _scheme is null || _fadeStarted) return;
        var session = _videoPlayer.PlaybackSession;
        var remaining = session.NaturalDuration - session.Position;
        if (remaining <= TimeSpan.FromSeconds(2))
        {
            _fadeStarted = true;
            _output.BeginFade(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1));
            _fadeTimer?.Stop();
        }
    }

    private void OnVideoEnded(MediaPlayer sender, object args)
    {
        if (_scheme?.EndBehavior == VideoEndBehavior.FadeToBlack)
            _output.ShowBlackAfterFade();
        else
            _output.HoldLastFrame();
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        ErrorOccurred?.Invoke($"媒体播放失败：{args.ErrorMessage}");
    }

    private static void DisposePlayer(ref MediaPlayer? player)
    {
        if (player is null) return;
        player.Pause();
        player.Source = null;
        player.Dispose();
        player = null;
    }
}
