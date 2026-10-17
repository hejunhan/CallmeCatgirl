# 验证状态

执行环境：2026-10-17，独立任务 `task-11`，Windows / .NET SDK 9.0.319。Unity 程序集只读引用来自 `D:/Unity/Editor/Data` 及主工程 `Library/ScriptAssemblies/UnityEngine.UI.dll`。

## 已执行

1. 直接读取试玩 HTML 的 difference clip-path、面板 motion/restoration、模型页 motion 和生命周期实现，并核对现有 CoreGameplayController/CanvasView 接口。
2. `Validation/Run-Checks.ps1`：34 项纯 C# 检查通过。涵盖 600ms 双向扫线及中途反转、重复请求、零/无效 delta、物理 45° 几何、整面板覆盖门槛、offscreen 保守处理（含旋转边穿过 viewport 角点的反例）、漂移延迟启动、hover 持有旋转、600ms 回位与再反转、独立随机、10000 步位姿边界、700ms 恢复可反向、difference 公式与遮罩并集、620ms 模型展开、精确矩形反转、resize 剩余时间、焦点零 delta、.78 揭示门槛、移动过的 trigger 再打开与 reduced motion。
3. 纯逻辑热路径预热后 10000 次 tick 的 `GC.GetAllocatedBytesForCurrentThread` 差值为 0。
4. `dotnet build EditorTests.csproj ... -m:1 -p:UseSharedCompilation=false`：Core、Runtime、六项 Editor 测试源代码均已对本机 Unity 引用程序集编译，最终结果 **0 个错误、0 个警告**。编译已发现并修复 Unity 6000.5 将 GetInstanceID 标记为错误级弃用的问题；自动种子不再使用该 API。

离线重现命令：

```powershell
cd C:\Users\81004\Documents\Codex\2026-10-17\task-11\Validation
.\Run-Checks.ps1
```

脚本把 dotnet/NuGet 临时目录放在 task-11 内，并清空外部 NuGet 源。可用参数覆盖本机 UnityEditorData 和 GameProject 路径。`Write-UnityMetadata.ps1` 可重建本模块稳定 GUID 的 meta 与两个材质；它只写 task-11 的 Assets。

## 已编写但未运行

Unity Test Runner 的六项 Editor 测试：时钟反转、整角覆盖门槛、hover 持有旋转、模型页矩形反转、反色 Graphic 永不拦截射线、Input System 正 pointerId 的 hover 状态。

这些测试已经编译，**没有在 Unity 中执行**。特别是两个 GameObject/EventSystem 测试需要 Unity 引擎环境，不能把离线 dotnet 编译视为输入验收。

## 主集成任务验收清单

以下均未实测；应在主任务持有的 Unity 实例中完成，勿为此再启动另一个实例。

- [ ] 导入全部 `.meta` 和材质后，无 shader 编译错误/粉色材质；DifferenceWipe 与 SignalGlow 引用正确 shader。
- [ ] 1920×1080、3440×1440、1080×1920：扫线从右下到左上，边缘保持物理 45°，完整行程 600ms；快按两次/多次暂停时无闪白和 progress 跳变。
- [ ] 拖动中进入/退出手动暂停，ghost 同步反色，pointer up 正确放置；暂停期间仍可旋转、撤回及提交已完成任务。
- [ ] Frame Debugger 中只有一层 difference draw，没有截图/CopyColor/GrabPass；第二实例配置按预期被拒绝，不发生两次反色相消。
- [ ] 左上大面板在最后角点覆盖之前保持基准位置；有后纸时等待两层全扫过；重进 planning 重新判断门槛。
- [ ] 漂移保留细微旋转。hover/Tab 焦点进入后持有当前旋转，用约 700ms 恢复原色；离开后反向恢复负片，不出现深浅叠加。
- [ ] 旋转档案前后两层重叠区域没有二次反色。试验同屏多个面板同时获得 hover/键盘焦点时的遮罩并集。
- [ ] 在同一面板子按钮之间 Tab，不触发整面板离开/回位；应用 Alt-Tab、最小化与返回只冻结/继续，不切换手动暂停意图，不误开模型页。
- [ ] 模型页仅一张白底由入口矩形向外扩；内容被 frame 的 RectMask2D 裁剪，.78 后按 Delay 出现。关闭中途重开无矩形瞬移。
- [ ] 模型页展开中和完全打开后 resize；剩余动画时间没有重启，落点与新 viewport 对齐；关闭时白底不阻挡操作。
- [ ] 模型页驱动持续激活，原有 popup 显隐没有提前截断回缩；只由真实模态状态阻断游戏命令。
- [ ] reduced motion：无漂移，页面和扫线直达终态，内容无位移；输入仍可用。
- [ ] 正常/反色/原色恢复三种状态下，检查 Linear 空间的灰阶差异是否符合美术验收。该实现不承诺浏览器 sRGB 像素匹配。
- [ ] Profiler 预热后测动效组件自身每帧 GC.Alloc；检查 willRenderCanvases 多次调用仍只采样一次，8 个面板时帧耗时可接受。
- [ ] 光晕在 UI 遮罩与缩放下无越界，不遮挡输入；不把 Overlay 加法光晕误当成已验证的 URP Bloom。

未执行 Unity 渲染、真实鼠标/键盘交互、PlayMode、Player build 或 GPU 性能测试。没有执行被拒绝或结果不确定的主工程写入操作；主工程始终只读。
