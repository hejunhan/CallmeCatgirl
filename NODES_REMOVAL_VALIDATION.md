# 旧 Nodes 配置清理

2026-10-13，Unity 6000.5.2f1。

已删除 UserStory.Nodes、StartNodeId、ReplyChoice.NextRequestId、运行时 NextRequest 状态/方法及旧节点格式的兼容分支。当前仅使用 Users → Sessions → Rounds → Choices，首轮从会话列表自动确定。StoryNode 数据类型和 NextNodeId 仍用于当前每轮对白与选项跳转，不是旧 Nodes 数组。

当前剧情资源中所有 Nodes 数组原本均为空。清理仅删除 207 行旧字段，逐字核对其余资源内容一致；会话、对白、选项、跳转、数值、图案及资源 GUID 均保留。清理前原文件备份在 `Logs/NodesRemovalImplementation/StoryConfiguration.before-removal.asset`（Logs 不提交 Git）。

原弹窗测试改用独立的新会话样本，覆盖冻结时间、指针点击、任务模板切换、质量选项、排队、历史隔离及长列表滚动。新增测试通过反射确认旧字段不再存在，首轮仍可正确确定。已有 JSON 与中文文本导入测试保留。

验证使用临时项目，未关闭原项目编辑器：

- 99 / 99 全量回归通过。临时场景使用空的剧情角色列表以测试基础请求模板；弹窗测试自行创建会话样本，不回退旧节点资源。
- 50 / 50 当前剧情专项通过。临时副本换回清理后的当前剧情资源，运行全部导入和对话弹窗测试。

本机报告位于 `Logs/NodesRemovalImplementation/FullRegressionRetest.xml` 和 `CurrentStoryTests.xml`。对应日志保留。最初调整迭代器测试时的两处编译错误已修复，再进行上述完整验证。

旧格式文档已更新；过往验证记录保留其历史结果，不表示现在仍支持 Nodes 格式。临时项目与执行脚本已清理，备份及测试结果保留在本项目 Logs。
