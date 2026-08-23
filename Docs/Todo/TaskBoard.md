# 任务板 · 猫咖营业计划

> 2026-08-11 更新：旧 CSV 分工和“效果仍硬编码”的条目已经失效。当前唯一数据源是 `GameDesign/CatCafeGameConfig.xlsx`，规则由 `Rules` 页驱动；最新格式见 `Docs/Design/DataTableSpec.md`。

> **给后来的 agent / 同事**：这是项目唯一的任务事实源。接手工作前先读本文件 +
> [readme.md](../../readme.md) 的文档索引。
>
> **维护规则**
> 1. 开始做某任务：把它移到「进行中」，标注执行者与日期（agent 写会话日期即可）。
> 2. 完成：移到「已完成」，一行写清做了什么、验收是否通过；有偏差必须注明。
> 3. 新发现的问题/技术债：加到对应分区，给出处（文件:行号 或 文档章节）。
> 4. 不确定的设计问题**不要自行拍板**——加到「待决策」并停在那里。
> 5. 任务 ID 永不复用；新任务顺延编号。
> 6. **协作红线（对 AI agent）**：只做加法。修改/删除**其他同学写的既有文件**
>    （模版、素材、场景、配置）一律先进「待决策」，由文件归属人确认后才能执行。
>    文件归属用 `git log -- <path>` 查。
>
> 行号引用基于 `main @ be8ffcc`，代码变动后以符号名为准。

---

## 进行中

（空）

---

## 术语对照与分工（2026-08-08 A/B 对话定案）

| 团队用语 | 本项目落点 | 归属 |
| --- | --- | --- |
| 棋子 / 棋子表 | 局内元素；`Assets/Resources/GameData/cats.csv`（+`items.csv`） | 效果列=B（房东策划），外显/收集列=A |
| 合成表 | 工作簿 `Breeding` 页（仅局内育儿窝）；局外解锁走 `Invite` 页 | A（局外/收集） |
| 整备界面 | `CatCafeHome` 场景（实景大厅+图鉴+猫咪小窝；成就=M3） | A |
| 效果与外显解耦 | cats.csv 的 effect 列组 vs 身份列组（同表不同列，天然无冲突） | — |
| 流程 | 开始（CatCafeStart）→ 整备（CatCafeHome）→ 局内（CatCafeDemo） | 已接线 |

B 写表须知：只动 `effect_id/p1/p2/bonus_id/b1/b2/rule_text` 列；招财猫式强绑定=向程序申请专属 effect_id。
注意：**效果解耦不适用于收集身份**——品种的 `color_gene` 参与合成表，改名/改基因要同步 breeding.csv。

## 待办 · 工程

依赖链：M0 全部完成并通过回归 → M1 → M2。M0 之内按编号顺序做。

### 前置（进 M0 之前）

| ID | 任务 | 说明 / 验收 |
| --- | --- | --- |
| CHORE-1 | Editor 可进 Play Mode 验证 | 2026-08-08 agent 尝试 batchmode 验证：编辑器正开着项目（占用锁）无法 CLI 验证；编辑器当天可正常操作包配置，[UnityProjectContext.md](../AI/UnityProjectContext.md) 记录的包解析问题疑似已消失。**待人工确认**：编辑器 Console 无报错 + 能进 Play Mode 即关闭此项 |

### M0 / M1 / M2 —— 2026-08-08 已由 agent 实装大部（见已完成 UNITY-1），剩余如下

