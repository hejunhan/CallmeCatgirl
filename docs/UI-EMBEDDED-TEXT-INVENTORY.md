# UI 图片嵌字盘点

2026-10-17，只读检查。范围是当前 CoreGameplay 实例（含已加载但隐藏的弹窗）、MenuScene 场景引用，以及模型广场和训练页生成代码。结合运行时 Image.sprite、ButtonArtwork 状态资源和源图目视核验；没有切换桌面或改造美术。

## 当前仍使用的图片符号

| 图片内容 | 使用位置 | 资源路径（相对项目） | 当前状态 |
| --- | --- | --- | --- |
| ×（关闭图标） | 当前请求详情右上角 CloseButton/ArtworkIcon | `Assets/_Game/Art/UI/独立切图_v1/icon_close.png` | 当前面板可见 |
| ×（关闭图标） | 聊天历史 CloseHistory、内存升级 CloseMemoryUpgradeButton | `Assets/_Game/Art/Pic/A03_IM窗口与联系人_1016V1/07_icons/close.png` | 已绑定，弹窗目前隐藏 |
| ＋（加点图标） | 内存升级四项能力 AllocateWriting/Code/Reasoning/RetrievalButton | `Assets/_Game/Art/UI/独立切图_v1/icon_plus.png` | 已绑定，弹窗目前隐藏 |

这三项是符号图形，没有中文按钮标签。对应旧 Text 被隐藏，避免图标与文字叠加。

## 当前按钮与艺术标题

- 暂停、0.5x/1x/2x/4x、新测试请求、重置：运行时底图为 `Assets/_Game/Art/UI/独立切图_v1/button_normal.png`、`button_selected.png`、`button_disabled.png`，文字由 HarmonyOS Sans Regular 动态绘制。源图已确认无字。
- 请求详情的旋转、撤回、提交、下一轮，以及内存入口等：同组无字底图加动态文字。
- 聊天回复用 `Assets/_Game/Art/Pic/A03_IM窗口与联系人_1016V1/04_buttons/reply_normal.png`，确认升级用同目录 `common_normal.png`，结算反馈用 `common_selected.png`；底图未见文字，标签由 UI 绘制。
- 主界面游戏标题、栏目标题、任务卡内容、余额、属性、日志和聊天内容是动态字体，不是图片嵌字。主菜单 Start Game 是 TMP；MenuScene 未引用带标题字样的自定义 Sprite。
- 模型广场和训练页按代码生成文字与纯色 UI/形状，没有嵌字图片引用；本次没有打开它们重新采集画面。
- `background_1920x1080.png`（桌面背景）、`im_window_1920x1080.png`（聊天/升级窗口边框）、`reply_region.png`（各面板）、`message_incoming.png`（任务卡）属于背景、边框或气泡，未发现可读艺术标题/装饰字。没有“当前仍需替换的艺术标题”条目。

## 旧嵌字资源：当前运行按钮未使用

以下路径均位于 `Assets/_Game/Art/UI/独立切图_v1/`，保留原文件，未删除：

| 文件 | 图片中的可读文字 |
| --- | --- |
| `button_new_request.png` | 新测试请求 |
| `button_pause.png` | 暂停 [空格] |
| `button_reset.png` | 重置 |
| `button_speed_1x.png`、`button_speed_1x_selected.png` | 1X |
| `button_speed_2x.png`、`button_speed_2x_selected.png` | 2X |
| `button_speed_4x.png`、`button_speed_4x_selected.png` | 4X |

注意：CoreGameplay 场景部分 Image.m_Sprite 仍保存这些旧初始引用；ButtonArtwork 在启用时改成无字状态图。上表表示“当前运行按钮未使用”，不表示全仓库零引用或可直接删除。

## 证据与边界

- 原始运行图片引用：工作区 `validation/ui-sprite-live-inventory.json`。
- 字体与 ButtonArtwork 状态字段：工作区 `validation/font-binding-live.json`。
- 未对所有未加载历史场景、未引用的完整设计稿或人物头像中的微小水印做全资产 OCR。这里的结论限定于当前游戏 UI，不推断所有旧资产都无字。
