# 会话条件与效果集成

本补丁扩展已有 `ConversationStory`，不创建第二套任务、经济或时钟系统。A/B/C 同学文本、默认 StoryConfiguration.asset、场景、Packages、控制器、钱包和 Canvas 均未修改。

## 文件所有权与完整清单

修改的原有文件：

- `Assets/_Game/Scripts/Runtime/Gameplay/ConversationStory.cs`
- `Assets/_Game/Scripts/Runtime/Gameplay/Configuration/StoryConfiguration.cs`
- `Assets/_Game/Scripts/Editor/StoryJsonImporter.cs`

新增文件：

- `Assets/_Game/Tests/EditMode/TaskGrid/StoryChoiceEffectsTests.cs` 及 `.meta`
- `Examples/StoryImport/placeholder-effects.example.json`
- `Verification/StoryRuntime/StoryRuntime.csproj`
- `Verification/StoryRuntime/Harness.cs`
- `Verification/StoryRuntime/NuGet.Config`
- `Verification/StoryRuntime/RESULTS.txt`
- `docs/STORY_RUNTIME_INTEGRATION.md`

Verification 中的 Unity/NUnit 替身只供 .NET 补充验证，位于 Assets 外，不进入 Unity 编译或玩家构建。不应将它们复制进 Assets。

## 由 task-2 集成者完成的三个接点

1. `ResetSimulation` 创建并赋值现有 `Economy` 后、首次 `ReadEvents()` 前，加 `Story.Economy = Economy;`。每次重置都重新绑定。未绑定时，普通旧回复正常，带收益或余额条件的选项被隐藏。
2. 控制器给 `ChooseReply` 增加 `(int index, int expectedRevision)` 入口，转发 `Story.ChooseAtRevision(index, expectedRevision)`；只有返回 true 才执行原有的剧情完成/关闭反馈/刷新逻辑。可保留旧入口供现有测试和非 UI 调用使用。
3. `ConversationPopupView.Refresh` 构建按钮时捕获局部变量 `int expectedRevision = story.Revision;`，监听器调用 `owner.ChooseReplyAtRevision(index, expectedRevision)`。不要读取稍后已变化的 view.revision 字段。

主控制器仍独占 `DrainEvents()`，继续原有 `Arrived`、`Submitted` 调用；不需额外订阅事件，不额外 Advance。下一任务继续走现有 `NextRound` / `TryCloseFeedback` / `BuildNextTask`。本补丁不自动创建下一任务。

示意（由集成者合入控制器，不能只调用 Story 而遗漏控制器原有关闭逻辑）：

```csharp
public void ChooseReply(int index) => ChooseReplyAtRevision(index, Story.Revision);
public void ChooseReplyAtRevision(int index, int expectedRevision)
{
    string user = Story.UserId, request = Story.RequestId;
    if (!Story.ChooseAtRevision(index, expectedRevision)) return;
    // 保留原 ChooseReply 中的 Finished / TryCloseFeedback / ReadEvents / Message / Refresh。
}
```

## 数据契约

JSON version 1 的 choice 可选字段：`minimumFunds`（最低余额）、`fundsReward`（奖励）。未填均为 0。字段对应资产中的 `MinimumFunds` / `FundsReward`；两者必须非负，非法配置会在 Validate/导入阶段拒绝。

可用性 = 原质量条件 + 当前钱包条件 + 奖励不导致 int 溢出。选择时再次检查钱包。奖励通过原 `EconomyManager.AddFunds` 写入真实余额并发送原有通知，不另建账本。不提供扣费/声望/能力平行状态。破产钱包不发奖励，但普通回复仍可关闭。

所有选项都被挡住时沿用原有安全默认回复，奖励为 0，按顺序推进；此行为是防止配置缺漏卡住提交后的模态窗口，不表示默认完成高门槛分支。应始终为正式内容配置普通回复。

`ChooseAtRevision(index, expectedRevision)` 拒绝旧按钮回调，含队列中另一联系人和同会话下一轮。`Choose(index)` 是保留的兼容入口，无法区分来自旧 UI 的点击，UI 必须接带 Revision 的入口。重复 Arrived/Submitted 忽略；未真正 Submitted 的任务不能打开回复。钱包通知中的重入选择被拒绝。

## 占位素材

`placeholder-effects.example.json` 包含折页、拾音两位原创占位联系人，各两次任务会话，总共四轮短回复。全部明确标注非最终策划，名称/文本/任务/分支均可替换。使用 A/B 模板 ID，必须导入到单独测试用 StoryConfiguration，并在测试配置副本上指向它；不要对正式同学资产执行 replaceAll。

每轮有普通交付及“补充说明 +5”两个选择；任务完成后才出现回复，结束当前会话后由现有 NextRound 创建下一任务。不会替换 `Examples/StoryText/A_林小满_10次会话.txt`、B_周序、C_苏禾或默认资产。

## 验证与限制

2026-10-17：同一个 `StoryChoiceEffectsTests` 测试文件在 .NET 9 无界面补充验证中运行，结果见 `Verification/StoryRuntime/RESULTS.txt`。链接真实 ConversationStory、StoryConfiguration、StoryJsonImporter、EconomyManager、TaskSimulation、TaskTypes、ShapePattern、TaskGenerationConfiguration 源文件；只替代 Unity 属性、ScriptableObject 生命周期、JsonUtility（以 System.Text.Json 模拟）及质量显示文本，NUnit 仅替代测试标记。

运行（项目根 PowerShell；环境变量仅当前进程）：

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'Verification/.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project Verification/StoryRuntime/StoryRuntime.csproj
```

本机选定环境仅找到 Unity 2022.3.53f1c1，尚未找到要求的 6000.5.2f1。因此 **未执行 Unity 测试、Unity JsonUtility 真正反序列化、场景 Play Mode、真实按钮接线或构建**，无界面结果不能替代这些验收。

集成者在 6000.5.2f1 应运行 EditMode `CallmeCatgirl.Tests.StoryChoiceEffectsTests` 和原 `ConversationStoryTests`、导入测试，并以测试配置在暂停状态完成 A/B 四次任务、点击奖励、切下一次会话、重置后确认历史/余额重置。正式资产和同学文本应保持原样。
