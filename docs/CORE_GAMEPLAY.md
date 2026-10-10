# 核心玩法：任务与格子、计算与能力

依据《基础玩法策划案（第3版）》当前有效正文，实现简版协作任务 J02、J03。

## 如何运行

用 Unity 6000.5.2f1 打开项目，打开 `Assets/_Game/Scenes/Prototypes/CoreGameplay.unity`，点击 Play。
场景与Prefab已经保存；直接从Project窗口打开场景即可，一次性生成和迁移工具已删除。

- 从候选区拖动请求到格子。绿色预览合法，红色预览非法。
- R 或旋转按钮旋转90度；运行中的任务可以拖动，拖动期间原位置仍占格且继续计算。
- 撤回释放空间，保留计算量、各项能力与计频余量；忍耐时间继续走。
- 空格或暂停按钮冻结全部游戏时间；默认速度读取配置，顶部按钮可切换1/2/4倍。
- 算完立即判质，仍占格。点击确认提交才释放格子，并输出一次结算事件。
- 点击“模拟回复结束”可建立同用户下一轮请求。此按钮是IM系统的接入示例，不含正式剧情。
- “重置测试”回到初始三个任务；“新测试请求”增加一位测试用户。

验证界面已经改为场景中保存的 Canvas/uGUI。主面板、按钮和初始任务卡可直接在 Hierarchy 中编辑；重复元素采用 Prefab。美术替换与程序绑定说明见 [CANVAS_ART_HANDOFF.md](CANVAS_ART_HANDOFF.md)。

## 程序对接

`TaskSimulation` 不依赖 Unity，唯一管理游戏时间、请求状态、占格与计算。

| 调用 | 用途 |
|---|---|
| `TryCreate(requestId, conversationId, roundId, definition, out task)` | 创建请求；同会话有未关闭请求时拒绝新轮次 |
| `Preview(id, origin, rotation)` | 查询摆放失败原因，不改变原占格或进度 |
| `TryPlace(id, origin, rotation, out failure)` | 合法时一次转移占格并开始/继续运行；非法不改变任何原状态 |
| `TryRotateCandidate(id)` | 候选或撤回请求旋转 |
| `TryWithdraw(id)` | 释放运行请求的格子，保留累计 |
| `Advance(gameSeconds)` / `Paused` | 统一计时；每帧只由一个驱动调用 |
| `TrySubmit(id)` | 待确认请求提交；只成功一次 |
| `TryCloseFeedback(id)` | IM处理完成功/失败反馈后关闭本轮，允许下一轮 |
| `DrainEvents()` | 取走按游戏时间排序的事件，交给账单、IM、声望等系统 |

事件由一个协调器读取后分发，多个系统不要分别调用 `DrainEvents()`。
`Submitted` 事件提供全额Token，`TimedOut` 提供 `总Token × 已算量 / 总计算量`。费用与质量后果由J04/J05配置，核心层不扣钱、不写声望。
`Ready` 携带冻结的逐项质量结果。`Tasks`、`Occupancy`、能力与质量结果为只读视图，界面不直接修改状态。

## 配置与能力

`Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset` 可在 Inspector 调整开放格、模型频率/点数、测试任务形状、计算量、速度、忍耐、Token以及各项完成/优秀门槛。
配置属于测试数值，不代表正式平衡；任务定义与运行实例分开。同一配置可生成多个互不影响的请求。

质量取各项质量档的最低值；任务未要求的能力不参与判定。例：要求写/码/理10/2/2，产出12/3/3达标；12/1/10因为代码不足不达标。优秀门槛独立配置且必须高于完成门槛。

计算与能力均只在运行时增长。并发不平分速度；大帧会先截止到实际完成/超时时刻，不因帧长多算能力。完成与截止同刻时完成优先。

## 当前实现口径（策划未定部分）

- 撤回保留计算量、各项能力及计频余量。
- 终点同刻的能力触发先计入，再判质。
- 算完待确认块只能确认，不能移动、旋转或撤回。
- 暂停时允许摆放和其他明确操作，但不推进时间。
- 模型切换API要求暂停；保留旧贡献，按旧周期的已走比例映射到新周期，切换瞬间不触发能力。验证界面暂不提供换模入口。

这些规则不是新定案；需要改变时应同步测试和本文，不悄悄改动J04或IM。

## 验证

EditMode测试覆盖非法移动的原子性、旋转、锁定格、独立并发、拖拽期间运行、暂停、撤回与重放、等待超时、终点与截止同刻、大帧/小帧一致、逐项判质、冻结结果、重复提交、会话绑定及换模。

运行：Unity Test Runner > EditMode > `CallmeCatgirl.Tests.TaskSimulationTests`。
验证报告见 `Logs/CoreGameplay-Canvas-Final.xml`。代码文件职责与清理说明见 [CODE_STRUCTURE.md](CODE_STRUCTURE.md)。

不在本次实现范围：正式IM/回复剧情、价格到账、声望、训练/扩容/政策、存档与正式UI美术。

## 本次验证结果

2026-10-10：Unity 6000.5.2f1 实际编译通过；EditMode 28/28通过，其中场景测试实际进入Play Mode，验证配置加载、并发运行、12/3/3产出、逐项质量与单次提交。结果见 Logs/CoreGameplay-EditMode.xml。鼠标拖拽的完整人工体验和正式美术仍需团队试玩。


Canvas迁移验证（2026-10-10）：实际项目编译通过，29/29测试通过；新增场景UI测试覆盖保存的Canvas、EventSystem拖放、非法落位保留、旋转、暂停、撤回重放、逐项能力和提交按钮。报告为 Logs/CoreGameplay-Canvas-Final.xml。已检查1280×800静态界面预览，图片位于 Logs/Canvas-Scene-Preview.png。后续美术替换参见 docs/CANVAS_ART_HANDOFF.md。

代码清理验证（2026-10-10）：删除一次性编辑器工具后，隔离副本编译及29项测试通过，见 Logs/CoreGameplay-Cleanup-EditMode.xml。

参考布局验证（2026-10-10）：复用用户MemoryPanel，新增模型信息、实际占格数量、用户通讯切换和1/2/4倍速，任务详情移到棋盘下方。隔离副本编译并通过29/29测试，增强的UI测试验证模型、占格、4倍速、关闭详情仍保留通讯、用户切换与下一轮请求。报告：Logs/ReferenceLayoutTests.xml。
