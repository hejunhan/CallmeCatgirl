# 实践依据与项目适配

查阅日期：2026-10-19。以下仅采用第一方材料，不将平台认证条款当成本项目必须实施的认证任务。

1. [Unity：Testing and quality assurance tips for Unity projects](https://unity.com/how-to/testing-and-quality-assurance-tips-unity-projects)
   - QA 应贯穿开发，而非收尾一次；功能检查面向需求，人工试玩能发现自动检查遗漏的 UI 和玩法问题。
   - 本项目适配：开发自测与独立 QA 分开，按改动风险安排定向回归，代码测试不替代真实交互。短周期与时间预算是本项目管理选择，不是 Unity 强制流程。
2. [Unity Learn / USC：Playtesting](https://learn.unity.com/course/design-and-publish-your-original-game-unity-usc-games-unlocked/unit/playtesting)
   - 试玩应围绕设计中的具体问题收集有用信息，而非仅问是否好玩。
   - 本项目适配：快速 QA 收集可观察问题，制作人审读反馈和体验。代理不能把数值通过直接等同于手感好。
3. [Unity 6.0：Override prefab instances](https://docs.unity3d.com/6000.0/Documentation/Manual/PrefabInstanceOverrides.html)
   - Prefab 实例可覆盖属性/组件/子对象，实例覆盖优先于资产值；编辑器有可查看标识。
   - 本项目适配：检查编辑器与 Play 不一致时追踪资产、覆盖和运行时初始化来源。静态 UI 可编辑/可追踪是用户明确要求，不是“禁止动态实例化”的普遍规则。实施 API 以项目安装的 Unity 版本核实。
4. [Microsoft GDK：Certification requirements and tests](https://learn.microsoft.com/en-us/gaming/gdk/docs/store/policies/Console/console-certification-requirements-and-tests?view=gdk-2510)
   - 作为平台级用例组织方式的参考：区分要求、步骤与期望，并关注挂起/恢复和保存场景。
   - 本项目不假装通过 Xbox 认证，也不增加账号切换/联网等未有功能；只借鉴可复现步骤和生命周期风险的组织方式。

设计来源：用户要求“QA 专用子代理，快模型做测试，制作人看手感”，并强调以功能和优化为重，避免不必要纯代码测试支出及旧场景断言限制新发展。严重度、20 分钟默认预算、角色分工与玩家旅程菜单是对此的项目化实现，不是外部来源的原文或强制标准。
