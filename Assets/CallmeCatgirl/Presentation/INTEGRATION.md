# 接入说明

本模块依据本机 `C:/Users/81004/Downloads/双击试玩.html` 的 CSS 和 JavaScript 实现。只读核对的目标工程为 Unity 6000.5.2f1、URP 17.5.0、uGUI 2.5.0，游戏 UI 位于 `_Game/Scripts/Runtime/UI/gameview`。

## 复制范围

复制本交付的 `Assets/CallmeCatgirl/Presentation` 以及它和上级文件夹的 `.meta`。保留 meta 中的 GUID，材质已直接引用 shader，构建时无需 `Shader.Find` 或 Always Included Shaders。没有 Unity 场景、共享控制器或 Package 的覆盖文件。

`Core` 是无 Unity 依赖的时钟和几何逻辑；`Runtime` 只引用 Core 与现有 `UnityEngine.UI`。主工程的 Assembly-CSharp 可直接引用公开类型。`Tests/Editor` 可保留供主集成任务执行，也可在最终打包时排除。

## 全屏反色层

创建一个独立且持续激活的根 Canvas，命名可用 `PlanningEffectsCanvas`：

1. 设置 Screen Space - Overlay、Target Display = 0，排序高于游戏、拖动预览和常规提示，例如 30000；模型页和真正的模态内容使用更高排序，例如 31000。
2. 该 Canvas 不添加 GraphicRaycaster、全屏 raycast Image 或阻挡输入的 CanvasGroup。Canvas 缩放保持默认即可。
3. 添加一个铺满父级的 RectTransform 子物体，挂 `DifferenceWipeGraphic`。它的 `Raycast` 永远返回 false，即使 Inspector 误勾选 Raycast Target 也不能挡住拖动。
4. 在根 Canvas 上挂 `PlanningPresentation`，将上述 Graphic、`Materials/DifferenceWipe.mat` 和需要漂移的 PlanningPaper 引用拖入字段。

每帧最多渲染一层 difference。代码会拒绝同一 display 上的第二个启用实例，并拒绝重复的面板引用和超过八个面板的配置。当前坐标路径针对主显示器；不要把该预制配置用于其它 Target Display 或非全屏相机 viewport。

Shader 用 `S + D - 2*S*D` 混合，在一个 pass 内得到 `lerp(D, 1-D, S)`。没有 GrabPass、屏幕纹理、RT 截图、CPU readback、相机叠加或额外 Renderer Feature。拖动 ghost 先画在底层，同样被最终扫线覆盖。

扫线按物理像素计算：`distance = width - x + y`。右下角为 0，左上角为 `width + height`。不直接使用归一化 UV 的 x+y，因此超宽屏和竖屏仍是物理 45°。进入时 progress 在 600ms 内由 0 到 1；恢复时朝 0 移动；再次请求立即从当前 progress 改变方向，不重置、不淡入第二层。往返速度相同，部分距离按比例耗时。

**色彩边界：** 主工程 `m_ActiveColorSpace: 1` 为 Linear。原生 framebuffer blending 按其工作空间反色，不能保证与浏览器在 sRGB 中的 difference 像素相同。白黑端点、扫线状态和原色恢复语义保持；中间灰阶会不同。若美术验收要求浏览器逐像素匹配，需要主任务另行决定一次最终合成采样路径，且采样必须包含 Overlay UI 与 ghost。本模块没有更改色彩配置，也没有假装线性反色是 sRGB 精确反色。

## 漂移面板及原色恢复

在真正包含整块纸张的外层对象上挂 `PlanningPaper`。可用队列面板、模型面板、说明纸和聊天档案面板；不要把可操作网格的内部坐标父节点当漂移目标。

- `Motion Root` 使用一个不受 LayoutGroup/Animator 同时写入位置、旋转的包装节点。LayoutGroup 管理其外层布局，包装节点负责视觉运动及随动 hit-test。包装节点及父级保持单位缩放，CanvasScaler 的缩放由模块转换为像素。
- 原有 anchoredPosition 与 localRotation 会被保存。漂移使用独立局部随机数，默认每个面板独立，不消耗游戏随机序列。平移取整到物理像素，旋转保留浮点精度与原有基准旋转。
- 默认随机目标为 ±10px、±12px、±0.9°，每段 6–13 秒。每段从当前姿态出发，五次平滑曲线保证端点速度和加速度归零；不会循环一条固定路径。
- `Rear Sheet` 可引用档案后层纸张的实际 RectTransform。它必须随同一个 Motion Root 移动，并与前层一起计入“整块面板扫完”的门槛。可按试玩页在右上方向错开 6px。
- 外部布局初始化完成后可调用一次 `CaptureHome()`；它将当前位置设为新的基准并重新初始化该纸张。不要每帧调用。

