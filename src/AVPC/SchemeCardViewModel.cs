using System.ComponentModel;
using System.Runtime.CompilerServices;
using AVPC.Core;
using AVPC.Services;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AVPC;

public sealed record AudioBranchRow(string Prefix, string Name);

public sealed class SchemeCardViewModel : INotifyPropertyChanged
{
    public SchemeCardViewModel(PlaybackScheme scheme)
    {
        Scheme = scheme;
    }

    public PlaybackScheme Scheme { get; }
    public string Name => Scheme.Name;
    public BitmapImage? Thumbnail { get; private set; }
    public IReadOnlyList<AudioBranchRow> AudioRows { get; private set; } = [];
    public string VisualName => Scheme.Video is not null
        ? Scheme.Video.DisplayName
        : Scheme.Images.Count > 1
            ? $"{Scheme.Images.Count} 张图片轮播"
            : Scheme.Images.Count == 1 ? "1 张图片固定" : "拖入视频或图片";

    public bool IsVideoLooping
    {
        get => Scheme.IsVideoLooping;
        set { Scheme.IsVideoLooping = value; OnPropertyChanged(); }
    }

    public bool IsAudioLooping
    {
        get => Scheme.IsAudioLooping;
        set { Scheme.IsAudioLooping = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh(IEnumerable<MediaLibraryItem> media)
    {
        var visual = Scheme.Video ?? Scheme.Images.FirstOrDefault();
        Thumbnail = media.FirstOrDefault(item => item.Asset.Path == visual?.Path)?.Thumbnail;
        AudioRows = Scheme.Audios.Count == 0
            ? [new AudioBranchRow("○", "拖入音频（可选）")]
            : Scheme.Audios.Select((audio, index) => new AudioBranchRow(
                Scheme.Audios.Count == 1 ? "─" : index == Scheme.Audios.Count - 1 ? "└─" : "├─",
                audio.DisplayName)).ToList();
        OnPropertyChanged(null);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
