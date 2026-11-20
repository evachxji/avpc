using System.Text.Json;
using System.Text.Json.Serialization;
namespace AVPC.Core;

public enum VideoEndBehavior
{
    HoldLastFrame,
    FadeToBlack
}

[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class PlaybackScheme
{
    private bool _hasLegacyImages;

    public string Name { get; set; } = string.Empty;
    public List<MediaAsset> Visuals { get; } = [];
    public List<MediaAsset> Audios { get; } = [];
    public bool IsLooping { get; set; }
    public int ImageDurationSeconds { get; set; } = 5;
    public VideoEndBehavior EndBehavior { get; set; }
    [JsonIgnore]
    public bool HasIndependentAudio => Audios.Count > 0;

    public void AppendVisual(MediaAsset asset)
    {
        EnsureVisual(asset);
        if (!Visuals.Any(item => SamePath(item, asset))) Visuals.Add(asset);
    }

    public void ReplaceVisual(MediaAsset asset)
    {
        EnsureVisual(asset);
        Visuals.Clear();
        Visuals.Add(asset);
    }

    public void AssignMedia(MediaAsset asset)
    {
        if (asset.Kind == MediaKind.Audio)
        {
            if (!Audios.Any(item => SamePath(item, asset))) Audios.Add(asset);
        }
        else
            ReplaceVisual(asset);
    }

    public void RemoveVisual(MediaAsset asset) =>
        Visuals.RemoveAll(visual => SamePath(visual, asset));

    public void RemoveAudio(MediaAsset asset) =>
        Audios.RemoveAll(audio => SamePath(audio, asset));

    public void MoveVisual(int oldIndex, int newIndex)
    {
        var asset = Visuals[oldIndex];
        Visuals.RemoveAt(oldIndex);
        Visuals.Insert(newIndex, asset);
    }

    public void MoveAudio(int oldIndex, int newIndex)
    {
        var asset = Audios[oldIndex];
        Audios.RemoveAt(oldIndex);
        Audios.Insert(newIndex, asset);
    }

    public string? Validate()
    {
        if (Visuals.Count == 0)
            return "请拖入视频或图片。";
        if (Visuals.Any(item => item.Kind is not (MediaKind.Video or MediaKind.Image)))
            return "画面队列只能包含视频或图片。";
        if (Visuals.Any(item => item.Kind == MediaKind.Image) && ImageDurationSeconds < 1)
            return "每张图片至少需要停留 1 秒。";
        return null;
    }

    [JsonPropertyName("Video")]
    public MediaAsset? LegacyVideo
    {
        set
        {
            if (value is not null)
            {
                _hasLegacyImages = false;
                ReplaceVisual(value);
            }
        }
    }

    [JsonPropertyName("Images")]
    public JsonElement LegacyImages
    {
        set
        {
            if (value.ValueKind != JsonValueKind.Array || Visuals.Count > 0) return;
            var images = value.Deserialize<MediaAsset[]>() ?? [];
            if (images.Length == 0) return;
            _hasLegacyImages = true;
            IsLooping = true;
            foreach (var image in images) AppendVisual(image);
        }
    }

    [JsonPropertyName("IsVideoLooping")]
    public bool LegacyVideoLooping
    {
        set
        {
            if (!_hasLegacyImages) IsLooping |= value;
        }
    }

    [JsonPropertyName("IsVisualLooping")]
    public bool LegacyVisualLooping
    {
        set => IsLooping |= value;
    }

    [JsonPropertyName("IsAudioLooping")]
    public bool LegacyAudioLooping
    {
        set => IsLooping |= value;
    }

    private static void EnsureVisual(MediaAsset asset)
    {
        if (asset.Kind is not (MediaKind.Video or MediaKind.Image))
            throw new ArgumentException("画面队列只能包含视频或图片。", nameof(asset));
    }

    private static bool SamePath(MediaAsset left, MediaAsset right) =>
        string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
}
