# 主工程迁移记录 · 2026-10-19

用户明确指定 `D:/Unity/CallmeCatgirl` 为后续主开发目录。在这个目录打开 OpenCode；本次没有为监督创建重复模型会话，也没有启动额外开发 agent。已有 CLI 可从该目录运行 `opencode`。

已完成运行实现从保留的 C 盘候选以 Git 快进迁入：原目标 `4d058e28af12a89598d6a18207b4cbb0b9c2532f` → 已验收候选 `3c06a5f50c0d2d9a11a9ce1ce9e3f1ff729c7b84`，共 10 个提交、190 个文件路径。包含已完成的内存安装、训练工作区、既有政策/叙事/设置、固定 UI 场景化和 1080p 可读性改进。没有复制 Library、Temp、Logs 缓存，没有删除 C 盘候选。

本地通知插件只从原候选精确绑定改为本 D 工程精确绑定；不扩权限、不携带旧通知队列、不安装后台任务。自动排队不代表自动回传 dot，发送仍需人工核对；不能称为已完成无人值守通知。

目标既有 UniversalRenderPipelineGlobalSettings 及 HarmonyOS SDF 字体资产修改保持原字节，原未跟踪 Verification 文件保留。源候选的原脏文件没有迁入或清理。

迁移备份位于 `C:/Users/81004/Documents/Codex/2026-10-17/task-10/migration-to-d-20261019/backup`，包括目标文件前像、源/目标二进制差异、两分支 Git bundle、86 个正式存档文件原字节，以及旧 D Editor 暂停运行态的原生存档。旧运行态备份写在任务目录，经原生 PrepareRestore 校验，未覆盖正式 main。

回退引用是 `backup/d-before-migration-20261019`。回退需要在编辑器干净关闭后按备份清单恢复目标已有修改；不要用 reset --hard 或整目录覆盖。

从 D 工程执行一次必要编译/Windows Player 构建，最终 0 错误、缺失脚本 0，菜单与 Gameplay 配置/固定 Canvas 引用有效。隔离构建入口为 `Builds/MigrationQA/CallmeCatgirlMigrationQA.exe`，产品身份 CodexValidation / CallmeCatgirl-Migration-QA-20261019，不使用正式存档。临时 Editor 验证脚本在构建后移除，构建自动改写的配置恢复为构建前字节。实际启动/截图结果以任务目录的运行证据为准，编译成功不等于玩家旅程通过。

新 Argon4 文案仅候选待审，未接入此次运行实现。新政策树、训练随机倍率及“随机取最大值”仍是未实现提案，不在本次迁移中伪装为完成。后续剧情文本按 AGENTS.md 中的 Argon4 作者边界执行。
