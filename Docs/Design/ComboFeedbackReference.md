# 联动特效调研 · 三消 / 卡牌 / 老虎机 Roguelike

> 调研日期：2026-08-15
> 目的：为「猫咖营业计划」的**局内结算演出**（相邻联动、成组、连锁）找参考与落地方案。
> 对照代码：`CatCafeGameController.PlayEventGroup / PlayPayoutBatch`、`CatCafeRewardFx`、`CatCafeInteractionFeedback`。
> 配套：[CatCafeGameDesign.md](CatCafeGameDesign.md) §3 相邻规则、§2.1 玩家管的是牌库。

---

## 0. 一句话结论

我们的品类不是三消，是**老虎机 deckbuilder**。三消（Royal Match / Candy Crush）只有「节奏与并发」值得抄，
真正该抄的是 **Balatro 的分步结算演出**和 **Backpack Battles 的相邻连线可视化**——
因为玩家在我们游戏里**没有摆位权**，所有的「爽」都必须来自"看懂谁给谁加了钱"，
而这恰恰是我们当前演出唯一没做的事。

---

## 1. 参考对象与它们的表现手法

| 游戏 | Steam 体量 | 与我们的关系 | 值得抄的表现 |
| --- | --- | --- | --- |
| **Balatro 小丑牌** | 品类 #1，97 分 / 19 万评 | 联动演出天花板 | 逐个 Joker 顺序触发、分级震屏、音高递进、颜色即标签 |
| **Luck be a Landlord 幸运房东** | 我们的直接原型 | 同为符号相邻结算 | 极简：符号弹一下 + 金币数字飘出；**关键是提供了 Instant 动画档** |
| **CloverPit** | 2025-09 发售，销量破 100 万 | 老虎机 roguelike 新标杆 | 停轴期待感（near-miss）、150+ 道具的叠加演出、氛围压迫 |
| **Ballionaire** | 2024-12，autobonker 开创者 | 纯连锁爆发 | 连锁起飞时全屏火焰/水痕/彩虹拖尾；每次碰撞独立音效 |
| **Backpack Battles** | 相邻格位 autobattler | **与我们 4×4 相邻规则最像** | 蓝色连线表示"可联动"、就绪时连线变金色、星标标注邻接来源 |
| **Royal Match / Candy Crush** | 三消营收头部 | 只借节奏 | Royal Match 允许"并发匹配"（棋盘还在落时就能继续操作），刻意拉长 cascade 快感 |

### 关键情报（有出处）

**Balatro** 的反馈不是"后期加的特效"，是和计分系统一起设计的：
- **逐个触发即教学**：每张 Joker 按从左到右顺序单独弹跳、单独结算、总分逐次跳字（约 300ms/次）。
  "By showing each Joker trigger individually, players learn which combinations matter"——**省掉了整套教学文案**。
- **震屏当数据通道**：小分 2px / 0.2s，中分 4px / 0.3s，大分（1万+）8px + 旋转 / 0.5s。数字还没出来，玩家先从震动幅度知道这次赢麻了。
- **音高递进**：5 张牌依次 C-D-E-F-G 升音；倍率触发是独立的 "ka-ching" 音层；过阈值有 bass drop。
- **颜色即标签**：蓝 `#009dff` = 底数 / 红 `#fe5f55` = 倍率 / 金 `#f0c040` = 钱。三种数值永不混色。
- 数字滚动 0.4s，缓动 `cubic-bezier(0.34, 1.56, 0.64, 1)`（回弹），逐位延迟 0/50/100/150ms。

**Backpack Battles**：商店里拿起一件物品，所有能与它联动的物品**同时射出蓝线**；
凑齐条件时**蓝线转金**。邻接加成用星标挂在被影响格上。玩家不用读文本就知道"放这儿有用"。

**Luck be a Landlord**：表现极简（符号 pop + 金币数字），但设置里可以把动画速度调到 **Instant**。
这是长线玩家留存的必需品——第 30 回合没人想再看一遍 16 个符号逐个弹。

---

## 2. 我们现状的诊断

已经做对的（保持，别推翻）：

- ✅ 无联动的基础产出**合并成一拍**（`BuildEventGroups` 的 `plain` 组），没让 16 个元素排队——这点比很多 Balatro-like 聪明。
- ✅ 联动簇按棋盘连通性 BFS 分簇，逐簇高亮 + 组序号。
- ✅ 三档反应 plain / linked / high，颜色与时长全部走表。
- ✅ 金币飞行带拖尾、来源格回弹（0.18 punch）、HUD 弹跳（1.15/1.20）、速度倍率可调。

