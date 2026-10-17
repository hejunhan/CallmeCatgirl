# 内存安装小游戏：patch-2 合并记录

- 独立场景：`Assets/_Game/Scenes/Prototypes/MemoryInstallScene.unity`。
- 美术：`Assets/_Game/Art/MemoryInstall/`。
- 旧主场景中的小游戏面板已提取为 `Assets/_Game/Prefabs/UI/CoreGameplay/MemoryInstallGamePanel.prefab`，保留上传的控件与引用。
- 保留 main 当前主场景、玩法、配置与 UI，没有用分支中的旧文件覆盖。

## 缺失的脚本

| 用途 | 类名 | 原脚本 GUID |
| --- | --- | --- |
| 独立场景 | CallmeCatgirl.UI.StandaloneMemoryInstall | e31785adf264bb64c80347437d979237 |
| 弹窗面板 | CallmeCatgirl.UI.MemoryInstallGame | c37f3edd185bf5f47adb8748b39108d5 |

需要补传对应 `.cs` 和原 `.meta`。补齐前，Unity 会显示 Missing Script，小游戏不能视为已可玩。

原面板包含两个 MemoryInstallGame 组件：一个字段未绑定，另一个已绑定。本次保留上传状态，补齐脚本后再确认应保留哪个。

本次只合并已有资源，没有连接主玩法入口或修改构建场景列表。补齐后需验证左右卡扣、内存条倾斜、跳过、完成和内存升级衔接。
