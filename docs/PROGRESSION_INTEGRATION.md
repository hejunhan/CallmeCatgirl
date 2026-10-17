# J07 天赋与政策模块集成

## 交付位置与范围

源码根目录：`C:\Users\81004\Documents\Codex\2026-10-17\task-7\CallmeCatgirl`。

开发基线是从 task-2 主工程读取的文件副本，任务指定版本为 `2ae6e2eff1e77f8ca7b78ca8250a5917c2036ca1`，Unity 版本为 `6000.5.2f1`。副本单独初始化了 `feature/progression` 分支，没有原仓库历史。只复制下列模块文件，勿覆盖整个项目或 cherry-pick 整仓初始快照。

需要复制的路径（均相对源码根目录）：

- `Assets/_Game/Scripts/Runtime/Gameplay/Progression.meta`
- `Assets/_Game/Scripts/Runtime/Gameplay/Progression/` 全目录，包含 Core、配置、控制器、面板和各自 meta。
- `Assets/_Game/Data/Progression.meta`
- `Assets/_Game/Data/Progression/` 全目录，包含 `DefaultProgression.asset` 及 meta。
- `Assets/_Game/Tests/EditMode/Progression.meta`
- `Assets/_Game/Tests/EditMode/Progression/` 全目录，包含测试程序集、NUnit 测试及 meta。
- `docs/PROGRESSION_INTEGRATION.md`

交付包 `task-7/deliverables/Progression-module.zip` 只含以上新增路径；同目录 `Progression-files.txt` 为逐文件清单，`Progression-sha256.csv` 为校验值。解压到 task-2/CallmeCatgirl 项目根目录即可合并新增文件。若目标已有同名 Progression 模块，应先由集成者比较再合并。

未改主工程、CoreGameplayController、EconomyManager、Canvas、场景、Packages 或 ProjectSettings，未 push。副本中其余文件仅用于读接口和验证，不属于交付内容。

## 接入点

1. 在现有 `CoreGameplayController` 的同一 GameObject 添加 `ProgressionController`。其 `configuration` 字段绑定 `Assets/_Game/Data/Progression/DefaultProgression.asset`；留空也使用相同默认九节点。
2. 在现有 Canvas 下新增至少 960×340 的 RectTransform 面板，挂 `ProgressionPanel`，绑定上述控制器。`labelFont` 绑定项目内支持中文的 Font；后备 LegacyRuntime 字体仅用于基本运行。沿用既有 EventSystem 和 GraphicRaycaster。
3. 外层管理按钮用面板 GameObject 的 `SetActive` 控制显示，建议初始隐藏。适配器运行时生成九个 uGUI 按钮，三条分支各一行、每行三级从左向右展开。当前是简易可用入口，未绘制连线，不替代最终美术。
4. 面板不暂停游戏。玩家沿用现有暂停操作，运行中或暂停时均能购买；剧情/扩容等既有模态打开或 GameEnded 时，控制器返回 Busy、拒绝购买。面板本身不是模态，也不阻止其他面板背后的操作。
5. `ProgressionController.TryPurchase(id)` 返回 Success / UnknownNode / AlreadyUnlocked / MissingPrerequisite / InsufficientFunds / Busy。调用者可据此播放反馈。唯一扣费入口为 `owner.Economy.TrySpendFunds`。
6. `ResetSimulation()` 生成新 Simulation 后，`EnsureBound()` 自动创建空树并使用当前钱包，上一局解锁不会带入新局。每帧只检查绑定，不推进时间。

不调用 `DrainEvents`，不重复结算任务或月费，`AdvanceGame` 仍为唯一时钟。Core 程序集只依赖 TaskGrid，无 UnityEngine 依赖；控制器、配置和 uGUI 留在 Assembly-CSharp，与现有工程结构兼容。

## 数值与状态约定

写作、代码、推理三条分支，每支三个永久节点。ID 为 Writing-1/2/3、Code-1/2/3、Reasoning-1/2/3。二级要求一级，三级要求二级。每节点为对应能力每计频轮加1，费用依次100/200/300，完整树1800。当前主配置初始资金1500，标准示例任务100 token、单价1，因此每层约为一/二/三次基础交付收入；这些是可调整的初始值，不是最终平衡。

配置资产允许更改节点ID、标题、成本、前置、AbilityKind和PointsPerTick，四种现有能力都可配置。配置缺少前置、包含环、重复ID、负成本、无效能力或非正/非有限加成都拒绝构造。

