# 方案浏览与混合媒体长廊实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将方案的画面来源改为图片与视频组成的有序混合队列，提供横向媒体长廊、末尾追加、缩略图整队替换和方案名称双击重命名。

**架构：** `PlaybackScheme.Visuals` 是方案画面的唯一数据源，核心 `VisualPlaylist` 负责顺序、循环与结束边界。WinUI 只负责拖放、展示和计时；预览与正式输出共用核心队列状态。旧 JSON 字段仅在反序列化时用于迁移，新工作区只保存统一队列。

**技术栈：** C# 12、.NET 8、WinUI 3、Windows App SDK、System.Text.Json、xUnit。

**实施前置条件：** 另一会话正在验证预览播放。开始任务 4 前必须确认该会话已结束验证；在此之前不操作 AVPC 进程，不修改 `MainWindow.xaml(.cs)`，不运行会与其冲突的构建。保留工作区现有未提交修改，不覆盖或回退。未经用户明确要求，不执行 Git commit。

---

## 文件结构

| 文件 | 变更职责 |
| --- | --- |
| `src/AVPC.Core/PlaybackScheme.cs` | 统一 `Visuals`、追加与替换、校验、旧字段反序列化迁移 |
| `src/AVPC.Core/VisualPlaylist.cs` | 新建混合队列的当前项、推进、循环与图片停留时长 |
| `src/AVPC.Core/ImageSlideshow.cs` | 被 `VisualPlaylist` 替代后删除 |
| `src/AVPC/SchemeCardViewModel.cs` | 方案封面、混合摘要、长廊条目、重命名状态、画面循环绑定 |
| `src/AVPC/MainWindow.xaml` | 横向长廊、末尾虚线框、方案内音频列表、行内重命名 |
| `src/AVPC/MainWindow.xaml.cs` | 拖放路由、编辑器加载、预览混合队列；保留其他会话的播放控制改动 |
| `src/AVPC/Services/PlaybackCoordinator.cs` | 正式输出按混合队列推进图片与视频 |
| `tests/AVPC.Tests/SchemeAssignmentTests.cs` | 追加、替换、去重与音频隔离测试 |
| `tests/AVPC.Tests/PlaybackSchemeTests.cs` | 混合画面校验测试 |
| `tests/AVPC.Tests/VisualPlaylistTests.cs` | 新建混合顺序、循环与非循环边界测试 |
| `tests/AVPC.Tests/ImageSlideshowTests.cs` | 由 `VisualPlaylistTests` 替代后删除 |
| `tests/AVPC.Tests/WorkspacePersistenceTests.cs` | 旧 JSON 迁移与新 JSON 往返测试 |
| `AGENTS.md` | 更新当前功能契约中的混合画面、拖放和循环描述 |

---

### 任务 1：统一方案画面模型

**文件：**
- 修改：`src/AVPC.Core/PlaybackScheme.cs`
- 测试：`tests/AVPC.Tests/SchemeAssignmentTests.cs`
- 测试：`tests/AVPC.Tests/PlaybackSchemeTests.cs`

- [ ] **步骤 1：编写追加、替换、去重和音频隔离的失败测试**

将 `SchemeAssignmentTests.cs` 的测试主体更新为以下行为，保留静态素材字段：

```csharp
[Fact]
public void AppendVisual_PreservesMixedOrderAndIgnoresDuplicate()
{
    var scheme = new PlaybackScheme();
    scheme.AppendVisual(Image);
    scheme.AppendVisual(Video);
    scheme.AppendVisual(Image);

    Assert.Equal([Image, Video], scheme.Visuals);
}

[Fact]
public void ReplaceVisual_ClearsExistingQueue()
{
    var scheme = new PlaybackScheme();
    scheme.AppendVisual(Image);
    scheme.AppendVisual(Video);
    var replacement = new MediaAsset("replacement.png", MediaKind.Image);

    scheme.ReplaceVisual(replacement);

    Assert.Same(replacement, Assert.Single(scheme.Visuals));
}

[Fact]
public void AssignVisual_UsesThumbnailDropReplacementSemantics()
{
    var scheme = new PlaybackScheme();
    scheme.AppendVisual(Image);

    scheme.AssignMedia(Video);

    Assert.Same(Video, Assert.Single(scheme.Visuals));
}

[Fact]
public void AssignAudio_DoesNotChangeVisualQueue()
{
    var scheme = new PlaybackScheme();
    scheme.AppendVisual(Image);
    scheme.AssignMedia(Audio);

    Assert.Same(Audio, Assert.Single(scheme.Audios));
    Assert.Same(Image, Assert.Single(scheme.Visuals));
}

[Fact]
public void AppendVisual_RejectsAudio()
{
    var scheme = new PlaybackScheme();
    Assert.Throws<ArgumentException>(() => scheme.AppendVisual(Audio));
    Assert.Empty(scheme.Visuals);
}
```

