# 中间预览时间轴与双媒体队列实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法来跟踪进度。

**目标：** 在中间浅色演播室面板中实现连续画面时间轴、可删除排序的画面与音频卡片队列，以及统一循环和正确的视频声音回退。

**架构：** `AVPC.Core` 负责媒体时长、队列变更、统一循环和纯时间轴计算；`MainWindow` 只负责 WinUI 拖放、媒体播放和进度显示；`SchemeCardViewModel` 提供两排卡片的即时数据。保留现有正式输出、工作区读写和右侧卡片结构。

**技术栈：** C#、.NET 8、WinUI 3、Windows App SDK 2.7、xUnit、System.Text.Json。

---

## 文件结构

- 创建 `src/AVPC.Core/VisualTimeline.cs`：画面项时长、全局位置和素材本地位置的纯计算。
- 修改 `src/AVPC.Core/MediaAsset.cs`：持久化音视频时长。
- 修改 `src/AVPC.Core/PlaybackScheme.cs`：统一循环、独立音频判定、双队列删除和移动、旧 JSON 迁移。
- 修改 `src/AVPC.Core/VisualPlaylist.cs`、`src/AVPC.Core/AudioPlaylist.cs`：使用统一循环，并允许画面预览按索引定位。
- 修改 `src/AVPC/Services/MediaImportService.cs`：导入和恢复媒体时保留时长。
- 修改 `src/AVPC/SchemeCardViewModel.cs`：画面和音频卡片数据、时长文字、统一循环绑定。
- 修改 `src/AVPC/MainWindow.xaml`：浅色中间面板、连续时间轴、双横向卡片列表和统一循环选项。
- 修改 `src/AVPC/MainWindow.xaml.cs`：双队列交互、连续预览、结束点定位和元数据恢复。
- 修改 `src/AVPC/Services/PlaybackCoordinator.cs`：正式输出使用统一循环和独立音频判定。
- 修改 `tests/AVPC.Tests/`：先增加核心失败测试，再更新旧循环字段断言。

### 任务 1：核心队列、统一循环与时间轴

- [x] **步骤 1：编写核心失败测试**

创建 `tests/AVPC.Tests/VisualTimelineTests.cs`，用 `DurationSeconds = 60` 的视频、5 秒图片、`DurationSeconds = 180` 的视频和另一张图片构造规格示例，断言：

```csharp
var timeline = new VisualTimeline(scheme.Visuals, scheme.ImageDurationSeconds);
Assert.Equal(TimeSpan.FromSeconds(250), timeline.TotalDuration);
Assert.Equal(TimeSpan.FromSeconds(65), timeline.GetItemEnd(1));
Assert.Equal(TimeSpan.FromSeconds(30), timeline.GetGlobalPosition(0, TimeSpan.FromSeconds(30)));
var located = timeline.Locate(TimeSpan.FromSeconds(125));
Assert.Equal(2, located.Index);
Assert.Equal(TimeSpan.FromSeconds(60), located.ItemPosition);
```

在 `PlaybackSchemeTests.cs` 增加 `MoveVisual`、`MoveAudio`、`RemoveVisual` 和 `HasIndependentAudio` 测试；在 `VisualPlaylistTests.cs`、`AudioPlaylistTests.cs` 把两个独立循环断言改为 `IsLooping`，并验证 `VisualPlaylist.MoveTo(1)` 后 `CurrentIndex` 和 `Current` 同步。

- [x] **步骤 2：运行测试验证红灯**

运行：`dotnet test AVPC.sln --no-restore`

预期：编译失败或断言失败，原因包括 `VisualTimeline`、`IsLooping`、`MoveVisual`、`DurationSeconds` 尚不存在；不得通过删除断言绕过。

- [x] **步骤 3：实现最少核心代码**

在 `MediaAsset` 增加：

```csharp
public double DurationSeconds { get; set; }
```

在 `PlaybackScheme` 增加统一循环、独立音频判定、按路径删除和按索引移动。旧 `IsVideoLooping`、`IsVisualLooping`、`IsAudioLooping` 使用只写 JSON 属性迁移为 `IsLooping |= value`；旧图片组仍设置 `IsLooping = true`。

`VisualTimeline` 使用以下公开契约：

