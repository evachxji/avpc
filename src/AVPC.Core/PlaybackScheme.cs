namespace AVPC.Core;

public enum VideoEndBehavior
{
    HoldLastFrame,
    FadeToBlack
}

public sealed class PlaybackScheme
{
    public string Name { get; set; } = string.Empty;
    public MediaAsset? Video { get; set; }
    public List<MediaAsset> Images { get; } = [];
    public List<MediaAsset> Audios { get; } = [];
    public bool IsVideoLooping { get; set; }
    public bool IsAudioLooping { get; set; }
    public int ImageDurationSeconds { get; set; } = 5;
    public VideoEndBehavior EndBehavior { get; set; }

    public void AssignMedia(MediaAsset asset)
    {
        switch (asset.Kind)
        {
            case MediaKind.Video:
                Video = asset;
                Images.Clear();
                break;
            case MediaKind.Image:
                Video = null;
                if (!Images.Any(image => string.Equals(image.Path, asset.Path, StringComparison.OrdinalIgnoreCase)))
                    Images.Add(asset);
                break;
            case MediaKind.Audio:
                if (!Audios.Any(audio => string.Equals(audio.Path, asset.Path, StringComparison.OrdinalIgnoreCase)))
                    Audios.Add(asset);
                break;
        }
    }
    public string? Validate()
    {
        if (Video is null && Images.Count == 0)
            return "请选择视频或图片。";
        if (Video is not null && Images.Count > 0)
            return "视频和图片不能同时作为画面来源。";
        if (Images.Count > 0 && ImageDurationSeconds < 1)
            return "每张图片至少需要停留 1 秒。";
        return null;
    }
}