- [ ] **步骤 2：更新方案校验测试**

把互斥测试替换为混合队列与非法类型测试：

```csharp
[Fact]
public void Validate_AcceptsMixedVisualQueue()
{
    var scheme = new PlaybackScheme();
    scheme.AppendVisual(Image);
    scheme.AppendVisual(Video);
    Assert.Null(scheme.Validate());
}

[Fact]
public void Validate_RejectsAudioInVisualQueue()
{
    var scheme = new PlaybackScheme();
    scheme.Visuals.Add(new MediaAsset("audio.mp3", MediaKind.Audio));
    Assert.NotNull(scheme.Validate());
}
```

保留空方案、图片停留时长、默认 5 秒和显示名测试；把其中对 `Video`、`Images` 的初始化改为 `AppendVisual`。

- [ ] **步骤 3：运行核心测试并确认失败**

运行：

```powershell
dotnet test tests/AVPC.Tests/AVPC.Tests.csproj
```

预期：编译失败，提示 `Visuals`、`AppendVisual` 或 `ReplaceVisual` 不存在。

- [ ] **步骤 4：实现最少的统一模型**

将 `PlaybackScheme.cs` 调整为以下结构；`using System.Text.Json.Serialization` 保持不变：

```csharp
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class PlaybackScheme
{
    public string Name { get; set; } = string.Empty;
    public List<MediaAsset> Visuals { get; } = [];
    public List<MediaAsset> Audios { get; } = [];
    public bool IsVisualLooping { get; set; }
    public bool IsAudioLooping { get; set; }
    public int ImageDurationSeconds { get; set; } = 5;
    public VideoEndBehavior EndBehavior { get; set; }

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

    public void RemoveAudio(MediaAsset asset) =>
        Audios.RemoveAll(audio => SamePath(audio, asset));

    public string? Validate()
    {
        if (Visuals.Count == 0) return "请拖入视频或图片。";
        if (Visuals.Any(item => item.Kind == MediaKind.Audio))
            return "画面队列只能包含视频或图片。";
        if (Visuals.Any(item => item.Kind == MediaKind.Image) && ImageDurationSeconds < 1)
            return "每张图片至少需要停留 1 秒。";
        return null;
    }

    [JsonPropertyName("Video")]
    public MediaAsset? LegacyVideo
    {
        set { if (value is not null) ReplaceVisual(value); }
    }

    [JsonPropertyName("Images")]
    public List<MediaAsset>? LegacyImages
    {
        set
        {
            if (value is null) return;
            Visuals.Clear();
            foreach (var image in value) AppendVisual(image);
        }
    }

    [JsonPropertyName("IsVideoLooping")]
    public bool LegacyVideoLooping
    {
        set => IsVisualLooping = value;
    }

    private static void EnsureVisual(MediaAsset asset)
    {
        if (asset.Kind is not (MediaKind.Video or MediaKind.Image))
            throw new ArgumentException("画面队列只能包含视频或图片。", nameof(asset));
    }

    private static bool SamePath(MediaAsset left, MediaAsset right) =>
        string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
}
```

旧字段使用 set-only 属性，仅供反序列化；任务 3 会验证它们不会写入新 JSON。

- [ ] **步骤 5：运行核心测试确认新测试通过**

运行：

```powershell
dotnet test tests/AVPC.Tests/AVPC.Tests.csproj
```

预期：任务 1 新测试通过；尚未迁移的 `ImageSlideshowTests`、播放列表和持久化测试可以失败或无法编译，这些失败由任务 2、3 处理。

---

### 任务 2：实现混合画面队列状态

**文件：**
- 创建：`src/AVPC.Core/VisualPlaylist.cs`
- 创建：`tests/AVPC.Tests/VisualPlaylistTests.cs`
- 删除：`src/AVPC.Core/ImageSlideshow.cs`
- 删除：`tests/AVPC.Tests/ImageSlideshowTests.cs`

