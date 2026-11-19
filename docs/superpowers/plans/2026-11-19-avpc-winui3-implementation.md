# AVPC 音视频播放控台实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法跟踪进度。

**目标：** 交付可运行的 C# / WinUI 3 双屏多媒体控台，支持视频、音频、图片、方案预览、拖拽排序、独立循环和逐套手动切换。

**架构：** 解决方案分为核心状态库、WinUI 3 应用和核心测试。核心库不依赖 UI，负责方案验证、队列状态与图片轮播；应用负责素材读取、双窗口、原生媒体播放和 Mica 界面。

**技术栈：** .NET 8、Windows App SDK、WinUI 3、Windows.Media.Playback.MediaPlayer、xUnit。

---

### 任务 1：建立解决方案与验证环境

**文件：**
- 创建：`AVPC.sln`
- 创建：`src/AVPC.Core/AVPC.Core.csproj`
- 创建：`src/AVPC/AVPC.csproj`
- 创建：`tests/AVPC.Tests/AVPC.Tests.csproj`
- 创建：`.gitignore`

- [x] **步骤 1：安装并验证 .NET 8 SDK**

运行：`winget install --id Microsoft.DotNet.SDK.8 --exact --accept-source-agreements --accept-package-agreements`，然后在新进程运行 `dotnet --version`。预期：输出 8.x SDK。

- [x] **步骤 2：创建解决方案和 3 个项目**

运行：

```powershell
dotnet new sln -n AVPC
dotnet new classlib -n AVPC.Core -o src/AVPC.Core -f net8.0
dotnet new xunit -n AVPC.Tests -o tests/AVPC.Tests -f net8.0
dotnet sln AVPC.sln add src/AVPC.Core src/AVPC tests/AVPC.Tests
dotnet add tests/AVPC.Tests reference src/AVPC.Core
```

手工建立 WinUI 应用项目，目标为 `net8.0-windows10.0.19041.0`，启用 `UseWinUI`、`WindowsPackageType=None`，加入 `Microsoft.WindowsAppSDK` 稳定包并引用核心库。

- [x] **步骤 3：忽略构建产物和本地素材**

`.gitignore` 至少包含：

```gitignore
bin/
obj/
.vs/
.superpowers/
素材库/
*.user
```

- [x] **步骤 4：验证项目结构**

运行：`dotnet sln AVPC.sln list`。预期：列出核心、应用、测试 3 个项目。

- [x] **步骤 5：提交**

运行：`git add AVPC.sln src tests .gitignore; git commit -m "chore: 建立 AVPC 解决方案"`。

### 任务 2：以测试驱动实现素材、方案和队列

**文件：**
- 创建：`src/AVPC.Core/MediaAsset.cs`
- 创建：`src/AVPC.Core/PlaybackScheme.cs`
- 创建：`src/AVPC.Core/PlaybackQueue.cs`
- 创建：`tests/AVPC.Tests/PlaybackQueueTests.cs`
- 创建：`tests/AVPC.Tests/PlaybackSchemeTests.cs`

- [x] **步骤 1：先写队列失败测试**

测试必须覆盖：空队列不播放、首次进入第一套、连续切换、最后一套后停止、停止后再次开始回到第一套、排序影响下一套。关键断言：

```csharp
var queue = new PlaybackQueue(new[] { first, second });
Assert.Same(first, queue.Advance());
Assert.Same(second, queue.Advance());
Assert.False(queue.IsPlaying);
Assert.Null(queue.Advance());
Assert.Null(queue.CurrentScheme);
Assert.Same(first, queue.Advance());
queue.Move(0, 1);
queue.Stop();
Assert.Same(second, queue.Advance());
```

- [x] **步骤 2：运行测试确认红灯**

运行：`dotnet test tests/AVPC.Tests`。预期：因核心类型尚未实现而编译失败。

- [x] **步骤 3：实现最少核心模型**

公开 API：

