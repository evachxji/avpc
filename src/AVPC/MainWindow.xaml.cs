using System.Collections.ObjectModel;
using AVPC.Core;
using AVPC.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AVPC;

public partial class MainWindow : Window, IPlaybackSurface
{
    private readonly ObservableCollection<MediaLibraryItem> _mediaItems = [];
    private readonly ObservableCollection<SchemeCardViewModel> _schemes = [];
    private PlaybackQueue _queue = new([]);
    private List<DisplayOption> _displays = [];
    private bool _loadingEditor;
    private PlaybackWindow? _outputWindow;
    private PlaybackCoordinator? _coordinator;
    private Storyboard? _outputMonitorFadeStoryboard;
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
    private uint _previewSeekPointerId;
    private readonly System.Diagnostics.Stopwatch _previewImageClock = new();
    private TimeSpan _previewImageOffset;
    private readonly DispatcherTimer _saveTimer = new();
    private Storyboard? _mediaSidebarStoryboard;
    private bool _isMediaSidebarCollapsed;
    private bool _loadingWorkspace;
    private bool _isMediaMarqueeSelecting;
    private uint _mediaMarqueePointerId;
    private Windows.Foundation.Point _mediaMarqueeStart;
    private IReadOnlyList<MediaLibraryItem> _mediaMarqueeBaseSelection = [];
    private MediaLibraryItem? _contextMediaItem;
    private bool _updatingSchemeVolumes;
    private SystemVolumeService? _systemVolumeService;

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
        PreviewProgress.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PreviewSeekPressed), true);
        PreviewProgress.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PreviewSeekMoved), true);
        PreviewProgress.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PreviewSeekReleased), true);
        PreviewProgress.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PreviewSeekCanceled), true);
        PreviewProgress.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PreviewSeekCaptureLost), true);
        PreviewProgress.ValueChanged += PreviewSeekValueChanged;
        PreviewVolume.ValueChanged += OnPreviewVolumeChanged;
        SystemVolumeSlider.ValueChanged += OnSystemVolumeChanged;
        try
        {
            _systemVolumeService = new SystemVolumeService();
            SystemVolumeSlider.Value = _systemVolumeService.Volume;
        }
        catch (Exception ex)
        {
            SystemVolumeSlider.IsEnabled = false;
            ShowError("无法读取系统音量", ex);
        }
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

    private void RefreshDisplaysClicked(object sender, RoutedEventArgs e)
    {
        if (!OutputDisplayPolicy.CanSwitch(_queue.IsPlaying)) return;
        InitializeDisplays();
    }

    private void ToggleMediaSidebarClicked(object sender, RoutedEventArgs e)
    {
        if (_mediaSidebarStoryboard is not null) return;

        const double expandedWidth = 270;
        const double collapsedWidth = 48;
        var collapsing = !_isMediaSidebarCollapsed;
        _isMediaSidebarCollapsed = collapsing;

        MediaSidebarContent.Visibility = Visibility.Visible;
        MediaSidebarCollapsedContent.Visibility = Visibility.Visible;
        if (collapsing)
            MediaSidebarCollapsedContent.Opacity = 0;
        else
            MediaSidebarContent.Opacity = 0;

        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var storyboard = new Storyboard();
        var widthAnimation = new DoubleAnimation
        {
            From = MediaSidebar.ActualWidth,
            To = collapsing ? collapsedWidth : expandedWidth,
            Duration = TimeSpan.FromMilliseconds(260),
            EnableDependentAnimation = true,
            EasingFunction = easing
        };
        Storyboard.SetTarget(widthAnimation, MediaSidebar);
        Storyboard.SetTargetProperty(widthAnimation, nameof(FrameworkElement.Width));
        storyboard.Children.Add(widthAnimation);

        AddSidebarOpacityAnimation(storyboard, MediaSidebarContent, collapsing ? 0 : 1, easing);
        AddSidebarOpacityAnimation(storyboard, MediaSidebarCollapsedContent, collapsing ? 1 : 0, easing);
        storyboard.Completed += (_, _) =>
        {
            MediaSidebar.Width = collapsing ? collapsedWidth : expandedWidth;
            MediaSidebarContent.Opacity = collapsing ? 0 : 1;
            MediaSidebarContent.Visibility = collapsing ? Visibility.Collapsed : Visibility.Visible;
            MediaSidebarCollapsedContent.Opacity = collapsing ? 1 : 0;
            MediaSidebarCollapsedContent.Visibility = collapsing ? Visibility.Visible : Visibility.Collapsed;
            _mediaSidebarStoryboard = null;
        };

        _mediaSidebarStoryboard = storyboard;
        storyboard.Begin();
    }

    private static void AddSidebarOpacityAnimation(Storyboard storyboard, UIElement target, double to, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = target.Opacity,
            To = to,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        storyboard.Children.Add(animation);
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

    private void MediaListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (FindVisualAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.Content is MediaLibraryItem item)
        {
            _contextMediaItem = item;
            if (!MediaList.SelectedItems.Contains(item))
            {
                MediaList.SelectedItems.Clear();
                MediaList.SelectedItem = item;
            }
        }
        else
        {
            _contextMediaItem = null;
            if (MediaList.SelectedItems.Count == 0) return;
        }

        MediaContextFlyout.ShowAt(MediaList, new FlyoutShowOptions { Position = e.GetPosition(MediaList) });
        e.Handled = true;
    }

    private void MediaContextMenuOpening(object sender, object e)
    {
        var count = MediaList.SelectedItems.OfType<MediaLibraryItem>().Count();
        OpenMediaFolderMenuItem.IsEnabled = _contextMediaItem is not null;
        DeleteMediaMenuItem.Text = count > 1 ? $"删除 {count} 个素材" : "删除素材";
        DeleteMediaMenuItem.IsEnabled = count > 0;
    }

    private void OpenMediaFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_contextMediaItem is not { } item) return;
        if (!File.Exists(item.Asset.Path))
        {
            ShowMessage("素材文件不存在，无法在文件夹中定位。", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
                Arguments = ExplorerSelection.BuildArguments(item.Asset.Path)
            });
        }
        catch (Exception ex)
        {
            ShowError("无法打开素材所在文件夹", ex);
        }
    }

    private async void DeleteSelectedMediaClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;
        var items = MediaList.SelectedItems.OfType<MediaLibraryItem>().ToList();
        if (items.Count == 0) return;

        var target = items.Count == 1 ? $"“{items[0].Asset.DisplayName}”" : $"选中的 {items.Count} 个素材";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "删除素材",
            Content = $"确定删除{target}吗？已分配到方案的素材会一并移除，但不会删除磁盘文件。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        StopPreview();
        foreach (var card in _schemes)
        {
            foreach (var item in items)
                card.Scheme.RemoveMedia(item.Asset);
        }
        foreach (var item in items)
            _mediaItems.Remove(item);

        RefreshMediaView();
        RefreshQueue();
        LoadEditor();
        StatusText.Text = items.Count == 1 ? $"已删除素材 {items[0].Asset.DisplayName}" : $"已删除 {items.Count} 个素材";
    }

    private void MediaListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(MediaList);
        if (point.Properties.IsRightButtonPressed ||
            FindVisualAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is not null ||
            FindVisualAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null ||
            !MediaList.CapturePointer(e.Pointer))
            return;

        _isMediaMarqueeSelecting = true;
        _mediaMarqueePointerId = e.Pointer.PointerId;
        _mediaMarqueeStart = point.Position;
        var additive = (e.KeyModifiers & (Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift)) != 0;
        _mediaMarqueeBaseSelection = additive
            ? MediaList.SelectedItems.OfType<MediaLibraryItem>().ToList()
            : [];
        if (!additive) MediaList.SelectedItems.Clear();
        UpdateMediaSelectionBox(_mediaMarqueeStart, _mediaMarqueeStart);
        e.Handled = true;
    }

    private void MediaListPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMediaMarqueeSelecting || e.Pointer.PointerId != _mediaMarqueePointerId) return;
        var position = e.GetCurrentPoint(MediaList).Position;
        UpdateMediaSelectionBox(_mediaMarqueeStart, position);
        UpdateMediaMarqueeSelection(_mediaMarqueeStart, position);
        e.Handled = true;
    }

    private void MediaListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMediaMarqueeSelecting || e.Pointer.PointerId != _mediaMarqueePointerId) return;
        FinishMediaMarquee();
        MediaList.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void MediaListPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _mediaMarqueePointerId) FinishMediaMarquee();
    }

    private void MediaListPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _mediaMarqueePointerId) FinishMediaMarquee();
    }

    private void FinishMediaMarquee()
    {
        if (!_isMediaMarqueeSelecting) return;
        _isMediaMarqueeSelecting = false;
        MediaSelectionBox.Visibility = Visibility.Collapsed;
    }

    private void UpdateMediaSelectionBox(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        Canvas.SetLeft(MediaSelectionBox, left);
        Canvas.SetTop(MediaSelectionBox, top);
        MediaSelectionBox.Width = Math.Abs(end.X - start.X);
        MediaSelectionBox.Height = Math.Abs(end.Y - start.Y);
        MediaSelectionBox.Visibility = Visibility.Visible;
    }

    private void UpdateMediaMarqueeSelection(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        var bounds = new Windows.Foundation.Rect(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));
        var selection = new List<MediaLibraryItem>(_mediaMarqueeBaseSelection);
        foreach (var item in MediaList.Items.OfType<MediaLibraryItem>())
        {
            if (MediaList.ContainerFromItem(item) is not ListViewItem container || container.ActualWidth == 0 || container.ActualHeight == 0)
                continue;
            var itemBounds = container.TransformToVisual(MediaList)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));
            var overlaps = bounds.X <= itemBounds.X + itemBounds.Width &&
                           bounds.X + bounds.Width >= itemBounds.X &&
                           bounds.Y <= itemBounds.Y + itemBounds.Height &&
                           bounds.Y + bounds.Height >= itemBounds.Y;
            if (overlaps && !selection.Contains(item)) selection.Add(item);
        }

        foreach (var item in MediaList.SelectedItems.OfType<MediaLibraryItem>().Where(item => !selection.Contains(item)).ToList())
            MediaList.SelectedItems.Remove(item);
        foreach (var item in selection.Where(item => !MediaList.SelectedItems.Contains(item)))
            MediaList.SelectedItems.Add(item);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject element) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (child is T match) return match;
            if (FindVisualDescendant<T>(child) is T descendant) return descendant;
        }
        return null;
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
        var scheme = new PlaybackScheme
        {
            Name = $"方案 {_schemes.Count + 1:00}",
            PlaybackVolume = PreviewVolume.Value,
            SystemVolume = SystemVolumeSlider.Value
        };
        var card = CreateSchemeCard(scheme);
        _schemes.Add(card);
        RefreshQueue();
        SchemeList.SelectedItem = card;
        SchemeList.ScrollIntoView(card);
        UpdateSchemeCount();
    }

    private async void DeleteSchemeCardClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable() || (sender as FrameworkElement)?.DataContext is not SchemeCardViewModel card) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "删除播放方案",
            Content = $"确定删除“{card.Scheme.Name}”吗？删除后无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (ReferenceEquals(_queue.CurrentScheme, card.Scheme))
        {
            StopOutput();
            HideOutputWindow();
        }
        StopPreview();
        _schemes.Remove(card);
        RefreshQueue();
        UpdateSchemeCount();
        SchemeList.SelectedItem = _schemes.FirstOrDefault();
        StatusText.Text = $"已删除 {card.Scheme.Name}";
    }

    private void SchemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadEditor();
        UpdateSchemeHighlight();
        if (!_queue.IsPlaying)
        {
            if (_queue.CurrentScheme is null && SelectedScheme is PlaybackScheme scheme)
                LoadSchemeVolumes(scheme);
            StopPreview();
        }
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
        if ((sender as FrameworkElement)?.DataContext is not SchemeCardViewModel card) return;
        BeginSchemeRename(card);
        e.Handled = true;
    }

    private void SchemeListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (FindVisualAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.Content is not SchemeCardViewModel card) return;
        SchemeList.SelectedItem = card;
        SchemeContextFlyout.ShowAt(SchemeList, new FlyoutShowOptions { Position = e.GetPosition(SchemeList) });
        e.Handled = true;
    }

    private void EditSchemeNameMenuClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedCard is SchemeCardViewModel card) BeginSchemeRename(card);
    }

    private void BeginSchemeRename(SchemeCardViewModel card)
    {
        SchemeList.SelectedItem = card;
        if (!card.IsRenaming) card.BeginRename();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (SchemeList.ContainerFromItem(card) is not ListViewItem container ||
                FindVisualDescendant<TextBox>(container) is not TextBox editor) return;
            editor.Focus(FocusState.Programmatic);
            editor.SelectAll();
        });
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
        if (!_loadingEditor && SelectedCard is SchemeCardViewModel card) card.IsLooping = looping;
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
        UpdateSchemeHighlight();
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
        if (!OutputDisplayPolicy.CanSwitch(_queue.IsPlaying)) return;
        if (_outputWindow?.AppWindow.IsVisible == true && OutputDisplaySelector.SelectedItem is DisplayOption display)
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
        if (!ValidateAllSchemes()) return;
        if (_outputWindow?.AppWindow.IsVisible == true)
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
            var scheme = _queue.Select(index);
            _queue.Start();
            PlayOutputScheme(scheme, autoPlay: true);
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
                if (_queue.CurrentScheme is not null)
                {
                    var selectedScheme = _queue.Start()!;
                    _coordinator.Resume();
                    _isOutputPaused = false;
                    UpdateOutputMode();
                    StatusText.Text = $"正在输出 {selectedScheme.Name} · {(_queue.CurrentIndex ?? 0) + 1}/{_schemes.Count}";
                    return;
                }
                PlayOutputScheme(_queue.Start(), autoPlay: true);
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
        if (_outputWindow is null || _coordinator is null) return;
        try
        {
            var scheme = _queue.Previous();
            if (scheme is not null) PlayOutputScheme(scheme, autoPlay: false);
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
        try
        {
            var scheme = _queue.Advance();
            if (scheme is null)
            {
                _coordinator.Stop();
                _isOutputPaused = false;
                UpdateOutputMode();
                StatusText.Text = "全部方案已展示完毕";
                return;
            }
            PlayOutputScheme(scheme, autoPlay: true);
        }
        catch (Exception ex)
        {
            StopOutput();
            ShowError("切换下一套失败", ex);
        }
    }

    private void PlayOutputScheme(PlaybackScheme? scheme, bool autoPlay)
    {
        if (scheme is null)
        {
            return;
        }
        StopPreview();
        EnsureOutputWindow();
        _coordinator!.Stop();
        // 先停止旧方案，避免新方案音量在切换瞬间作用到仍在播放的旧音频。
        LoadSchemeVolumes(scheme);
        if (autoPlay)
            _coordinator.Play(scheme);
        else
            _coordinator.Prepare(scheme);
        _isOutputPaused = !autoPlay;
        UpdateOutputMode();
        StatusText.Text = autoPlay
            ? $"正在输出 {scheme.Name} · {(_queue.CurrentIndex ?? 0) + 1}/{_schemes.Count}"
            : $"已定位 {scheme.Name}，点击开始后播放";
    }

    private void EndOutputClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            StopOutput();
            HideOutputWindow();
            UpdateOutputMode();
            StatusText.Text = "正式输出已结束，输出窗口已关闭";
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

    private void HideOutputWindow()
    {
        if (_outputWindow?.AppWindow.IsVisible == true)
            _outputWindow.AppWindow.Hide();
        UpdateOutputMode();
    }

    private void UpdateOutputMode()
    {
        var outputVisible = _outputWindow?.AppWindow.IsVisible == true;
        OutputMonitorPanel.Visibility = outputVisible ? Visibility.Visible : Visibility.Collapsed;
        SchemePreviewContent.Visibility = outputVisible ? Visibility.Collapsed : Visibility.Visible;
        SchemeEditorScroll.Visibility = outputVisible ? Visibility.Collapsed : Visibility.Visible;
        var canReorderSchemes = SchemeReorderPolicy.CanReorder(outputVisible);
        SchemeList.CanDragItems = canReorderSchemes;
        SchemeList.CanReorderItems = canReorderSchemes;
        var canSwitchOutputDisplay = OutputDisplayPolicy.CanSwitch(_queue.IsPlaying);
        OutputDisplaySelector.IsEnabled = canSwitchOutputDisplay;
        RefreshDisplayButton.IsEnabled = canSwitchOutputDisplay;
        UpdateSchemeHighlight();
        UpdatePreviewFrameSizes();
        PlayPauseOutputButton.Content = !_queue.IsPlaying ? "▶ 开始" : _isOutputPaused ? "▶ 继续" : "⏸ 暂停";
        ModeText.Text = _queue.IsPlaying
            ? _isOutputPaused ? "●  正式输出已暂停" : "●  正式输出中"
            : !outputVisible ? "●  预览模式"
            : _queue.IsPastEnd ? "●  展示已结束"
            : _queue.CurrentScheme is not null ? "●  展示已定位"
            : "●  展示待开始";
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
            _coordinator = new PlaybackCoordinator(_outputWindow, this);
            _coordinator.Volume = PreviewVolume.Value;
            _coordinator.PlaybackRate = SelectedPlaybackRate;
            _coordinator.ErrorOccurred += message => ShowMessage(message, InfoBarSeverity.Error);
            _coordinator.VideoProgressChanged += UpdateOutputVideoProgress;
            _outputWindow.Closed += (sender, args) =>
            {
                if (!ReferenceEquals(sender, _outputWindow)) return;
                _outputWindow = null;
                _coordinator = null;
                _isOutputPaused = false;
                _queue.Stop();
                UpdateOutputMode();
            };
        }
        if (_outputWindow.AppWindow.IsVisible) return;
        DisplayService.ShowFullScreen(_outputWindow, SelectedOutputDisplay);
        _outputWindow.Activate();
    }

    private void PreviewPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewFrameSizes(e.NewSize.Width);
    }

    private void UpdatePreviewFrameSizes(double panelWidth = double.NaN)
    {
        var width = double.IsNaN(panelWidth) ? PreviewPanel.ActualWidth : panelWidth;
        var contentWidth = Math.Max(0, width - 38);
        PreviewFrame.Height = contentWidth * 9 / 16;
    }

    private void UpdateSchemeHighlight()
    {
        var outputVisible = _outputWindow?.AppWindow.IsVisible == true;
        var highlightedScheme = SchemeHighlightResolver.Resolve(_queue.CurrentScheme, SelectedScheme, outputVisible);
        foreach (var card in _schemes)
            card.SetHighlighted(ReferenceEquals(card.Scheme, highlightedScheme));
    }

    private void UpdateOutputVideoProgress(PlaybackCoordinator.VideoProgress? progress)
    {
        if (progress is not { } value)
        {
            OutputVideoProgress.Visibility = Visibility.Collapsed;
            OutputVideoProgress.Value = 0;
            OutputVideoProgress.IsIndeterminate = false;
            return;
        }

        OutputVideoProgress.Visibility = Visibility.Visible;
        OutputVideoProgress.Maximum = Math.Max(0, value.Duration.TotalSeconds);
        OutputVideoProgress.Value = Math.Clamp(value.Position.TotalSeconds, 0, OutputVideoProgress.Maximum);
        OutputVideoProgress.IsIndeterminate = value.Duration <= TimeSpan.Zero;
    }

    void IPlaybackSurface.ShowVideo(MediaPlayer player)
    {
        OutputMonitorView.SetMediaPlayer(player);
        OutputMonitorView.Visibility = Visibility.Visible;
        OutputMonitorImage.Source = null;
        OutputMonitorFadeOverlay.Opacity = 0;
    }

    void IPlaybackSurface.ShowImage(string path)
    {
        OutputMonitorView.SetMediaPlayer(null);
        OutputMonitorView.Visibility = Visibility.Collapsed;
        OutputMonitorImage.Source = new BitmapImage(new Uri(path));
        OutputMonitorFadeOverlay.Opacity = 0;
    }

    void IPlaybackSurface.ResetFade() => ResetOutputMonitorFade();

    void IPlaybackSurface.BeginFade(TimeSpan duration)
    {
        ResetOutputMonitorFade();
        _outputMonitorFadeStoryboard = PlaybackWindow.FadeToBlack(OutputMonitorFadeOverlay, duration);
    }

    void IPlaybackSurface.PauseFade() => _outputMonitorFadeStoryboard?.Pause();

    void IPlaybackSurface.ResumeFade() => _outputMonitorFadeStoryboard?.Resume();

    void IPlaybackSurface.HoldLastFrame() => OutputMonitorFadeOverlay.Opacity = 0;

    void IPlaybackSurface.ShowBlackAfterFade()
    {
        _outputMonitorFadeStoryboard?.Stop();
        _outputMonitorFadeStoryboard = null;
        OutputMonitorFadeOverlay.Opacity = 1;
    }

    void IPlaybackSurface.Clear()
    {
        ResetOutputMonitorFade();
        OutputMonitorView.SetMediaPlayer(null);
        OutputMonitorView.Visibility = Visibility.Visible;
        OutputMonitorImage.Source = null;
        OutputMonitorFadeOverlay.Opacity = 1;
    }

    private void ResetOutputMonitorFade()
    {
        _outputMonitorFadeStoryboard?.Stop();
        _outputMonitorFadeStoryboard = null;
        OutputMonitorFadeOverlay.Opacity = 0;
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
            var player = CreatePreviewPlayer(false, _previewScheme.HasIndependentAudio);
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
                player.Source = CreatePreviewSource(asset);
            }
            else
            {
                player.Source = CreatePreviewSource(asset);
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

    private MediaPlayer CreatePreviewPlayer(bool loop, bool muted)
    {
        var player = new MediaPlayer
        {
            IsLoopingEnabled = loop,
            IsMuted = muted,
            Volume = PreviewVolume.Value
        };
        player.MediaFailed += (sender, args) => ShowMessage($"预览播放失败：{args.ErrorMessage}", InfoBarSeverity.Error);
        return player;
    }

    private static MediaSource CreatePreviewSource(MediaAsset asset) =>
        MediaSource.CreateFromUri(new Uri(asset.Path));

    private void PlayPreviewAudio()
    {
        if (_previewAudioPlaylist is null) return;
        DisposePreviewPlayer(ref _previewAudio);
        _previewAudio = CreatePreviewPlayer(false, false);
        _previewAudio.MediaEnded += OnPreviewAudioEnded;
        _previewAudio.Source = CreatePreviewSource(_previewAudioPlaylist.Current);
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

    private void PreviewSeekPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(PreviewProgress).Properties.IsLeftButtonPressed ||
            _previewTimeline is null || _previewPlaylist is null)
            return;

        _isPreviewSeeking = true;
        _previewSeekPointerId = e.Pointer.PointerId;
        if (!_isPreviewSeeking) return;
        _previewWasPausedBeforeSeeking = _isPreviewPaused;
        _previewVideo?.Pause();
        _previewAudio?.Pause();
        _previewTimer?.Stop();
        _previewImageClock.Stop();
        PreviewProgress.CapturePointer(e.Pointer);
        UpdatePreviewProgressFromPointer(e.GetCurrentPoint(PreviewProgress).Position);
        e.Handled = true;
    }

    private void PreviewSeekMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewSeeking || e.Pointer.PointerId != _previewSeekPointerId) return;
        UpdatePreviewProgressFromPointer(e.GetCurrentPoint(PreviewProgress).Position);
        e.Handled = true;
    }

    private void PreviewSeekReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewSeeking || e.Pointer.PointerId != _previewSeekPointerId) return;
        UpdatePreviewProgressFromPointer(e.GetCurrentPoint(PreviewProgress).Position);
        PreviewProgress.ReleasePointerCapture(e.Pointer);
        PreviewSeekCompleted();
        e.Handled = true;
    }

    private void PreviewSeekCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewSeeking || e.Pointer.PointerId != _previewSeekPointerId) return;
        PreviewSeekCompleted();
        e.Handled = true;
    }

    private void PreviewSeekCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPreviewSeeking || e.Pointer.PointerId != _previewSeekPointerId) return;
        PreviewSeekCompleted();
    }

    private void UpdatePreviewProgressFromPointer(Windows.Foundation.Point position)
    {
        if (PreviewProgress.ActualWidth <= 0 || PreviewProgress.Maximum <= PreviewProgress.Minimum) return;
        var ratio = Math.Clamp(position.X / PreviewProgress.ActualWidth, 0, 1);
        PreviewProgress.Value = PreviewProgress.Minimum + ratio * (PreviewProgress.Maximum - PreviewProgress.Minimum);
    }

    private void PreviewSeekValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isPreviewSeeking || _previewTimeline is null) return;
        var total = _previewTimeline.HasCompleteDuration ? FormatPreviewTime(_previewTimeline.TotalDuration) : "--:--";
        PreviewTimeText.Text = $"{FormatPreviewTime(TimeSpan.FromSeconds(e.NewValue))} / {total}";
    }

    private void PreviewSeekCompleted()
    {
        if (!_isPreviewSeeking || _previewTimeline is null || _previewPlaylist is null) return;
        _isPreviewSeeking = false;
        var location = _previewTimeline.Locate(TimeSpan.FromSeconds(PreviewProgress.Value));
        var itemDuration = location.ItemEnd - location.ItemStart;
        var atEnd = location.ItemPosition >= itemDuration;
        var autoPlay = !_previewWasPausedBeforeSeeking && !atEnd;
        if (!location.CrossesVisualBoundary(_previewPlaylist.CurrentIndex) &&
            SeekCurrentPreviewVisual(location, autoPlay, atEnd))
        {
            if (autoPlay) ResumePreviewAudio();
            else _previewAudio?.Pause();
            return;
        }
        _previewPlaylist.MoveTo(location.Index);
        PlayCurrentPreviewVisual(location.ItemPosition, autoPlay, atEnd);
        if (autoPlay) ResumePreviewAudio();
        else _previewAudio?.Pause();
    }

    private bool SeekCurrentPreviewVisual(VisualTimelineLocation location, bool autoPlay, bool atEnd)
    {
        if (_previewTimeline is null) return false;
        _previewAtItemEnd = atEnd;
        _isPreviewPaused = !autoPlay;
        _previewFadeStarted = false;
        PreviewFadeOverlay.Opacity = 0;

        if (location.Asset.Kind == MediaKind.Video && _previewVideo is not null)
        {
            var session = _previewVideo.PlaybackSession;
            var duration = session.NaturalDuration > TimeSpan.Zero
                ? session.NaturalDuration
                : TimeSpan.FromSeconds(location.Asset.DurationSeconds);
            var target = atEnd && duration > TimeSpan.Zero
                ? duration - TimeSpan.FromMilliseconds(80)
                : location.ItemPosition;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && target > duration) target = duration;

            _previewVideo.Position = target;
            if (autoPlay)
            {
                _previewVideo.Play();
                StartPreviewTimer();
            }
            else
            {
                _previewVideo.Pause();
                _previewTimer?.Stop();
            }

            UpdatePreviewPosition(atEnd
                ? location.ItemEnd
                : _previewTimeline.GetGlobalPosition(location.Index, target));
            PreviewButtonIcon.Symbol = autoPlay ? Symbol.Pause : Symbol.Play;
            return true;
        }

        if (location.Asset.Kind == MediaKind.Image && PreviewImage.Source is not null)
        {
            _previewImageClock.Reset();
            _previewImageOffset = location.ItemPosition;
            if (autoPlay)
            {
                _previewImageClock.Start();
                StartPreviewTimer();
            }
            else
            {
                _previewTimer?.Stop();
            }

            UpdatePreviewPosition(_previewTimeline.GetGlobalPosition(location.Index, location.ItemPosition));
            PreviewButtonIcon.Symbol = autoPlay ? Symbol.Pause : Symbol.Play;
            return true;
        }

        return false;
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
        if (_updatingSchemeVolumes) return;
        ApplyPlaybackVolume(e.NewValue);
        if ((_queue.CurrentScheme ?? SelectedScheme) is PlaybackScheme scheme)
        {
            scheme.PlaybackVolume = e.NewValue;
            ScheduleSave();
        }
    }

    private void OnSystemVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSchemeVolumes) return;
        ApplySystemVolume(e.NewValue);
        if ((_queue.CurrentScheme ?? SelectedScheme) is PlaybackScheme scheme)
        {
            scheme.SystemVolume = e.NewValue;
            ScheduleSave();
        }
    }

    private void LoadSchemeVolumes(PlaybackScheme scheme)
    {
        var playbackVolume = scheme.PlaybackVolume ?? PreviewVolume.Value;
        var systemVolume = scheme.SystemVolume ?? GetCurrentSystemVolume();
        _updatingSchemeVolumes = true;
        try
        {
            PreviewVolume.Value = playbackVolume;
            SystemVolumeSlider.Value = systemVolume;
            scheme.PlaybackVolume = playbackVolume;
            scheme.SystemVolume = systemVolume;
        }
        finally
        {
            _updatingSchemeVolumes = false;
        }

        ApplyPlaybackVolume(playbackVolume);
        ApplySystemVolume(systemVolume);
        ScheduleSave();
    }

    private void ApplyPlaybackVolume(double volume)
    {
        if (_previewVideo is not null) _previewVideo.Volume = volume;
        if (_previewAudio is not null) _previewAudio.Volume = volume;
        if (_coordinator is not null) _coordinator.Volume = volume;
    }

    private void ApplySystemVolume(double volume)
    {
        if (_systemVolumeService is null) return;
        try
        {
            _systemVolumeService.Volume = volume;
        }
        catch (Exception ex)
        {
            ShowError("无法设置系统音量", ex);
        }
    }

    private double GetCurrentSystemVolume()
    {
        if (_systemVolumeService is not null)
        {
            try
            {
                return _systemVolumeService.Volume;
            }
            catch (Exception ex)
            {
                ShowError("无法读取系统音量", ex);
            }
        }
        return SystemVolumeSlider.Value;
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
                scheme.PlaybackVolume ??= PreviewVolume.Value;
                scheme.SystemVolume ??= GetCurrentSystemVolume();
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
        _systemVolumeService?.Dispose();
        Application.Current.Exit();
    }
}
