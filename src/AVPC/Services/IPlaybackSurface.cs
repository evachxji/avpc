using Windows.Media.Playback;

namespace AVPC.Services;

public interface IPlaybackSurface
{
    void ShowVideo(MediaPlayer player);

    void ShowImage(string path);

    void ResetFade();

    void BeginFade(TimeSpan duration);

    void PauseFade();

    void ResumeFade();

    void HoldLastFrame();

    void ShowBlackAfterFade();

    void Clear();
}
