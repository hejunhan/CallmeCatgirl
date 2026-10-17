# 头像资源

头像位于 `Assets/_Game/Art/Pic/Head`，按外观重命名，保留原 `.meta` GUID。

用户 A 使用 `Avatar_Female_BrownHair_Glasses`，用户 B 使用 `Avatar_Male_BlackHair_Glasses`，用户 C 使用 `Avatar_Female_SilverHair_Bow`。新增请求从其余头像开始依次使用；同一用户下一轮保持同一头像。

`GameplayCanvas` 的 `CoreGameplayCanvasView` 中，`User Avatars` 数组前三项对应 A/B/C，其余项供新增用户使用。可在 Inspector 拖入其他 Sprite 替换。

场景中的 `ConversationPanel/User_A/Avatar`（B/C 同理）是切换按钮头像；`ConversationPanel/ConversationAvatar` 是当前用户头像。`RequestCard` Prefab 的 `Avatar` 是请求卡片头像。这些都是普通 uGUI Image，可直接编辑尺寸和位置，头像不会遮挡点击或拖动。

| 原文件 | 新文件 |
|---|---|
| 1ABC28EB2780E035262B5241A792BC4D.jpg | Avatar_Male_GrayHair_Glasses.jpg |
| 355F04FFA9F6591E5DCA6D305008A9FC.jpg | Avatar_Female_BlueHair_Buns.jpg |
| 61C3A210C333028FD04B340E276C60DE.jpg | Avatar_Male_BlondHair_Hoodie.jpg |
| 653F0E6F220055F42500AB09CC6D81F4.jpg | Avatar_Female_PurpleHair.jpg |
| 7FD8E9F3F4F7955E70A102B9655C8309.jpg | Avatar_Female_BrownHair_Glasses.jpg |
| AB2F264EAAA11E6E58C772E322E0330C.jpg | Avatar_Male_BlackHair_Glasses.jpg |
| B98B6E88F7A7EBF3E55C403ED32E3CFF.jpg | Avatar_Male_BlackHair.jpg |
| BEF69368D5D29EE90777F9E19BAD56B0.jpg | Avatar_Female_SilverHair_Beret.jpg |
| C26846250C45735C0548777AED0DE36D.jpg | Avatar_Female_RedHair.jpg |
| C80A89CC9C19005C43044455610F6224.jpg | Avatar_Male_BlackHair_Beard.jpg |
| CBF70069187CD7650E5B92583CCB992A.jpg | Avatar_Female_SilverHair_Bow.jpg |
