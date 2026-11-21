using AVPC.Core;
using Microsoft.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AVPC.Services;

public sealed class PlaybackCoordinator
{
    public readonly record struct VideoProgress(TimeSpan Position, TimeSpan Duration);

    private readonly PlaybackWindow _output;
    private readonly IPlaybackSurface? _monitor;
    private MediaPlayer? _videoPlayer;
    private MediaPlayer? _monitorVideoPlayer;
    private MediaPlayer? _audioPlayer;
    private AudioPlaylist? _audioPlaylist;
    private DispatcherTimer? _visualTimer;
    private DispatcherTimer? _fadeTimer;
    private VisualPlaylist? _visualPlaylist;
    private PlaybackScheme? _scheme;
    private bool _fadeStarted;
    private bool _isPaused;
    private TimeSpan _remainingVisualDelay;
    private DateTimeOffset _visualDeadline;
    private double _volume = 1;
    private double _playbackRate = 1;

    public PlaybackCoordinator(PlaybackWindow output, IPlaybackSurface? monitor = null)
    {
        _output = output;
        _monitor = monitor;
    }

    public event Action<string>? ErrorOccurred;
    public event Action<VideoProgress?>? VideoProgressChanged;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_videoPlayer is not null) _videoPlayer.Volume = _volume;
            if (_monitorVideoPlayer is not null) _monitorVideoPlayer.Volume = _volume;
            if (_audioPlayer is not null) _audioPlayer.Volume = _volume;
        }
    }

    public double PlaybackRate
    {
        get => _playbackRate;
        set
        {
            _playbackRate = value;
            if (_videoPlayer is not null) _videoPlayer.PlaybackSession.PlaybackRate = _playbackRate;
            if (_monitorVideoPlayer is not null) _monitorVideoPlayer.PlaybackSession.PlaybackRate = _playbackRate;
            if (_audioPlayer is not null) _audioPlayer.PlaybackSession.PlaybackRate = _playbackRate;
        }
    }
    public void Play(PlaybackScheme scheme)
    {
        Stop();
        _scheme = scheme;
        _output.ResetFade();
        _monitor?.ResetFade();
        _visualPlaylist = new VisualPlaylist(scheme);
        PlayCurrentVisual();

        if (scheme.HasIndependentAudio)
        {
            _audioPlaylist = new AudioPlaylist(scheme);
            PlayAudio();
        }
    }

    public void Prepare(PlaybackScheme scheme)
    {
        Play(scheme);
        Pause();
    }

    public void Stop()
    {
        _visualTimer?.Stop();
        _fadeTimer?.Stop();
        _visualTimer = null;
        _fadeTimer = null;
        _visualPlaylist = null;
        _scheme = null;
        _fadeStarted = false;
        _isPaused = false;
        _remainingVisualDelay = TimeSpan.Zero;
        DisposePlayer(ref _videoPlayer);
        DisposePlayer(ref _monitorVideoPlayer);
        DisposePlayer(ref _audioPlayer);
        _audioPlaylist = null;
        _output.Clear();
        _monitor?.Clear();
        VideoProgressChanged?.Invoke(null);
    }

    public void Pause()
    {
        if (_scheme is null || _isPaused) return;
        if (_visualTimer?.IsEnabled == true)
        {
            _remainingVisualDelay = _visualDeadline - DateTimeOffset.UtcNow;
            if (_remainingVisualDelay < TimeSpan.Zero) _remainingVisualDelay = TimeSpan.Zero;
            _visualTimer.Stop();
        }
        _fadeTimer?.Stop();
        _videoPlayer?.Pause();
        _monitorVideoPlayer?.Pause();
        _audioPlayer?.Pause();
        _output.PauseFade();
        _monitor?.PauseFade();
        _isPaused = true;
    }

    public void Resume()
    {
        if (_scheme is null || !_isPaused) return;
        _isPaused = false;
        if (_visualTimer is not null)
        {
            var delay = _remainingVisualDelay > TimeSpan.Zero
                ? _remainingVisualDelay
                : TimeSpan.FromMilliseconds(1);
            StartVisualTimer(delay);
        }
        if (!_fadeStarted) _fadeTimer?.Start();
        _output.ResumeFade();
        _monitor?.ResumeFade();
        _videoPlayer?.Play();
        _monitorVideoPlayer?.Play();
        _audioPlayer?.Play();
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
        DispatchToOutput(() =>
        {
            if (!ReferenceEquals(sender, _audioPlayer)) return;
            if (_audioPlaylist?.MoveNext() is not null)
                PlayAudio();
            else
                DisposePlayer(ref _audioPlayer);
        });
    }

    private void PlayCurrentVisual()
    {
        if (_visualPlaylist is null || _scheme is null) return;
        _visualTimer?.Stop();
        _visualTimer = null;
        _fadeTimer?.Stop();
        _fadeTimer = null;
        _fadeStarted = false;
        DisposePlayer(ref _videoPlayer);
        DisposePlayer(ref _monitorVideoPlayer);
        _output.ResetFade();
        _monitor?.ResetFade();
        if (_visualPlaylist.Current.Kind == MediaKind.Video)
            PlayVideo(_visualPlaylist.Current);
        else
            PlayImage();
    }

    private void PlayVideo(MediaAsset asset)
    {
        if (_scheme is null || _visualPlaylist is null) return;
        _videoPlayer = CreatePlayer(asset, false, _scheme.HasIndependentAudio);
        _videoPlayer.MediaEnded += OnVideoEnded;
        _videoPlayer.PlaybackSession.PositionChanged += OnVideoPositionChanged;
        VideoProgressChanged?.Invoke(new VideoProgress(TimeSpan.Zero, _videoPlayer.PlaybackSession.NaturalDuration));
        _output.ShowVideo(_videoPlayer);
        if (_monitor is not null)
        {
            _monitorVideoPlayer = CreatePlayer(asset, false, muted: true);
            _monitor.ShowVideo(_monitorVideoPlayer);
        }
        if (_visualPlaylist.IsLast && !_scheme.IsLooping && _scheme.EndBehavior == VideoEndBehavior.FadeToBlack)
        {
            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _fadeTimer.Tick += OnFadeTimerTick;
            _fadeTimer.Start();
        }
        _videoPlayer.Play();
        _monitorVideoPlayer?.Play();
    }

    private void PlayImage()
    {
        if (_visualPlaylist is null || _scheme is null) return;
        _output.ShowImage(_visualPlaylist.Current.Path);
        _monitor?.ShowImage(_visualPlaylist.Current.Path);
        VideoProgressChanged?.Invoke(null);
        if (_visualPlaylist.IsLast && !_scheme.IsLooping) return;
        StartVisualTimer(_visualPlaylist.Interval);
    }

    private void StartVisualTimer(TimeSpan delay)
    {
        _visualTimer = new DispatcherTimer { Interval = delay };
        _visualTimer.Tick += OnImageTick;
        _visualDeadline = DateTimeOffset.UtcNow + delay;
        _visualTimer.Start();
    }

    private MediaPlayer CreatePlayer(MediaAsset asset, bool loop, bool muted)
    {
        var player = new MediaPlayer
        {
            Source = MediaSource.CreateFromUri(new Uri(asset.Path)),
            IsLoopingEnabled = loop,
            IsMuted = muted,
            Volume = _volume
        };
        player.PlaybackSession.PlaybackRate = _playbackRate;
        player.MediaFailed += OnMediaFailed;
        return player;
    }

    private void OnImageTick(object? sender, object e)
    {
        if (_visualPlaylist?.MoveNext() is not null) PlayCurrentVisual();
        else _visualTimer?.Stop();
    }

    private void OnFadeTimerTick(object? sender, object e)
    {
        if (_videoPlayer is null || _scheme is null || _visualPlaylist?.IsLast != true || _fadeStarted) return;
        var session = _videoPlayer.PlaybackSession;
        var fade = FadeTiming.GetFadeDuration(session.Position, session.NaturalDuration, false, _scheme.EndBehavior);
        if (fade is not TimeSpan duration) return;
        _fadeStarted = true;
        _output.BeginFade(duration);
        _monitor?.BeginFade(duration);
        _fadeTimer?.Stop();
    }

    private void OnVideoEnded(MediaPlayer sender, object args)
    {
        DispatchToOutput(() =>
        {
            if (!ReferenceEquals(sender, _videoPlayer) || _visualPlaylist is null) return;
            if (_visualPlaylist.ShouldReplayCurrentAtEnd)
            {
                sender.PlaybackSession.Position = TimeSpan.Zero;
                sender.Play();
                if (_monitorVideoPlayer is not null)
                {
                    _monitorVideoPlayer.PlaybackSession.Position = TimeSpan.Zero;
                    _monitorVideoPlayer.Play();
                }
                return;
            }
            if (_visualPlaylist.MoveNext() is not null)
            {
                PlayCurrentVisual();
                return;
            }
            VideoProgressChanged?.Invoke(null);
            if (_scheme?.EndBehavior == VideoEndBehavior.FadeToBlack)
            {
                _output.ShowBlackAfterFade();
                _monitor?.ShowBlackAfterFade();
            }
            else
            {
                _output.HoldLastFrame();
                _monitor?.HoldLastFrame();
            }
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatchToOutput(() =>
        {
            if (ReferenceEquals(sender, _videoPlayer)) VideoProgressChanged?.Invoke(null);
            ErrorOccurred?.Invoke($"媒体播放失败：{args.ErrorMessage}");
        });
    }

    private void OnVideoPositionChanged(MediaPlaybackSession sender, object args)
    {
        var position = sender.Position;
        var duration = sender.NaturalDuration;
        DispatchToOutput(() =>
        {
            if (!ReferenceEquals(sender, _videoPlayer?.PlaybackSession)) return;
            VideoProgressChanged?.Invoke(new VideoProgress(position, duration));
        });
    }

    private void DispatchToOutput(Action action)
    {
        _output.DispatcherQueue.TryEnqueue(() => action());
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
