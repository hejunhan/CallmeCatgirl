# 半速与暂停按钮高亮验证

2026-10-14，Unity 6000.5.2f1。使用当前场景、模型、剧情和内存配置的临时副本。

- **2 / 2 场景用例通过**，结果：`Logs/SpeedPauseHighlight/FinalSceneTests.xml`，同目录对应Unity日志。
- 实际鼠标射线点击验证0.5、1、2、4倍速，倍率正确、所选按钮持续高亮、其余恢复原色；逐帧核对游戏计时。
- 默认0.5倍速且暂停时，半速和暂停按钮同时高亮。速度切换不会自动开始；开始/继续后暂停按钮恢复原色，再暂停重新高亮。
- 切换UI焦点不丢失状态高亮。弹窗冻结期间暂停按钮禁用且显示暂停颜色；关闭后恢复原运行/暂停状态。TogglePause命令（空格键共用）立即刷新表现。
- 重开恢复默认0.5倍速、暂停、高亮和冻结计时。旧测试写死的90秒忍耐改为读取任务配置，当前剧情的100秒设置不变。
- 新断言最初捕获了跨EnterPlayMode的局部变量，域重载后闭包丢失导致空引用；改为直接循环比较后通过，未因此修改游戏配置。
- 保留用户保存的`0.5SpeedButton`，修正其OnClick浮点参数1为0.5，并加入Canvas速度引用及倍率列表；不新增按钮。源场景对象ID、RectTransform位置/尺寸、按钮文本和美术字段保持不变。
- Gameplay、Story、Memory三份配置哈希均未改变。修改前源文件在`Logs/SpeedPauseHighlight/Before`，日志和备份不提交Git。

Canvas组件中`speedButtons`与`speedValues`一一对应；当前顺序为0.5、1、2、4。速度与暂停高亮颜色可分别调整`speedActiveColor`与`pauseActiveColor`。如果Unity仍打开修改前的场景快照，请重新打开CoreGameplay场景后运行。
