# 多段对话合并训练

将多张对话语料卡依次拖到训练区，或点击卡上的“添加”。已添加的对话不会被下一段覆盖；点击已添加卡可以切换编辑对象，通过滑块调整该段投入量，右上“移除此段”只移除当前段。投入 0 点的对话不参与本次训练。

确认前显示本批段数、总点数、各项能力、占格、费用、总计算量及预计游戏时间。预览不消耗库存；确认生成一次训练任务时，一次性扣除所有选定投入，剩余语料留存。暂停、等待落位不计算，完成后仍需确认产出模型。能力不继承旧模型。

## 数值配置

在 `Assets/_Game/Data/Training/TrainingDefaults.asset` 的 Recipes 中设置：

| 配置 | 默认 | 含义 |
|---|---:|---|
| WorkTokens | 120 | 基础计算量 |
| WorkGrowthCoefficient | 5 | 计算量增长系数 |
| WorkGrowthExponent | 1.5 | 幂指数，至少 1 |
| PointsPerExtraCell | 2 | 每投入多少点额外占一格 |
| MaximumCorpusPoints | 100 | 单次所有对话合计上限 |
| PointsPerScore | 1 | 每种语料能力转化为模型能力的比例 |
| Cost | 20 | 每批固定费用，仍受训练费用政策影响 |
| Shape | 3×2 共6格 | 基础形状 |

设 P 为所有对话实际投入点数之和：

- 计算量 = `(WorkTokens + WorkGrowthCoefficient × P^WorkGrowthExponent) × 训练计算量政策倍率`。
- 格子数 = `基础形状格数 + floor(P / PointsPerExtraCell)`。保留基础格，沿相邻位置紧凑扩展；总形状超出棋盘或合计超上限会在扣点前拒绝。格子仍须完整放入开放的同一隔间，不能跨隔板。即使整块未超过棋盘，也可能需要升级内存后才能放下。
- 每项模型能力 = `各段实际投入中该能力的总分 × PointsPerScore × 训练能力政策倍率`。分项相加，不将写作、代码、推理等合成一个能力值。
- 预计耗时 = `总计算量 / 当前每任务算力`，单位为游戏秒；按当前使用的模型架构、硬件及政策估算，暂停不会推进。全局 0.5× 会让实际等待时间翻倍，训练中新升级算力会改变剩余时间。所选产出架构并不提前加速本次训练。

默认值、算力10时，10点约278计算量、11格、27.8游戏秒；20点约567计算量、16格、56.7游戏秒；100点5120计算量、56格、512游戏秒。数值仅为验证玩法的暂定参数。

## 程序接口与保存

`TrainingSession.PreviewTraining(recipeId, investments)` 与 `StartConversationsTraining(...)` 使用同一分配和形状规则；每项 `ConversationInvestment` 指定 `ConversationId` 与 `Points`。同一段不能重复传入，任一段不足、总量超限、无效数值、形状过大、资金不足，整批不扣语料。旧单段入口仍可用。

GameCommand 的 `train` 新增 `investments: [{"ConversationId":"A","Points":4},{"ConversationId":"B","Points":6}]`。不得与旧 conversationId/corpusPoints/corpusIds 输入混用。旧记录ID入口对配置了增长系数的配方也应用合计上限和增长形状，避免绕过限制；增长系数为0的历史固定配方保留兼容。

训练任务继续沿用分记录扣点账本，冻结创建时的能力、计算量与任务形状，存档恢复不会重新扣除或重新计算。新配置会改变内容版本，旧版本运行存档需按现有版本规则重新开始；不会擅自用新数值覆盖已保存进度。

固定控件保存在 CoreGameplay Scene；`CallmeCatgirl/Integrate Conversation Corpus` 可补齐“移除此段”控件，不在运行时重新布局固定界面。