| ID | 任务 | 状态 / 说明 |
| --- | --- | --- |
| M0-3 | effect_id 参数化重构：`CalculateEvents()` key-switch → 枚举分发 | **待做（有意推迟）**——为最小化对局内控制器（橘猫）的侵入，本次联动未做该重构；局内 27 元素效果仍硬编码，cats.csv 的 effect 列暂为"策划意图记录"。B 开始批量出新棋子前必须完成本项。单独 commit + 逐元素回归 |
| M0-V | Editor 人工验收 | 编辑器 Console 无报错；Play `CatCafeStart` 走通 开始→整备→局内→打烊返回；杀进程重开罐头/图鉴不丢（存档在 `persistentDataPath/catcafe_save.json`） |
| M2-3 | ~~`BuildBoard()` 加权抽样支持 board_weight perk~~ | **已被 D5/D6 取代**（2026-08-13）：加权代码实际已生效（`CatCafeGameController:502`），但设计上改为随行猫限定 → 并入 INTIMACY-4 |
| M1-4b | 独立 RunSummary 结算页（当前折算明细并入现有结果面板文案） | 待做，低优先 |
| M3 | 成就 / 每日营业 / 装修 / 特殊配方条件（高级育儿窝、特殊猫粮元素实装） | 未排期 |

### 亲密度系统 MVP（2026-08-13 拍板 D4–D7，见 [MetaGameDesign §6](../Design/MetaGameDesign.md)）

| ID | 任务 | 说明 |
| --- | --- | --- |
| INTIMACY-1 | intimacy 表 + 存档字段；levels.csv 购买列冻结 | 存档 SaveDto 加 intimacy，version 升级；绒毛回归纯解锁材料（2026-08-15 已由 META-1 落地：局内掉毛产出、局外呼朋唤友消耗） |
| INTIMACY-2 | 三个获取打点：同行完局 +1 / 首次发现 +2 / 繁育出后代 +1 | 打点位都在现有代码（ConvertRunToCans / Discover / SettleBreeding） |
| INTIMACY-3 | 图鉴改造：升级按钮 → 亲密度进度条 + `dex_hint` 分级揭示 | `CatCafeHomeController.RebuildDex`；受 TECH-2 影响，注意顺序 |
| INTIMACY-4 | 随行猫：Home 开始营业前指定一只；`board_weight` 改为随行限定 | `BuildBoard` 加权代码已在（`CatCafeGameController:502`），改判定条件 |
| INTIMACY-5 | 大厅喂食：点猫 → 喂食（罐头，表配置价格与冷却）→ 亲密度 +1 | 罐头 sink；依赖 TECH-2 的程度待评估 |
| INTIMACY-6 | RunSummary 结算页展示本局亲密度变化 | 与 M1-4b 合并实施——回家仪式与亲密度同一条流水线 |

### 世界观与体验（2026-08-13 拍板 D8，见 [WorldAndExperience.md](../Design/WorldAndExperience.md)）

| ID | 任务 | 说明 |
| --- | --- | --- |
| ~~EXP-1~~ | ~~文案全量 pass：房租单/失败页/打烊确认/Toast 等按 §6 口吻规范重写~~ | **08-14 完成**：术语表拍板并落地（§6.1）。局内 HUD/结算页/Toast、大厅、开始页、Excel 的 `name`/`rule_text` 全量过一遍；余下裸机制词只留在 `design_note` 与代码注释里 |
| RUN-1 | 局内挂起存档：回合边界检查点 `catcafe_run.json`，开局页「店还开着——继续营业」 | §5.1；存未决三选一选项 key 防刷；RNG 可刷为已接受取舍 |
| ~~SAVE-1~~ | ~~多档存档：主界面选择/新建/删除小店~~ | **08-14 完成**：`CatCafeSaveSlots` 管档位（`meta_save_slot_count` 默认 3），`CatCafeMeta` 只认当前档路径；旧单档 `catcafe_save.json` 首次启动自动搬进 1 号店。**RUN-1 落地时注意**：`catcafe_run.json` 也要按档拆成 `catcafe_run_{n}.json` |
| EXP-2 | 翻门牌转场（营业中/歇业）+ 打烊账本 | 与 M1-4b / INTIMACY-6 同批（回家仪式三件套），兼加载转场 P0 |
| EXP-3 | 房东字条/立绘视觉 | 美术任务，不阻塞；字条可先用 modal-main-v2 裁切占位 |

### 新手教程 · 房东奶奶字条（2026-08-13 文案定稿，见 [WorldAndExperience §8](../Design/WorldAndExperience.md)）

前提：主角是店长猫；房东=和蔼老太太「房东奶奶」。字条每张只出现一次（存档记已读）。