```csharp
public enum MediaKind { Video, Audio, Image }
public sealed record MediaAsset(string Path, MediaKind Kind)
{
    public string DisplayName => /* 去除开头的数字-十六进制-前缀 */;
}
public enum VideoEndBehavior { HoldLastFrame, FadeToBlack }
public sealed class PlaybackScheme
{
    public string Name { get; set; }
    public MediaAsset? Video { get; set; }
    public List<MediaAsset> Images { get; } = new();
    public MediaAsset? Audio { get; set; }
    public bool IsVideoLooping { get; set; }
    public bool IsAudioLooping { get; set; }
    public int ImageDurationSeconds { get; set; } = 5;
    public VideoEndBehavior EndBehavior { get; set; }
    public string? Validate();
}
public sealed class PlaybackQueue
{
    public PlaybackQueue(IEnumerable<PlaybackScheme> schemes);
    public IReadOnlyList<PlaybackScheme> Schemes { get; }
    public int? CurrentIndex { get; private set; }
    public PlaybackScheme? CurrentScheme { get; }
    public bool IsPlaying => CurrentIndex.HasValue;
    public PlaybackScheme? Advance();
    public void Stop();
    public void Move(int oldIndex, int newIndex);
}
```

`Advance` 在空闲时选择索引 0；播放中到达末尾时调用 `Stop` 并返回 `null`。`Move` 只调整顺序，不改变当前方案对象。

- [x] **步骤 4：先写方案验证失败测试并确认失败**

覆盖：无画面无效、视频与图片同时存在无效、图片秒数小于 1 无效、纯视频有效、纯图片有效、图片默认 5 秒、显示名清理。

- [x] **步骤 5：实现验证并运行绿灯**

运行：`dotnet test tests/AVPC.Tests`。预期：所有测试通过。

- [x] **步骤 6：提交**

运行：`git add src/AVPC.Core tests/AVPC.Tests; git commit -m "feat: 实现方案队列与验证"`。

### 任务 3：以测试驱动实现图片轮播状态

**文件：**
- 创建：`src/AVPC.Core/ImageSlideshow.cs`
- 创建：`tests/AVPC.Tests/ImageSlideshowTests.cs`

- [x] **步骤 1：编写失败测试**

覆盖单张固定、多张顺序、末尾回到第一张、使用方案停留秒数。预期 API：

```csharp
var show = new ImageSlideshow(scheme);
Assert.Equal(scheme.Images[0], show.Current);
Assert.Equal(scheme.Images[1], show.MoveNext());
Assert.Equal(scheme.Images[0], show.MoveNext());
Assert.Equal(TimeSpan.FromSeconds(5), show.Interval);
```

- [x] **步骤 2：确认失败后实现**

`ImageSlideshow` 只保存图片引用与当前索引；`MoveNext` 对图片数量取模。空图片组由 `PlaybackScheme.Validate` 阻止，不另建抽象。

- [x] **步骤 3：验证并提交**

运行：`dotnet test tests/AVPC.Tests`，随后提交 `feat: 实现图片顺序轮播`。

### 任务 4：实现 WinUI 外壳、Mica 与双屏窗口

**文件：**
- 创建：`src/AVPC/App.xaml`
- 创建：`src/AVPC/App.xaml.cs`
- 创建：`src/AVPC/MainWindow.xaml`
- 创建：`src/AVPC/MainWindow.xaml.cs`
- 创建：`src/AVPC/PlaybackWindow.xaml`
- 创建：`src/AVPC/PlaybackWindow.xaml.cs`
- 创建：`src/AVPC/Services/DisplayService.cs`
- 创建：`src/AVPC/app.manifest`

- [x] **步骤 1：建立应用入口和 Mica 窗口**

`App.OnLaunched` 创建并激活 `MainWindow`。主窗口使用 `MicaBackdrop`、自定义标题栏和 `net8.0-windows10.0.19041.0` 支持的 WinUI 控件。

- [x] **步骤 2：建立正式播放窗口**

播放窗口包含：

```xml
<Grid Background="Black">
    <MediaPlayerElement x:Name="VideoView" Stretch="Uniform" />
    <Image x:Name="ImageView" Stretch="Uniform" />
    <Grid x:Name="FadeOverlay" Background="Black" Opacity="0" />
</Grid>
```

窗口不提供媒体控制条，正式输出始终无边框全屏。

- [x] **步骤 3：实现显示器选择和全屏定位**

用 `DisplayArea.FindAll()` 获取显示器，用 `AppWindow.Move` 将窗口移到目标工作区；正式窗口使用 `FullScreen` presenter。控制台与输出分别可选，只有一个显示器时安全回退。

- [x] **步骤 4：编译验证**

运行：`dotnet build src/AVPC`。预期：WinUI XAML 编译和 C# 编译无错误。

- [x] **步骤 5：提交**

提交 `feat: 建立 Mica 双屏窗口外壳`。

