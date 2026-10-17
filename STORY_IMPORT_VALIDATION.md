# 外部剧情导入验证记录

日期：2026-10-13。Unity：6000.5.2f1。

使用 Assets / Packages / ProjectSettings / Library / Examples 的临时副本运行 Unity `-batchmode -nographics -runTests -testPlatform EditMode`，未关闭当前项目编辑器，也未覆盖现有剧情配置和场景。

结果：72 / 72 通过，失败 0。原有 55 项回归通过，新增 17 项覆盖 JSON 导入、单角色替换、完整替换、非法数据的原配置保护、会话难度覆盖、一次会话多轮连续选择、质量兜底、提前结束、提交排队、角色隔离及实际场景弹窗与结算流程。

测试源码：`Assets/_Game/Tests/EditMode/TaskGrid/StoryImportTests.cs`。

本机原始结果：`Logs/StoryImportImplementation/StoryImportBatchTests.xml`，对应日志 `StoryImportBatchTests.log`（Logs 不提交 Git）。

实际场景测试验证多轮按钮点击后仍在弹窗、进度文字更新、经营时间冻结、资金和达标数只增加一次、最终关闭反馈、完成角色不再创建任务及重开恢复。导入按钮的文件选择窗口需要用户在编辑器中操作；底层导入路径已自动测试。
