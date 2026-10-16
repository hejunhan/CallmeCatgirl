# 当前 UI 美术接入

主场景：`Assets/_Game/Scenes/Prototypes/CoreGameplay.unity`。
背景、弹窗与回复素材：`Assets/_Game/Art/Pic/A03_IM窗口与联系人_1016V1`。
顶部操作按钮仍使用 `Assets/_Game/Art/UI/独立切图_v1`，两套素材保持浅色、深色选中的共同风格。

| 场景组件 | 当前素材 |
|---|---|
| WorkbenchBackground | 01_background/background_1920x1080，全屏背景，不拦截点击 |
| ConversationPopup/Window/WindowArtwork | 02_window/im_window_1920x1080 |
| MemoryUpgradePopup/Window/WindowArtwork | 02_window/im_window_1920x1080 |
| 剧情 ChoiceScroll | 02_window/reply_region |
| 剧情 ChoiceTemplate | 04_buttons/reply 的普通、悬停、按下、选中和禁用五态 |
| ConfirmMemoryUpgradeButton | 04_buttons/common 五态 |
| 剧情与升级弹窗的关闭图标 | 07_icons/close |
| 主面板 | 同套 reply_region 浅色底板，保留现有布局 |
| RequestCard 预制体 | message_incoming 浅色卡片，深色文字和形状缩略图 |
| 能力／结算浮动提示 | common_selected 深色底板，保留功能反馈色的对比度 |

## 后续换图

- 背景和面板：选中场景对应对象，在 Image 的 Source Image 更换素材。
- 操作按钮：在 ButtonArtwork 中更换 Normal、Selected、Disabled；有五态素材时同时配置 Hover、Pressed。回复选项克隆 ChoiceTemplate，继承其外观。
- 拖动任务和候选卡：修改 `Assets/_Game/Prefabs/UI/CoreGameplay` 下的预制体；格子开放和锁定底色在 GridCell 预制体的 GridCellView 中修改。

素材按 manifest 配置 Sprite/UI、Full Rect、关闭 Mipmaps，窗口边距 100 px，按钮与回复区边距 16 px。窗口中心已有阅读底，原先不透明的深色覆盖层已去除；不要再用整张花纹底板遮住窗口边框。

保留 HarmonyOS Sans、角色头像、现有对话文字格式、布局、玩法配置及点击事件。未新增逐条聊天气泡、联系人在线状态或最小化功能；这些素材仍供后续使用。source 文件和预览图未绑定为运行资源。

本次验证与截图在 `Logs/IMArtwork`，此前按钮替换记录保留在 `Logs/UIArtwork`。