- [ ] **步骤 1：编写混合顺序、循环和结束边界测试**

创建 `VisualPlaylistTests.cs`：

```csharp
using AVPC.Core;

namespace AVPC.Tests;

public sealed class VisualPlaylistTests
{
    private static readonly MediaAsset FirstImage = new("first.png", MediaKind.Image);
    private static readonly MediaAsset Video = new("video.mp4", MediaKind.Video);
    private static readonly MediaAsset LastImage = new("last.png", MediaKind.Image);

    [Fact]
    public void Playlist_MovesThroughMixedItemsInOrder()
    {
        var scheme = CreateScheme();
        var playlist = new VisualPlaylist(scheme);

        Assert.Same(FirstImage, playlist.Current);
        Assert.Same(Video, playlist.MoveNext());
        Assert.Same(LastImage, playlist.MoveNext());
        Assert.Null(playlist.MoveNext());
    }

    [Fact]
    public void Playlist_WrapsWholeQueueWhenLooping()
    {
        var scheme = CreateScheme();
        scheme.IsVisualLooping = true;
        var playlist = new VisualPlaylist(scheme);
        playlist.MoveNext();

        Assert.Same(LastImage, playlist.MoveNext());
        Assert.Same(FirstImage, playlist.MoveNext());
    }

    [Fact]
    public void Playlist_ExposesLastItemAndImageInterval()
    {
        var scheme = CreateScheme();
        scheme.ImageDurationSeconds = 8;
        var playlist = new VisualPlaylist(scheme);

        Assert.False(playlist.IsLast);
        playlist.MoveNext();
        playlist.MoveNext();

        Assert.True(playlist.IsLast);
        Assert.Equal(TimeSpan.FromSeconds(8), playlist.Interval);
    }

    private static PlaybackScheme CreateScheme()
    {
        var scheme = new PlaybackScheme();
        scheme.AppendVisual(FirstImage);
        scheme.AppendVisual(Video);
        scheme.AppendVisual(LastImage);
        return scheme;
    }
}
```

- [ ] **步骤 2：运行测试确认失败**

运行：

```powershell
dotnet test tests/AVPC.Tests/AVPC.Tests.csproj --filter VisualPlaylistTests
```

预期：编译失败，提示 `VisualPlaylist` 不存在。

- [ ] **步骤 3：实现 `VisualPlaylist`**

创建 `src/AVPC.Core/VisualPlaylist.cs`：

```csharp
namespace AVPC.Core;

public sealed class VisualPlaylist
{
    private readonly PlaybackScheme _scheme;
    private int _index;

    public VisualPlaylist(PlaybackScheme scheme)
    {
        if (scheme.Visuals.Count == 0)
            throw new ArgumentException("画面队列至少需要一个视频或图片。", nameof(scheme));
        _scheme = scheme;
    }

    public MediaAsset Current => _scheme.Visuals[_index];
    public bool IsLast => _index == _scheme.Visuals.Count - 1;
    public TimeSpan Interval => TimeSpan.FromSeconds(_scheme.ImageDurationSeconds);

    public MediaAsset? MoveNext()
    {
        if (IsLast)
        {
            if (!_scheme.IsVisualLooping) return null;
            _index = 0;
        }
        else
            _index++;
        return Current;
    }
}
```

删除已被替代的 `ImageSlideshow.cs` 和 `ImageSlideshowTests.cs`，不保留只负责转发的旧类。

- [ ] **步骤 4：运行核心队列测试**

运行：

```powershell
dotnet test tests/AVPC.Tests/AVPC.Tests.csproj --filter "VisualPlaylistTests|AudioPlaylistTests|PlaybackQueueTests"
```

预期：全部通过；画面队列和音频队列各自推进，互不影响。

---

### 任务 3：迁移旧工作区并验证新 JSON 往返

**文件：**
- 测试：`tests/AVPC.Tests/WorkspacePersistenceTests.cs`
- 必要时修改：`src/AVPC.Core/PlaybackScheme.cs`

- [ ] **步骤 1：编写旧视频、旧图片组迁移测试**

在 `WorkspacePersistenceTests.cs` 添加：