每次进入 planning 时清除“已扫过”标记。先应用已有姿态，再在 `Canvas.willRenderCanvases` 中采样实际旋转后的四角，用其可见包围矩形保守判断覆盖；前、后纸张都满足后，下一帧才允许首次漂移。完全屏外的纸张等待 progress=1。这里使用包围矩形是为了避免斜边穿过屏幕角点时，单独截取四角坐标产生提前启动。重新进入 planning 时也重新检查覆盖范围。

hover 和选中焦点都只冻结当前姿态，不把旋转清零。选中判断读取 `EventSystem.current.currentSelectedGameObject` 的父子关系，所以在同一面板的按钮之间 Tab 不会误判为离开整个面板。指针使用事件系统的 pointerId 集合，同时兼容 Input System 的正数 ID 与旧输入模块的负数 ID，不读取 Legacy Input。

当该面板已被当前扫线完全覆盖且 hover/焦点在其内部时，恢复量在 700ms 内趋向 1，并用 smoothstep 呈现；离开时趋向 0。Shader 用真实旋转四边形从全屏反色层中减去该区域，恢复的是已经合成的纸张像素。前后纸张及不同面板重叠处取最大恢复量，不会累计成二次反色。遮罩无截图，几何每纸每帧只采样一次；回调按 frameCount 去重。

矩形边缘有一个物理像素抗锯齿。当前遮罩不模拟 CSS 的圆角；建议使用极简直角纸张。Mask/ScrollRect 裁剪区域不是独立的遮罩边界，因此应注册整个可见外层纸张，避免注册只显示一部分的滚动内容本身。旋转文字不会保持逐像素对齐，模块有意保留用户要求的旋转。

## 手动暂停与暂时阻断

`SetPlanning(bool)` 只接受**手动暂停意图**。不根据 `Time.timeScale`、程序焦点或“任何原因的暂停”自动推断。相同请求是空操作。

现有 `CoreGameplayController.TogglePause()` 是手动入口。主集成任务可在成功切换 `Simulation.Paused` 后调用 CanvasView 的一个转发方法：

```csharp
// CoreGameplayCanvasView 内，由主集成任务添加并赋 Inspector 引用。
[SerializeField] private CallmeCatgirl.Presentation.PlanningPresentation planningPresentation;
public void SetManualPlanningVisual(bool paused)
    => planningPresentation?.SetPlanning(paused);
```

```csharp
// 原有 TogglePause 的成功分支内，保留其既有 GameEnded/IsModalOpen 检查。
Simulation.Paused = !Simulation.Paused;
canvasView?.SetManualPlanningVisual(Simulation.Paused);
canvasView?.Refresh();

// ResetSimulation/首次等待开始时：
canvasView?.SetManualPlanningVisual(false);
```

初始 `Simulation.Paused = true` 表示等待开局，不能在每次 Refresh 中直接映射成 planning，否则开局就会错误反色。主集成任务应在真正的手动操作后转发意图。

Refresh 中可以重复调用 `planningPresentation.SetPresentationBlocked(controller.IsModalOpen)`。它只冻结装饰时间并保持当前画面，不切换 planning 请求，不取消拖动，不关闭模型页。`OnApplicationFocus` 与 `OnApplicationPause` 也只冻结时间，回来后的第一帧丢弃异常时间差。按钮选择焦点不会被当成应用离开。

该模块从不写入 Simulation、Time.timeScale、Selectable.interactable（模型页自己的内容项除外）或 EventSystem 选中项，不调用 CancelDrag。现有 `BeginDrag/SubmitSelected` 的暂停可用性应保留：只有真实模态窗口、结束状态等原有条件阻断；手动暂停继续允许拖动、旋转与交付。

## 模型页白色外扩与错峰内容

模型页 Canvas 排在 difference 层上方。建议结构如下；`ModelPageReveal` 所在物体保持激活，并放在 frame 之外：

