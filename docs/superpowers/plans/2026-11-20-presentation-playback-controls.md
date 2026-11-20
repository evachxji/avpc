# 展示播放控制实现计划

> **面向 AI 代理的工作者：** 在当前会话按顺序执行；遵循测试先行，不回退或提交工作区已有修改。

**目标：** 将正式输出改为开启展示、结束展示、上一套、开始/暂停、下一套五按钮，并支持完整暂停与恢复。

**架构：** 保留 `MainWindow` 作为按钮状态入口、`PlaybackQueue` 管理方案索引、`PlaybackCoordinator` 管理媒体和计时、`PlaybackWindow` 管理淡出动画。开启窗口与开始播放分离，暂停不清理当前队列。

**技术栈：** C#、.NET 8、WinUI 3、Windows App SDK、xUnit。

---

### 任务 1：上一套核心导航

**文件：** `src/AVPC.Core/PlaybackQueue.cs`、`tests/AVPC.Tests/PlaybackQueueTests.cs`

- [ ] 新增测试：未开始和第一套时 `Previous()` 返回 `null` 且索引不变；从后续方案返回上一套。
- [ ] 运行 `dotnet test tests/AVPC.Tests/AVPC.Tests.csproj --filter PlaybackQueueTests`，确认因缺少 `Previous` 编译失败。
- [ ] 实现 `Previous()`：仅当当前索引大于 0 时递减并返回 `CurrentScheme`，否则返回 `null`。
- [ ] 重跑测试确认通过。

### 任务 2：五按钮及正式输出状态

**文件：** `src/AVPC/MainWindow.xaml`、`src/AVPC/MainWindow.xaml.cs`

- [ ] 将底栏改为五列按钮，依次绑定 `OpenOutputClicked`、`EndOutputClicked`、`PreviousOutputClicked`、`PlayPauseOutputClicked`、`NextOutputClicked`。
- [ ] 增加 `_isOutputPaused`；开启展示仅调用 `EnsureOutputWindow()`，不停止预览、不推进队列。
- [ ] 开始/暂停：无当前方案时验证并 `Advance()`；播放中调用 `Pause()`；暂停中调用 `Resume()`。
- [ ] 上一套调用 `Previous()`，下一套调用 `Advance()`；目标非空时停止预览并调用 `_coordinator.Play(scheme)`，目标为空时停止输出但保留黑屏窗口。
- [ ] 结束展示复用 `StopOutput()`，随后关闭并清空窗口和协调器；状态栏和开始/暂停/继续文字随状态更新。

### 任务 3：暂停与恢复全部输出

**文件：** `src/AVPC/Services/PlaybackCoordinator.cs`、`src/AVPC/PlaybackWindow.xaml.cs`

- [ ] `PlaybackCoordinator` 增加幂等的 `Pause()`/`Resume()`，成对暂停和恢复 `_videoPlayer`、`_audioPlayer`、`_fadeTimer`。
- [ ] 图片计时启动时记录截止时间和剩余时间；暂停时停止计时器，恢复时用剩余时间重建，避免重新计时整张图片。
- [ ] `PlaybackWindow` 保存输出淡出 `Storyboard`，提供 `PauseFade()`/`ResumeFade()`，并在清屏、重置和结束时停止旧动画；保留预览使用的静态 `FadeToBlack`。
- [ ] `Play()`、`Stop()` 和手动切换方案复位暂停状态，确保新方案直接播放。

### 任务 4：验证

- [ ] 运行 `dotnet test AVPC.sln`，全部通过。
- [ ] 运行 `dotnet build AVPC.sln`，0 错误并检查新增警告。
- [ ] 运行 `dotnet build src/AVPC/AVPC.csproj`，更新实际启动输出。
- [ ] 提供手动验证步骤，不启动或操控用户窗口；明确物理双屏、声音、视觉和实际暂停效果未由代理验证。