```csharp
[Fact]
public void LegacyVideoWorkspace_MigratesToVisualQueue()
{
    const string json = """
    {"Media":[],"Schemes":[{"Name":"旧视频","Video":{"Path":"old.mp4","Kind":0},"Images":[],"Audios":[],"IsVideoLooping":true,"ImageDurationSeconds":5,"EndBehavior":1}]}
    """;

    var scheme = Assert.Single(WorkspacePersistence.Deserialize(json).Schemes);

    var visual = Assert.Single(scheme.Visuals);
    Assert.Equal("old.mp4", visual.Path);
    Assert.True(scheme.IsVisualLooping);
    Assert.Equal(VideoEndBehavior.FadeToBlack, scheme.EndBehavior);
}

[Fact]
public void LegacyImageWorkspace_PreservesOrderAndSettings()
{
    const string json = """
    {"Media":[],"Schemes":[{"Name":"旧图片","Video":null,"Images":[{"Path":"a.png","Kind":2},{"Path":"b.png","Kind":2}],"Audios":[{"Path":"a.mp3","Kind":1}],"IsVideoLooping":false,"IsAudioLooping":true,"ImageDurationSeconds":7,"EndBehavior":0}]}
    """;

    var scheme = Assert.Single(WorkspacePersistence.Deserialize(json).Schemes);

    Assert.Equal(["a.png", "b.png"], scheme.Visuals.Select(item => item.Path));
    Assert.Equal("a.mp3", Assert.Single(scheme.Audios).Path);
    Assert.Equal(7, scheme.ImageDurationSeconds);
    Assert.True(scheme.IsAudioLooping);
}
```

- [ ] **步骤 2：编写新格式往返和旧字段不落地测试**

将原往返测试改为混合队列，并添加：

```csharp
[Fact]
public void Workspace_RoundTrip_RestoresMixedVisualsAndSchemeSettings()
{
    var video = new MediaAsset("video.mp4", MediaKind.Video);
    var image = new MediaAsset("image.png", MediaKind.Image);
    var audio = new MediaAsset("audio.mp3", MediaKind.Audio);
    var scheme = new PlaybackScheme
    {
        Name = "混合方案",
        IsVisualLooping = true,
        IsAudioLooping = true,
        ImageDurationSeconds = 8
    };
    scheme.AppendVisual(image);
    scheme.AppendVisual(video);
    scheme.AssignMedia(audio);
    var workspace = new WorkspaceState { Media = [video, image, audio], Schemes = [scheme] };

    var json = WorkspacePersistence.Serialize(workspace);
    var restored = Assert.Single(WorkspacePersistence.Deserialize(json).Schemes);

    Assert.Equal([image.Path, video.Path], restored.Visuals.Select(item => item.Path));
    Assert.Equal(audio.Path, Assert.Single(restored.Audios).Path);
    Assert.True(restored.IsVisualLooping);
    Assert.True(restored.IsAudioLooping);
    Assert.Equal(8, restored.ImageDurationSeconds);
    Assert.Contains("\"Visuals\"", json);
    Assert.DoesNotContain("\"Video\"", json);
    Assert.DoesNotContain("\"Images\"", json);
    Assert.DoesNotContain("\"IsVideoLooping\"", json);
}
```

- [ ] **步骤 3：运行持久化测试确认失败**

运行：

```powershell
dotnet test tests/AVPC.Tests/AVPC.Tests.csproj --filter WorkspacePersistenceTests
```

预期：至少旧字段迁移或旧字段不落地断言失败。

- [ ] **步骤 4：只修正序列化边界**

若旧 JSON 未迁移，检查 3 个 set-only 属性的 `JsonPropertyName` 是否与旧字段完全一致。若旧字段仍被写入，确保这些属性没有 getter；不要增加第二套运行时画面列表，也不要从 `WorkspaceState.Media` 补全方案。

- [ ] **步骤 5：运行全部核心测试**

运行：

```powershell
dotnet test tests/AVPC.Tests/AVPC.Tests.csproj
```

预期：全部通过。

---

### 任务 4：实现方案卡片、横向长廊和重命名界面

**文件：**
- 修改：`src/AVPC/SchemeCardViewModel.cs`
- 修改：`src/AVPC/MainWindow.xaml`
- 修改：`src/AVPC/MainWindow.xaml.cs`

**前置检查：** 确认另一会话已经明确完成预览验证。后续编辑必须基于其最新文件，保留播放／暂停、进度和音量同步改动。

