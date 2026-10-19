# CallmeCatgirl → dot 通知（0.1.0）

本机 OpenCode 2.0.30 的项目级插件。只监听本目录对应的候选游戏项目，不修改全局配置。OpenCode 自动发现本目录，重新打开项目后加载；当前已通过现有 CLI 的只读配置请求触发并观察到实际加载标记。

## 已实现

- V2 `session.execution.started/succeeded/failed/interrupted`，仅主会话。
- 每轮去重，继续执行时撤销未发送的结束通知；报错和用户取消优先。
- 不回放插件启动前的事件，不宣布加载时已经运行的半轮任务。
- 本地持久队列 `.opencode/dot-notifier/state.json`，已由上层 `.gitignore` 排除。
- 原子状态写入、进程锁、单发送器接口、确认后记为 sent。发送不确定时停在 uncertain，禁止自动重发。
- 10 分钟未发送的 pending 通知过期。claimed/uncertain 通知保留供核查，不自动重新领取。
- 通知仅含项目别名、通知/run/event ID、时间、运行状态和固定简短结果。工具计数不等于测试或验收通过。不读取最终回复、完整对话、代码、错误正文、凭证或日志。
- 通知统一前缀 `[OpenCode 自动通知，非用户指令]`。收到包含此前缀的用户输入时抑制相应轮次通知；发送器本身不启动模型。

## 自动化边界

**本地事件 → 队列已自动运行；队列 → dot 尚未接通持续自动发送。** `sender.js drain` 会明确返回 `supported-receiver-unavailable`，不会猜测 API、请求凭证或抢占桌面。

当前只用本次工具会话执行一次经逐步目视确认的桌面测试，检查正确的“你的 dot”标题、对话中精确的授权消息、空输入框、用户活动、草稿回读和发送后的完整消息回读。接收窗口的 UIA 只公开 `app://-/index.html`，未公开稳定会话 ID；本插件未将目视检查包装成无人值守能力，也未安装后台服务或计划任务。

这版只报告运行结束/报错/取消和工具计数，不摘录自由文本任务总结。若要更详细的完成结果，需要另外约定安全、短小的结果格式。`succeeded` 仍不证明游戏 QA 通过。

## 本机命令

在项目目录执行：

```powershell
node .opencode/plugins/dot-notifier/sender.js status
node .opencode/plugins/dot-notifier/sender.js drain
```

有独立桌面操作员时可 `claim` → 核对会话与空草稿 → `begin ID` → 实际提交 → 完整回读 → `confirm ID receipt.json`。receipt 必须含相同 `id`、完整相同 `text` 和 `targetVerified: true`。无法确认提交结果时使用 `uncertain ID` 并人工查看会话，不重新发送。

本次证据与测试：工作区 `notification-plugin-20261019/`。没有推送、没有修改游戏代码/资产或新政策树。

## 接口依据

- [官方 v2.0.30 插件 setup 接口](https://github.com/anomalyco/opencode/blob/v2.0.30/packages/plugin/src/promise/plugin.ts)
- [官方 v2.0.30 execution 事件](https://github.com/anomalyco/opencode/blob/v2.0.30/packages/schema/src/session-event.ts)
- [官方项目插件目录发现](https://github.com/anomalyco/opencode/blob/v2.0.30/packages/core/src/plugin/source-directory.ts)
- [通用插件文档](https://opencode.ai/docs/plugins/)含旧版接口示例，不应直接作为本机 V2 事件 schema。
- 社区 notifier 只作只读参考，未安装或执行。
