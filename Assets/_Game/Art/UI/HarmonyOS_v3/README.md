# HarmonyOS v3 主菜单接入

来自用户提供的 `主界面_程序交付_HarmonyOS_v3/程序交付_HarmonyOS_v3`。9 张 PNG 已导入为无压缩、无 mipmap、带透明通道的 Sprite。原始说明与 layout.json 保留于本目录。

MenuScene 的 HarmonyMenuLayout 使用按钮 normal/hover/pressed/disabled 四态、独立 arrow_white、rule_long、rule_short、status_ready、divider_vertical。按钮保持方形，长线按原 640:32 比例展示，不把中央刻度单独拉宽。

TTF 与现有字体哈希相同，因此没有重复复制 fonts：实际复用 `Assets/_Game/Art/Fonts/HarmonyOS_Sans/` 的原始字体及 LICENSE。节点数字使用真实 Thin，建立连接使用真实 Medium；其他菜单文字使用现有 Regular SDF/TMP。未使用 Light 小字、Bold/Black，不做伪粗体或新增大字库。

以交付 1920×1080 左侧节点构图为基准；普通辅助字提高到 30–32，等待字改为 #525252，保留完整动态文本。系统设置尚无业务功能，未增加空入口；角色、Logo 和背景图不在此素材包，未擅自补画。

菜单和工作台使用 WorkbenchGeometry 的简洁底色、网格、角标和弧线。后续背景精细制作记录于 docs/GAME-DESIGN.md。

已实际查看 2560×1243、1920×1080、1280×720；按钮事件新游戏→CoreGameplay→返回菜单通过。桌面注入点击没有触发入口，因此不把它作为原生点击通过证据；事件、输入模块与素材绑定的检查记录保留于工作区 validation/menu-art-*.json。继续会话在没有 main 存档时禁用，没有为验收创建或覆盖用户存档。未测试退出关闭动作，避免关闭用户当前编辑器运行。

入口文案后续按用户要求明确为“初始化连接”（新游戏、始终可用）与“继续连接”（读取已有 main 存档）。初始化连接的文字与箭头共享 272×64 点击区域，沿用同一个 StartNewGame 事件；没有新增初始化业务逻辑或确认弹窗。用户说明可能同时操作了鼠标，因此此前注入现象不归因于游戏缺陷；后续以 API 核验正常新局状态，原生注入验收仍未宣称通过。