缺口（按影响排序）：

| # | 缺口 | 后果 |
| --- | --- | --- |
| G1 | **看不出"谁给谁加的钱"** | 只有一圈同色 marker，玩家不知道是猫吃了牛奶、还是客人被猫吸引。**落地观景窗的单向八格视野更是完全不可见** |
| G2 | **没有连锁递进感** | 每个 group 是独立的一拍，播 8 个 group 和播 2 个 group 的情绪曲线一样平 |
| G3 | **没有震屏 / 顿帧** | high 档只比 plain 档 marker 大一点，"这局爆了"和"这局普通"体感差不多 |
| G4 | **`BuildPayoutBatches` 按金额合并** | 把毫不相关的元素凑成一批飞金币，刚建立的因果关系又被打散 |
| G5 | **成套坐垫（connected_same≥3）没有专属表现** | 这是我们唯一的"三消血统"机制，却和普通联动共用 marker |
| G6 | **停轴没有预告** | 老虎机品类最值钱的期待感（CloverPit / LbaL 都靠这个）完全没用上 |

---

## 3. 落地方案（分优先级）

### P0 — 联动连线（补 G1）

**做什么**：`PlayEventGroup` 亮起簇的同时，从每个**贡献者格**向**收益者格**画一条带流动点的曲线，
0.12s 内长出，停留到本组结束。单向规则（落地观景窗、猫喝牛奶）画**带箭头**的线，方向即语义。

- 数据已经有了：`RoundEvent` 里 `HasLink`、`Neighbors(index, seesDiagonals)` 已经算出来了，
  只需要在 `RoundEvent` 上多存一个 `List<int> ContributorIndices`。
- 表字段（新增，`settlement_*` 系列）：
  `settlement_link_grow_seconds`、`settlement_link_width`、`settlement_link_flow_speed`、
  `ui_settlement_link_color`、`ui_settlement_link_directed_color`。
- 参考 Backpack Battles：**未触发时不画**，只在结算瞬间画——我们没有摆位权，画"潜在联动"没意义。

**收益**：这一条顶得上一整套教学文案。玩家第一次看见"温牛奶 → 黑猫"的箭头，就永久理解了消耗品机制。

### P0 — 连锁计数 + 音高递进（补 G2）

**做什么**：结算队列里维护一个 combo 序号，每播一个 linked group：

- 屏幕侧边 `连锁 ×N` 计数跳字（复用 `chainSequenceText` 的样式体系）；
- 音效 pitch 按半音递进：`pitch = base * pow(2, min(n, 12) / 12)`，封顶 12 阶后改为叠加一层"铃铛"音；
- group 间隔随 n 递减：`gap = settlement_reaction_group_gap_seconds * pow(0.92, n)`，封底 40%。
  ——**越到后面越快**，这是 Balatro 长连锁不烦人的真正原因，也顺带解决长局时长问题。
- 表字段：`settlement_chain_pitch_step`、`settlement_chain_pitch_cap`、
  `settlement_chain_gap_decay`、`settlement_chain_gap_floor`。
- 音频落在 `CatCafeAudioFeedback`，它已经是集中入口。

### P1 — 分级震屏 + 顿帧（补 G3）

按 Balatro 的数据通道思路，但**幅度砍半**（猫咖是治愈基调，不是赌场）：

| 档位 | 触发条件 | 位移 | 时长 | 附加 |
| --- | --- | --- | --- | --- |
| plain | 默认 | 0 | — | 无 |
| linked | `IsLinked` | 1.5px | 0.15s | 无 |
| high | `IsHighValue` | 4px | 0.28s | 顿帧 70ms + 暖光白闪 alpha 0.25 |

- 表字段：`settlement_reaction_{level}_shake_pixels` / `_shake_seconds` / `_hitstop_seconds`。
- 震屏对象是 `designRoot`（16:9 固定根），**不能动 Canvas 缩放**，避免破坏 UI 锁定约束。
- 无障碍：`CatCafeUserSettings` 里加"减少震动"开关，默认开启震动。

### P1 — 联动簇独立飞金币（补 G4）

`BuildPayoutBatches` 保留给 `plain` 组（省时间是对的），
但 **linked 组不进合批**，每簇结算完立刻从簇中心飞一次金币，金额 = 全簇合计。
这样"看见连线 → 立刻收到钱"的因果闭环才成立。

