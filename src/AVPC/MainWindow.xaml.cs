using System.Collections.ObjectModel;
using AVPC.Core;
using AVPC.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
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
    private bool _isOutputPaused;
    private MediaPlayer? _previewVideo;
    private MediaPlayer? _previewAudio;
    private AudioPlaylist? _previewAudioPlaylist;
    private DispatcherTimer? _previewTimer;
    private VisualPlaylist? _previewPlaylist;
    private VisualTimeline? _previewTimeline;
    private PlaybackScheme? _previewScheme;
    private bool _previewFadeStarted;
    private bool _isPreviewSeeking;
    private bool _isPreviewPaused;
    private bool _previewAtItemEnd;
    private bool _previewWasPausedBeforeSeeking;
    private readonly System.Diagnostics.Stopwatch _previewImageClock = new();
    private TimeSpan _previewImageOffset;
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
        OutputDisplaySelector.DisplayMemberPath = "Name";
        AppWindow.Closing += OnClosing;
        PreviewProgress.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PreviewSeekStarted), true);
        PreviewProgress.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PreviewSeekCompleted), true);
        PreviewProgress.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PreviewSeekCompleted), true);
        PreviewVolume.ValueChanged += OnPreviewVolumeChanged;
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.Tick += (sender, args) => SaveWorkspace();
    }

    public DisplayArea SelectedOutputDisplay => (OutputDisplaySelector.SelectedItem as DisplayOption)?.Area
        ?? DisplayService.GetDisplays()[0];

    public void InitializeDisplays()
    {
        var selectedDisplayId = (OutputDisplaySelector.SelectedItem as DisplayOption)?.Area.DisplayId;
        _displays = DisplayService.GetDisplayOptions();
        OutputDisplaySelector.ItemsSource = _displays;
        OutputDisplaySelector.SelectedItem = _displays.FirstOrDefault(d => d.Area.DisplayId == selectedDisplayId)
            ?? _displays.FirstOrDefault(d => d.Area.IsPrimary)
            ?? _displays[0];
    }

    private void RefreshDisplaysClicked(object sender, RoutedEventArgs e) => InitializeDisplays();

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
        StatusText.Text = $"正在拖动 {item.Asset.DisplayName}，请放到方案缩略图或画面队列";
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
            e.DragUIOverride.Caption = "更新方案";
            StatusText.Text = "视频／图片替换画面队列，音频追加到方案";
        }
        e.Handled = true;
    }

    private void VisualAppendDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        e.AcceptedOperation = _queue.IsPlaying ? DataPackageOperation.None : DataPackageOperation.Copy;
        if (!_queue.IsPlaying) e.DragUIOverride.Caption = "追加到画面队列";
        e.Handled = true;
    }

    private async void VisualAppendDrop(object sender, DragEventArgs e)
    {
        await DropMediaAsync(e, append: true);
    }

    private void AudioAppendDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text)) return;
        e.AcceptedOperation = _queue.IsPlaying ? DataPackageOperation.None : DataPackageOperation.Copy;
        if (!_queue.IsPlaying) e.DragUIOverride.Caption = "追加到音频播放列表";
        e.Handled = true;
    }

    private async void AudioAppendDrop(object sender, DragEventArgs e)
    {
        await DropMediaAsync(e, append: true, audioAppend: true);
    }

    private async void SchemeDrop(object sender, DragEventArgs e)
    {
        await DropMediaAsync(e, append: false, sender as FrameworkElement);
    }

    private async Task DropMediaAsync(DragEventArgs e, bool append, FrameworkElement? target = null, bool audioAppend = false)
    {
        if (e.Handled || !e.DataView.Contains(StandardDataFormats.Text)) return;
        e.Handled = true;
        if (!EnsureEditable()) return;
        var path = await e.DataView.GetTextAsync();
        var media = _mediaItems.FirstOrDefault(item =>
            string.Equals(item.Asset.Path, path, StringComparison.OrdinalIgnoreCase));
        var card = append
            ? SelectedCard
            : target?.DataContext as SchemeCardViewModel
              ?? (e.OriginalSource as FrameworkElement)?.DataContext as SchemeCardViewModel;
        if (media is null || card is null) return;
        if (append && audioAppend && media.Asset.Kind != MediaKind.Audio)
        {
            ShowMessage("音频播放列表只接受音频；图片和视频请拖到画面播放队列。", InfoBarSeverity.Warning);
            return;
        }
        if (append && !audioAppend && media.Asset.Kind == MediaKind.Audio)
        {
            ShowMessage("画面队列只接受图片和视频；请将音频拖到音频播放列表。", InfoBarSeverity.Warning);
            return;
        }
        var restartPreview = PrepareSchemeEdit(card);
        if (append && !audioAppend) card.Scheme.AppendVisual(media.Asset);
        else card.Scheme.AssignMedia(media.Asset);
        RefreshEditedScheme(card, restartPreview);
        StatusText.Text = append
            ? $"已将 {media.Asset.DisplayName} 追加到 {card.Scheme.Name}"
            : media.Asset.Kind == MediaKind.Audio
                ? $"已将 {media.Asset.DisplayName} 加入 {card.Scheme.Name} 的独立音频"
                : $"已更新 {card.Scheme.Name} 的画面队列";
    }

    private static bool IsSameAsset(MediaAsset left, MediaAsset right) =>
        string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);

    private bool PrepareSchemeEdit(SchemeCardViewModel card)
    {
        var restart = ReferenceEquals(_previewScheme, card.Scheme);
        if (restart) StopPreview();
        return restart;
    }

    private void RefreshEditedScheme(SchemeCardViewModel card, bool restartPreview)
    {
        RefreshQueue();
        card.Refresh(_mediaItems);
        SchemeList.SelectedItem = card;
        LoadEditor();
        if (!restartPreview || card.Scheme.Validate() is not null) return;
        try { PlayPreview(card.Scheme); }
        catch (Exception ex) { ShowError("无法重新预览方案", ex); }
    }

    private void RemoveVisualClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;
        if ((sender as FrameworkElement)?.DataContext is not VisualItemRow row) return;
        var restartPreview = PrepareSchemeEdit(row.Card);
        row.Card.Scheme.RemoveVisual(row.Asset);
        RefreshEditedScheme(row.Card, restartPreview);
        StatusText.Text = $"已从 {row.Card.Scheme.Name} 删除 {row.Asset.DisplayName}";
    }

    private void RemoveAudioClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AudioBranchRow { Audio: not null } row)
            RemoveAudio(row.Card, row.Audio);
    }

    private void RemoveAudioItemClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AudioItemRow row)
            RemoveAudio(row.Card, row.Asset);
    }

    private void RemoveAudio(SchemeCardViewModel card, MediaAsset audio)
    {
        if (!EnsureEditable()) return;
        var restartPreview = PrepareSchemeEdit(card);
        card.Scheme.RemoveAudio(audio);
        RefreshEditedScheme(card, restartPreview);
        StatusText.Text = $"已从 {card.Scheme.Name} 删除 {audio.DisplayName}";
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
        var card = SelectedCard;
        var scheme = card?.Scheme;
        if (scheme is null)
        {
            PreviewSchemeText.Text = "尚未选择方案";
            VisualGallery.ItemsSource = null;
            AudioGallery.ItemsSource = null;
            _loadingEditor = false;
            return;
        }

        VisualGallery.ItemsSource = card!.VisualGalleryItems;
        AudioGallery.ItemsSource = card.AudioGalleryItems;
        ImageDurationBox.Text = scheme.ImageDurationSeconds.ToString();
        LoopCheck.IsChecked = scheme.IsLooping;
        EndBehaviorSelector.IsEnabled = !scheme.IsLooping;
        EndBehaviorSelector.SelectedIndex = scheme.EndBehavior == VideoEndBehavior.FadeToBlack ? 1 : 0;
        PreviewSchemeText.Text = scheme.Name;
        _loadingEditor = false;
    }

    private void SchemeNameDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!EnsureEditable() || (sender as FrameworkElement)?.DataContext is not SchemeCardViewModel card) return;
        SchemeList.SelectedItem = card;
        card.BeginRename();
        if (sender is TextBlock textBlock && textBlock.Parent is Grid grid &&
            grid.Children.OfType<TextBox>().FirstOrDefault() is TextBox editor)
        {
            editor.Focus(FocusState.Programmatic);
            editor.SelectAll();
        }
        e.Handled = true;
    }

    private void SchemeNameEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SchemeCardViewModel card || !card.IsRenaming) return;
        card.CommitRename();
        card.Refresh(_mediaItems);
        if (ReferenceEquals(SelectedCard, card)) PreviewSchemeText.Text = card.Scheme.Name;
    }

    private void SchemeNameEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SchemeCardViewModel card) return;
        if (e.Key == Windows.System.VirtualKey.Enter) card.CommitRename();
        else if (e.Key == Windows.System.VirtualKey.Escape) card.CancelRename();
        else return;
        card.Refresh(_mediaItems);
        if (ReferenceEquals(SelectedCard, card)) PreviewSchemeText.Text = card.Scheme.Name;
        e.Handled = true;
    }

    private void ImageDurationBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        args.Cancel = args.NewText.Any(character => character is < '0' or > '9');
    }

    private void ImageDurationChanged(object sender, TextChangedEventArgs args)
    {
        if (!_loadingEditor && SelectedCard is SchemeCardViewModel card &&
            int.TryParse(((TextBox)sender).Text, out var seconds) && seconds is >= 1 and <= 3600 &&
            card.Scheme.ImageDurationSeconds != seconds)
        {
            var restartPreview = PrepareSchemeEdit(card);
            card.Scheme.ImageDurationSeconds = seconds;
            RefreshEditedScheme(card, restartPreview);
        }
        ScheduleSave();
    }

    private void ImageDurationLostFocus(object sender, RoutedEventArgs e)
    {
        if (SelectedScheme is null || sender is not TextBox textBox) return;
        if (!int.TryParse(textBox.Text, out var seconds) || seconds is < 1 or > 3600)
            textBox.Text = SelectedScheme.ImageDurationSeconds.ToString();
    }

    private void LoopChanged(object sender, RoutedEventArgs e)
    {
        var looping = LoopCheck.IsChecked == true;
        if (!_loadingEditor && SelectedScheme is not null) SelectedScheme.IsLooping = looping;
        EndBehaviorSelector.IsEnabled = !looping;
        ScheduleSave();
    }

    private void EndBehaviorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEditor && SelectedScheme is not null)
            SelectedScheme.EndBehavior = EndBehaviorSelector.SelectedIndex == 1 ? VideoEndBehavior.FadeToBlack : VideoEndBehavior.HoldLastFrame;
        ScheduleSave();
    }

    private void MediaQueueDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (!EnsureEditable() || e.Items.Any(item => item is VisualAppendRow or AudioAppendRow))
            e.Cancel = true;
    }

    private void QueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListViewBase gallery && e.AddedItems.Any(item => item is VisualAppendRow or AudioAppendRow))
            gallery.SelectedItem = null;
    }

    private void VisualsReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.Items.OfType<VisualItemRow>().FirstOrDefault() is not VisualItemRow row) return;
        var oldIndex = row.Card.Scheme.Visuals.FindIndex(asset => IsSameAsset(asset, row.Asset));
        var visualItems = sender.Items.OfType<VisualItemRow>().ToList();
        var newIndex = visualItems.IndexOf(row);
        if (oldIndex < 0 || newIndex < 0) return;
        var previewAsset = ReferenceEquals(_previewScheme, row.Card.Scheme) ? _previewPlaylist?.Current : null;
        if (oldIndex != newIndex) row.Card.Scheme.MoveVisual(oldIndex, newIndex);
        foreach (var item in visualItems.Select((item, index) => (item, index)))
            item.item.SetNumber(item.index + 1);
        row.Card.Refresh(_mediaItems);
        LoadEditor();
        if (previewAsset is not null && _previewPlaylist is not null)
        {
            var previewIndex = row.Card.Scheme.Visuals.FindIndex(asset => IsSameAsset(asset, previewAsset));
            _previewPlaylist.MoveTo(previewIndex);
            VisualGallery.SelectedIndex = previewIndex;
        }
        ScheduleSave();
        StatusText.Text = $"{row.Card.Scheme.Name} 的画面顺序已更新";
    }

    private void AudiosReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.Items.OfType<AudioItemRow>().FirstOrDefault() is not AudioItemRow row) return;
        var oldIndex = row.Card.Scheme.Audios.FindIndex(asset => IsSameAsset(asset, row.Asset));
        var newIndex = sender.Items.OfType<AudioItemRow>().ToList().IndexOf(row);
        if (oldIndex < 0 || newIndex < 0) return;
        if (oldIndex == newIndex)
        {
            row.Card.Refresh(_mediaItems);
            LoadEditor();
            return;
        }
        var restartPreview = PrepareSchemeEdit(row.Card);
        row.Card.Scheme.MoveAudio(oldIndex, newIndex);
        RefreshEditedScheme(row.Card, restartPreview);
        StatusText.Text = $"{row.Card.Scheme.Name} 的音频顺序已更新";
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
        if (_previewScheme is not null && ReferenceEquals(_previewScheme, SelectedScheme))
        {
            SetPreviewPaused(!_isPreviewPaused);
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

    private void SetPreviewPaused(bool paused)
    {
        if (_previewScheme is null) return;
        _isPreviewPaused = paused;
        if (paused)
        {
            _previewVideo?.Pause();
            _previewAudio?.Pause();
            _previewTimer?.Stop();
            _previewImageClock.Stop();
        }
        else if (_previewAtItemEnd)
        {
            _previewAtItemEnd = false;
            if (_previewPlaylist?.MoveNext() is null)
            {
                var hasPausedAudio = _previewAudio is not null;
                FinishPreviewAtEnd();
                if (hasPausedAudio)
                {
                    ResumePreviewAudio();
                    _isPreviewPaused = false;
                    PreviewButtonIcon.Symbol = Symbol.Pause;
                }
                return;
            }
            PlayCurrentPreviewVisual();
            ResumePreviewAudio();
        }
        else
        {
            _previewVideo?.Play();
            ResumePreviewAudio();
            if (PreviewImage.Source is not null) _previewImageClock.Start();
            StartPreviewTimer();
        }
        if (_isPreviewPaused)
        {
            PreviewButtonIcon.Symbol = Symbol.Play;
            StatusText.Text = $"已暂停预览 {_previewScheme.Name}";
            return;
        }
        PreviewButtonIcon.Symbol = Symbol.Pause;
        StatusText.Text = $"正在预览 {_previewScheme.Name}";
    }

    private void StopPreview()
    {
        _previewTimer?.Stop();
        _previewTimer = null;
        _previewImageClock.Reset();
        _previewImageOffset = TimeSpan.Zero;
        _previewPlaylist = null;
        _previewTimeline = null;
        _previewScheme = null;
        _previewFadeStarted = false;
        _isPreviewSeeking = false;
        _isPreviewPaused = false;
        _previewAtItemEnd = false;
        PreviewButtonIcon.Symbol = Symbol.Play;
        DisposePreviewPlayer(ref _previewVideo);
        DisposePreviewPlayer(ref _previewAudio);
        _previewAudioPlaylist = null;
        PreviewView.SetMediaPlayer(null);
        PreviewView.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        PreviewFadeOverlay.Opacity = 0;
        PreviewProgress.Maximum = 0;
        PreviewProgress.Value = 0;
        PreviewProgress.IsEnabled = false;
        PreviewTimeText.Text = "00:00 / 00:00";
    }

    private async void PlaySchemeCardClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SchemeCardViewModel card) return;
        if (!_queue.IsPlaying && !ValidateAllSchemes()) return;
        if (_outputWindow is not null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "切换展示方案",
                Content = $"确定停止当前展示内容并切换到“{card.Scheme.Name}”吗？",
                PrimaryButtonText = "切换",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        try
        {
            var index = _schemes.IndexOf(card);
            if (index < 0) return;
            PlayOutputScheme(_queue.Select(index));
        }
        catch (Exception ex)
        {
            StopOutput();
            ShowError("切换展示方案失败", ex);
        }
    }
    private double SelectedPlaybackRate => PlaybackRateSelector.SelectedIndex switch
    {
        1 => 2,
        2 => 5,
        3 => 10,
        4 => 20,
        _ => 1
    };

    private void PlaybackRateChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_coordinator is not null) _coordinator.PlaybackRate = SelectedPlaybackRate;
        }
        catch (Exception ex)
        {
            ShowError("无法设置播放倍速", ex);
        }
    }
    private void OpenOutputClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureOutputWindow();
            UpdateOutputMode();
            if (!_queue.IsPlaying) StatusText.Text = "展示屏已开启，等待开始";
        }
        catch (Exception ex)
        {
            ShowError("开启展示失败", ex);
        }
    }

    private void PlayPauseOutputClicked(object sender, RoutedEventArgs e)
    {
        if (_outputWindow is null || _coordinator is null) return;
        try
        {
            if (!_queue.IsPlaying)
            {
                if (!ValidateAllSchemes()) return;
                PlayOutputScheme(_queue.Advance());
                return;
            }

            if (_isOutputPaused)
                _coordinator.Resume();
            else
                _coordinator.Pause();
            _isOutputPaused = !_isOutputPaused;
            UpdateOutputMode();
            var scheme = _queue.CurrentScheme!;
            var action = _isOutputPaused ? "已暂停" : "正在输出";
            StatusText.Text = $"{action} {scheme.Name} · {(_queue.CurrentIndex ?? 0) + 1}/{_schemes.Count}";
        }
        catch (Exception ex)
        {
            StopOutput();
            ShowError("正式播放控制失败", ex);
        }
    }

    private void PreviousOutputClicked(object sender, RoutedEventArgs e)
    {
        if (_outputWindow is null || _coordinator is null || !_queue.IsPlaying) return;
        try
        {
            var scheme = _queue.Previous();
            if (scheme is not null) PlayOutputScheme(scheme);
        }
        catch (Exception ex)
        {
            StopOutput();
            ShowError("切换上一套失败", ex);
        }
    }

    private void NextOutputClicked(object sender, RoutedEventArgs e)
    {
        if (_outputWindow is null || _coordinator is null) return;
        if (!_queue.IsPlaying && !ValidateAllSchemes()) return;
        try
        {
            PlayOutputScheme(_queue.Advance());
        }
        catch (Exception ex)
        {
            StopOutput();
            ShowError("切换下一套失败", ex);
        }
    }

    private void PlayOutputScheme(PlaybackScheme? scheme)
    {
        if (scheme is null)
        {
            StopOutput();
            StatusText.Text = "全部方案已播放完毕，正式输出已停止";
            return;
        }
        StopPreview();
        EnsureOutputWindow();
        _coordinator!.Play(scheme);
        _isOutputPaused = false;
        UpdateOutputMode();
        StatusText.Text = $"正在输出 {scheme.Name} · {(_queue.CurrentIndex ?? 0) + 1}/{_schemes.Count}";
    }

    private void EndOutputClicked(object sender, RoutedEventArgs e)
    {
        Diagnostics.Log("结束输出：开始停止");
        try
        {
            StopOutput();
            Diagnostics.Log("结束输出：停止完成，准备调用输出窗口 Close");
            _outputWindow?.Close();
            Diagnostics.Log("结束输出：输出窗口 Close 已返回");
            _outputWindow = null;
            _coordinator = null;
            UpdateOutputMode();
            StatusText.Text = "正式输出已结束，输出窗口已关闭";
            Diagnostics.Log("结束输出：完成");
        }
        catch (Exception ex)
        {
            ShowError("结束正式输出失败", ex);
        }
    }

    private void StopOutput()
    {
        _queue.Stop();
        _coordinator?.Stop();
        _isOutputPaused = false;
        SaveWorkspace();
        UpdateOutputMode();
    }

    private void UpdateOutputMode()
    {
        PlayPauseOutputButton.Content = !_queue.IsPlaying ? "▶ 开始" : _isOutputPaused ? "▶ 继续" : "⏸ 暂停";
        ModeText.Text = _queue.IsPlaying
            ? _isOutputPaused ? "●  正式输出已暂停" : "●  正式输出中"
            : _outputWindow is null ? "●  预览模式" : "●  展示待开始";
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
            var paths = scheme.Visuals.Select(item => item.Path)
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
            _coordinator.Volume = PreviewVolume.Value;
            _coordinator.PlaybackRate = SelectedPlaybackRate;
            _coordinator.ErrorOccurred += message => ShowMessage(message, InfoBarSeverity.Error);
        }
        DisplayService.ShowFullScreen(_outputWindow, SelectedOutputDisplay);
        _outputWindow.Activate();
    }

    private void PreviewPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var contentWidth = Math.Max(0, e.NewSize.Width - 38);
        PreviewFrame.Height = Math.Clamp(contentWidth * 9 / 16, 130, 260);
    }

    private void VisualItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VisualAppendRow)
        {
            VisualGallery.SelectedItem = null;
            return;
        }
        if (!EnsureEditable() || e.ClickedItem is not VisualItemRow row) return;
        var scheme = row.Card.Scheme;
        if (!ReferenceEquals(_previewScheme, scheme))
        {
            StopPreview();
            _previewScheme = scheme;
            _previewPlaylist = new VisualPlaylist(scheme);
            _previewTimeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);
            _previewAudioPlaylist = scheme.HasIndependentAudio ? new AudioPlaylist(scheme) : null;
            PreviewSchemeText.Text = scheme.Name;
            ConfigurePreviewTimeline();
        }
        if (_previewPlaylist is null || _previewTimeline is null) return;
        _previewAudio?.Pause();
        _previewPlaylist.MoveTo(row.Number - 1);
        PlayCurrentPreviewVisual(TimeSpan.Zero, autoPlay: false, atItemEnd: false);
        StatusText.Text = $"已定位到 {row.Asset.DisplayName} 的起点";
    }

    private void PlayPreview(PlaybackScheme scheme)
    {
        StopPreview();
        PreviewButtonIcon.Symbol = Symbol.Pause;
        _previewScheme = scheme;
        PreviewSchemeText.Text = scheme.Name;
        _previewPlaylist = new VisualPlaylist(scheme);
        _previewTimeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);
        ConfigurePreviewTimeline();
        UpdatePreviewPosition(TimeSpan.Zero);
        PlayCurrentPreviewVisual();
        if (scheme.HasIndependentAudio)
        {
            _previewAudioPlaylist = new AudioPlaylist(scheme);
            PlayPreviewAudio();
        }
        StatusText.Text = $"正在预览 {scheme.Name}";
    }

    private void PlayCurrentPreviewVisual(TimeSpan? localPosition = null, bool autoPlay = true, bool atItemEnd = false)
    {
        if (_previewPlaylist is null || _previewScheme is null || _previewTimeline is null) return;
        _previewTimer?.Stop();
        _previewTimer = null;
        _previewImageClock.Reset();
        _previewImageOffset = TimeSpan.Zero;
        DisposePreviewPlayer(ref _previewVideo);
        PreviewFadeOverlay.Opacity = 0;
        _previewFadeStarted = false;
        _isPreviewSeeking = false;
        _previewAtItemEnd = atItemEnd;
        _isPreviewPaused = !autoPlay;
        VisualGallery.SelectedIndex = _previewPlaylist.CurrentIndex;

        var asset = _previewPlaylist.Current;
        if (asset.Kind == MediaKind.Video)
        {
            var player = CreatePreviewPlayer(asset, false, _previewScheme.HasIndependentAudio);
            _previewVideo = player;
            player.MediaEnded += OnPreviewVideoEnded;
            PreviewView.SetMediaPlayer(player);
            PreviewView.Visibility = Visibility.Visible;
            PreviewImage.Source = null;
            if (localPosition.HasValue || atItemEnd)
            {
                player.MediaOpened += (sender, args) =>
                {
                    if (!ReferenceEquals(sender, _previewVideo) || _previewTimeline is null || _previewPlaylist is null) return;
                    var duration = sender.NaturalDuration;
                    if (duration > TimeSpan.Zero)
                    {
                        asset.DurationSeconds = duration.TotalSeconds;
                        ConfigurePreviewTimeline();
                    }
                    if (duration <= TimeSpan.Zero) duration = TimeSpan.FromSeconds(asset.DurationSeconds);
                    var target = atItemEnd
                        ? duration - TimeSpan.FromMilliseconds(80)
                        : localPosition ?? TimeSpan.Zero;
                    if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                    if (duration > TimeSpan.Zero && target > duration) target = duration;
                    sender.Position = target;
                    sender.Play();
                    sender.Pause();
                    UpdatePreviewPosition(atItemEnd
                        ? _previewTimeline.GetItemEnd(_previewPlaylist.CurrentIndex)
                        : _previewTimeline.GetGlobalPosition(_previewPlaylist.CurrentIndex, target));
                    if (autoPlay && !atItemEnd)
                    {
                        sender.Play();
                        StartPreviewTimer();
                    }
                };
            }
            else
            {
                player.Play();
                StartPreviewTimer();
            }
            var initial = atItemEnd
                ? _previewTimeline.GetItemEnd(_previewPlaylist.CurrentIndex)
                : _previewTimeline.GetGlobalPosition(_previewPlaylist.CurrentIndex, localPosition ?? TimeSpan.Zero);
            UpdatePreviewPosition(initial);
        }
        else
        {
            PreviewView.SetMediaPlayer(null);
            PreviewView.Visibility = Visibility.Collapsed;
            PreviewImage.Source = new BitmapImage(new Uri(asset.Path));
            var initialLocal = atItemEnd ? _previewPlaylist.Interval : localPosition ?? TimeSpan.Zero;
            _previewImageOffset = initialLocal;
            if (autoPlay && !atItemEnd)
            {
                _previewImageClock.Start();
                StartPreviewTimer();
            }
            UpdatePreviewPosition(_previewTimeline.GetGlobalPosition(_previewPlaylist.CurrentIndex, initialLocal));
        }
        PreviewButtonIcon.Symbol = autoPlay ? Symbol.Pause : Symbol.Play;
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

    private void ResumePreviewAudio()
    {
        if (_previewAudio is not null) _previewAudio.Play();
        else if (_previewAudioPlaylist is not null) PlayPreviewAudio();
    }

    private void OnPreviewAudioEnded(MediaPlayer sender, object args)
    {
        if (_previewAudioPlaylist?.MoveNext() is not null)
            PlayPreviewAudio();
        else
        {
            DisposePreviewPlayer(ref _previewAudio);
            _previewAudioPlaylist = null;
        }
    }

    private void StartPreviewTimer()
    {
        if (_previewTimer is null)
        {
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _previewTimer.Tick += OnPreviewTick;
        }
        _previewTimer.Start();
    }

    private void OnPreviewTick(object? sender, object e)
    {
        if (_previewScheme is null || _previewPlaylist is null || _previewTimeline is null || _isPreviewPaused) return;
        if (_previewVideo is not null)
        {
            var session = _previewVideo.PlaybackSession;
            var duration = session.NaturalDuration;
            if (duration > TimeSpan.Zero)
            {
                if (Math.Abs(_previewPlaylist.Current.DurationSeconds - duration.TotalSeconds) > 0.01)
                {
                    _previewPlaylist.Current.DurationSeconds = duration.TotalSeconds;
                    ConfigurePreviewTimeline();
                    ScheduleSave();
                }
                if (!_isPreviewSeeking)
                    UpdatePreviewPosition(_previewTimeline.GetGlobalPosition(_previewPlaylist.CurrentIndex, session.Position));
            }
            if (_previewFadeStarted || _previewAtItemEnd) return;
            var canFade = _previewPlaylist.IsLast && !_previewScheme.IsLooping;
            var fadeDuration = canFade
                ? FadeTiming.GetFadeDuration(session.Position, duration, false, _previewScheme.EndBehavior)
                : null;
            if (fadeDuration is TimeSpan fade)
            {
                _previewFadeStarted = true;
                PreviewFadeOverlay.Opacity = 0;
                PlaybackWindow.FadeToBlack(PreviewFadeOverlay, fade);
            }
            return;
        }

        var imagePosition = _previewImageOffset + _previewImageClock.Elapsed;
        if (imagePosition < _previewPlaylist.Interval)
        {
            if (!_isPreviewSeeking)
                UpdatePreviewPosition(_previewTimeline.GetGlobalPosition(_previewPlaylist.CurrentIndex, imagePosition));
            return;
        }
        UpdatePreviewPosition(_previewTimeline.GetItemEnd(_previewPlaylist.CurrentIndex));
        if (_previewPlaylist.MoveNext() is not null) PlayCurrentPreviewVisual();
        else FinishPreviewAtEnd();
    }

    private void PreviewSeekStarted(object sender, PointerRoutedEventArgs e)
    {
        _isPreviewSeeking = _previewTimeline is not null && _previewPlaylist is not null;
        if (!_isPreviewSeeking) return;
        _previewWasPausedBeforeSeeking = _isPreviewPaused;
        _previewVideo?.Pause();
        _previewAudio?.Pause();
        _previewTimer?.Stop();
        _previewImageClock.Stop();
    }

    private void PreviewSeekCompleted(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewSeeking || _previewTimeline is null || _previewPlaylist is null) return;
        _isPreviewSeeking = false;
        var location = _previewTimeline.Locate(TimeSpan.FromSeconds(PreviewProgress.Value));
        var itemDuration = location.ItemEnd - location.ItemStart;
        var atEnd = location.ItemPosition >= itemDuration;
        var autoPlay = !_previewWasPausedBeforeSeeking && !atEnd;
        _previewPlaylist.MoveTo(location.Index);
        PlayCurrentPreviewVisual(location.ItemPosition, autoPlay, atEnd);
        if (autoPlay) ResumePreviewAudio();
        else _previewAudio?.Pause();
    }

    private void OnPreviewVideoEnded(MediaPlayer sender, object args)
    {
        if (_previewAtItemEnd || _previewPlaylist is null) return;
        if (_previewPlaylist.MoveNext() is not null)
        {
            PlayCurrentPreviewVisual();
            return;
        }
        _previewTimer?.Stop();
        if (_previewScheme?.EndBehavior == VideoEndBehavior.FadeToBlack)
            PreviewFadeOverlay.Opacity = 1;
        FinishPreviewAtEnd();
    }

    private void FinishPreviewAtEnd()
    {
        _previewTimer?.Stop();
        _previewImageClock.Stop();
        _previewVideo?.Pause();
        _previewAtItemEnd = true;
        _isPreviewPaused = _previewAudio is null;
        if (_previewTimeline is not null) UpdatePreviewPosition(_previewTimeline.TotalDuration);
        PreviewButtonIcon.Symbol = _isPreviewPaused ? Symbol.Play : Symbol.Pause;
        if (_previewScheme is not null)
            StatusText.Text = _isPreviewPaused ? $"预览已结束 {_previewScheme.Name}" : $"画面已结束，独立音频继续播放 {_previewScheme.Name}";
    }

    private void ConfigurePreviewTimeline()
    {
        if (_previewTimeline is null) return;
        PreviewProgress.Maximum = _previewTimeline.TotalDuration.TotalSeconds;
        PreviewProgress.IsEnabled = _previewTimeline.HasCompleteDuration && PreviewProgress.Maximum > 0;
    }

    private void UpdatePreviewPosition(TimeSpan position)
    {
        if (_previewTimeline is null) return;
        if (!_isPreviewSeeking)
            PreviewProgress.Value = Math.Clamp(position.TotalSeconds, PreviewProgress.Minimum, PreviewProgress.Maximum);
        var total = _previewTimeline.HasCompleteDuration ? FormatPreviewTime(_previewTimeline.TotalDuration) : "--:--";
        PreviewTimeText.Text = $"{FormatPreviewTime(position)} / {total}";
    }

    private static string FormatPreviewTime(TimeSpan duration) =>
        duration.ToString(duration.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");

    private void OnPreviewVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_previewVideo is not null) _previewVideo.Volume = e.NewValue;
        if (_previewAudio is not null) _previewAudio.Volume = e.NewValue;
        if (_coordinator is not null) _coordinator.Volume = e.NewValue;
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
            {
                foreach (var asset in scheme.Visuals.Concat(scheme.Audios))
                {
                    var restored = _mediaItems.FirstOrDefault(item => IsSameAsset(item.Asset, asset));
                    if (restored is not null && restored.Asset.DurationSeconds > 0)
                        asset.DurationSeconds = restored.Asset.DurationSeconds;
                }
                _schemes.Add(CreateSchemeCard(scheme));
            }
            RefreshMediaView();
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
        Diagnostics.LogException(title, ex);
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
