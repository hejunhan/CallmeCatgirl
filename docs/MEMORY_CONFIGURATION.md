# 可视化内存配置

唯一的棋盘配置是 `Assets/_Game/Data/TaskGrid/MemoryUpgradeConfiguration.asset`。主配置 `CoreGameplayConfiguration.asset` 只通过“内存配置”引用它，不再保存另一份初始格子列表。

## 编辑格子

1. 在 Project 中选中 `MemoryUpgradeConfiguration.asset`。
2. Inspector 中选“编辑布局”。
3. 选择“初始”“2级”“3级”等画笔，再点击棋盘格子。格子上的标记就是它的解锁等级，不用填写向量或坐标。
4. “锁定”表示任何等级都不开放。每格只能归属一个等级，不会重复计数。
5. Ctrl+Z 撤销修改。切到“预览解锁”，拖动等级滑条查看该等级的完整开放区域。

棋盘左上角对应第一行第一列。当前场景使用8×8棋盘。新增和累计格子数自动从涂格结果统计；升级档位只填写等级、价格和累计任务要求。

## 现有数据

开放格数与位置以当前内存配置资源为准，弹窗预览同样读取这份配置。你已调整的格子布局会保留。

“初始内存等级”控制新一轮从哪一级布局开始，默认1。配置修改后重新运行或重开生效。

## 程序对接

`MemoryUpgradeConfiguration.GetOpenCells(level)` 返回指定等级的完整开放集合；更低等级的格子会保留，标记为锁定的格子不会开放。

`TryGetNextLevel(currentLevel, out step)` 查询下一档价格和任务要求。初始启动也通过同一份配置生成棋盘。

## 运行中升级

主界面只保留“内存升级”入口和一行等级/资金摘要。点击入口打开独立弹窗，查看下一档等级、价格、新增格数和达标任务条件。打开不扣费，只有弹窗内的“确认升级”才执行升级。条件不足或满级时禁用确认按钮，仍可打开查看。

任务门槛按达标（完成或优秀）且已提交计数，未达标提交、超时和重复提交不会增加计数。

当前主配置中的“初始资金”默认为1500。升级实际扣除该档价格。当前版本已接入达标提交和超时部分Token收入，统一由 EconomyManager 管理余额；外部系统仍可调用 `CoreGameplayController.AddFunds(amount)` 入账。

点击升级后，`TaskSimulation.TryUnlockCells(cells)` 只增加开放格子，运行任务的进度、能力、占格和时间保持不变。可以在准备阶段或暂停时升级；升级不会自动开始或解除暂停。

重开恢复配置中的初始等级、初始资金和初始布局，并清零本轮达标任务计数。目前未保存这些进度到存档。

## 升级弹窗

位于 `GameplayCanvas/MemoryUpgradePopup/Window`，由真实uGUI场景对象组成，可直接修改布局与替换美术。棋盘预览中紫色表示本次新增格，蓝灰色表示已开放，深色表示尚未开放。颜色可在 `MemoryUpgradePopupView` 组件中调整。

打开时任务进度、忍耐和月度维护计时均冻结。点“关闭”或按 Esc 返回，恢复打开前的运行或暂停状态。升级成功后保持弹窗开启，更新到下一档，不重复扣上一档费用。剧情回复弹窗优先，两者不会叠加；重开会关闭升级弹窗。

仍使用 `MemoryUpgradeConfiguration.asset` 配置价格、达标数和涂格解锁区域，没有新增另一份升级数据。

## 本次验证

保留当前内存布局及通讯栏关闭设置，其他51项回归通过，新增4项升级弹窗专项测试通过。报告位于 `Logs/MemoryPopupImplementation/MemoryPopupBatchTests.xml` 和 `MemoryPopupFocusedTests.xml`；首轮两个弹窗测试在同帧连续点击导致渲染尚未更新，修正为等待一帧后点击，4项全部通过。验证涵盖了确认扣费、取消不扣费、条件不足、满级、时间冻结、原任务不变、剧情优先与重开。

已检查1600×1000和1280×720的Unity弹窗渲染画面，预览和按钮没有重叠：[升级弹窗](images/memory-upgrade-popup.png)、[小分辨率](images/memory-upgrade-popup-small.png)。一次性场景生成工具和临时Unity验证项目已清理。