### P2 — 成套坐垫专属表现（补 G5）

`ConnectedSameCount >= chainThreshold` 时，不画 marker 圈，改为：
整块连通区域套一圈**同色描边**（Royal Match 的整组高亮做法）→ 整体缩 0.94 → 弹回 1.06 → 一次性飘出总额。
这是全局唯一"成组"机制，值得一个能被记住的独立演出。

### P2 — 停轴联动预告（补 G6）

逐列停轴时，若刚落定的元素**会在本回合产生联动**，该格落地帧加一声"叮" + 一圈 0.2s 外发光。
最后一列停轴速度 ×0.75（慢一点）。这是 CloverPit / LbaL 的期待感来源，几乎零成本。

### P2 — 速度档位补齐

`SettlementSpeedMultiplier` 已存在，补两件事：
1. 设置里加 **Instant 档**（幸运房东同款），跳过所有演出直接出总额；
2. 回合数 > N 后默认档位自动上调一级（可在设置里关）。

---

## 4. 明确不要做的

- ❌ **不要做三消式的"手动交换 / 消除"表现**。我们没有摆位权，模仿消除动画会让玩家误以为可以操作。
  顺带：开始界面卖点 2「摆放相邻元素赚取金币」本身就夸大了操作权，建议改成「相邻成链，自动结算」。
- ❌ **不要 Ballionaire 式粒子暴风**（火焰/彩虹拖尾）。基调不符，且 16:9 固定 UI 下会挡住棋盘。
  我们的爆发感走「暖光 + 毛绒粒子 + 铃铛/猫叫音层」。
- ❌ **不要给每个基础产出单独飞金币**。现在的合批是对的，别为了"更爽"退回去。
- ❌ **不要在特效里硬编码任何数值**。上面所有参数都必须进 `CatCafeGameConfig.xlsx`，
  遵循 AGENTS.md 的数据驱动约束。

---

## 5. 建议实施顺序

```
P0-A 联动连线        ← 收益最高，直接解决"看不懂"
P0-B 连锁计数+音高    ← 情绪曲线，顺带压缩长局时长
P1-A 分级震屏+顿帧    ← 高潮时刻的记忆点
P1-B 联动簇独立飞币   ← 让因果闭环
P2   成套坐垫 / 停轴预告 / Instant 档
```

P0 两条做完就能明显拉开与"普通 Balatro-like"的差距；P1 之后是打磨。

---

## 参考来源

- [Balatro: Juicy Feedback in a Poker Roguelike — Blake Crosley](https://blakecrosley.com/guides/design/balatro)
- [Guide: Activation Sequence — Balatro Wiki](https://balatrogame.fandom.com/wiki/Guide:_Activation_Sequence)
- [四两拨千斤？——《Balatro》系统简单拆解 — indienova](https://indienova.com/indie-game-development/a-simple-breakdown-of-the-balatro-system/)
- [Symbols — Luck be a Landlord Wiki](https://luck-be-a-landlord.fandom.com/wiki/Symbols)
- [Luck Be a Landlord Mobile Review — TouchArcade](https://toucharcade.com/2023/07/25/luck-be-a-landlord-mobile-review-iphone-ipad-android/)
- [CloverPit on Steam](https://store.steampowered.com/app/3314790/CloverPit/)
- [CloverPit explained — The Escapist](https://www.escapistmagazine.com/cloverpit-explained/)
- [Ballionaire Review — PC Gamer](https://www.pcgamer.com/games/roguelike/ballionaire-review/)
- [Ballionaire Review — VegasSlotsOnline](https://www.vegasslotsonline.com/news/2024/12/30/ballionaire-review-colorful-pachinko-roguelike-is-an-autobonking-delight/)
- [How Does Backpack Layout and Item Positioning Work — Casual Game Guides](https://casualgameguides.com/walkthroughs/backpack-battles/backpack-layout-item-positioning)
- [How To Combine Items in Backpack Battles — The Nerd Stash](https://thenerdstash.com/how-to-combine-items-in-backpack-battles-explained/)
- [Royal Match - The New King from Turkey? — Deconstructor of Fun](https://www.deconstructoroffun.com/blog/2021/3/21/royal-match-the-new-king-from-turkey)
- [Best Roguelike Deckbuilder Games on Steam — Steambase](https://steambase.io/games/best-roguelike-deckbuilder-steam-games)
