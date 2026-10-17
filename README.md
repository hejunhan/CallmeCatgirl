# CallmeCatgirl

Unity 游戏项目。当前处于工程搭建阶段，游戏类型与核心玩法尚待确定。

## 开发环境

- Unity **6000.5.2f1**，准确版本以 `ProjectSettings/ProjectVersion.txt` 为准。
- Universal Render Pipeline **17.5.0**。
- Input System **1.19.0**。
- 包依赖及其锁定版本由 `Packages/manifest.json` 和 `Packages/packages-lock.json` 管理。

## 打开项目

1. 克隆仓库，在 Unity Hub 中选择「添加来自磁盘的项目」，选中仓库根目录。
2. 使用上述 Unity 版本打开，等待包恢复和资源导入。
3. 打开 `Assets/_Game/Scenes/Prototypes/SampleScene.unity`。
4. 点击 Play。当前场景是保留的 URP 示例场景，尚未实现游戏玩法。

## 文件放在哪里

| 内容 | 位置 |
| --- | --- |
| 自研游戏内容 | `Assets/_Game/` |
| 游戏脚本 | `Assets/_Game/Scripts/Runtime/` |
| 编辑器工具 | `Assets/_Game/Scripts/Editor/` |
| 场景与预制体 | `Assets/_Game/Scenes/`、`Assets/_Game/Prefabs/` |
| 美术与音频 | `Assets/_Game/Art/`、`Assets/_Game/Audio/` |
| 游戏配置数据 | `Assets/_Game/Data/` |
| 输入配置 | `Assets/_Game/Settings/Input/` |
| URP 渲染配置 | `Assets/Settings/` |
| 第三方资源 | `Assets/ThirdParty/`；UPM 包由 `Packages/` 管理 |
| 策划与开发说明 | `docs/` |

- [完整目录规划与放置规则](docs/PROJECT_STRUCTURE.md)
- [开发约定与 Git 工作流](docs/DEVELOPMENT.md)
- [游戏设计待办与首个可玩版本](docs/GAME_DESIGN.md)

## 版本管理

提交 `Assets/`（包括对应的 `.meta`）、`Packages/`、`ProjectSettings/` 和文档。
`Library/`、`Temp/`、`Logs/`、`UserSettings/` 由本机生成或维护，不上传。

空目录使用 `.gitkeep` 保留，并为 Unity 资源目录提供 `.meta`。目录有实际内容后可移除其中的 `.gitkeep`。