- [ ] **步骤 1：扩展卡片视图模型**

在 `SchemeCardViewModel.cs` 添加长廊条目记录：

```csharp
public sealed record VisualItemRow(
    int Number,
    string Name,
    BitmapImage? Thumbnail,
    MediaAsset Asset,
    SchemeCardViewModel Card);
```

将 `IsVideoLooping` 包装属性改名为 `IsVisualLooping`，读写 `Scheme.IsVisualLooping`。添加以下状态和成员：

```csharp
public IReadOnlyList<VisualItemRow> VisualRows { get; private set; } = [];
public bool IsRenaming { get; private set; }
public string EditingName { get; set; } = string.Empty;
public Visibility NameVisibility => IsRenaming ? Visibility.Collapsed : Visibility.Visible;
public Visibility EditorVisibility => IsRenaming ? Visibility.Visible : Visibility.Collapsed;

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
```

在 `Refresh` 中用 `Visuals` 生成封面、摘要和长廊条目：

```csharp
var visual = Scheme.Visuals.FirstOrDefault();
Thumbnail = media.FirstOrDefault(item => item.Asset.Path == visual?.Path)?.Thumbnail;
VisualRows = Scheme.Visuals.Select((asset, index) =>
{
    var item = media.FirstOrDefault(candidate => candidate.Asset.Path == asset.Path);
    return new VisualItemRow(index + 1, asset.DisplayName, item?.Thumbnail, asset, this);
}).ToList();
```

`VisualName` 改为按实际数量返回摘要：空队列显示「拖入视频或图片」，非空队列显示图片数和视频数，例如 `2 张图片 + 1 个视频`。保留现有 `AudioRows` 生成逻辑。

- [ ] **步骤 2：把中间来源选择器替换为横向长廊**

在 `MainWindow.xaml` 删除 `SchemeNameBox`、`VisualKindSelector`、`VideoSelector`、`ImageSelector` 和原 `AudioSelector`。在「选中方案设置」中放入：

```xml
<TextBlock Text="画面播放队列" FontSize="13" FontWeight="SemiBold" />
<ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
    <StackPanel Orientation="Horizontal" Spacing="12">
        <ItemsControl x:Name="VisualGallery">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><ItemsStackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <StackPanel Width="126" Margin="0,0,12,0">
                        <Border Height="82" CornerRadius="7" Background="#FF101318">
                            <Image Source="{Binding Thumbnail}" Stretch="UniformToFill" />
                        </Border>
                        <TextBlock Text="{Binding Name}" FontSize="11" TextTrimming="CharacterEllipsis" Margin="0,6,0,0" />
                    </StackPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <Border Width="146" Height="82" BorderBrush="#FF6685AC" BorderThickness="2"
                CornerRadius="8" Background="#FF182231" AllowDrop="True"
                DragOver="VisualAppendDragOver" Drop="VisualAppendDrop">
            <TextBlock Text="＋ 拖拽图片、视频到这里&#x0a;追加到结尾" TextAlignment="Center"
                       HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="12" />
        </Border>
    </StackPanel>
</ScrollViewer>
```

长廊下方保留 `ImageDurationBox`。独立音频区域改为只绑定当前方案条目的 `ItemsControl`：

```xml
<ItemsControl x:Name="SelectedAudioList">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Grid ColumnSpacing="4" Margin="0,2">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="22" /><ColumnDefinition Width="*" /><ColumnDefinition Width="24" />
                </Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Prefix}" FontSize="10" />
                <TextBlock Grid.Column="1" Text="{Binding Name}" FontSize="11" TextTrimming="CharacterEllipsis" />
                <Button Grid.Column="2" Content="×" Width="24" Height="24" Padding="0"
                        Background="Transparent" BorderThickness="0" Visibility="{Binding DeleteVisibility}"
                        Click="RemoveAudioClicked" />
            </Grid>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

将画面循环复选框的显示文本和绑定改为：

```xml
<CheckBox Content="画面循环" IsChecked="{Binding IsVisualLooping, Mode=TwoWay}" />
```

中间设置区的 `VideoLoopCheck` 同步改名为 `VisualLoopCheck`，事件处理名改为 `VisualLoopChanged`。

- [ ] **步骤 3：加入方案名称行内编辑器**

将右侧卡片名称所在的单个 `TextBlock` 替换为以下结构，保留右侧排序手柄：

```xml
<Grid>
    <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="13"
               Visibility="{Binding NameVisibility}" DoubleTapped="SchemeNameDoubleTapped" />
    <TextBox Text="{Binding EditingName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
             Visibility="{Binding EditorVisibility}" MinHeight="30" Padding="6,2"
             LostFocus="SchemeNameEditorLostFocus" KeyDown="SchemeNameEditorKeyDown" />
    <TextBlock Text="⋮⋮" HorizontalAlignment="Right"
               Foreground="{ThemeResource TextFillColorTertiaryBrush}" />
