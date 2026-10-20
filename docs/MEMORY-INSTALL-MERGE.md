# 内存安装小游戏

小游戏资源最初从 patch-2 合入；缺失的两个脚本现已从 `CallmeCatgirl-mainV02/CallmeCatgirl-mainV02` 完整项目中补齐，原脚本 GUID 保留。其他旧版玩法、配置、渲染设置和主 UI 没有覆盖回 main。

## 升级流程

1. 打开内存升级，确认资金与任务条件。
2. 确认后打开安装小游戏，此时不扣费、不扩容。
3. 先按压左侧，再按压右侧；内存条和卡扣放平后点“完成”，也可以点“跳过安装”。校验通过后只扣费一次并扩容；取消或校验失败不扣费。
4. 回到升级详情，关闭后恢复经营。内存升级不再奖励能力点或提供加点，模型能力通过训练提升；重复完成回调不会重复扣费或扩容。

安装过程中冻结经营时间和任务倒计时，保持玩家原来的暂停、速度设置；底层操作、再次升级和保存暂不可用。重开会清理安装状态；成功读档会关闭安装窗口，失败读档保留当前安装状态。小游戏的中间步骤不写入存档，完成或跳过后可正常保存。

## 场景与美术编辑

- 主场景：`Assets/_Game/Scenes/Prototypes/CoreGameplay.unity`，在 GameplayCanvas / WindowLayer / MemoryInstallPopup 中直接编辑固定 UI。
- 面板模板：`Assets/_Game/Prefabs/UI/CoreGameplay/MemoryInstallGamePanel.prefab`。场景实例和 Prefab 保留关联，修改模板或场景实例均可。
- 独立验证：`Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity`，打开直接 Play。
- 原美术：`Assets/_Game/Art/MemoryInstall/`，没有重新生成贴图。
- `MemoryInstallGame` 负责共用的交互和完成事件；`StandaloneMemoryInstall` 只负责独立场景自动开始。主场景实例关闭 Auto Start，由升级成功后打开。
- 提示文字、左右卡扣、内存条、按钮、外翻角度和初始倾斜角度都在组件上序列化，可在 Inspector 调整。弹窗布局保存在 Scene 和 Prefab 中，运行时只更新状态和角度。

原旧面板中重复、空引用的两个组件已随面板整合替换为一个绑定完整的控制器。编辑器菜单 CallmeCatgirl / Integrate Memory Installation 是一次性接入工具，已接入的场景会跳过，避免覆盖后续布局。

针对性验证记录和截图位于 `Logs/LocalProjectIntegration`。