| ID | 任务 | 说明 |
| --- | --- | --- |
| TUT-1 | 字条组件：纸艺字条弹层 + 可选聚光高亮 + Tutorial 触发表 + 已读存档标记 | 弹层复用 CatCafeOverlay；触发表进 xlsx（trigger_key / 文案 / 聚光目标） |
| TUT-2 | 主线 9 张字条接入触发点（§8.1） | 触发点分散在 Home/Start/Game 三控制器；第 3 张依赖「首局首转保联动」待决策（§8.4，A/B 店长定） |
| TUT-3 | 情境字条 9 张（§8.2）+ 设置里「跳过全部」「重新阅读」入口 | 跳过/重读文案已定稿（§8.3） |

### 局外审计遗留（2026-08-15 发现，META-1 已随呼朋唤友改造修掉一部分）

| ID | 任务 | 出处 / 说明 |
| --- | --- | --- |
| META-2 | **亲密度零产出**：`AddIntimacy` 全项目无调用方，里程碑恒为 ❤1 | 图鉴卡片的 `dex_flavor` / `dex_hint` 两列数据因此永远读不到，永远显示占位文案。等同 INTIMACY-2，优先级应提到 INTIMACY 组最前 |
| META-3 | **perk 通道整条是死的**：`PerkValue` 挂在亲密度里程碑上（恒为 1），`Levels` 页为空 | `mutation_up` / `material_bonus` / `board_weight` 都不生效。`CatCafeMeta.TryLevelUp` 与 `LevelRow.cost_cans/cost_fur` 是 D5 已作废的死代码，可随 INTIMACY-1 一并清掉 |
| META-4 | **`ConsumeNewCatHomeArrival()` 与 `ui_home_collection_unlock_bubble` 无调用点** | "有新猫住下"的回家提示从来不会触发；`main_08_new_cat` 字条也因此打不上。`CatCafeMeta.cs` / `CatCafeHomeController.cs` |
| META-5 | **`Breeding.requires` 列运行时从不读取** | special 档四条（英短/金渐层/布偶/长毛）设计上要挂「高级育儿窝 / 特殊猫粮」，现在局内可直接凑普通配对产出。并入 M3 的特殊配方条件元素 |
| META-6 | **绒毛没有常驻显示位** | 大厅 HUD 只有罐头与图鉴进度；掉毛只在局末结算页有一行汇总，平时看不到存量。依赖 ART-5 的绒毛 icon |
| META-7 | **`Stages` 页 6 关目标全为 1**（调试值） | 与 MetaGameSpec §1/§9 写的 35/85/175 不符。罐头折算含「剩余金币 ×20%」，该状态下收益会异常高——调平衡前须先确认目标值归属人是否有意为之 |

### 技术债与验收（2026-08-13 会话汇总）

| ID | 任务 | 出处 / 说明 |
| --- | --- | --- |
| TECH-1 | 开局页 StartPanel 接纸艺（调用点标注 PaperButtonRole/PaperSurface 即可，派发机制已就绪） | `CatCafeStartController.BuildUi` |
| TECH-2 | Home 大厅迁移到 CatCafePresentation：去 Legacy Text 分叉，接入音效与金币特效。**P0 场景吃不到纸艺皮肤的根因**，也是大厅实景导航改造的前置 | `CatCafeHomeController`（866 行，0 处引用 Presentation） |
| TECH-3 | BuildLegacyUi 参考分辨率 1600×900 → 1536×864；三场景 pixelPerfect 统一（Start/Home=true，Demo=false） | `CatCafeGameController` |
| TECH-4 | 三选一卡片槽位进 Settings 表 + MakeText 节点改语义命名，让美术可在 Play Mode 调参后回填 | `PlaceOnCard` 常量、`CatCafePresentation.MakeText` |
| TECH-5 | `Resources/CatCafe/` 根目录 16 张图（大厅家具/旧猫图）未被任何 importer 覆盖：DXT 有损压缩 + Point 过滤，需补根目录导入规则 | 会话纹理审计 |
| TECH-6 | 超采样素材出小尺寸版：`InGameUI/coin.png` 1254px 显示 54px（5% 缩放）等 | 会话纹理审计 |
| TECH-7 | 实机验收：BGM 实听（音量/交叉淡入/切场景续播）、诞生特效、设置面板、换一批只刷卡、选牌飞入棋子盒 | 0.2.0 包 |
| TECH-8 | **闪屏禁令**：开启 Unity 闪屏会导致独立包启动即崩（根因见提交 ce9250a），构建入口已强制关闭并告警。除非根治烹制数据，勿重新勾选 | `CatCafeBuild.EnforceSplashDisabled` |

