using System.Collections.ObjectModel;
using AVPC.Core;
using AVPC.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AVPC;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MediaLibraryItem> _mediaItems = [];
    private readonly ObservableCollection<PlaybackScheme> _schemes = [];
    private PlaybackQueue _queue = new([]);
    private List<DisplayOption> _displays = [];
    private bool _loadingEditor;

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "AVPC 播放控台";
        MediaList.ItemsSource = _mediaItems;
        SchemeList.ItemsSource = _schemes;
        VideoSelector.DisplayMemberPath = "Asset.DisplayName";
        AudioSelector.DisplayMemberPath = "Asset.DisplayName";
        ConsoleDisplaySelector.DisplayMemberPath = "Name";
        OutputDisplaySelector.DisplayMemberPath = "Name";
        AppWindow.Closing += OnClosing;
    }

    public DisplayArea SelectedOutputDisplay => (OutputDisplaySelector.SelectedItem as DisplayOption)?.Area
        ?? DisplayService.GetDisplays()[0];

    public void InitializeDisplays()
    {
        _displays = DisplayService.GetDisplayOptions();
        ConsoleDisplaySelector.ItemsSource = _displays;
        OutputDisplaySelector.ItemsSource = _displays;
        ConsoleDisplaySelector.SelectedIndex = _displays.Count > 1 ? 1 : 0;
        OutputDisplaySelector.SelectedItem = _displays.FirstOrDefault(d => d.Area.IsPrimary) ?? _displays[0];
        RefreshEditorSources();
    }

    private PlaybackScheme? SelectedScheme => SchemeList.SelectedItem as PlaybackScheme;

    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var items = await MediaImportService.ImportAsync(this);
            foreach (var item in items)
            {
                if (_mediaItems.Any(existing => string.Equals(existing.Asset.Path, item.Asset.Path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _mediaItems.Add(item);
            }
            RefreshMediaView();
            RefreshEditorSources();
            StatusText.Text = $"素材库 · {_mediaItems.Count} 个素材";
        }
        catch (Exception ex)
        {
            ShowError("无法导入素材", ex);
        }
    }

    private void SearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => RefreshMediaView();

    private void RefreshMediaView()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        MediaList.ItemsSource = _mediaItems
            .Where(item => query.Length == 0 || item.Asset.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RefreshEditorSources()
    {
        var video = VideoSelector.SelectedItem;
        var audio = AudioSelector.SelectedItem;
        VideoSelector.ItemsSource = _mediaItems.Where(item => item.Asset.Kind == MediaKind.Video).ToList();
        AudioSelector.ItemsSource = _mediaItems.Where(item => item.Asset.Kind == MediaKind.Audio).ToList();
        ImageSelector.ItemsSource = _mediaItems.Where(item => item.Asset.Kind == MediaKind.Image).ToList();
        if (!_loadingEditor)
        {
            VideoSelector.SelectedItem = video;
            AudioSelector.SelectedItem = audio;
        }
    }

    private void AddSchemeClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;
        var scheme = new PlaybackScheme { Name = $"方案 {_schemes.Count + 1:00}" };
        _schemes.Add(scheme);
        RefreshQueue();
        SchemeList.SelectedItem = scheme;
        SchemeList.ScrollIntoView(scheme);
        UpdateSchemeCount();
    }

    private void DeleteSchemeClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable() || SelectedScheme is not PlaybackScheme scheme) return;
        StopPreview();
        _schemes.Remove(scheme);
        RefreshQueue();
        UpdateSchemeCount();
        SchemeList.SelectedItem = _schemes.FirstOrDefault();
    }

    private void SchemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadEditor();
        if (!_queue.IsPlaying)
            StopPreview();
    }

    private void LoadEditor()
    {
        _loadingEditor = true;
        var scheme = SelectedScheme;
        if (scheme is null)
        {
            PreviewSchemeText.Text = "尚未选择方案";
            _loadingEditor = false;
            return;
        }

        RefreshEditorSources();
        SchemeNameBox.Text = scheme.Name;
        VisualKindSelector.SelectedIndex = scheme.Video is not null || scheme.Images.Count == 0 ? 0 : 1;
        VideoSelector.SelectedItem = FindMediaItem(VideoSelector, scheme.Video);
        AudioSelector.SelectedItem = FindMediaItem(AudioSelector, scheme.Audio);
        ImageSelector.SelectedItems.Clear();
        foreach (var image in scheme.Images)
        {
            var item = FindMediaItem(ImageSelector, image);
            if (item is not null) ImageSelector.SelectedItems.Add(item);
        }
        ImageDurationBox.Value = scheme.ImageDurationSeconds;
        VideoLoopCheck.IsChecked = scheme.IsVideoLooping;
        AudioLoopCheck.IsChecked = scheme.IsAudioLooping;
        EndBehaviorSelector.SelectedIndex = scheme.EndBehavior == VideoEndBehavior.FadeToBlack ? 1 : 0;
        PreviewSchemeText.Text = scheme.Name;
        _loadingEditor = false;
    }

    private static MediaLibraryItem? FindMediaItem(ListView selector, MediaAsset? asset)
    {
        if (asset is null) return null;
        return selector.Items.OfType<MediaLibraryItem>().FirstOrDefault(item => item.Asset.Path == asset.Path);
    }

    private static MediaLibraryItem? FindMediaItem(ComboBox selector, MediaAsset? asset)
    {
        if (asset is null) return null;
        return selector.Items.OfType<MediaLibraryItem>().FirstOrDefault(item => item.Asset.Path == asset.Path);
    }

    private void SchemeNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null) SelectedScheme.Name = SchemeNameBox.Text;
    }

    private void VisualKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || SelectedScheme is not PlaybackScheme scheme) return;
        if (VisualKindSelector.SelectedIndex == 0)
            scheme.Images.Clear();
        else
            scheme.Video = null;
        RefreshQueue();
    }

    private void VideoChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || SelectedScheme is not PlaybackScheme scheme) return;
        scheme.Video = (VideoSelector.SelectedItem as MediaLibraryItem)?.Asset;
        if (scheme.Video is not null) scheme.Images.Clear();
        RefreshQueue();
    }

    private void ImagesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || SelectedScheme is not PlaybackScheme scheme) return;
        scheme.Images.Clear();
        foreach (var item in ImageSelector.SelectedItems.OfType<MediaLibraryItem>())
            scheme.Images.Add(item.Asset);
        if (scheme.Images.Count > 0) scheme.Video = null;
        RefreshQueue();
    }

    private void ImageDurationChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loadingEditor && SelectedScheme is not null && !double.IsNaN(sender.Value))
            SelectedScheme.ImageDurationSeconds = Math.Max(1, (int)sender.Value);
    }

    private void AudioChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null)
            SelectedScheme.Audio = (AudioSelector.SelectedItem as MediaLibraryItem)?.Asset;
    }

    private void VideoLoopChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null) SelectedScheme.IsVideoLooping = VideoLoopCheck.IsChecked == true;
    }

    private void AudioLoopChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null) SelectedScheme.IsAudioLooping = AudioLoopCheck.IsChecked == true;
    }

    private void EndBehaviorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null)
            SelectedScheme.EndBehavior = EndBehaviorSelector.SelectedIndex == 1 ? VideoEndBehavior.FadeToBlack : VideoEndBehavior.HoldLastFrame;
    }

    private void SchemesReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        RefreshQueue();
        StatusText.Text = "方案顺序已更新";
    }

    private void RefreshQueue()
    {
        if (!_queue.IsPlaying) _queue = new PlaybackQueue(_schemes);
    }

    private void UpdateSchemeCount() => SchemeCountText.Text = $"播放方案 · {_schemes.Count} 套";

    private bool EnsureEditable()
    {
        if (!_queue.IsPlaying) return true;
        ShowMessage("请先停止正式输出，再修改方案结构。", InfoBarSeverity.Warning);
        return false;
    }

    private void ConsoleDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConsoleDisplaySelector.SelectedItem is DisplayOption display && _displays.Count > 0)
            DisplayService.PlaceConsole(this, display.Area);
    }

    private void OutputDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void PreviewClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedScheme?.Validate() is string error)
            ShowMessage(error, InfoBarSeverity.Warning);
    }

    private void StopPreviewClicked(object sender, RoutedEventArgs e) => StopPreview();

    private void StopPreview()
    {
        PreviewView.Source = null;
        PreviewImage.Source = null;
        PreviewFadeOverlay.Opacity = 0;
    }

    private void PlayNextClicked(object sender, RoutedEventArgs e)
    {
        if (_schemes.Count == 0)
            ShowMessage("请先创建并配置至少一套方案。", InfoBarSeverity.Warning);
    }

    private void StopOutputClicked(object sender, RoutedEventArgs e)
    {
        _queue.Stop();
        PlayNextButton.Content = "▶ 开始正式播放";
        ModeText.Text = "●  预览模式";
        StatusText.Text = "正式输出已停止";
    }

    private void ShowError(string title, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(ex);
        ShowMessage($"{title}：{ex.Message}", InfoBarSeverity.Error);
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        MessageBar.Title = "AVPC";
        MessageBar.Message = message;
        MessageBar.Severity = severity;
        MessageBar.IsOpen = true;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        StopPreview();
        Application.Current.Exit();
    }
}

