using System.ComponentModel;
using System.Runtime.CompilerServices;
using AVPC.Core;
using AVPC.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AVPC;

public sealed record AudioBranchRow(string Prefix, string Name, SchemeCardViewModel Card, MediaAsset? Audio = null)
{
    public Visibility DeleteVisibility => Audio is null ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class VisualItemRow : INotifyPropertyChanged
{
    public VisualItemRow(int number, string name, string duration, BitmapImage? thumbnail, MediaAsset asset, SchemeCardViewModel card)
    {
        Number = number;
        Name = name;
        Duration = duration;
        Thumbnail = thumbnail;
        Asset = asset;
        Card = card;
    }

    public int Number { get; private set; }
    public string Name { get; }
    public string Duration { get; }
    public BitmapImage? Thumbnail { get; }
    public MediaAsset Asset { get; }
    public SchemeCardViewModel Card { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetNumber(int number)
    {
        if (Number == number) return;
        Number = number;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
    }
}

public sealed record AudioItemRow(
    int Number,
    string Name,
    string Duration,
    BitmapImage? Thumbnail,
    MediaAsset Asset,
    SchemeCardViewModel Card);

public sealed class VisualAppendRow
{
    public static VisualAppendRow Instance { get; } = new();

    private VisualAppendRow()
    {
    }
}

public sealed class AudioAppendRow
{
    public static AudioAppendRow Instance { get; } = new();

    private AudioAppendRow()
    {
    }
}

public sealed class QueueItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? VisualItemTemplate { get; set; }
    public DataTemplate? VisualAppendTemplate { get; set; }
    public DataTemplate? AudioItemTemplate { get; set; }
    public DataTemplate? AudioAppendTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        VisualItemRow => VisualItemTemplate!,
        VisualAppendRow => VisualAppendTemplate!,
        AudioItemRow => AudioItemTemplate!,
        AudioAppendRow => AudioAppendTemplate!,
        _ => throw new InvalidOperationException($"不支持的队列项：{item.GetType().Name}")
    };
}

public sealed class SchemeCardViewModel : INotifyPropertyChanged
{
    public SchemeCardViewModel(PlaybackScheme scheme)
    {
        Scheme = scheme;
    }

    public PlaybackScheme Scheme { get; }
    public string Name => Scheme.Name;
    public BitmapImage? Thumbnail { get; private set; }
    public IReadOnlyList<VisualItemRow> VisualRows { get; private set; } = [];
    public IReadOnlyList<AudioItemRow> AudioItemRows { get; private set; } = [];
    public IReadOnlyList<object> VisualGalleryItems { get; private set; } = [];
    public IReadOnlyList<object> AudioGalleryItems { get; private set; } = [];
    public IReadOnlyList<AudioBranchRow> AudioRows { get; private set; } = [];
    public bool IsRenaming { get; private set; }
    private bool _isHighlighted;
    public string EditingName { get; set; } = string.Empty;
    public Visibility NameVisibility => IsRenaming ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditorVisibility => IsRenaming ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HighlightVisibility => _isHighlighted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LoopVisibility => Scheme.IsLooping ? Visibility.Visible : Visibility.Collapsed;

    public string VisualName
    {
        get
        {
            var imageCount = Scheme.Visuals.Count(item => item.Kind == MediaKind.Image);
            var videoCount = Scheme.Visuals.Count(item => item.Kind == MediaKind.Video);
            if (imageCount + videoCount == 0) return "拖入视频或图片";
            if (imageCount == 0) return $"{videoCount} 个视频";
            if (videoCount == 0) return $"{imageCount} 张图片";
            return $"{imageCount} 张图片 + {videoCount} 个视频";
        }
    }

    public bool IsLooping
    {
        get => Scheme.IsLooping;
        set
        {
            Scheme.IsLooping = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoopVisibility));
            Changed?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public void BeginRename()
    {
        EditingName = Name;
        IsRenaming = true;
        OnPropertyChanged(null);
    }

    public void CommitRename()
    {
        if (!IsRenaming) return;
        Scheme.Name = EditingName;
        IsRenaming = false;
        OnPropertyChanged(null);
        Changed?.Invoke();
    }

    public void CancelRename()
    {
        if (!IsRenaming) return;
        EditingName = Name;
        IsRenaming = false;
        OnPropertyChanged(null);
    }

    public void SetHighlighted(bool isHighlighted)
    {
        if (_isHighlighted == isHighlighted) return;
        _isHighlighted = isHighlighted;
        OnPropertyChanged(nameof(HighlightVisibility));
    }

    public void RefreshSummary(IEnumerable<MediaLibraryItem> media)
    {
        var items = media.ToList();
        var visual = Scheme.Visuals.FirstOrDefault();
        Thumbnail = items.FirstOrDefault(item => item.Asset.Path == visual?.Path)?.Thumbnail;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(VisualName));
    }

    public void Refresh(IEnumerable<MediaLibraryItem> media)
    {
        var items = media.ToList();
        var visual = Scheme.Visuals.FirstOrDefault();
        Thumbnail = items.FirstOrDefault(item => item.Asset.Path == visual?.Path)?.Thumbnail;
        VisualRows = Scheme.Visuals.Select((asset, index) =>
        {
            var item = items.FirstOrDefault(candidate => candidate.Asset.Path == asset.Path);
            var duration = asset.Kind == MediaKind.Image ? Scheme.ImageDurationSeconds : asset.DurationSeconds;
            return new VisualItemRow(index + 1, asset.DisplayName, FormatDuration(duration), item?.Thumbnail, asset, this);
        }).ToList();
        AudioItemRows = Scheme.Audios.Select((audio, index) =>
        {
            var item = items.FirstOrDefault(candidate => candidate.Asset.Path == audio.Path);
            return new AudioItemRow(index + 1, audio.DisplayName, FormatDuration(audio.DurationSeconds), item?.Thumbnail, audio, this);
        }).ToList();
        VisualGalleryItems = [.. VisualRows, VisualAppendRow.Instance];
        AudioGalleryItems = [.. AudioItemRows, AudioAppendRow.Instance];
        AudioRows = Scheme.Audios.Count == 0
            ? [new AudioBranchRow("○", "拖入音频（可选）", this)]
            : Scheme.Audios.Select((audio, index) => new AudioBranchRow(
                Scheme.Audios.Count == 1 ? "─" : index == Scheme.Audios.Count - 1 ? "└─" : "├─",
                audio.DisplayName,
                this,
                audio)).ToList();
        OnPropertyChanged(null);
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "--:--";
        var duration = TimeSpan.FromSeconds(seconds);
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
