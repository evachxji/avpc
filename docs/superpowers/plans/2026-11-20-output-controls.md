# 正式输出控制调整实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [x]`）语法来跟踪进度。

**目标：** 将右上角合并为唯一的“展示屏:”选择器，让“停止输出”仅在正式播放期间显示，并新增可关闭输出窗口但保留主控台的“结束”按钮。

**架构：** 沿用 `MainWindow` 中现有的 `PlaybackQueue`、`PlaybackCoordinator` 和 `PlaybackWindow` 状态流。XAML 负责初始可见性与布局，事件处理负责播放状态切换和输出窗口关闭；不修改 `AVPC.Core`，不引入新 ViewModel。

**技术栈：** C#、.NET 8、WinUI 3、Windows App SDK、xUnit（现有回归测试）。

**测试策略：** 按项目 AGENTS.md，核心纯状态行为才要求先写失败测试；本次没有核心状态转换或持久化模型变化，不新增单元测试。成功标准为现有测试通过、解决方案构建通过，以及实际界面状态检查。未经用户要求不创建 Git 提交。

---

## 文件结构

- 修改 `src/AVPC/MainWindow.xaml`：右上角展示屏选择和底栏按钮布局。
- 修改 `src/AVPC/MainWindow.xaml.cs`：删除控制台屏幕选择事件，同步停止按钮可见性，实现输出窗口关闭与重建所需的引用清理。
- 保留 `src/AVPC/App.xaml.cs`：启动时自动定位主控台的逻辑不变。
- 保留 `src/AVPC/Services/PlaybackCoordinator.cs` 与 `src/AVPC/PlaybackWindow.xaml.cs`：现有停止与清空行为不变。

### 任务 1：合并展示屏选择并调整底栏

**文件：**
- 修改：`src/AVPC/MainWindow.xaml:31-45`
- 修改：`src/AVPC/MainWindow.xaml.cs:37-70`
- 修改：`src/AVPC/MainWindow.xaml.cs:354-364`

- [x] **步骤 1：确认并保留工作区已有修改**

运行：`git diff -- src/AVPC/MainWindow.xaml src/AVPC/MainWindow.xaml.cs`

预期：仍能看到另一任务已有的预览播放/暂停、进度拖动、音量同步和音频删除改动；本次编辑不得覆盖这些改动。

- [x] **步骤 2：将右上角改为单个展示屏选择器**

用下列布局替换顶部三列 Grid 的列定义和两个 ComboBox；标题区域保持原样：

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="Auto" />
</Grid.ColumnDefinitions>
<StackPanel Orientation="Horizontal" Spacing="12" VerticalAlignment="Center">
    <TextBlock Text="播放控制台" FontSize="24" FontWeight="SemiBold" VerticalAlignment="Center" />
    <Border Background="#226F9FF4" CornerRadius="6" Padding="10,5" VerticalAlignment="Center">
        <TextBlock x:Name="ModeText" Text="●  预览模式" Foreground="#FFACCAFF" FontSize="12" />
    </Border>
</StackPanel>
<StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
    <TextBlock Text="展示屏:" VerticalAlignment="Center" />
    <ComboBox x:Name="OutputDisplaySelector" MinWidth="195" SelectionChanged="OutputDisplayChanged" />
</StackPanel>
```

- [x] **步骤 3：调整底栏按钮**

将底栏 Grid 改为四列，保留状态文字，并加入初始隐藏的停止按钮和始终显示的结束按钮：

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="Auto" />
    <ColumnDefinition Width="Auto" />
    <ColumnDefinition Width="Auto" />
</Grid.ColumnDefinitions>
<StackPanel VerticalAlignment="Center">
    <TextBlock x:Name="StatusText" Text="准备就绪" FontSize="12" />
    <TextBlock Text="正式播放按方案顺序手动切换，最后一套之后停止" FontSize="10" Foreground="{ThemeResource TextFillColorSecondaryBrush}" Margin="0,3,0,0" />
</StackPanel>
<Button Grid.Column="1" Content="结束" Click="EndOutputClicked" />
<Button x:Name="StopOutputButton" Grid.Column="2" Content="■ 停止输出" Visibility="Collapsed" Click="StopOutputClicked" />
<Button x:Name="PlayNextButton" Grid.Column="3" Content="▶ 开始正式播放" Style="{StaticResource AccentButtonStyle}" MinWidth="180" Click="PlayNextClicked" />
```

- [x] **步骤 4：删除控制台屏幕选择代码**

