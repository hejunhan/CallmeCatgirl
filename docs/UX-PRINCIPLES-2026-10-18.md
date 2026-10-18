# 本项目 UI/UX 学习与验收要点

以下为依据公开交互资料整理的**项目建议**，不是官方 Unity 标准，也不照搬网页技术实现。界面保持现有工业、线描风格，优先层级、对齐、可读和分辨率适应，避免增加干扰性特效。

## 直接用于实现

- 训练页左库存、右编排，投入汇总和开始/取消固定可见。筛选只改变库存视图，不清空已选配方。参考 [Applying Filters](https://www.nngroup.com/articles/applying-filters/)。
- 拖放显示合法位置预览，非法落点回位；关键动作还应有点击放置等替代入口。数值提示兼容聚焦或点击固定。参考 [WCAG：Dragging Movements](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html)。
- 对话窗口支持标题拖动、边角缩放、最小尺寸和屏幕夹限；长选项换行滚动。选项选择推进当前对话；移除选项同时清除失效焦点，关闭窗口后焦点回到有效控件。模态交互原则参考 [Dialog Modal Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/)，Unity 使用自身 EventSystem 实现。
- 训练块用颜色、纹理和文字标签共同区分；政策节点用清晰的已购/可购/锁定/互斥状态。参考 [不要只靠固定颜色传递关键信息](https://gameaccessibilityguidelines.com/ensure-no-essential-information-is-conveyed-by-a-fixed-colour-alone/)。
- 机制数值、价格和谜语风味文案分区，正文和背景有明确对比，长中文不裁切。参考 [文字、界面与背景的高对比](https://gameaccessibilityguidelines.com/provide-high-contrast-between-text-ui-and-background/)。
- 继续游戏恢复训练、政策、剧情一次性标记；倍速标签统一尺寸与基线。这些是本项目需求，不是上述来源的原文要求。

## 可见验收

1280×720 与 1920×1080 各检查：训练页零库存/筛选/满编排、拖放与点击替代、长中文提示和对话选项、对话缩放边界、政策树状态及正文、开始页继续、倍速标签。保留实际截图与相关输入结果，不把静态文档或预制体存在当成运行通过。
