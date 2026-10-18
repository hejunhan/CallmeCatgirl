# 训练模块接入合同

本轮实现由 C 模块 owner 在隔离项目完成；没有打开或修改用户正在 Play 的 D 盘项目。构建、Unity 测试、截图和整合由主代理统一执行，此文档不是已通过验收的声明。

## 账本与接口

- `TrainingSession.GrantScriptedCorpus(string eventId, string conversationId, IDictionary<AbilityKind,double> scores, string sourceLabel = "剧情奖励")`：给予确定的脚本语料，返回 `Success`、`AlreadyRecorded` 或输入错误。事件编号必须稳定；开场两种选项共享 `narrative-opening-corpus`。语料独立标为 `CorpusSourceKind.ScriptedReward`，不生成虚假的完成请求，也不放宽 `RecordDialogue` 的真实提交检查。
- `BestAvailableCorpusPoints`：当前剩余单条记录的最高点数，无库存为 0。政策在扣款前读取并冻结奖励；恢复待发奖事件时不得重新查询。
- `GrantBestCorpusReward(eventId, conversationId, quantityMultiplier = 3, improvementMultiplier = 1.25, minimumPoints = 4)`：即时发奖便利入口，以当时最佳剩余材料冻结更大、数值更高的语料池，空库存有写作基准。若政策已保存 `FrozenPoints`，改用 `GrantScriptedCorpus` 精确发放该点数，成功或已记录后确认事件。
- `StarterRecipeId = "tutorial-first-model"`、`StarterTrainingAvailable`：内建首次配方自动加入会话和恢复校验，已有训练入口即可使用。准确 1 点语料、0 钱、10 计算量、1 格实际计算形状，只能创建一次。普通配方和架构稳定 ID 不变。

`TrainingSessionState` 保持 v1，兼容增加来源字段和事件编号；旧记录缺省为 Dialogue。奖励和部分预留都保存在原语料/任务账本，未增加全局存档 DTO 或注册逻辑。零格教程允许架构转换和执行概要采用空轮廓；训练仍要求实际开放格足够。

## UI 与集成

`TrainingWorkspaceView.Bind` 构建训练界面；`ModelGalleryView.Redraw` 的训练分支将页面重挂到全 Canvas 的管理根层、铺满锚点、清零边距并置顶，避开原白框固定尺寸和遮罩。关闭管理根层会一并关闭训练页；训练页的“模型目录”恢复原目录页。保留场景内旧对象但在运行时隐藏旧训练布局。左侧搜索、领域过滤和四域推荐来自当前剩余库存；过滤与草稿分离。右侧空编排板支持 `ConfigureArrangement(columns, rows)`，缺省 3×3；此板只管理投入顺序，不占用真实运算格。

卡片支持点击添加和拖入空位。拖放显示颜色与文字合法反馈，无效落点不改变草稿；悬停、键盘聚焦和点击均更新可滚动的详细数值。底部固定显示费用、投入、产出、实际占格和时间，开始通过现有 `train` 命令预留一次并关闭训练页，取消只清草稿。训练候选另有背景、横线纹理和 TRAIN 标签。

共享集成 owner 仍须确保：教程开奖→显露中心一格→训练完成回调；训练候选按 `IsTrainingRequest` 优先排序；政策待发奖事件在成功/已记录后 Ack。UI 没有编辑 `CoreGameplayController`、Canvas、GameCommands、全局持久化或场景。

## 验证边界

新增 `TrainingWorkspaceContractTests` 覆盖写作/推理一点首训、一格真实完成、重复启动、草稿取消、部分预留、奖励/训练恢复、书籍冻结和编排非法落点。测试尚未由本 worker 执行；主代理应结合现有 Training 套件运行，并在 1280×720 与 1920×1080 检查空库存、长中文、满编排、下拉、拖放及返回主画面。
