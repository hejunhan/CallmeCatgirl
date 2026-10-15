> 策划推荐使用 [中文纯文字格式](../StoryText/README.md)，在一个角色文件里编辑对白、数值和方块。下文为保留的 JSON 导入方式；JSON 的 task 现在也支持可选 shape 字符串数组，如 `["#.", "#.", "##"]`。

# 外部对话导入

一次会话只摆放、计算、提交和结算一次。提交后打开对话弹窗，连续选择直到本次会话结束；弹窗内暂停经营时间。下一次会话重新产生计算任务。同次会话的所有轮次沿用提交时的质量，不会重复收费或增加达标任务数。

## 如何使用

1. 在 Unity Project 选中 `Assets/_Game/Data/TaskGrid/StoryConfiguration.asset`。
2. Inspector 点击 **导入角色 JSON（保留其他角色）**，选择本目录的 JSON。按角色 id 替换，未包含的角色保留，支持 Undo。
3. 停止运行后重新运行，或使用游戏的重开按钮。首次建议导入 `minimal.example.json`，A 有两次会话，第二次有三轮，便于快速验证。

**导入整份 JSON（替换全部角色）** 会替换全部剧情角色列表。**从 JSON 创建新的剧情配置资源** 可以另存一份，随后将 `CoreGameplayConfiguration.asset` 的 Story 引用指向新资源。导入只使用 Sessions 会话格式；旧的 Nodes 格式已移除。

JSON 是编辑器导入源文件。导入后内容保存在 Unity 的剧情配置资源内，打包不需要把 JSON 放在 StreamingAssets。修改外部文件后必须再次导入，不会自动热更新。

## 示例文件

- `minimal.example.json`：A 的两次会话，轮数 1 / 3，演示普通选项、质量条件和提前结束。
- `story-10-sessions.example.json`：三个角色各 10 次会话，后期逐渐增加轮数。用于格式及流程示范，文案和数值尚未平衡。
- `character-A.example.json`、`character-B.example.json`、`character-C.example.json`：分别导入一个角色，方便多人独立编辑。

每个角色都独立决定数量：`sessions` 数组长度是会话次数；每次会话的 `rounds` 长度是轮数；每轮的 `choices` 长度是选项数。没有统一 10 次的限制，也不要求角色之间数量相同。每个数组至少有一项，数量无固定上限。没有单独的计数字段，避免计数与内容不一致。

## 格式说明

最外层 `version` 固定为 1，`characters` 放角色列表。文件须为 UTF-8、标准 JSON（不能添加注释或尾逗号），字段名大小写与示例一致。

| 层级 | 字段 | 含义 |
|---|---|---|
| 角色 | `id` | 对应主玩法请求模板 id，目前 A / B / C；新增 D 须先添加 D 请求模板 |
| 角色 | `name`, `initialHistory` | 显示姓名、开局过往对话，可省略 |
| 角色 | `sessions` | 本角色的会话列表，按数组顺序推进 |
| 会话 | `id`, `title` | 唯一会话 id、任务标题；title 可省略 |
| 会话 | `requestId` | 任务模板 id，省略时使用角色 id；速度、token 继承模板；task.shape有值优先使用，没有则从形状库随机（未引用形状库的旧配置沿用模板） |
| 会话 | `task` | 可选，本次会话的计算量、忍耐和能力门槛 |
| 会话 | `rounds` | 本次会话的对话轮次 |
| 轮次 | `id`, `message`, `choices` | 轮次 id、用户发言、回复选项；必须填写 |
| 选项 | `label` | 按钮文字，必须填写 |
| 选项 | `reply`, `response` | 玩家发出的正文、用户回应；reply 省略时使用 label，response 可省略 |
| 选项 | `requirement` | `Any` 总能选；`CompleteOrBetter` 达标或优秀；`ExcellentOnly` 仅优秀，省略为 Any |
| 选项 | `nextRoundId` | 省略或 `NEXT` 按顺序进入下一轮；`END` 提前结束本次会话；填 `r3` 跳到本次会话 r3 |

会话 id 在同一角色内唯一；轮次 id 在同一会话内唯一，不同会话可重复使用 r1。不得使用 `/` 或保留字 NEXT / END 作为 id。显式跳转只能在同一会话内，不能直接跳进另一会话；到另一会话要先结束当前会话、重新计算。最后一轮的 NEXT 自动结束本次会话；最后一次会话结束后该角色剧情完成，下一轮按钮禁用，仍可查看历史。

每轮最好保留至少一个 Any 选项。如果所有选项都被质量条件挡住，会出现“发送回复”兜底并顺序推进，避免卡死。可以做局部循环，但应保留能退出的选项。

### 会话难度

```json
"task": {
  "calculation": 90,
  "patience": 120,
  "abilities": [
    { "kind": "Writing", "minimum": 12, "excellent": 16 },
    { "kind": "Code", "minimum": 3, "excellent": 4 },
    { "kind": "Reasoning", "minimum": 3, "excellent": 4 }
  ]
}
```

各能力分别判定，不能用总和抵消缺项。类型为 Writing / Code / Reasoning / Retrieval。calculation、patience 省略或为 0 时继承模板；abilities 省略或为空时继承模板，否则完整替换该任务的能力要求。excellent 必须大于 minimum，数值必须非负且有限，同一能力不能重复。

后期轮数和要求都可以提高，但没有根据轮数自动提升难度：数值在每次会话 task 中明确填写，便于设计调整。示例模型每 3 秒获得 4 / 1 / 1 能力，速度 10，因此计算量 90 对应 9 秒、12 / 3 / 3，适合验证三轮会话。示例优秀选项需要比基础模型更高的能力，当前基础模型可能看不到；可调整主配置 ModelPoints 或示例 excellent。

同一次会话的质量不会随着选择提高；选项不修改能力、资金或好感度。本次仅实现文案、跳转、质量门槛和独立会话流程。历史与进度属于本局，重开清空，不是磁盘存档。

导入会检查角色/会话/轮次 id、任务模板、跳转目标、必填内容、质量条件及能力要求。验证通过后才替换目标配置，失败保留原数据。

形状缺省规则更新：主配置已指定任务生成配置时，未填写方块的会话从 `TaskGenerationConfiguration.asset` 的预设库随机分配；已填写的剧情图案优先。未引用任务生成配置的旧配置仍沿用模板形状。详细数值和编辑说明见 `docs/TASK_GENERATION.md`。