构造函数只保留输出选择器的显示成员设置：

```csharp
VideoSelector.DisplayMemberPath = "Asset.DisplayName";
OutputDisplaySelector.DisplayMemberPath = "Name";
```

`InitializeDisplays` 只初始化和选择输出屏幕：

```csharp
public void InitializeDisplays()
{
    _displays = DisplayService.GetDisplayOptions();
    OutputDisplaySelector.ItemsSource = _displays;
    OutputDisplaySelector.SelectedItem = _displays.FirstOrDefault(d => d.Area.IsPrimary) ?? _displays[0];
    RefreshEditorSources();
}
```

完整删除以下不再被 XAML 引用的方法：

```csharp
private void ConsoleDisplayChanged(object sender, SelectionChangedEventArgs e)
{
    if (ConsoleDisplaySelector.SelectedItem is DisplayOption display && _displays.Count > 0)
        DisplayService.PlaceConsole(this, display.Area);
}
```

保留 `OutputDisplayChanged`，使播放中切换展示屏仍调用 `DisplayService.ShowFullScreen`。

### 任务 2：同步停止按钮状态并实现结束按钮

**文件：**
- 修改：`src/AVPC/MainWindow.xaml.cs:428-463`

- [x] **步骤 1：正式播放成功后显示停止按钮**

在 `PlayNextClicked` 中，只有 `_coordinator.Play(scheme)` 返回后才设置可见：

```csharp
_coordinator!.Play(scheme);
StopOutputButton.Visibility = Visibility.Visible;
PlayNextButton.Content = "▶ 下一套";
ModeText.Text = "●  正式输出中";
StatusText.Text = $"正在输出 {scheme.Name} · {(_queue.CurrentIndex ?? 0) + 1}/{_schemes.Count}";
```

在 `_queue.Advance()` 返回 `null` 的现有自然停止分支中加入：

```csharp
StopOutputButton.Visibility = Visibility.Collapsed;
```

该分支其余状态复位文字保持不变。

- [x] **步骤 2：抽出停止输出的共享操作**

用以下实现替换现有 `StopOutputClicked`，让停止与结束共用队列、媒体和按钮状态复位：

```csharp
private void StopOutputClicked(object sender, RoutedEventArgs e)
{
    StopOutput();
    StatusText.Text = "正式输出已停止";
}

private void EndOutputClicked(object sender, RoutedEventArgs e)
{
    StopOutput();
    _outputWindow?.Close();
    _outputWindow = null;
    _coordinator = null;
    StatusText.Text = "正式输出已结束，输出窗口已关闭";
}

private void StopOutput()
{
    _queue.Stop();
    _coordinator?.Stop();
    SaveWorkspace();
    StopOutputButton.Visibility = Visibility.Collapsed;
    PlayNextButton.Content = "▶ 开始正式播放";
    ModeText.Text = "●  预览模式";
}
```

`EnsureOutputWindow` 保持不变：下次播放时，如果窗口引用为空，会创建新的 `PlaybackWindow` 和 `PlaybackCoordinator`，再使用 `SelectedOutputDisplay` 全屏显示。

- [x] **步骤 3：运行全部自动测试**

运行：`dotnet test AVPC.sln`

预期：所有现有测试通过，0 失败。

- [x] **步骤 4：构建整个解决方案**

运行：`dotnet build AVPC.sln`

预期：0 错误；检查输出中是否有本次修改引入的新警告。

- [x] **步骤 5：构建实际启动项目**

运行：`dotnet build src/AVPC/AVPC.csproj`

预期：构建成功，确保 `start.cmd` 不会继续使用旧程序。

- [x] **步骤 6：提供手动界面验证步骤**

遵循 AGENTS.md：不启动应用，不进行桌面自动化、鼠标/键盘注入或窗口操作。向用户提供以下手动检查步骤，由用户运行 `.\start.cmd` 后验证：

1. 初始右上角仅有“展示屏:”和一个下拉框，底栏没有“停止输出”。
2. 成功开始正式播放后显示“停止输出”，输出位于下拉框选中的屏幕。
3. 点击“停止输出”后媒体停止、按钮隐藏、输出窗口保留。
4. 再次正式播放并点击“结束”，输出窗口关闭而主控台保持运行；再次开始可重建输出窗口。
5. 播到最后一套后再次点击“下一套”，停止按钮隐藏。

无法实际验证的物理双屏、声音或视觉效果必须在交付说明中列出，不得宣称已验证。