</Grid>
```

- [ ] **步骤 4：连接编辑器加载和名称事件**

在 `MainWindow.xaml.cs` 的 `LoadEditor` 中删除旧选择器赋值，保留现有加载保护，并加入：

```csharp
VisualGallery.ItemsSource = SelectedCard?.VisualRows;
SelectedAudioList.ItemsSource = SelectedCard?.AudioRows;
VisualLoopCheck.IsChecked = scheme.IsVisualLooping;
```

无选中方案时，把两个 `ItemsSource` 设为 `null`，再执行现有空状态返回逻辑。

添加名称事件：

```csharp
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
    if ((sender as FrameworkElement)?.DataContext is SchemeCardViewModel card)
    {
        card.CommitRename();
        SelectedCard?.Refresh(_mediaItems);
        PreviewSchemeText.Text = card.Scheme.Name;
    }
}

private void SchemeNameEditorKeyDown(object sender, KeyRoutedEventArgs e)
{
    if ((sender as FrameworkElement)?.DataContext is not SchemeCardViewModel card) return;
    if (e.Key == Windows.System.VirtualKey.Enter) card.CommitRename();
    else if (e.Key == Windows.System.VirtualKey.Escape) card.CancelRename();
    else return;
    SelectedCard?.Refresh(_mediaItems);
    e.Handled = true;
}
```

删除已经被界面移除的 `SchemeNameChanged`、`VisualKindChanged`、`VideoChanged`、`ImagesChanged`、`AudioChanged`、`FindMediaItem` 两个重载，以及旧 `RefreshEditorSources` 和它的调用。保留其他会话新增的预览控制方法。

- [ ] **步骤 5：构建检查界面绑定**

运行：

```powershell
dotnet build src/AVPC/AVPC.csproj
```

预期：0 错误。若另一会话尚未结束验证，不执行此步骤，先继续处理不冲突的核心文件并等待其通知。

---

### 任务 5：实现缩略图替换与虚线框追加

**文件：**
- 修改：`src/AVPC/MainWindow.xaml.cs`
- 必要时修改：`src/AVPC/MainWindow.xaml` 中方案缩略图的拖放事件

- [ ] **步骤 1：为右侧缩略图设置明确拖放目标**

在方案卡片的缩略图 `Border` 上保留或添加：

```xml
AllowDrop="True" DragOver="SchemeDragOver" Drop="SchemeDrop"
```

卡片外层仍接收音频拖放；`SchemeDrop` 对视频和图片采用整队替换，对音频采用追加。中间虚线框使用独立的 `VisualAppendDragOver`、`VisualAppendDrop`。

- [ ] **步骤 2：实现共享拖放解析和两条写入路径**

把现有 `SchemeDrop` 的媒体解析提取为以下代码，并新增追加事件：

```csharp
private void VisualAppendDragOver(object sender, DragEventArgs e)
{
    if (!e.DataView.Contains(StandardDataFormats.Text)) return;
    e.AcceptedOperation = _queue.IsPlaying ? DataPackageOperation.None : DataPackageOperation.Copy;
    if (!_queue.IsPlaying) e.DragUIOverride.Caption = "追加到画面队列";
    e.Handled = true;
}

private async void VisualAppendDrop(object sender, DragEventArgs e)
{
    await DropVisualAsync(e, append: true);
}

private async void SchemeDrop(object sender, DragEventArgs e)
{
    await DropVisualAsync(e, append: false, sender as FrameworkElement);
}

