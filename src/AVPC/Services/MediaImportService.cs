using AVPC.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;

namespace AVPC.Services;

public sealed class MediaLibraryItem
{
    public required MediaAsset Asset { get; init; }
    public BitmapImage? Thumbnail { get; init; }
    public required string Details { get; init; }
    public string TagBackground => Asset.Kind switch
    {
        MediaKind.Video => "#FF274D78",
        MediaKind.Audio => "#FF493563",
        MediaKind.Image => "#FF24513F",
        _ => "#FF343942"
    };
    public string TagForeground => Asset.Kind switch
    {
        MediaKind.Video => "#FFBBD7FF",
        MediaKind.Audio => "#FFE6CCFF",
        MediaKind.Image => "#FFBAE8CE",
        _ => "#FFD6D9E0"
    };
    public string KindName => Asset.Kind switch
    {
        MediaKind.Video => "视频",
        MediaKind.Audio => "音频",
        MediaKind.Image => "图片",
        _ => string.Empty
    };
}

public static class MediaImportService
{
    private static readonly string[] Extensions = [".mp4", ".mov", ".mkv", ".avi", ".mp3", ".wav", ".m4a", ".jpg", ".jpeg", ".png", ".bmp"];

    public static async Task<IReadOnlyList<MediaLibraryItem>> ImportAsync(Window owner)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        foreach (var extension in Extensions)
            picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var files = await picker.PickMultipleFilesAsync();
        var items = new List<MediaLibraryItem>();
        foreach (var file in files)
            items.Add(await CreateItemAsync(file));
        return items;
    }

    public static async Task<MediaLibraryItem> LoadItemAsync(MediaAsset asset)
    {
        try
        {
            return await CreateItemAsync(await StorageFile.GetFileFromPathAsync(asset.Path));
        }
        catch
        {
            return new MediaLibraryItem { Asset = asset, Details = "文件不可用", Thumbnail = GetDefaultThumbnail(asset.Kind) };
        }
    }
    private static async Task<MediaLibraryItem> CreateItemAsync(StorageFile file)
    {
        var extension = file.FileType.ToLowerInvariant();
        var kind = extension switch
        {
            ".mp3" or ".wav" or ".m4a" => MediaKind.Audio,
            ".jpg" or ".jpeg" or ".png" or ".bmp" => MediaKind.Image,
            _ => MediaKind.Video
        };
        string details;
        if (kind == MediaKind.Video)
        {
            var properties = await file.Properties.GetVideoPropertiesAsync();
            details = $"{FormatDuration(properties.Duration)} · {properties.Width}×{properties.Height}";
        }
        else if (kind == MediaKind.Audio)
        {
            var properties = await file.Properties.GetMusicPropertiesAsync();
            details = FormatDuration(properties.Duration);
        }
        else
        {
            var properties = await file.Properties.GetImagePropertiesAsync();
            details = $"{properties.Width}×{properties.Height}";
        }

        BitmapImage? thumbnail = GetDefaultThumbnail(kind);
        if (kind != MediaKind.Audio)
        {
            using var stream = await file.GetThumbnailAsync(
                kind == MediaKind.Video ? ThumbnailMode.VideosView : ThumbnailMode.PicturesView, 240);
            if (stream is not null)
            {
                thumbnail = new BitmapImage();
                await thumbnail.SetSourceAsync(stream);
            }
        }
        return new MediaLibraryItem
        {
            Asset = new MediaAsset(file.Path, kind),
            Thumbnail = thumbnail,
            Details = $"{extension.TrimStart('.').ToUpperInvariant()} · {details}"
        };
    }

    private static BitmapImage? GetDefaultThumbnail(MediaKind kind) => kind == MediaKind.Audio
        ? new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "audio.png")))
        : null;
    private static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
}

