# UI 窗口层级与暂停反色

本规则用于 CoreGameplay。排序和窗口接入由 `UIWindowLayers` 统一管理；不要在单个窗口中添加 Canvas、overrideSorting 或自行设置高 sortingOrder。

## 唯一层级

```text
GameplayCanvas                         order = UIWindowLayers.GameplayOrder (0)
  BaseInteractionRoot                  固定栏、候选、运算、普通操作
  DragAndHints                         拖拽预览、关联虚线、结算提示；不拦鼠标
  WindowLayer
    ModalInputBlocker                  统一透明输入阻挡层
    ConversationPopup                 共用 IM 浮窗
    MemoryUpgradePopup                升级模态页
    ProgressionOverlay                旧政策页，保留现有入口开关
    ModelGallery                      模型/训练模态页，首次打开创建后复用
    EndingPanel                       结局层
PlanningEffectsCanvas                  order = UIWindowLayers.PauseEffectOrder (100)
```

仅两个根 Canvas 参与呈现顺序。数值只定义在 `UIWindowLayers`；场景安装代码引用该常量。固定栏继承主 Canvas，不得越过窗口层。目录也继承主 Canvas 和缩放，不再建立32767根Canvas。窗口按 Floating、ModalPage、Ending 的优先级及实际打开顺序由宿主排列；所有窗口都在暂停特效之前绘制。

## 窗口接入

场景内现有窗口由宿主接管。运行时新建窗口统一调用：

```csharp
layers.Register(windowRoot, opaqueSurfaceRect, UIWindowKind.ModalPage);
```

`windowRoot` 必须是 RectTransform，宿主会把它挂到 WindowLayer 并拉伸到全画布；实际窗口的尺寸、位置由其内部内容矩形负责。第二个参数为实心可见内容矩形，不是全屏透明遮罩。注册同一根对象会更新绑定，不重复创建窗口。目录使用 WhiteFrame，聊天与升级使用 Window，结局使用 Content。当前固定窗口最多5类，反色层支持最多8个同时可见surface。

打开/关闭继续使用原业务入口和同一窗口实例，不新建第二套Story、训练状态或存档状态。双击会话和任务交付继续共用ConversationPopupView。不得通过复制窗口解决绑定或排序问题。

## 输入规则

- 没有模态窗口时，手动暂停不会关闭BaseInteractionRoot的交互；拖放、旋转、交付等仍由原命令入口处理。
- 所有 IM（历史、剧情、待回复）均为非模态 Floating，只挡自身矩形。窗口外仍可拖放、旋转模块；窗口底板接收 raycast，气泡、选项和窗口空白不穿透；拖拽在窗口内释放只取消预览，不移动底下模块。
- 升级、目录、旧政策、结局页属于阻塞交互。宿主打开透明ModalInputBlocker，并将BaseInteractionRoot的CanvasGroup统一设为interactable=false、blocksRaycasts=false。侧栏没有例外。
- 只有最上层可见窗口的CanvasGroup可交互，下面的窗口不能接收点击。拖拽/提示/反色Graphic始终不接收raycast。
- 命令层原业务校验继续有效；CanvasGroup不是业务授权。旧政策入口继续保持当前禁用状态，不借层级收口恢复旧成长机制。
- 待回复关窗仅隐藏视图，保留原待回复状态；同会话双击重开。隐藏不等于取消回复、重做结算或清除语料。切换会话由原待回复队列保留未完成回复，不产生全屏门禁。
- 棋盘模块和候选模块均可左键双击打开所属共用会话窗。拖拽由 EventSystem 阈值触发，已经开始拖拽的手势不能被当作双击。
- 运算完成后，IM 显示原质量允许的回复选项；选择时才调用原 TrySubmit → ReadEvents → ChooseAtRevision → 记录语料链路。普通请求不再需要独立提交按钮，训练任务保留原完成入口。未 Ready 时不提供可执行回复，过期回调不能重复结算。
- 打开、关闭、切换、提交、重开 IM 均不读写 Simulation.Paused，也不作为 AdvanceGame 或暂停按钮的阻断原因。不设阅读剧情暂停理由或标签。运行中保持运行，手动暂停中保持暂停；真实模态页规则保持不变。

## 唯一反色规则

手动暂停仍通过CoreGameplayController.TogglePause驱动PlanningPresentation.SetPlanning，保留45°扫动和当前纸面运动。初始“待开始”的Simulation.Paused与手动暂停呈现仍按原行为区分。

暂停时仅**可见运算区域**保持原色。当前统一绑定BoardPanel（包含棋盘与其操作区），而不是逐格或逐窗口设置排序例外。侧栏、候选、浮窗、升级和目录均反色。

宿主每帧提供运算surface和全部可见窗口surface的屏幕四角，唯一DifferenceWipe计算：

```text
invertAmount = sweep × [1 − computeCoverage × (1 − windowCoverage)]
```

因此窗口覆盖运算区时，该窗口仍反色；不会被运算矩形洞穿。透明输入遮罩不当作实心窗口surface，原窗口的背景变暗效果仍按原视觉保留。禁止把BoardPanel再抬到特效之上，否则会反过来遮住窗口。

旧papers仍负责既有漂移、旋转和悬停运动采样；它们不再拥有非运算区的颜色豁免。最新“仅运算区豁免”优先于旧的左纸面恢复白色。模态状态可暂停底层纸面运动，但不冻结sweep更新或窗口覆盖采样。全屏差值效果仍保持每display一个实例，不能叠两个反色层。

## 验收

1. 实际Canvas只有主UI与唯一反色层；固定栏、目录没有独立overrideSorting。
2. 升级打开后不被侧栏遮挡，侧栏raycast只命中模态遮罩/统一阻挡层，底层按钮不可交互。
3. 暂停只豁免可见BoardPanel，其余UI反色；叠在BoardPanel上的任意窗口内容仍反色。
4. 普通历史浮窗可由侧栏切换；交付仍打开同一实例，旧回复不残留；目录关闭重开不复制根对象。
5. 手动暂停下工作区仍可操作，Ready请求可交付；模态窗口的确认操作仍走原业务入口。
6. 先验证720与2560关键画面和一次正常流程，不为层级修正扩大美术范围或改网格容量。
