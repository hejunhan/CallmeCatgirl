# 场景 UI 编辑说明

打开 `Assets/_Game/Scenes/Prototypes/CoreGameplay.unity`，在非运行状态修改并保存。固定 UI 已保存进 Scene；启动、重开、读取存档只绑定功能和刷新数据，不再重新生成主布局。

## 常用位置

| 调整内容 | Hierarchy 路径（从 GameplayCanvas 开始） |
| --- | --- |
| 倍速、暂停、新测试请求 | BaseInteractionRoot / Header / Toolbar |
| 内存棋盘、候选区 | BaseInteractionRoot / BoardPanel、CandidatePanel |
| 左侧导航和联系人面板 | BaseInteractionRoot / FixedNavigation |
| 保存、读取按钮 | BaseInteractionRoot / SaveRun、LoadRun |
| 对话弹窗 | WindowLayer / ConversationPopup / Window |
| 内存升级弹窗 | WindowLayer 下的 MemoryUpgradePopup |
| 模型广场范围与展开终点 | WindowLayer / ModelGallery / GalleryLayout / Viewport |
| 模型广场内容 | 同上 GalleryLayout / WhiteFrame / Content |
| 训练页 | 模型广场 Content / TrainingWorkspace |

可以修改 RectTransform、Image 和 Text；有布局组的子对象由布局组排版，应修改父对象的 Grid/Vertical/Horizontal Layout Group、子对象的 LayoutElement，或按需要调整布局方式。

## 动态内容编辑模板

以下模板保存在场景里，运行时根据实际数据复制。修改模板后，新生成的对应内容使用修改后的样式；预览行只用于看效果，不是数据源。

- 联系人：FixedNavigation / MemoryPanel / ConversationList / ContactTemplate。
- 对话气泡：ConversationPopup / Window / ConversationTemplates 下的 IncomingMessageTemplate、OutgoingMessageTemplate、SystemMessageTemplate。
- 回复按钮：ConversationPopupView 组件的 Choice Template 所指对象。
- 模型卡：模型广场 Content / ModelTemplates / ModelCardTemplate。
- 模型轮廓单格：同一 ModelTemplates 下的 OutlineCellTemplate。
- 训练架构、语料行、训练任务行：TrainingWorkspace / TrainingTemplates。
- 候选任务、棋盘任务块：CoreGameplayCanvasView 组件中已有的 Card Template 和 Module Template 引用。

模板或其父级默认关闭；编辑时可暂时开启查看，完成后恢复关闭。保留模板中的绑定子对象名称和功能组件，例如 Name、Summary、Corpus、ModelName、Rename、Action；替换图片、字号和位置即可。行高度由 LayoutElement、文本高度和布局组共同决定。模型轮廓与任务形状仍由玩法数据确定。

## 保留的运行时表现

- PlanningEffectsCanvas 在 Scene 中默认关闭，运行时自动开启并同步暂停状态。
- 暂停反色、悬停、选中描边、任务落位反馈和模型页展开动画继续生效；这些状态变化会使局部外观和静态预览有差别。
- 模型广场默认关闭。编辑时展开 ModelGallery 可查看完整页面；Viewport 决定动画展开后的范围。
- 剧情内容、亲密度、任务数量、格子开放状态、训练和升级数值继续读取原来的配置。

`CallmeCatgirl > Save Current UI Layout To Scene` 是旧场景的一次性迁移入口。当前场景已完成迁移，不需要反复执行；已有标记的场景不会重新套用默认布局。

## 本轮验证

先用编辑态/运行态对比复现了弹窗被重新挂到其他层级的问题，再将固定构建步骤转为编辑器保存。

在隔离 Unity 工程验证：固定面板的层级、锚点、位置、尺寸、缩放、字体和图片；连续重开两次后保留手动调整；模型页边界；暂停特效启用；任务拖放旋转、倍速和暂停、对话、训练、存档、任务池等相关流程。详细结果和对照截图保存在 `Logs/SceneAuthoring`，不属于全项目测试结果。