private async Task DropVisualAsync(DragEventArgs e, bool append, FrameworkElement? target = null)
{
    if (!e.DataView.Contains(StandardDataFormats.Text) || !EnsureEditable()) return;
    e.Handled = true; // 在异步等待前阻止缩略图与外层卡片重复处理同一次 Drop。
    var path = await e.DataView.GetTextAsync();
    var media = _mediaItems.FirstOrDefault(item =>
        string.Equals(item.Asset.Path, path, StringComparison.OrdinalIgnoreCase));
    var card = append
        ? SelectedCard
        : target?.DataContext as SchemeCardViewModel
          ?? (e.OriginalSource as FrameworkElement)?.DataContext as SchemeCardViewModel;
    if (media is null || card is null) return;
    if (append && media.Asset.Kind == MediaKind.Audio)
    {
        ShowMessage("画面队列只接受图片和视频；请将音频拖到方案卡片。", InfoBarSeverity.Warning);
        return;
    }
    if (_previewScheme == card.Scheme) StopPreview();
    if (append) card.Scheme.AppendVisual(media.Asset);
    else card.Scheme.AssignMedia(media.Asset);
    RefreshQueue();
    card.Refresh(_mediaItems);
    SchemeList.SelectedItem = card;
    LoadEditor();
    StatusText.Text = append
        ? $"已将 {media.Asset.DisplayName} 追加到 {card.Scheme.Name}"
        : $"已更新 {card.Scheme.Name} 的画面队列";
    e.Handled = true;
}
```

保留 `SchemeDragOver`，但将其提示改为「视频／图片替换画面队列，音频追加到方案」。所有写入只使用拖放路径匹配到的素材，不遍历素材库向方案补入其他条目。

- [ ] **步骤 3：更新文件校验与卡片刷新**

把 `ValidateAllSchemes` 的路径枚举改为：

```csharp
var paths = scheme.Visuals.Select(item => item.Path)
    .Concat(scheme.Audios.Select(audio => audio.Path));
```

确保 `RefreshQueue` 后调用目标卡片 `Refresh`，且 `LoadEditor` 重新绑定 `VisualRows` 和 `AudioRows`。

- [ ] **步骤 4：构建验证拖放代码**

运行：

```powershell
dotnet build src/AVPC/AVPC.csproj
```

预期：0 错误；拖放事件签名与 WinUI 委托匹配。

---

### 任务 6：让正式输出和预览播放混合队列

**文件：**
- 修改：`src/AVPC/Services/PlaybackCoordinator.cs`
- 修改：`src/AVPC/MainWindow.xaml.cs`

- [ ] **步骤 1：正式输出按当前项类型分派**

在 `PlaybackCoordinator` 中把 `_imageTimer` 改名为 `_visualTimer`，把 `_slideshow` 改为 `VisualPlaylist? _visualPlaylist`。`Play` 在独立音频启动前执行：

```csharp
_visualPlaylist = new VisualPlaylist(scheme);
PlayCurrentVisual();
```

添加统一分派：

```csharp
private void PlayCurrentVisual()
{
    if (_visualPlaylist is null || _scheme is null) return;
    _visualTimer?.Stop();
    _fadeTimer?.Stop();
    DisposePlayer(ref _videoPlayer);
    _output.ResetFade();
    if (_visualPlaylist.Current.Kind == MediaKind.Video)
        PlayVideo(_visualPlaylist.Current);
    else
        PlayImage();
}
```

`PlayVideo` 接收 `MediaAsset`，创建播放器时 `IsLoopingEnabled` 固定为 `false`；只有当前项是末项且队列不循环时启动淡出计时。`PlayImage` 显示当前图片；只有存在下一项或允许循环时启动 `_visualTimer`。

- [ ] **步骤 2：在图片计时和视频结束时推进**

使用以下推进逻辑：

```csharp
private void OnImageTick(object? sender, object e)
{
    if (_visualPlaylist?.MoveNext() is not null) PlayCurrentVisual();
    else _visualTimer?.Stop();
}

