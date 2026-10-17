# UI 规则

构图、排版和视觉取舍见[美术指导](ART-DIRECTION.md)；可读性、交互状态和适配验收仍遵守本页。

## 依据与验收

- [Xbox XAG 101](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/101)：1080p PC 建议实际字身至少 18 px。Unity fontSize 是字号，不等于最终字身；以实际游戏截图检查。
- [WCAG 普通文字对比](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html)：正文以至少 4.5:1 为目标。[必要非文字边界和状态](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html)以至少 3:1 为目标。
- Carbon 的[字号层级](https://www.carbondesignsystem.com/building-blocks/foundations/typography/style-strategies)、[间距](https://www.carbondesignsystem.com/building-blocks/foundations/spacing/overview)及[动效](https://www.carbondesignsystem.com/building-blocks/foundations/motion/overview)用于保持一致，不覆盖本项目已确定的效果。

## 项目取值

以下是项目建议，不是上述规范的硬性数值：1920×1080 参考画布的初始字号为标题 32、关键数值 40、正文和按钮 28、参数和辅助文字 24。当前项目采用 1600×1000、Expand，按最终字身和空间换算，不重复乘父级缩放。

- HarmonyOS Sans Regular 用于正文；标题、按钮可用已有真实 Medium/Bold。无需重建庞大字库。
- 正文关闭 BestFit，不缩放 Text Transform。长内容换行、增大容器或使用现有滚动区，不裁掉必要信息。
- UUID 按用户要求置于卡片右下角，使用独立的次级小字样式，完整且可读；不增加详情或复制按钮。
- 浅底 `#F4F4F4`，主字 `#161616`（约 16.45:1），次字 `#525252`（约 7.10:1），必要边界 `#6F6F6F`（约 4.57:1）。
- 严格 RGB 反色分别为 `#0B0B0B`、`#E9E9E9`、`#ADADAD`、`#909090`。按最终 shader 与透明叠加后的实际画面检查；不用整组透明度降低对比。
- 间距按 8 的节奏：外边距约 32、面板间约 24、内边距约 24、同组 8–16。按钮高度约 56、左右留白约 24；适配既有布局，不强塞所有建议数值。

## 内容与状态

- 不恢复已删除的左上额外总标题；删除 AI 解释空话、无意义英文、假日志、伪终端编号和重复说明。中文承担主要理解，CHAT、COMPUTE 等短英文栏目可保留；真实编号和时间有用途时可用。候选元素只保留用户指定形状，不额外添加编号或标签。
- 保留模型能力、速度、轮廓和 UUID；保留任务目标、状态、质量、收益、必要操作及通讯正文。
- 按钮选中状态不能仅靠发光。禁用文字仍需可读。
- 保留用户指定的右侧固定、左侧漂浮旋转、暂停时 45° 扫动和 0.7 秒悬停。反色过渡若出现文字与背景同灰，应根据可见底色切换高对比字色，不引入复杂架构。

最小验收：普通、模型广场、暂停、真实悬停四种画面无溢出、缺字，字号可读，左右边界正常；通过编译及关键正常流程冒烟。不为本轮 UI 小改运行全量测试。

## HSV 与分辨率

HSV 使用 H 度数、S/V 百分比。中性色无有效色相，下表以 H=0 表示；ΔV 只能辅助选色，不能替代相对亮度及对比度检查。

| 用途 | RGB | HSV（H°, S%, V%） | 反色 RGB | 反色 HSV |
| --- | --- | --- | --- | --- |
| 浅底 | #F4F4F4 | 0, 0, 95.7 | #0B0B0B | 0, 0, 4.3 |
| 主字 | #161616 | 0, 0, 8.6 | #E9E9E9 | 0, 0, 91.4 |
| 次字 | #525252 | 0, 0, 32.2 | #ADADAD | 0, 0, 67.8 |
| 必要边界 | #6F6F6F | 0, 0, 43.5 | #909090 | 0, 0, 56.5 |

以最终渲染像素核验颜色、实际字身与边界。当前 GameView 是 2560×1243，非 16:9；1920×1080 是设计检查基准，1280×720 用于缩小布局检查。GameView 的显示缩放与游戏渲染分辨率是两件事，截图记录两者。不能简单整体缩小后接受失读文字；不足时让内容换行、滚动或调整排布，不新增设置系统。

仅对本项目这些近黑/白实色，稳态配色建议主字 ΔV≥80、次字≥60、必要边界≥50 个百分点。纯装饰底色可差 4–8，但不能承担唯一状态。这不是官方可读性阈值，不能推广到彩色、中灰或过渡态，更不能替代最终文字 4.5:1、必要控件 3:1 的亮度对比。过渡中灰时根据实际底色选黑/白字。

1280×720 相对 1080p 是 0.667 倍，24 的配置会名义降到 16，实际字身还需另算。项目在小分辨率下以必要文字字身尽量不低于 16 px、正文 18 px 为目标；提高对应字号或减少无用内容，不能继续缩字。2560×1243 约 2.06:1，额外宽度用于留白和间距，不拉伸文字。现有稳定 Scaler 保留；参考 [CanvasScaler](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/script-CanvasScaler.html) 和 [GameView](https://docs.unity.com/en-us/engine/6000.0/manual/unity-editor/editor-windows-views-reference/game-view)，不机械套用新的匹配模式。