### M3 · 二期（未排期，勿动）

每日营业 / 访客事件 / 镇店猫 / 成就 / 装修 / 特殊配方（英短、布偶…）/ aura_variant / 局内断线重连。
详见 [MetaGameDesign §9](../Design/MetaGameDesign.md)。

---

## 待办 · 美术

规格统一见 [DataTableSpec §6](../Design/DataTableSpec.md)（256×256 PNG / 透明底 / 点过滤 / kebab-case / 放 `Assets/Resources/CatCafe/`）。

| ID | 任务 | 优先级 |
| --- | --- | --- |
| ART-1 | `orange-white-cat.png` 橘白猫（修复与三花猫撞图） | P0，阻塞 M1 验收 |
| ART-2 | `odd-eyed-cat.png` 异瞳猫（白底异色瞳） | P0 |
| ART-3 | `can-icon.png` 罐头货币 icon | P0 |
| ART-4 | 局内 9 张历史欠账：coffee / pastry / milk / teaser / cat-tree / barista / tip-jar / lucky-bell / adoption-poster | P1，不阻塞 |
| ART-5 | Home 大厅背景 1600×900、展示位底座、绒毛 icon | P2，M2 前就位 |
| ART-6 | rare 稀有度徽章正式版：`PaperSkin/badge-orange.png` 与 `badge-orange-v2.png` 现为程序推导占位（badge-brown 加饱和），文件名不变直接替换即生效 | P1 |

---

## 待办 · 策划

| ID | 任务 | 说明 |
| --- | --- | --- |
| PLAN-1 | 审阅并补全 `cats.csv` 文案列（rule_text / dex_hint / dex_flavor） | 写表规则见 DataTableSpec §0–§1；M0-2 时程序会先填功能列 |
| PLAN-2 | ~~定案 levels.csv 全品种的 perk 分配与消耗曲线~~ | **已被 D4/D5 取代**（2026-08-13）：购买式升级作废，改为亲密度 → 见 PLAN-4 |
| PLAN-4 | 亲密度里程碑表定案 + 3–5 只基础猫的 ❤5 横向特性与故事文案 | D7 内容闸门：里程碑全品种统一，特性只做基础猫；特性设计规范见 MetaGameDesign §6.3（禁数值型） |
| PLAN-3 | 变异率 / 绒毛掉落率 / 呼朋唤友消耗 / 罐头折算系数首轮手感调参 | M1 完成后进行 |

---

## 待决策

设计决策 D1–D9 已拍板（见 [MetaGameDesign §1](../Design/MetaGameDesign.md)；D4–D7 为 2026-08-13 亲密度方向，D9 为 2026-08-15 呼朋唤友方向，均由店长定案）。以下为**协作红线**项——涉及其他同学的文件，需归属人确认：

| ID | 事项 | 归属人 | 说明 |
| --- | --- | --- | --- |
| DEC-1 | SampleScene 是否移出 Build Settings 并删除 | chuanzhou（初始配置引入） | Unity 模板场景，当前无玩法引用；2026-08-08 agent 曾误删已还原 |
| DEC-2 | `Assets/Art/Board/Start_BG/UI.png` 去留 | 橘猫（8-2 选关界面提交引入） | 当前 cat cafe 代码无引用，但可能是保留素材；agent 曾误删已还原 |
| DEC-3 | `readme.md` 是否更新为猫咖内容 | chuanzhou（团队模版作者） | 现内容仍是初代变脸解谜概念；更新提案已写好放在 [Docs/ProjectOverview.md](../ProjectOverview.md)，认可即可整体替换 |