private void OnVideoEnded(MediaPlayer sender, object args)
{
    if (_visualPlaylist?.MoveNext() is not null)
    {
        PlayCurrentVisual();
        return;
    }
    if (_scheme?.EndBehavior == VideoEndBehavior.FadeToBlack)
        _output.ShowBlackAfterFade();
    else
        _output.HoldLastFrame();
}
```

`OnFadeTimerTick` 继续调用 `FadeTiming.GetFadeDuration`，并额外要求 `_visualPlaylist.IsLast`；时长为 0 时不能触发。`Stop` 清理新计时器和 `_visualPlaylist`，音频逻辑保持原样。

- [ ] **步骤 3：让预览使用同一核心队列**

把 `_previewSlideshow` 改为 `VisualPlaylist? _previewPlaylist`。`PlayPreview` 初始化 `_previewPlaylist` 后调用统一的 `PlayCurrentPreviewVisual`：

```csharp
private void PlayCurrentPreviewVisual()
{
    if (_previewPlaylist is null || _previewScheme is null) return;
    _previewTimer?.Stop();
    DisposePreviewPlayer(ref _previewVideo);
    PreviewFadeOverlay.Opacity = 0;
    _previewFadeStarted = false;
    PreviewProgress.Value = 0;

    if (_previewPlaylist.Current.Kind == MediaKind.Video)
    {
        _previewVideo = CreatePreviewPlayer(
            _previewPlaylist.Current, false, _previewScheme.Audios.Count > 0);
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
        PreviewImage.Source = new BitmapImage(new Uri(_previewPlaylist.Current.Path));
        if (!_previewPlaylist.IsLast || _previewScheme.IsVisualLooping)
        {
            _previewTimer = new DispatcherTimer { Interval = _previewPlaylist.Interval };
            _previewTimer.Tick += OnPreviewImageTick;
            _previewTimer.Start();
        }
    }
}
```

把这段分派合并进另一会话的最新预览实现，保留其暂停状态、进度拖动和音量同步，不用旧文件覆盖。图片计时与视频结束都在 `MoveNext()` 返回项时调用 `PlayCurrentPreviewVisual`；返回 `null` 时停止计时，视频末项再应用原结束行为。

- [ ] **步骤 4：更新所有剩余模型引用**

用内容搜索确认生产代码不再引用 `Video`、`Images`、`IsVideoLooping` 或 `ImageSlideshow`：

```powershell
git grep -n -E "\.Video\b|\.Images\b|IsVideoLooping|ImageSlideshow" -- src tests
```

预期：只剩 `PlaybackScheme` 的旧 JSON 迁移属性和明确针对旧格式的测试数据；界面与播放器均使用 `Visuals`、`IsVisualLooping`、`VisualPlaylist`。

- [ ] **步骤 5：运行自动测试与构建**

运行：

```powershell
dotnet test AVPC.sln
dotnet build AVPC.sln
dotnet build src/AVPC/AVPC.csproj
```

预期：测试全部通过，两次构建均 0 错误；处理本次修改新增的警告。

---

### 任务 7：更新功能契约并完成实际验收

**文件：**
- 修改：`AGENTS.md`
- 验证：`src/AVPC/MainWindow.xaml`、`src/AVPC/Services/PlaybackCoordinator.cs`

- [ ] **步骤 1：同步当前功能契约**

把「每套方案的画面来源二选一」改为「每套方案使用图片和视频组成的有序混合队列」；把「视频循环」改为「画面循环，控制整个混合队列」；补充中间长廊追加、右侧缩略图替换、名称双击重命名和方案素材隔离规则。保留音频独立循环、逐套手动切换和持久化要求。

- [ ] **步骤 2：启动应用并检查基本界面**

在其他会话已释放 AVPC 进程后运行：

```powershell
.\start.cmd
```

预期：主控台正常打开；中间显示横向长廊，右侧方案名称可双击编辑，Enter 保存、Esc 取消，失去焦点保存。

- [ ] **步骤 3：验证拖放与即时刷新**

用至少 2 张图片、2 个视频和 1 个音频检查：

1. 拖到右侧缩略图后旧画面队列被替换，封面立即变为新首项。
2. 拖到中间虚线框后素材追加到末尾，横向滚动可查看。
3. 音频只进入当前方案音频列表，不进入画面队列。
4. 中间音频列表不显示未分配素材。
5. 重启应用后混合顺序、名称、循环、停留时长和音频保持。

- [ ] **步骤 4：验证预览与正式输出**

依次检查图片到时切换、视频完整播放后切换、末项循环回首项、非循环末项停止、末项视频保持最后一帧或淡出。确认独立音频仍按自己的列表播放，素材自然结束不切换下一套方案。物理双屏、声音或视觉效果无法实际验证时，在交付说明中明确列出，不声称已验证。

- [ ] **步骤 5：汇总变更与验证证据**

最终回复列出修改范围、实际运行的测试和构建结果、界面验证结果及未验证项。未经用户明确要求，不创建 Git commit。