### 任务 5：实现素材导入与方案编辑

**文件：**
- 创建：`src/AVPC/Services/MediaImportService.cs`
- 修改：`src/AVPC/MainWindow.xaml`
- 修改：`src/AVPC/MainWindow.xaml.cs`

- [x] **步骤 1：实现多选导入**

`FileOpenPicker` 支持 `.mp4 .mov .mkv .avi .mp3 .wav .m4a .jpg .jpeg .png .bmp`，通过窗口 HWND 初始化。根据扩展名创建 `MediaAsset`，读取可用的视频属性与缩略图；重复路径不重复加入。

- [x] **步骤 2：实现三栏控制台**

左栏素材库，中栏预览，右栏方案 ListView；开启 `CanReorderItems`、`CanDragItems`、`AllowDrop`。底部固定开始／下一套和停止按钮，顶部提供屏幕 ComboBox。

- [x] **步骤 3：实现方案编辑**

每套可命名，选择视频或图片组；图片支持多选和默认 5 秒的 NumberBox；音频 ComboBox 包含「使用视频原声／无独立音频」。视频和音频各有 CheckBox，视频结束行为 ComboBox 包含「最后一帧」与「淡出 2 秒」。

- [x] **步骤 4：验证导入和排序**

构建后运行应用，从 `素材库` 导入实际文件，新建 4 套并拖拽排序；检查列表显示名、选中状态和队列顺序。

- [x] **步骤 5：提交**

提交 `feat: 实现素材导入与方案编排`。

### 任务 6：实现预览、双播放器与结束行为

**文件：**
- 创建：`src/AVPC/Services/PlaybackCoordinator.cs`
- 修改：`src/AVPC/PlaybackWindow.xaml.cs`
- 修改：`src/AVPC/MainWindow.xaml.cs`

- [x] **步骤 1：实现预览隔离**

预览使用控制台自己的 MediaPlayerElement 和音频 MediaPlayer；选中方案可预览，正式开始必须停止预览并清空其媒体源。

- [x] **步骤 2：实现正式播放**

协调器对每次 `Advance` 返回的方案执行：停止上一组播放器与轮播定时器；视频方案加载视频，图片方案建立 `ImageSlideshow` 和 DispatcherTimer；可选音频独立播放。存在外部音频时 `VideoPlayer.IsMuted = true`。

- [x] **步骤 3：实现循环和结尾**

循环直接映射到 `IsLoopingEnabled`。非循环视频自然结束保持最后帧；淡出方案在剩余 2 秒启动黑色覆盖层线性动画，结束后保留黑色。音频自然结束不换套。

- [x] **步骤 4：实现主按钮与停止**

空闲点击开始第一套；播放中点击切换下一套；队列末尾点击停止。停止同时清空视频、图片、音频、淡入淡出状态和轮播计时器。

- [x] **步骤 5：验证**

运行核心测试并构建应用；用实际素材验证外部音频、循环互不影响、预览不换正式方案、末尾停止。

- [x] **步骤 6：提交**

提交 `feat: 实现预览与全屏多媒体播放`。

### 任务 7：错误提示与最终验收

**文件：**
- 修改：`src/AVPC/MainWindow.xaml`
- 修改：`src/AVPC/MainWindow.xaml.cs`
- 修改：`src/AVPC/Services/PlaybackCoordinator.cs`
- 创建：`README.md`

- [x] **步骤 1：加入可见错误状态**

使用 InfoBar 显示缺少画面、文件不可访问、媒体解码失败与屏幕回退；错误不清空用户方案，正式窗口保持黑色。

- [x] **步骤 2：编写运行说明**

README 记录 .NET 8 SDK、Windows App SDK 运行要求，以及 `dotnet test`、`dotnet build src/AVPC`、`dotnet run --project src/AVPC` 命令和基本操作。

- [x] **步骤 3：完整自动验证**

运行：

```powershell
dotnet test AVPC.sln
dotnet build AVPC.sln
```

预期：测试全部通过，构建 0 错误。

- [x] **步骤 4：实际界面验收**

启动应用，检查 Mica、真实素材、预览、图片轮播、双屏全屏、逐套切换和最后停止。若当前环境无法验证第二台物理显示器，明确记录为未完成的物理双屏验证，不宣称已验证。

- [x] **步骤 5：提交**

提交 `docs: 添加运行说明并完成验收`。