```csharp
public sealed record VisualTimelineLocation(
    int Index, MediaAsset Asset, TimeSpan ItemStart, TimeSpan ItemEnd, TimeSpan ItemPosition);

public sealed class VisualTimeline
{
    public VisualTimeline(IReadOnlyList<MediaAsset> visuals, int imageDurationSeconds);
    public TimeSpan TotalDuration { get; }
    public bool HasCompleteDuration { get; }
    public TimeSpan GetItemEnd(int index);
    public TimeSpan GetGlobalPosition(int index, TimeSpan itemPosition);
    public VisualTimelineLocation Locate(TimeSpan position);
}
```

图片使用方案停留秒数，视频使用 `DurationSeconds`；`Locate` 将位置限制在 `[0, TotalDuration]`，边界采用当前项终点语义。

- [x] **步骤 4：运行核心测试验证绿灯**

运行：`dotnet test AVPC.sln --no-restore`
预期：全部核心测试通过。

### 任务 2：工作区时长和旧循环迁移

- [x] **步骤 1：增加持久化失败测试**

在 `WorkspacePersistenceTests.cs` 断言新 JSON 保存并恢复 `IsLooping`、`DurationSeconds`、画面顺序和音频顺序；旧 JSON 任一循环字段为 `true` 时恢复统一循环。明确检查不再写出 `IsVisualLooping` 和 `IsAudioLooping`。

- [x] **步骤 2：运行持久化测试**

运行：`dotnet test AVPC.sln --no-restore --filter WorkspacePersistenceTests`
预期：新增迁移或时长断言失败。

- [x] **步骤 3：记录导入时长并恢复旧记录**

在 `MediaImportService.CreateItemAsync` 分别读取视频或音频属性后构造：

```csharp
Asset = new MediaAsset(file.Path, kind) { DurationSeconds = duration.TotalSeconds }
```

图片时长保持 0。`MainWindow.InitializeWorkspaceAsync` 先载入全部 `MediaLibraryItem`，再按不区分大小写的路径把最新 `DurationSeconds` 写回工作区素材和每个方案的 `Visuals`、`Audios`，最后创建卡片。

- [x] **步骤 4：重跑持久化与完整测试**

运行：`dotnet test AVPC.sln --no-restore`
预期：全部测试通过。

### 任务 3：浅色双队列界面和即时数据

- [x] **步骤 1：扩展卡片行数据**

保留右侧 `AudioBranchRow`，新增中间 `AudioItemRow`。`VisualItemRow` 和 `AudioItemRow` 都提供 `Number`、`Name`、`Thumbnail`、`Duration`、`Asset`、`Card`。`SchemeCardViewModel.Refresh` 每次重建 `VisualRows` 和 `AudioItemRows`；增加单一 `IsLooping` 绑定属性并触发 `Changed`。

- [x] **步骤 2：重做中间 XAML**

只替换 `MainWindow.xaml` 的 `Grid.Column="1"` 区域：

- 根 `Border` 使用 `#FFF5F6F7`，标题和正文使用深色。
- 保留 `PreviewView`、`PreviewImage`、`PreviewFadeOverlay`、`PreviewProgress`、`PreviewVolume` 及现有事件名。
- 新增 `PreviewTimeText`，显示 `当前时间 / 总时长`。
- `VisualGallery` 和 `AudioGallery` 使用横向 `ItemsStackPanel`、`SelectionMode="Single"`、`CanDragItems="True"`、`CanReorderItems="True"`。
- 两个列表的卡片都包含序号、缩略图、文件名、时长、右上角删除按钮和拖拽手柄；选中容器使用绿色描边。
- 两个独立虚线框分别连接 `VisualAppendDrop` 和 `AudioAppendDrop`。
- 保留 `ImageDurationBox`、`EndBehaviorSelector`，用一个 `LoopCheck` 替换 `VisualLoopCheck` 和 `AudioLoopCheck`。
- 移除中间旧 `SelectedAudioList`，右侧方案卡片只删除两个循环 `CheckBox`，保留音频摘要。

- [x] **步骤 3：编译检查 XAML**

运行：`dotnet build src/AVPC/AVPC.csproj --no-restore`
预期：新增事件处理器尚未连接完整时出现明确编译错误；完成步骤 4 后再以 0 错误为通过标准。

- [x] **步骤 4：连接选择和删除处理器**

