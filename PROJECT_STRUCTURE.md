# 目录规划

本结构面向尚未确定具体玩法的 Unity 游戏。先为稳定的资源类型建立位置；战斗、背包、对话、关卡等功能，确定需求后再增加对应模块。

## 当前目录

```text
CallmeCatgirl/
├── Assets/
│   ├── _Game/                       自研游戏内容
│   │   ├── Art/
│   │   │   ├── Animations/          动画片段、Animator Controller
│   │   │   ├── Fonts/               字体资源
│   │   │   ├── Materials/           材质
│   │   │   ├── Models/              模型
│   │   │   ├── Sprites/             2D 图片、UI 图标、图集
│   │   │   ├── Textures/            贴图
│   │   │   └── VFX/                 特效专用资源
│   │   ├── Audio/
│   │   │   ├── Music/               背景音乐
│   │   │   ├── SFX/                 游戏音效
│   │   │   └── UI/                  界面音效
│   │   ├── Data/                    ScriptableObject 实例、JSON 等配置
│   │   ├── Prefabs/
│   │   │   ├── Common/              跨模块复用的预制体
│   │   │   ├── Gameplay/            玩法对象预制体
│   │   │   └── UI/                  界面和控件预制体
│   │   ├── Scenes/
│   │   │   ├── Gameplay/            正式游戏场景
│   │   │   ├── UI/                  独立菜单等场景，按需使用
│   │   │   └── Prototypes/          原型与实验场景
│   │   │       └── SampleScene.unity
│   │   ├── Scripts/
│   │   │   ├── Runtime/
│   │   │   │   ├── Core/            跨模块基础接口、通用状态和流程
│   │   │   │   ├── Gameplay/        具体玩法，内部按功能分目录
│   │   │   │   ├── Infrastructure/  存档、音频播放等基础设施，按需实现
│   │   │   │   └── UI/              界面逻辑
│   │   │   └── Editor/              仅在 Unity 编辑器运行的工具
│   │   └── Settings/
│   │       └── Input/
│   │           └── InputSystem_Actions.inputactions
│   ├── Settings/                    现有 URP 管线、Renderer 和 Volume 配置
│   ├── ThirdParty/                  第三方插件与资源，按供应方或资源包分目录
│   ├── TutorialInfo/                Unity 模板教程资源
│   └── Readme.asset                 Unity 模板说明资源
├── Packages/                       UPM 依赖清单和锁定文件
├── ProjectSettings/                共享项目设置
├── docs/
│   ├── PROJECT_STRUCTURE.md
│   ├── DEVELOPMENT.md
│   └── GAME_DESIGN.md
├── .gitattributes                  文本换行约定
├── .gitignore                      缓存和个人设置忽略规则
└── README.md
```

## 放置规则

- **脚本按功能组织。** 例如，确定有交互玩法后建立 `Scripts/Runtime/Gameplay/Interaction/`。同一功能相关脚本放在一起；不预先创建大量空的 Manager、Service、Helper 类。
- **Core 保持小而稳定。** 具体角色行为、数值规则和界面逻辑放在相应功能目录。
- **代码与配置分开。** ScriptableObject 的 C# 类型放在对应脚本模块；由该类型创建的资源实例放在 `Data/<功能>/`。
- **场景按用途区分。** 实验放在 `Prototypes/`，可发布的玩法场景放在 `Gameplay/`。并非每个界面都需要独立场景，普通面板优先使用 UI 预制体。
- **特效以便于维护为准。** 特效专属的材质、贴图、预制体可放在同一个 `Art/VFX/<效果名>/`；跨模块复用的资源放到对应公共目录。
- **工程输入与玩法数值分开。** Input Actions 放在 `Settings/Input/`，角色数值等放在 `Data/`。项目级图形、物理等设置仍由 Unity 的 Project Settings 管理。
- **第三方包保留自身结构。** UPM 包通过 Package Manager 管理；Assets 下的插件优先放 `ThirdParty/`，保留其说明与许可证。若插件要求特定路径，遵循该插件的要求。
- **源工程按需扩展。** 日后需要保留 PSD、Blender、DAW 等制作源文件时，先确定团队是否需要 Unity 自动导入及文件体积，再选择项目内位置或单独素材仓库；当前不创建重复的素材副本。

## 现有资源整理

- 示例场景从 `Assets/Scenes/SampleScene.unity` 移至 `Assets/_Game/Scenes/Prototypes/SampleScene.unity`，场景及原场景目录的 GUID 保持不变。
- Input Actions 从 Assets 根目录移至 `Assets/_Game/Settings/Input/`，资源 GUID 保持不变。
- 构建场景列表中的路径同步到新位置；Input System 的项目引用继续使用原 GUID。
- `Assets/Settings/` 中的 URP 配置继续使用原路径，便于追踪现有渲染配置。
- 模板教程资源仍保留，后续确认不再使用后可单独清理。

## 空目录与 .meta

Git 不保存空目录，因此预留目录使用隐藏的 `.gitkeep` 文件占位。
Unity 目录对应的 `.meta` 已一并创建并纳入版本管理；`.gitkeep` 自身不需要 Unity 元数据。
在 Unity 编辑器内移动或重命名资源，让 Unity 一起维护 `.meta`。
