# 内存背包隔板验证

Unity 6000.5.2f1，2026-10-13。使用临时项目副本测试，不关闭原编辑器。

- 最终用户配置下隔板专项：**12 / 12 通过**，`Logs/MemoryPartitions/FinalCurrentConfigurationTests.xml`。
- 完整回归：**118 / 118 通过**，`Logs/MemoryPartitions/FullRegressionTests.xml`。旧测试仍需要固定初始模型4/1/1/0及通用剧情，替换仅发生在临时副本；内存配置使用最终左右分区布局。原项目的模型1/1/1/0、用户剧情和所有升级数值保留。
- 专项覆盖：跨包拒绝、整体跨包搬移、旋转后的判定、非法操作保留占格/进度/能力/事件、L形实际占格、升级开放其他背包、数组隔离、旧构造兼容、错误配置、序列化涂色和撤销、实际Canvas拖拽与按钮、预览和主棋盘隔板网格。
- Scene通过Unity保存，主棋盘 `BoardPartitions` 与升级弹窗 `PreviewPartitions` 都是场景对象；原有场景对象ID全部保留。两个图层 `raycastTarget=false`，不拦截操作。
- 检查截图：`Logs/MemoryPartitions/DefaultLayoutPopup.png` 为最终左右布局；`PartitionPopup.png`、`PartitionBoard.png` 为专项测试中的上下布局，覆盖两种隔板方向。已人工查看图层位置及弹窗文字。
- 初次专项测试11/12，失败为测试读取 `Graphic.OnPopulateMesh` 时没有区分重载，明确指定 `VertexHelper` 参数后通过；运行代码未因此回退。
- 格式检查通过。初始开放位置、各等级解锁格、价格、任务数门槛和点数奖励与修改前一致；主玩法配置及剧情文件哈希未改变。备份在 `Logs/MemoryPartitions/Before`，测试日志和备份不提交Git。

配置入口和操作例子见 `docs/MEMORY_PARTITIONS.md`。配置更改后重开游戏生效；没有增加运行中拆隔板、玩家购买隔板或动态拆分正在计算任务的操作。