实际效果经 `TaskSimulation.TryChangeModel` 更新模型能力点。所有并发任务继续独立运算；已产出能力点、计算进度和计频余量保持。计算速度、计频间隔和忍耐不变，因此不会因提速而减少能力积累。临时设置 Paused 换模后在 finally 恢复原状态，不修改 GameTime 或 PlaybackSpeed。

修饰器按已解锁节点求和，只将新旧总和的差加到当前模型。重复购买、重复读档不会叠加，现有 `AllocateAbility` 从当前模型复制后加点，与之兼容。配置/状态返回副本，不能通过修改视图绕过购买。

`SaveJson()` 返回 Version=1 和已解锁ID数组，`LoadJson(json)` 验证版本、ID、唯一性和前置闭包再应用。非法状态不改变解锁或模型。它们是可信统一存档接口，不扣费、不退款，不作为玩家免费导入入口。

加载顺序：先创建基础 Simulation，恢复普通能力加点和钱包，再加载天赋状态。不要把已包含天赋加成的模型另存为“基础模型”，在新 Simulation 上又加载天赋而造成重复。

当前完整换模尚无玩家入口。若集成后在同一 Simulation 上整体替换基础模型，应先保存天赋状态并 TryRestore 空状态，替换基础模型后再恢复天赋；普通 AllocateAbility 不需要此处理。载入存档之后再次读取同一状态是幂等的。

## 策划来源与边界

已读取 `docs/CORE_GAMEPLAY.md`，现有经济、任务运算、模型和扩容接口，以及 Downloads 中《策划案：用户说我是一个猫娘.docx》和《分工.xlsx》。J07 要求“实现内存安装与布局扩展，提供政策树骨架和固定方案选择入口”；政策页的确定方向是横向展开、不同路线纵向分支。

本交付先实现可实际影响任务品质的永久能力专精树。互斥、退款重置、政策切换、风险情报、计费渠道、固定方案工作台和结局未实现，不将临时能力节点宣称为完整策划系统。

## 已执行验证

工作目录外层验证工具：`C:\Users\81004\Documents\Codex\2026-10-17\task-7\validation`。

执行：

```powershell
& 'C:\Users\81004\Documents\Codex\2026-10-17\task-7\validation\Run-ProgressionSmoke.ps1'
```

脚本将 DOTNET_CLI_HOME 放到自身可写目录，使用无远程源 NuGet.Config，不需第三方包。项目直接编译副本的真实 TaskSimulation、TaskTypes、EconomyManager 和本模块 Core 源码，没有模拟经济或运算实现。

本轮实际结果见 `validation/ProgressionSmoke-results.txt`：28项检查通过。涵盖唯一扣费、前置/重复/未知节点拒绝、余额不足、非法存档、重复恢复、独立能力加点、钱包回调重入、三分支效果、暂停、在途旧贡献和半轮计频保持、配置防外部修改。

实际链路证据：相同任务原写作产出2、质量 BelowStandard；购买 Writing-1 后产出4、质量 Excellent；提交通过原 EconomyManager 取得100收入。

## 未执行的 Unity 验收

本任务没有运行 Unity 编辑器、EditMode Runner、Play 或桌面UI，未声称 Unity 编译通过。以下须由 task-2 集成负责人在 6000.5.2f1 中执行：

- 打开项目并等待所有程序集编译；确认默认配置资产可加载且9节点字段正确。
- Test Runner > EditMode > `CallmeCatgirl.Tests.ProgressionTests`。共9个测试用例（含两个暂停参数用例）。
- Play：点击 Writing-1，余额只减100、模型 Writing 每轮只加1；重复点击无额外扣费或加成。
- 对同一请求比较未购买与购买后的真实能力积累/品质；检查在途购买不清进度。
- 暂停、切换现有各倍速，确认购买不改变暂停/PlaybackSpeed，时间仍由原控制器推进。
- 扩容获得普通能力点并 AllocateAbility，再购天赋，两类增量均保留。
- 重开后天赋为空；在基础模型/钱包恢复之后读档两次无叠加。
- 打开/隐藏面板，确认中文、三行三级布局、点击命中、模态/经营失败时拒绝购买；正式接入时设置中文字体。

可用批处理命令（由集成者填入已安装编辑器路径，不要加 `-quit` 以免 Test Runner 提前退出）：

```powershell
& '<Unity6000.5.2f1>\Editor\Unity.exe' -batchmode -nographics -projectPath 'C:\Users\81004\Documents\Codex\2026-10-17\task-2\CallmeCatgirl' -runTests -testPlatform EditMode -testFilter 'CallmeCatgirl.Tests.ProgressionTests' -testResults '<可写日志目录>\Progression-EditMode.xml' -logFile '<可写日志目录>\Progression-Unity.log'
```
