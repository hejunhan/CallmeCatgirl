# 中文纯文字剧情导入验证

2026-10-13，Unity 6000.5.2f1。使用 AppData/Local/Temp 下的临时项目，未关闭原编辑器。原项目已导入剧情文件保持原样，前后 SHA-256 相同：795d0766f78e3f2c82006398d547317ccedde68609bb80f09fba2a0b12161189。

## 结果

- **98 / 98 全部通过**：临时项目使用 Git HEAD 的旧剧情资源作为既有测试的固定文案样本，检查兼容性和完整回归。仅临时副本使用这个样本，原项目没有回退。
- **43 / 43 全部通过**：临时副本换回用户当前已经导入的剧情，运行 JSON 导入及纯文字导入全部测试，确认现有资源仍继承模板形状、速度和 Token 数量。
- 新增纯文字测试 26 项，涵盖独立数量、默认值及会话覆盖、中文/ASCII 图案、旋转、能力分项、零收入、换行/BOM、多行正文、命名跳转、单角色更新、错误导入不修改目标、棋盘尺寸限制和实际场景中的多轮弹窗与结算。

第一次回归的测试构造问题已修正：Windows CRLF 导致部分错误样本替换没有生效；临时旧剧情样本缺少文件末尾换行导致 Unity YAML 解析日志。修正测试输入及临时样本后全量重跑通过，没有因此改动用户剧情文件。

实际场景测试验证纯文字的正方形和 L 形进入任务定义并能合法占格，旋转形状正确，速度和收入覆盖生效，连续选项保持弹窗，经营时间冻结，一次会话只增加一次资金与达标数，完成剧情后停止创建下一次任务。文件选择窗口需要用户在原编辑器点击；按钮调用的解析入口已自动验证。

测试源码：`Assets/_Game/Tests/EditMode/TaskGrid/StoryTextImportTests.cs`。

本机结果（Logs 不提交 Git）：

- `Logs/StoryTextImplementation/LegacyRegressionRetest.xml`
- `Logs/StoryTextImplementation/CurrentStoryTests.xml`
- 同目录对应 Unity 日志，首次失败记录也保留。

示例及使用说明：`Examples/StoryText/README.md`。原有 JSON 示例保持可用，另外增加可选的 task.shape 图案。