实现 `VisualItemClicked`、`RemoveVisualClicked`、中间与右侧共用的音频删除入口。音频列表不连接 `ItemClick`。所有删除先通过 `EnsureEditable`，随后更新核心集合、刷新卡片、保存；若删除前正在预览该方案且画面队列仍有效，则从头重新预览。

### 任务 4：双队列追加和拖拽排序

- [x] **步骤 1：实现音频追加入口**

将现有 `VisualAppendDragOver` 泛化为按目标类型检查拖放，新增：

```csharp
private void AudioAppendDragOver(object sender, DragEventArgs e);
private async void AudioAppendDrop(object sender, DragEventArgs e);
```

`DropMediaAsync` 接收追加入口允许的 `MediaKind`：画面入口拒绝音频并提示使用音频队列；音频入口只执行 `AssignMedia` 的音频追加，拒绝图片和视频。

- [x] **步骤 2：实现排序同步**

在两个列表的 `DragItemsCompleted` 中，从拖动行取得旧索引，从列表 `Items.IndexOf(row)` 取得新索引，调用 `MoveVisual` 或 `MoveAudio`。随后刷新对应卡片和编辑器、重新编号、保存，并按规格重启受影响的预览。

- [x] **步骤 3：构建验证交互代码**

运行：`dotnet build src/AVPC/AVPC.csproj --no-restore`
预期：0 错误；新增 XAML 事件均能找到处理器。

### 任务 5：连续预览和素材结束点定位

- [x] **步骤 1：建立预览时间轴状态**

在 `MainWindow` 增加 `_previewTimeline`、`_previewAtItemEnd` 和图片计时 `Stopwatch`。`PlayPreview` 创建 `VisualPlaylist` 与 `VisualTimeline`，设置总进度最大值和时间文字；`PlayCurrentPreviewVisual` 不再把进度归零。

视频 tick 使用：

```csharp
var position = _previewTimeline.GetGlobalPosition(
    _previewPlaylist.CurrentIndex, _previewVideo.PlaybackSession.Position);
PreviewProgress.Value = position.TotalSeconds;
```

图片使用同一个 50 ms 定时器和 `Stopwatch.Elapsed` 更新本地及全局位置；到达图片停留时长后推进，末张图片非循环也先完整停留再结束。

- [x] **步骤 2：实现点击结束点和统一 seeking**

`VisualItemClicked` 调用 `MoveTo(row.Number - 1)`、暂停独立音频与计时、设置 `_previewAtItemEnd`，图片直接显示，视频在 `MediaOpened` 后定位到末尾附近并暂停；界面进度使用 `GetItemEnd`。

`SetPreviewPaused(false)` 在 `_previewAtItemEnd` 时先推进到下一项；末项按 `IsLooping` 回绕或保持结束。进度条 released 事件通过 `VisualTimeline.Locate` 找到素材和本地位置，再创建对应预览播放器。

- [x] **步骤 3：统一正式输出循环与声音**

把 `PlaybackCoordinator` 和预览中的 `scheme.IsVisualLooping` 改为 `scheme.IsLooping`，视频静音条件统一为 `scheme.HasIndependentAudio`。保留独立 `AudioPlaylist` 的自然顺序，不把列表循环映射为单曲循环。

- [x] **步骤 4：运行完整自动测试**

运行：`dotnet test AVPC.sln`
预期：失败 0，所有新增和现有测试通过。

### 任务 6：最终验证和交付

- [x] **步骤 1：检查修改范围和并发改动**

运行：`git diff --check`、`git status --short`、`git diff --stat`
确认不触碰 `AGENTS.md`、素材库、构建产物和无关模块，不覆盖其他任务的已有修改。

- [x] **步骤 2：执行规定构建**

依次运行：

```powershell
dotnet test AVPC.sln
dotnet build AVPC.sln
dotnet build src/AVPC/AVPC.csproj
```

预期：测试失败 0；两次构建均 0 错误，并检查新增警告。

- [x] **步骤 3：提供用户手动验收清单**

覆盖：C 浅色布局、示例总时长 04:10、点击第 2 张图片到 01:05、跨素材进度和 seeking、画面与音频删除排序、音频点击不试听、统一循环、无音频时视频原声、有音频时视频静音、重启恢复。明确这些界面、声音、双屏和视觉效果由用户验证。
