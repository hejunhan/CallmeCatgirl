# 开发约定

## 环境与启动

使用 `ProjectSettings/ProjectVersion.txt` 指定的 Unity 版本，当前为 **6000.5.2f1**。
包版本以 `Packages/manifest.json` 和 `Packages/packages-lock.json` 为准。
入口示例场景为 `Assets/_Game/Scenes/Prototypes/SampleScene.unity`。

第一次打开克隆项目时，需要等待 Unity 恢复依赖、导入资源和编译。
`Library/` 不在 Git 中，首次导入耗时长于日常打开属于预期。
若需验证完整性，克隆到新的目录，用同版本 Unity 打开，检查 Console 并运行场景。

## 新增功能的最小流程

1. 在 `docs/GAME_DESIGN.md` 写清玩家操作、反馈以及可验证的完成条件。
2. 按功能在 `Assets/_Game/Scripts/Runtime/Gameplay/` 下建立目录。
3. 对应配置实例放 `Data/<功能>/`，预制体放 `Prefabs/Gameplay/<功能>/`。
4. 在 `Scenes/Prototypes/` 中验证交互；稳定后再接入正式游戏场景。
5. 保存场景与资源，检查 Console，确认新增资源和 `.meta` 都已纳入提交。
6. 提交一次能够独立解释的变化，例如「添加基础交互原型」。

## 命名与代码

- 目录、文件、C# 类型使用清晰的英文 PascalCase，例如 `Interaction`、`InteractionPrompt.cs`。
- MonoBehaviour 与 ScriptableObject 的类名和文件名保持一致。
- 自研脚本命名空间使用 `CallmeCatgirl` 前缀，再按模块划分，例如 `CallmeCatgirl.Gameplay.Interaction`。
- UI 资源可使用 `MainMenuPanel`、`ConfirmButton` 等表达用途的名称，避免 `NewPrefab`、`Test2`。
- Editor 工具放在 `Scripts/Editor/`；运行时代码不直接依赖 `UnityEditor`。
- 当前没有新增运行时代码或程序集定义。模块稳定后再按实际依赖拆分 asmdef，避免初期引入不必要的引用管理。
- 后续添加自动化测试时，通过 Unity Test Runner 创建对应测试程序集，再组织 EditMode 和 PlayMode 测试。

## 资源与引用

- 通过 Unity Project 窗口移动或重命名资源，保持资源与 `.meta` 同步。
- 变更场景路径后检查 Build Profiles 中的场景列表。
- 新增场景不会因为放进 `Scenes/` 就自动参与构建，需要明确加入构建场景列表。
- 游戏存档在运行时写入 `Application.persistentDataPath` 等适当位置，不把玩家存档当作项目配置提交。
- 用户布局等 `UserSettings/` 内容保留在本机；共享图形、物理和输入等工程设置通过相应资源或 `ProjectSettings/` 提交。

## Git 工作流

开始工作前，在工作区没有未提交改动时运行：

```powershell
git pull --ff-only
```

完成一个小功能后，先查看变更，再提交确实属于本次工作的文件：

```powershell
git status
git diff
git add <本次修改的文件或目录>
git diff --cached
git commit -m "描述本次改动"
git push
```

`git add` 中的尖括号内容是占位符，需要替换成真实路径。
对新增资源，连同资源文件和对应 `.meta` 一起暂存。
若工作区中还有别的未完成改动，不使用整仓暂存把它们混进当前提交。
遇到远程分叉或冲突时先检查差异，保留双方有效修改。

## 文本与大文件

文本文件采用 UTF-8；`.gitattributes` 规定常用源码、文档和 Unity 文本资源使用 LF。
已有资源不会仅为了换行统一而在本次整理中整仓重写。
当前未启用 Git LFS；后续引入较大的模型、音频、视频等资源时，再决定需要由 LFS 管理的具体类型。

## 提交前检查

- 修改过的场景和资源已保存。
- Console 没有与本次改动相关的新编译错误。
- 场景、预制体没有新增 Missing Script 或丢失的资源引用。
- 已运行与本次修改直接相关的交互。
- 暂存区没有 Library、Temp、Logs、UserSettings 等本机文件。
