# 会话剧情与导入

推荐策划使用 [中文纯文字格式](../Examples/StoryText/README.md)：一角色一文件，对白、方块、能力、时间和收入一起编辑。JSON 格式详见 [导入说明](../Examples/StoryImport/README.md)。

当前唯一结构为 **角色 Users → 会话 Sessions → 轮次 Rounds → 选项 Choices**。旧 Nodes、StartNodeId、选项级 NextRequestId 及旧格式运行逻辑已移除。现有配置的 Nodes 均为空，清理只删除这些旧键，不改对白、数值、形状、会话顺序或资源 GUID。旧格式文档不再适用。

每个角色独立设置会话数、每次的轮数、每轮的选项数。首轮自动读取第一次会话的第一轮，不用填写首段 ID。任务模板属于会话，通过会话 RequestId 指定，能力、时间、收入和形状可覆盖模板。

一次会话只计算、提交和结算一次。提交立即释放格子并打开剧情弹窗。同次会话在弹窗内连续选择，共用提交质量，直到进入下一次会话或剧情结束。弹窗内经营时间冻结，回复没有选完不能关闭；结束后恢复原来的暂停状态。下一次会话需重新计算。多次提交依次排队，重复提交不会重复结算。

NextNodeId 仍作为内部跳转目标保留，指向某个会话的某轮对话；纯文字编译器自动生成，策划可以用“下一步：结束本次会话”或“跳转到：第3轮”。StoryNode 是每轮对话的数据类型，不是旧 Nodes 数组。

没有配置剧情的用户使用默认回复，仍可继续产生请求。已配置角色全部会话结束后停止创建后续任务，仍可查看历史。历史和进度属于本局，重开清空。

主场景为 `Assets/_Game/Scenes/Prototypes/CoreGameplay.unity`，主配置的 Story 引用剧情资源。`StoryConfiguration` 保存数据，`ConversationStory` 管理进度/历史/队列，控制器通过提交事件打开弹窗，`ConversationPopupView` 显示并处理选项。真实 Canvas 布局和美术接口保留。
