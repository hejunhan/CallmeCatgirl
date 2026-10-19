# 剧情文本唯一作者源

正式玩家文本维护在 `Resources/NarrativeText/*.json`（UTF-8）。每份文件包含 `schemaVersion: 1` 和 `entries: [{ "id": "稳定语义 ID", "text": "正文" }]`。按角色或章节拆分；Unity 直接导入为 TextAsset，Editor 预览和 Player 使用同一份内容。

修改正文只改 `text`，不要随正文变更或重排而重命名 `id`。例如 `special.doctor.opening.body`、`special.doctor.opening.choice.mew`。原有阶段/章节编号属于既有节点身份；不要把新文本 ID 定义成数组索引或正文 hash。

`special-*.json` 对应特殊剧情、阶段回应、林栖、结局及联系人/教程提示。C# 状态机只保留节点、选项、条件、效果和文本 ID 引用；本次保持原字符串拼接和换行，未调整演出或剧情。

`ordinary-*.json` 对应原 `StoryConfiguration.asset` 和 `OrdinaryDialoguePool.asset` 的全部玩家文本字段，包括显示名、历史、会话标题、用户消息、回复标签、玩家回复和用户回应。`Resources/NarrativeTextBindings.json` 显式把原资产/UserId/session.Id/node.Id/选择键/字段绑定到语义文本 ID。旧 `choice-N`、`default-N` 是本轮保留的既有存档定位协议，不是新文本 ID。新增选择或调整原选择顺序时，须同步维护绑定并考虑原协议的存档兼容性。

两个正式 ScriptableObject 中的文本是生成缓存，不再单独维护。加载和验证时必须从 JSON 按 ID 填充；原缓存仅保留既有字段、Inspector 结构和存档内容哈希的兼容性，不作缺失 ID 的备用来源。数值、任务参数、节点与跳转仍由原配置管理。正式资产名和 binding 的 `assetName` 应一致；当前注册 `StoryConfiguration`、`OrdinaryDialoguePool`，不要只重命名其中一侧。

Inspector 将正式文本字段只读显示，入口指向 JSON。保存 JSON 后 Unity 导入器会验证并刷新已加载的缓存；可用 `CallmeCatgirl/Text Library/Validate And Reload` 手动检查，再重新打开 authored UI 预览。Play 中的重载会延后到停止 Play，避免同一局正文与已固定的存档内容哈希不一致；重新开始 Play 后使用新正文。此操作不保存场景、asset 或游戏进度。

缺失文本、重复 ID、非法 JSON 或不完整绑定会明确报错，包含文件或具体 ID。不得用旧硬编码或缓存悄悄兜底。修复 JSON 后重新导入即可；无需修改全局配置、安装依赖或重新生成剧情引擎。

Web/审核站消费同一组 JSON 与 binding。按稳定 ID 提交正文 diff，不从另外一份 Markdown 或旧候选手工复制。旧 `docs/NARRATIVE-2026-10-18.md` 是迁移前历史参考，不是当前作者源。Argon4 未审候选不在这个正式库里。