```text
ModelPageCanvas (Overlay sortingOrder=31000, GraphicRaycaster)
  ModelPageDriver (ModelPageReveal)
  Viewport (全屏 RectTransform，无背景)
  ExpandingFrame (白色 Image + RectMask2D；脚本控制尺寸，不受布局驱动)
    Content (CanvasGroup，无第二张全屏白底)
      Header (CanvasGroup)
      CurrentModel (CanvasGroup + 可移动包装)
      Cards... (各自 CanvasGroup + 可移动包装)
      Footer (CanvasGroup)
```

指定 trigger 为当前模型按钮，viewport 为静止的全屏矩形，frame 为上述唯一白底，content 为其直接子物体。frame 与 content 的缩放保持 1。frame 的 Image 始终不参与射线，RectMask2D 将内容限制在正在展开的白底内部。

`Open()` / `Close()` / `SetOpen(bool)` 可直接接现有模型库入口；重复请求不会重播。白底从 trigger 的实际轴对齐包围矩形向 viewport 展开，620ms 到达端点；关闭与中途反转从已画出的矩形继续。屏幕尺寸改变后，保留剩余时间从当前矩形改向新目标。已完全关闭后再次打开，会重新测量移动过的 trigger。

Expansion 达到 .78 后开始内容计时，各 RevealItem 的建议 Delay 为 0、.06、.10、.14、.195、.25、.305、.33、.36 秒；每项用 .32 秒淡入并从下方 12px 回位。Rise 和 Delay 均可在 Inspector 单独设置。内容使用原本模型 UI 数据和按钮，不复制天赋、剧情或模型选择逻辑。

RevealItem 的 MotionRoot 必须是布局节点内部的独立包装；若没有包装可留空，只淡入。一个可见项目应完整处于对应 CanvasGroup 下，避免没有列入 items 的内容从第一帧就出现。内容项达到全显后才接受点击，关闭请求立即释放本模块的内容射线；白底回缩期间仍不拦截游戏。

模态语义由现有游戏控制器负责。主任务可在 `OpenRequestedChanged` 事件中同步原有模型/升级模态状态，或由相同按钮依次请求游戏模态和动画。不要把原有 `popup.SetActive(false)` 直接作用在该驱动或 frame 的祖先上，否则会截断关闭动画；让 `ModelPageReveal` 自己在完全收起后隐藏 frame。需要模态外部点击阻断时，由现有模态背景处理，勿把阻断加到 planning 层。

`ReducedMotion` 可同时启用于规划层和模型页：扫线/白底直达终态，漂移回基准，内容不做位移动画。它不修改全局动画、时间或用户配置。

## 可选描边光晕

在装饰用的普通 Image 上使用 `Materials/SignalGlow.mat`，Image Type=Simple、不使用图集 sprite（UV 为完整 0..1）。默认细线在图像边缘向内 8px，周围生成指数衰减光晕；可把 Image 比可见纸边放大 16px，使亮线对齐纸边。

可挂 `SignalGlowPulse`，用 3.6 秒周期、12% 深度的轻呼吸。该组件只写自身 CanvasRenderer 的 alpha，不克隆材质，使用 unscaledDeltaTime，焦点失去时冻结；装饰 Image 的 Raycast Target 自动关闭。Shader 支持 stencil Mask 与矩形裁剪。

这是自带几何范围的加法光晕，不依赖 URP Bloom，也没有给生产 Volume 或管线开关加修改。Overlay Canvas 通常在后处理之后，不能声称此描边受 Bloom 验证。

## 性能与边界

- 最多 8 个纸张、每张最多前后两层，固定容量 16 个四边形。没有运行时屏幕纹理采样。
- 每屏一套 material 实例，在启用时创建，在禁用时销毁；无每帧 Material、LINQ、字符串拼接、GetComponentsInChildren 或 RaycastAll。
- 固定数组上传并复用四角缓存；HashSet 仅在指针进入/退出时维护。纯逻辑连续 10000 次 tick 的热路径分配检查为 0 字节；Unity 本身的 Profiler GC 仍待主任务实测。
- 当前范围是单主显示器、常规非 XR uGUI。不要在 HDR/XR、RenderTexture UI、非全屏相机或多显示器场景下直接承诺一致性；这些路径没有实测。
- 本交付没有自启 Unity、没有桌面输入，也没有修改主工程配置。
