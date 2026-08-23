# 项目现状总览 —— 猫咖营业计划

> 本文是 AI 会话维护的项目现状说明（**只增不改**原则下的新增文档）。
> 根目录 `readme.md` 是 chuanzhou 的团队模版，描述的是初代变脸解谜概念；
> 是否用本文内容更新它，由团队决定（见任务板「待决策」）。

项目实际内容：像素风猫咖经营小游戏。**局内**是幸运房东式随机棋盘经营
（老虎机重排 + 相邻结算 + 三选一构筑），**局外**是猫咪收集/繁育/养成的长期循环（开发中）。

历史沿革：变脸解谜（仅 readme 存在）→ 幸运自走棋 / BoardCombat（已删除）→ 猫咖营业计划（当前，橘猫 8-8 提交）。

## 场景与入口

| 场景 | 职责 |
| --- | --- |
| `Assets/Scenes/CatCafeStart.unity` | 开始页 |
| `Assets/Scenes/CatCafeDemo.unity` | 局内玩法，入口 `Assets/Scripts/CatCafe/CatCafeGameController.cs` |
| `Assets/Scenes/SampleScene.unity` | Unity 模板场景（是否移出 Build Settings 待团队决策） |

技术形态：Unity 6000.5.3f1 / URP / 2D / 新 Input System；UI 全部运行时 uGUI 代码构建（零 prefab）；
元素图 `Resources/CatCafe/`（256×256 PNG，点过滤）；二进制走 Git LFS。

## 文档索引

| 文档 | 内容 |
| --- | --- |
| [Design/CatCafeGameDesign.md](Design/CatCafeGameDesign.md) | 局内世界观与玩法（代码逆向整理） |
| [Design/MetaGameSpec.md](Design/MetaGameSpec.md) | **局外系统策划案（当前实装版，先读这个）** |
| [Design/MetaGameDesign.md](Design/MetaGameDesign.md) | 局外成长系统定案（含决策记录与二期 backlog） |
| [Design/MetaGameTechDesign.md](Design/MetaGameTechDesign.md) | 局外技术设计（场景/存档/改造点/里程碑） |
| [Design/DataTableSpec.md](Design/DataTableSpec.md) | 策划写表接口 + 美术资产规范 |
| [Todo/TaskBoard.md](Todo/TaskBoard.md) | **任务板**（唯一任务事实源，含维护规则） |
| [AI/UnityProjectContext.md](AI/UnityProjectContext.md) | 橘猫的 AI 会话上下文记录 |
| `../Prototype/meta-cafe.html` | 局外系统 HTML 交流稿（双击可开） |
