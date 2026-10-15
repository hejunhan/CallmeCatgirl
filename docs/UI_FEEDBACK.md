# UI 描边与系统动效

在 Project 中选择 `Assets/_Game/Data/TaskGrid/UIFeedbackConfiguration.asset`，直接在 Inspector 调整。场景 `GameplayCanvas` 上的 `CoreGameplayCanvasView / Feedback` 已绑定这个资源。

| 配置 | 用途 |
| --- | --- |
| Task Normal / Task Selected | 内存任务正常填色和选中填色 |
| Task Borders | 普通任务外缘配色；按任务 ID 稳定分配，撤回重新放置不改变颜色 |
| Selected Border / Border Width | 选中描边、外缘线宽（Canvas 单位） |
| Card Selected | 候选卡片选中覆盖色，建议 Alpha 保持约 0.16，避免遮住文字 |
| Speed Selected / Pause Selected | 倍速与暂停按钮持续高亮，普通颜色沿用场景美术 |
| Legal Preview / Illegal Preview / Error | 拖动预览合法、非法颜色及落位失败文字反馈 |
| Below Standard / Complete / Excellent | 计算结束后的三档结果色及提交弹窗短暂反馈 |
| Upgrade Preview | 内存升级弹窗中待开放格子的颜色 |
| Signal | 扫描线、数据点、芯片呼吸、资金与升级提示颜色 |
| Animate | 关闭所有动画，仍保留描边、选中和结果颜色 |
| Scan Opacity / Scan Seconds | 扫描强度、每轮扫描的游戏秒数 |
| Pulse Seconds / Pulse Strength | 操作脉冲的真实秒数和强度 |

外缘描边仅画形状与外界相接的边，任务内部相连格子不会重复描边。配色数量有限，相同颜色可能重复；空间分隔及任务编号也用于区分。扫描线和数据点仅在运算中显示，跟随游戏时钟，因此受倍速、暂停和弹窗冻结影响。落位、完成、新请求、提交和升级反馈使用真实时间，暂停时整理内存仍有反馈。

`TaskCell.prefab / SystemSignal` 是实际保存在 Prefab 中的独立装饰图层，`TaskSignalGraphic` 的 Raycast Target 关闭。后续可在 Prefab 中更换装饰、美术图片与材质，任务坐标、点击和拖拽仍由原控件处理。没有修改任务数值、资金、剧情数据或隔板规则。


## 强化版四个反馈节点

- 落位：拖动预览放大4%，合法落位约0.28秒回弹；只改变ShapeCells显示层，任务Origin、占格和计时不受影响。非法落位局部抖动约4像素，0.2秒后归位；候选卡片抖动形状预览，在板任务抖动显示层。
- 能力增长：读取TaskInstance各项能力与上次显示值的差；大步长合并显示真实增长量，暂停/重复Refresh不产生新增长。默认仅选中任务显示具体能力跳字，其他运行任务闪亮；Show Unselected Ability Text可开启全部任务跳字。撤回重新放回不重复播报已有能力。
- 完成：显示“未达标/完成/优秀 · 待提交”约1.1秒，轻微缩放和结果色脉冲。若最终能力增长与完成同帧，结果和增长一起显示。之后保留慢速呼吸描边，直到提交或任务移出内存。
- 结算：直接使用EconomyManager.Settle返回的实际收入，乘价、质量门槛、部分超时收入及四舍六入五成双规则均由原经济系统决定。显示收入+N（未达标明确为+0），资金读数约0.77秒滚动到真实余额，经济系统余额立即到账。提示位于弹窗之上，不拦截选项按钮；任务提交、超时结算均可提示。

新增参数仍在同一个UIFeedbackConfiguration.asset：Drag Scale、Placement Scale/Seconds、Reject Shake Pixels/Seconds、Ability Text Seconds、Show Unselected Ability Text、Completion Text Seconds/Scale、Ready Pulse Strength/Seconds、Settlement Seconds、Floating Text Rise。数值为显示效果，不参与能力、时间或资金计算。Animate关闭会同时关闭跳字、缩放、局部抖动、呼吸和滚动；仍保留静态状态配色。

美术替换位置：`TaskModule.prefab / TaskFeedback`（任务跳字）与`CoreGameplay / GameplayCanvas / SettlementFeedback`（收入提示），都是已保存的uGUI控件，可直接改位置、字体、背景与尺寸。没有BGM、音效或额外动画插件。