---

## 已完成

| 日期 | ID | 内容 |
| --- | --- | --- |
| 2026-08-15 | META-1 | **猫咪小窝整条线重做（店长指令，拍板 D9）**。①**绒毛掉落**：每波次盘面上每只成年猫按 `meta_fur_drop_chance`（20%）各判一次掉自己品种绒毛，每波次即时落盘（`CatCafeGameController.SettleFurDrops`）——修掉「`AddFur` 全项目零调用 → 小窝所有按钮永远灰着」的死锁。②**呼朋唤友取代两只猫合成**：新增 Excel `Invite` 页（12 条）与 `CatCatalog.InviteRow`，面板改为「攒够 1~2 位已入住伙伴的绒毛 + 罐头 → 让它们出门把新朋友请回来」，按钮文案「准备育儿窝」→「呼朋唤友」；解锁演出**沿用原图鉴揭晓页未动**。局内育儿窝保留不变。`Breeding.craft_fur/craft_cans` 作废（Excel 列名已标注、领域模型已摘除）。③**罐头局末折算实装**：文档写了但 `AddCans` 从来没人调；按 MetaGameDesign §7 公式接进 `SettleRunResult`，四个结局分支统一追加收益行。④**房东奶奶字条 +2**：`context_fur_drop`（局内首次掉毛，聚光那只猫）、`context_fur_stock`（带着绒毛回大厅）；`context_breeding` 改写为 `context_invite`。全部数值与文案走表（Settings +18 键）。**待人工验收：编辑器无 Library，本次无法编译验证；需在 Unity 里跑一次导出并进 Play Mode** |
| 2026-08-13 | UNITY-2 | **局内 UI 体验批**：CatCafeOverlay 统一弹层出入场（淡入缩放/点空白关/Esc 分层）；三选一入场错开+选中演出+券数常驻+选牌飞入棋子盒；设置面板（音乐/音效音量 5 档、结算速度 3 档、打烊二次确认）；棋子盒同类合并 ×N；卡片按 reward-card 美术槽位对位；纸艺派发角色化（PaperSurface/PaperButtonRole，素材缺失告警回退）；稀有度徽章表驱动（Rarities.badge 列）。提交散见 08-13 git log |
| 2026-08-13 | UNITY-3 | **BGM 系统**：`Assets/Audio/bgm` 中文名 mp3 迁至 `Resources/CatCafe/Bgm` kebab-case；Settings 表新增 bgm_* 键（场景歌单/音量/交叉淡入/乱序/bgm_enabled 总开关）；CatCafeMusicPlayer 跨场景双源交叉淡入淡出、失焦续播不跳曲；音量偏好存 PlayerPrefs（与进度存档分离） |
| 2026-08-13 | UNITY-4 | **打包管线与 0.2.0 闪退**：CatCafeBuild 出包入口（版本常量/开发版带堆栈/闪屏强制关/自动删 BurstDebug）。闪退根因：ProjectSettings 被多版本编辑器序列化后，闪屏渲染路径（DrawSplashScreenBackground→Material::SetTextureInternal）解引用坏 PPtr；关闭闪屏绕过（ce9250a），构建禁令防回归（TECH-8）。0.2.0 带音乐完整包实测启动通过，7z 已重压 |
| 2026-08-13 | UNITY-5 | **育儿窝诞生演出**：小猫弹出+光环（首次发现金色双环），首次发现即时 SaveNow 保证局外图鉴联动可靠（a8db969）。**待 TECH-7 实机验收** |
| 2026-08-08 | — | 仓库同步至 `origin/main @ be8ffcc`；本地 Html demo v2 分支完整备份至 `~/PycharmProjects/many_face_html_demo_v2`（含 LFS 对象与未跟踪构建产物） |
| 2026-08-08 | DOC-1 | [CatCafeGameDesign.md](../Design/CatCafeGameDesign.md)：局内玩法逆向整理（27 元素/13 物品/繁育/经济全量） |
| 2026-08-08 | DOC-2 | [MetaGameDesign.md](../Design/MetaGameDesign.md)：局外系统定案，D1/D2/D3 决策拍板 |
| 2026-08-08 | DOC-3 | [MetaGameTechDesign.md](../Design/MetaGameTechDesign.md) + [DataTableSpec.md](../Design/DataTableSpec.md)：技术方案与写表接口 |
| 2026-08-08 | CHORE-0 | ~~readme.md 重写~~ **已撤销**——readme 是 chuanzhou 的团队模版，改动转为提案 [Docs/ProjectOverview.md](../ProjectOverview.md)，进 DEC-3 待决策 |
| 2026-08-08 | — | 项目现状总览新增：[Docs/ProjectOverview.md](../ProjectOverview.md)（只增不改原则） |
| 2026-08-08 | PROTO-1 | **HTML 局外交流稿** `Prototype/meta-cafe.html`（单文件 898KB，双击可开）：大厅/图鉴13格/繁育室/升级/快速营业模拟器/调参面板；数据区=CSV 镜像；localStorage 存档。闭环模拟验证：前3局发现4只✓、定向合成生效✓、集齐(除异瞳)中位7局、三关通过率 100/99/84% |
| 2026-08-08 | — | 设计修正：新增**繁育掉毛**规则（双亲各掉1绒毛）——模拟发现原设计下基础猫绒毛无来源、tier1 定向合成死锁；图鉴格数勘误 14→13。已回写 MetaGameDesign §5/§3.5 与 DataTableSpec §2 |
| 2026-08-08 | DOC-4 | [MetaGameSpec.md](../Design/MetaGameSpec.md)：局外系统**当前实装版**策划案（简明版，给策划/美术上手；在线版 https://claude.ai/code/artifact/57b211f0-5c8e-4dca-9581-d5d1cd90a7eb ） |
| 2026-08-08 | TABLE-1 | **六张种子表落地** `Assets/Resources/GameData/`：cats（43 条含新品种与幼崽）/ breeding（14 配方含变异与合成消耗）/ levels / stages / weights / items。A 填外显列、B 填效果列即可并行开工 |
| 2026-08-08 | UNITY-1 | **HTML 稿转 Unity + 局内外联动**（店长指令）。新增：`Core/CatCatalog`（CSV 解析+跨表校验）、`Persistence/CatCafeMeta`（JSON 原子写存档：图鉴/等级/罐头/绒毛/收钱罐/统计）、`Meta/CatCafeHomeController`（整备界面：实景大厅游走猫+家具+收钱罐、图鉴含剪影与升级、繁育室图形配方定向合成）、`CatCafeHome.unity`。局内接线（改 橘猫 的 CatCafeGameController，外科手术式）：繁育查表+变异+掉毛+发现打点（M1-1/2）、奖励池按图鉴门控（M1-3，D1 生效：燕尾服/奶牛/三花移出基础池）、罐头折算+失败保底（并入结果面板）、「打烊」HUD 按钮与结果面板「返回猫咖」。流程接线：Start→Home→Demo→Home，Build Settings 加入 CatCafeHome。**待 M0-V 人工验收（编辑器当时被占用，无法 CLI 编译验证）** |
| 2026-08-08 | — | 触及既有文件备案（店长联动指令授权）：`CatCafeGameController.cs`（橘猫）、`CatCafeStartController.cs`（橘猫，跳转目标一行）、`EditorBuildSettings.asset`（加 CatCafeHome 一条，未删任何项） |
| 2026-08-08 | PROTO-2 | 交流稿 v2（店长反馈迭代）：①大厅实景化——已收集猫游走/家具陈列/**收钱罐放置收益**（jarRate 可调，上限50）；②图鉴 13→18（新增英短/金渐层/布偶/长毛/德文卷毛二代链，原型用普通配对，正式版挂特殊条件）；③繁育室重做为图形配方式+三步说明；④修同品种配对合成的双倍绒毛结算。设计回写 MetaGameDesign §8。模拟：三关通过率 100/100/82%，二代链为长线内容（自动模拟 60 局集齐 3/30 档，真人凑对会显著快于此） |
