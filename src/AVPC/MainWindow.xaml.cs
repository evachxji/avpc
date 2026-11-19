using System.Collections.ObjectModel;
using AVPC.Core;
using AVPC.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AVPC;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MediaLibraryItem> _mediaItems = [];
    private readonly ObservableCollection<SchemeCardViewModel> _schemes = [];
    private PlaybackQueue _queue = new([]);
    private List<DisplayOption> _displays = [];
    private bool _loadingEditor;
    private PlaybackWindow? _outputWindow;
    private PlaybackCoordinator? _coordinator;
    private MediaPlayer? _previewVideo;
    private MediaPlayer? _previewAudio;
    private AudioPlaylist? _previewAudioPlaylist;
    private DispatcherTimer? _previewTimer;
    private ImageSlideshow? _previewSlideshow;
    private PlaybackScheme? _previewScheme;
    private bool _previewFadeStarted;
    private readonly DispatcherTimer _saveTimer = new();
    private bool _loadingWorkspace;

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "AVPC 播放控台";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AVPC.ico"));
        MediaList.ItemsSource = _mediaItems;
        SchemeList.ItemsSource = _schemes;
        VideoSelector.DisplayMemberPath = "Asset.DisplayName";
        ConsoleDisplaySelector.DisplayMemberPath = "Name";
        OutputDisplaySelector.DisplayMemberPath = "Name";
        AppWindow.Closing += OnClosing;
        PreviewVolume.ValueChanged += OnPreviewVolumeChanged;
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.Tick += (sender, args) => SaveWorkspace();
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

    private SchemeCardViewModel? SelectedCard => SchemeList.SelectedItem as SchemeCardViewModel;
    private PlaybackScheme? SelectedScheme => SelectedCard?.Scheme;

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
            ScheduleSave();
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
        var audioPaths = AudioSelector.SelectedItems.OfType<MediaLibraryItem>().Select(item => item.Asset.Path).ToList();
        VideoSelector.ItemsSource = _mediaItems.Where(item => item.Asset.Kind == MediaKind.Video).ToList();
        AudioSelector.ItemsSource = _mediaItems.Where(item => item.Asset.Kind == MediaKind.Audio).ToList();
        ImageSelector.ItemsSource = _mediaItems.Where(item => item.Asset.Kind == MediaKind.Image).ToList();
        if (!_loadingEditor)
        {
            VideoSelector.SelectedItem = video;
            foreach (var item in AudioSelector.Items.OfType<MediaLibraryItem>().Where(item => audioPaths.Contains(item.Asset.Path)))
                AudioSelector.SelectedItems.Add(item);
        }
    }

    private void MediaDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (!EnsureEditable())
        {
            e.Cancel = true;
            return;
        }
        if (e.Items.OfType<MediaLibraryItem>().FirstOrDefault() is not MediaLibraryItem item)
        {
            e.Cancel = true;
            return;
        }
        StatusText.Text = $"正在拖动 {item.Asset.DisplayName}，请放到方案卡片上";
        e.Data.SetText(item.Asset.Path);
        e.Data.RequestedOperation = DataPackageOperation.Copy;
    }

    private void SchemeDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        if (_queue.IsPlaying)
            e.AcceptedOperation = DataPackageOperation.None;
        else
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "放入方案";
            StatusText.Text = "松开鼠标即可放入当前方案";
        }
        e.Handled = true;
    }
    private async void SchemeDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        if (!EnsureEditable()) return;
        var path = await e.DataView.GetTextAsync();
        var media = _mediaItems.FirstOrDefault(item => string.Equals(item.Asset.Path, path, StringComparison.OrdinalIgnoreCase));
        var card = (sender as FrameworkElement)?.DataContext as SchemeCardViewModel
            ?? (e.OriginalSource as FrameworkElement)?.DataContext as SchemeCardViewModel;
        if (media is null || card is null) return;
        var scheme = card.Scheme;
        scheme.AssignMedia(media.Asset);
        RefreshQueue();
        card.Refresh(_mediaItems);
        SchemeList.SelectedItem = card;
        LoadEditor();
        StatusText.Text = $"已将 {media.Asset.DisplayName} 放入 {scheme.Name}";
        e.Handled = true;
    }
    private void AddSchemeClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;
        var scheme = new PlaybackScheme { Name = $"方案 {_schemes.Count + 1:00}" };
        var card = CreateSchemeCard(scheme);
        _schemes.Add(card);
        RefreshQueue();
        SchemeList.SelectedItem = card;
        SchemeList.ScrollIntoView(card);
        UpdateSchemeCount();
    }

    private void DeleteSchemeClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable() || SelectedScheme is not PlaybackScheme scheme) return;
        StopPreview();
        _schemes.Remove(SelectedCard!);
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
        AudioSelector.SelectedItems.Clear();
        foreach (var audio in scheme.Audios)
        {
            var item = FindMediaItem(AudioSelector, audio);
            if (item is not null) AudioSelector.SelectedItems.Add(item);
        }
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
        if (!_loadingEditor && SelectedScheme is not null)
        {
            SelectedScheme.Name = SchemeNameBox.Text;
            SelectedCard?.Refresh(_mediaItems);
            ScheduleSave();
        }
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
        ScheduleSave();
    }

    private void AudioChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || SelectedScheme is not PlaybackScheme scheme) return;
        scheme.Audios.Clear();
        foreach (var item in AudioSelector.SelectedItems.OfType<MediaLibraryItem>())
            scheme.Audios.Add(item.Asset);
        SelectedCard?.Refresh(_mediaItems);
    }

    private void VideoLoopChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null) SelectedScheme.IsVideoLooping = VideoLoopCheck.IsChecked == true;
        ScheduleSave();
    }

    private void AudioLoopChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null) SelectedScheme.IsAudioLooping = AudioLoopCheck.IsChecked == true;
        ScheduleSave();
    }

    private void EndBehaviorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null)
            SelectedScheme.EndBehavior = EndBehaviorSelector.SelectedIndex == 1 ? VideoEndBehavior.FadeToBlack : VideoEndBehavior.HoldLastFrame;
        ScheduleSave();
    }

    private void SchemesReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        RefreshQueue();
        StatusText.Text = "方案顺序已更新";
    }

    private void RefreshQueue()
    {
        if (!_queue.IsPlaying) _queue = new PlaybackQueue(_schemes.Select(card => card.Scheme));
        ScheduleSave();
        SelectedCard?.Refresh(_mediaItems);
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
        if (_outputWindow is not null && OutputDisplaySelector.SelectedItem is DisplayOption display)
            DisplayService.ShowFullScreen(_outputWindow, display.Area);
    }

    private void PreviewClicked(object sender, RoutedEventArgs e)
    {
        if (_queue.IsPlaying)
        {
            ShowMessage("正式播放期间请使用主屏输出；请先停止输出再预览。", InfoBarSeverity.Warning);
            return;
        }
        if (SelectedScheme is not PlaybackScheme scheme) return;
        if (scheme.Validate() is string error)
        {
            ShowMessage(error, InfoBarSeverity.Warning);
            return;
        }
        try { PlayPreview(scheme); }
        catch (Exception ex) { ShowError("无法预览方案", ex); }
    }

    private void StopPreviewClicked(object sender, RoutedEventArgs e) => StopPreview();

    private void StopPreview()
    {
        _previewTimer?.Stop();
        _previewTimer = null;
        _previewSlideshow = null;
        _previewScheme = null;
        _previewFadeStarted = false;
        DisposePreviewPlayer(ref _previewVideo);
        DisposePreviewPlayer(ref _previewAudio);
        _previewAudioPlaylist = null;
        PreviewView.SetMediaPlayer(null);
        PreviewView.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        PreviewFadeOverlay.Opacity = 0;
        PreviewProgress.Value = 0;
    }

    private void PlayNextClicked(object sender, RoutedEventArgs e)
    {
        if (!_queue.IsPlaying && !ValidateAllSchemes()) return;
        try
        {
            StopPreview();
            EnsureOutputWindow();
            var scheme = _queue.Advance();
            if (scheme is null)
            {
                _coordinator!.Stop();
                PlayNextButton.Content = "▶ 开始正式播放";
                ModeText.Text = "●  预览模式";
                StatusText.Text = "全部方案已播放完毕，正式输出已停止";
                return;
            }
            _coordinator!.Play(scheme);
            PlayNextButton.Content = "▶ 下一套";
            ModeText.Text = "●  正式输出中";
            StatusText.Text = $"正在输出 {scheme.Name} · {(_queue.CurrentIndex ?? 0) + 1}/{_schemes.Count}";
        }
        catch (Exception ex)
        {
            ShowError("正式播放失败", ex);
        }
    }

    private void StopOutputClicked(object sender, RoutedEventArgs e)
    {
        _queue.Stop();
        _coordinator?.Stop();
        SaveWorkspace();
        PlayNextButton.Content = "▶ 开始正式播放";
        ModeText.Text = "●  预览模式";
        StatusText.Text = "正式输出已停止";
    }

    private bool ValidateAllSchemes()
    {
        if (_schemes.Count == 0)
        {
            ShowMessage("请先创建并配置至少一套方案。", InfoBarSeverity.Warning);
            return false;
        }
        foreach (var card in _schemes)
        {
            var scheme = card.Scheme;
            if (scheme.Validate() is string error)
            {
                ShowMessage($"{scheme.Name}：{error}", InfoBarSeverity.Warning);
                return false;
            }
            var paths = scheme.Images.Select(image => image.Path)
                .Concat(scheme.Video is null ? [] : [scheme.Video.Path])
                .Concat(scheme.Audios.Select(audio => audio.Path));
            if (paths.Any(path => !File.Exists(path)))
            {
                ShowMessage($"{scheme.Name}：有素材文件不存在或已被移动。", InfoBarSeverity.Error);
                return false;
            }
        }
        return true;
    }

    private void EnsureOutputWindow()
    {
        if (_outputWindow is null)
        {
            _outputWindow = new PlaybackWindow();
            _coordinator = new PlaybackCoordinator(_outputWindow);
            _coordinator.ErrorOccurred += message => ShowMessage(message, InfoBarSeverity.Error);
        }
        DisplayService.ShowFullScreen(_outputWindow, SelectedOutputDisplay);
        _outputWindow.Activate();
    }

    private void PlayPreview(PlaybackScheme scheme)
    {
        StopPreview();
        _previewScheme = scheme;
        PreviewSchemeText.Text = scheme.Name;
        if (scheme.Video is not null)
        {
            _previewVideo = CreatePreviewPlayer(scheme.Video, scheme.IsVideoLooping, scheme.Audios.Count > 0);
            _previewVideo.MediaEnded += OnPreviewVideoEnded;
            PreviewView.SetMediaPlayer(_previewVideo);
            PreviewView.Visibility = Visibility.Visible;
            PreviewImage.Source = null;
            _previewVideo.Play();
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _previewTimer.Tick += OnPreviewVideoTick;
            _previewTimer.Start();
        }
        else
        {
            PreviewView.SetMediaPlayer(null);
            PreviewView.Visibility = Visibility.Collapsed;
            _previewSlideshow = new ImageSlideshow(scheme);
            PreviewImage.Source = new BitmapImage(new Uri(_previewSlideshow.Current.Path));
            if (scheme.Images.Count > 1)
            {
                _previewTimer = new DispatcherTimer { Interval = _previewSlideshow.Interval };
                _previewTimer.Tick += OnPreviewImageTick;
                _previewTimer.Start();
            }
        }
        if (scheme.Audios.Count > 0)
        {
            _previewAudioPlaylist = new AudioPlaylist(scheme);
            PlayPreviewAudio();
        }
        StatusText.Text = $"正在预览 {scheme.Name}";
    }

    private MediaPlayer CreatePreviewPlayer(MediaAsset asset, bool loop, bool muted)
    {
        var player = new MediaPlayer
        {
            Source = MediaSource.CreateFromUri(new Uri(asset.Path)),
            IsLoopingEnabled = loop,
            IsMuted = muted,
            Volume = PreviewVolume.Value
        };
        player.MediaFailed += (sender, args) => ShowMessage($"预览播放失败：{args.ErrorMessage}", InfoBarSeverity.Error);
        return player;
    }

    private void PlayPreviewAudio()
    {
        if (_previewAudioPlaylist is null) return;
        DisposePreviewPlayer(ref _previewAudio);
        _previewAudio = CreatePreviewPlayer(_previewAudioPlaylist.Current, false, false);
        _previewAudio.MediaEnded += OnPreviewAudioEnded;
        _previewAudio.Play();
    }

    private void OnPreviewAudioEnded(MediaPlayer sender, object args)
    {
        if (_previewAudioPlaylist?.MoveNext() is not null)
            PlayPreviewAudio();
        else
            DisposePreviewPlayer(ref _previewAudio);
    }
    private void OnPreviewVideoTick(object? sender, object e)
    {
        if (_previewVideo is null || _previewScheme is null) return;
        var session = _previewVideo.PlaybackSession;
        var duration = session.NaturalDuration;
        if (duration > TimeSpan.Zero)
        {
            PreviewProgress.Maximum = duration.TotalSeconds;
            PreviewProgress.Value = session.Position.TotalSeconds;
        }
        if (_previewFadeStarted) return;
        var fadeDuration = FadeTiming.GetFadeDuration(session.Position, duration, _previewScheme.IsVideoLooping, _previewScheme.EndBehavior);
        if (fadeDuration is TimeSpan fade)
        {
            _previewFadeStarted = true;
            PreviewFadeOverlay.Opacity = 0;
            PlaybackWindow.FadeToBlack(PreviewFadeOverlay, fade);
        }
    }

    private void OnPreviewVideoEnded(MediaPlayer sender, object args)
    {
        if (_previewScheme?.EndBehavior == VideoEndBehavior.FadeToBlack)
            PreviewFadeOverlay.Opacity = 1;
    }

    private void OnPreviewImageTick(object? sender, object e)
    {
        if (_previewSlideshow is null) return;
        PreviewImage.Source = new BitmapImage(new Uri(_previewSlideshow.MoveNext().Path));
    }

    private void OnPreviewVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_previewVideo is not null) _previewVideo.Volume = e.NewValue;
        if (_previewAudio is not null) _previewAudio.Volume = e.NewValue;
    }

    private static void DisposePreviewPlayer(ref MediaPlayer? player)
    {
        if (player is null) return;
        player.Pause();
        player.Source = null;
        player.Dispose();
        player = null;
    }
    public async Task InitializeWorkspaceAsync()
    {
        _loadingWorkspace = true;
        try
        {
            var workspace = WorkspaceStore.Load();
            foreach (var asset in workspace.Media)
                _mediaItems.Add(await MediaImportService.LoadItemAsync(asset));
            foreach (var scheme in workspace.Schemes)
                _schemes.Add(CreateSchemeCard(scheme));
            RefreshMediaView();
            RefreshEditorSources();
            RefreshQueue();
            UpdateSchemeCount();
            SchemeList.SelectedItem = _schemes.FirstOrDefault();
            StatusText.Text = $"已恢复 {_mediaItems.Count} 个素材、{_schemes.Count} 套方案";
        }
        catch (Exception ex)
        {
            ShowError("恢复上次工作区失败", ex);
        }
        finally
        {
            _loadingWorkspace = false;
        }
    }

    private SchemeCardViewModel CreateSchemeCard(PlaybackScheme scheme)
    {
        var card = new SchemeCardViewModel(scheme);
        card.Refresh(_mediaItems);
        card.Changed += ScheduleSave;
        return card;
    }

    private void ScheduleSave()
    {
        if (_loadingWorkspace) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveWorkspace()
    {
        if (_loadingWorkspace) return;
        _saveTimer.Stop();
        try
        {
            WorkspaceStore.Save(new WorkspaceState
            {
                Media = _mediaItems.Select(item => item.Asset).ToList(),
                Schemes = _schemes.Select(card => card.Scheme).ToList()
            });
        }
        catch (Exception ex)
        {
            ShowError("保存工作区失败", ex);
        }
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
        _coordinator?.Stop();
        SaveWorkspace();
        Application.Current.Exit();
    }
}
