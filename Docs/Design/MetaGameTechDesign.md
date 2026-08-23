# 猫咖营业计划 · 局外系统技术设计

> 2026-08-11 更新：本文中的旧 CSV 与 `BuildDefinitions()` 描述仅保留为历史背景。当前实现以 `GameDesign/CatCafeGameConfig.xlsx` 为唯一数据源，运行时读取统一导出的 `cat_cafe_config.json`。最新接口见 [DataTableSpec.md](DataTableSpec.md)。

> 配套文档：[MetaGameDesign.md](MetaGameDesign.md)（玩法定案）、
> [DataTableSpec.md](DataTableSpec.md)（表接口）、
> [CatCafeGameDesign.md](CatCafeGameDesign.md)（局内现状）。
> 所有行号引用基于 `main @ be8ffcc` 的 `Assets/Scripts/CatCafe/CatCafeGameController.cs`。

---

## 1. 现状盘点（技术事实）

| 项 | 现状 |
| --- | --- |
| 引擎 | Unity 6000.5.3f1，URP，2D，新 Input System（UI 走 `InputSystemUIInputModule`） |
| 场景 | `CatCafeStart`（开始页）、`CatCafeDemo`（局内）、`SampleScene`（模板残留，仍在 Build Settings） |
| UI | 全部运行时 uGUI 代码构建，**零 prefab**；参考分辨率 1600×900；字体 `LegacyRuntime.ttf` |
| 局内代码 | 单文件 `CatCafeGameController.cs` 2207 行：数据表、状态机、结算、UI 构建全在里面 |
| 元素数据 | 硬编码在 `BuildDefinitions()`（L253–298），27 元素 + 13 物品 |
| 美术加载 | `Resources.Load<Sprite>("CatCafe/" + asset)`（L1286），点过滤，256×256 PNG |
| 存档 | **完全没有**。无 PlayerPrefs、无序列化，`ResetGame()` 全清 |
| 版本管理 | Git LFS 接管所有 png/zip 等二进制 |
| 已知问题 | `Docs/AI/UnityProjectContext.md` 记录包解析报错，Editor 完整验证 pending |

对局外系统的含义：

1. **地基先行**：存档层与共享数据层是零，必须先建（M0）。
2. **数据必须出仓**：图鉴/猫咪招募/展示间都要读同一份猫咪目录，`BuildDefinitions()`
   不抽出来就会复制三份。
3. **UI 风格延续**：局外界面继续用运行时 uGUI 构建，与现有代码风格一致，不引入
   UI Toolkit / prefab 工作流（避免双轨制）。

---

## 2. 场景架构与流程

### 2.1 场景划分

```
CatCafeStart.unity  （保留）开始界面 —— 按团队定案流程「开始 → 整备 → 局内」（2026-08-08 A/B 对话）
CatCafeHome.unity   （新建）整备界面（局外）：实景大厅 + 图鉴 + 猫咪招募 + 开始营业
CatCafeDemo.unity   （保留）局内：现有玩法，改造点见 §6
SampleScene.unity   （是否移出 Build Settings 待团队决策 DEC-1）
```

Build Settings 顺序：`CatCafeStart`(0) → `CatCafeHome`(1) → `CatCafeDemo`(2)。

图鉴和猫咪招募**不单独开场景**——它们是 Home 内的全屏 overlay（复用局内 `NewOverlay`
的面板模式），避免场景切换开销和状态传递复杂度。

### 2.2 流程图

```
[启动] → Bootstrap(RuntimeInitializeOnLoadMethod)
             │ 加载表 → CatCatalog（只读）
             │ 读盘   → MetaState
             ▼
        CatCafeHome ──开始营业──▶ CatCafeDemo
             ▲                        │
             │   RunSummary 结算页     │
             └──返回猫咖（写盘）────────┘
```

### 2.3 Bootstrap（无场景依赖）

```csharp
public static class GameBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        CatCatalog.LoadFromResources();   // 解析 GameData/*.csv，失败时抛带行号的异常
        SaveService.Load();               // 生成/迁移/读取 MetaState
    }
}
```

选 `RuntimeInitializeOnLoadMethod` 而不是 DontDestroyOnLoad 的 GameRoot 物体：
两者等价，但前者让**任意场景都能直接进 Play Mode 调试**（编辑器里直接开
CatCafeDemo 也能跑），这对无 prefab 的纯代码工程尤其重要。服务全部是静态类 +
纯 C# 对象，没有 MonoBehaviour 生命周期问题。

需要 Unity 生命周期回调的唯一场合是退出时写盘，由 Home/Run 场景的控制器在
`OnApplicationPause/OnApplicationQuit` 里调 `SaveService.Flush()`。

---

## 3. 代码模块划分

```
Assets/Scripts/CatCafe/
  Core/
    CatCatalog.cs        表加载 + 只读查询（元素定义、配方、等级、关卡、权重）
    ElementDef.cs        元素定义（含 effect_id + 参数，替代内嵌 Definition 类）
    BreedRecipe.cs       局内育儿窝配方（含变异）；局外解锁另见 Invite 表
    TableParser.cs       CSV 解析（UTF-8/BOM 容错，错误带 文件:行:列）
  Persistence/
    SaveService.cs       JSON 原子读写 + 版本迁移
    MetaState.cs         运行时存档对象：图鉴、等级、罐头、绒毛、统计
  Meta/
    HomeController.cs    Home 场景根（运行时构建 UI，风格同现有代码）
    DexPanel.cs          图鉴 overlay
    InvitePanel.cs       猫咪招募 overlay（呼朋唤友）
    RunSummaryData.cs    局末结算的传递对象（静态字段跨场景传递即可）
  Run/
    CatCafeGameController.cs   现文件迁入，按 §6 改造
    CatCafeRewardFx.cs         不动
```

**重构纪律**：M0 只做"数据出仓 + 打点"，**不重写** UI 构建器、不动老虎机/特效、
不引入 DI 框架。2207 行的控制器保持单文件没问题——它的问题从来不是行数，
而是数据被锁在里面。

---

## 4. 数据层

### 4.1 管线

```
策划编辑 Assets/Resources/GameData/*.csv   （UTF-8，规范见 DataTableSpec）
        │  Unity 作为 TextAsset 导入（.csv 默认支持）
        ▼
CatCatalog.LoadFromResources()  启动时一次性解析为不可变对象
        │  任何格式错误 → 直接抛异常并指明 文件:行:列（宁可启动失败，不可带病运行）
        ▼
局内/局外统一从 CatCatalog 查询
```

不用 ScriptableObject 的理由：SO 需要编辑器导入工具链，而表是策划高频改的东西，
CSV + 启动解析的迭代路径最短（改完表进 Play Mode 即生效）。后续若表膨胀再上
编辑器校验菜单（`CatCafe/Validate Tables`，二期）。

### 4.2 效果参数化（本次唯一的行为层重构）

现状 `CalculateEvents()`（L467–547）按 `element.Key` 硬编码 switch。改为按
`(effect_id, p1, p2)` 分发，key 只做身份标识：

```
现状:  else if (element.Key == "coffee") amount = guests * 2;
改后:  case EffectId.PerAdjacentGuest: amount = guests * p1; break;
```

- 现有 27 元素的效果归纳为 **18 个 effect_id**（映射表见 DataTableSpec §3）。
- 收益：策划加"复用现有效果、只换数值/美术"的新猫**零代码**；只有全新机制才找程序。
- 风险控制：改造完成后用现表数值回归（M0 验收 = 与现版行为一致）。

### 4.3 遗传查询

`KittenFor()`（L603–618）替换为配方表查询：

```
LookupBreedResult(parentAKey, parentBKey):
  1. 无序对标准化（key 字典序）
  2. 命中 breeding.csv → 掷变异（mutation_rate，受双亲等级 perk 修正）
     → 变异则返回 mutation_child，否则返回 child
  3. 未命中 → 返回 null（该配对不产仔；替代现在的"混色幼崽"兜底）
```

注意行为变更：**未配置的配对不再产仔**（现在任何两只成年猫都能出混色幼崽）。
育儿窝判定处（L563）要相应改为"取到的两只亲代查表无果时跳过该育儿窝"。

---

## 5. 存档系统

### 5.1 Schema（v1）

```json
{
  "version": 1,
  "cans": 120,
  "dex": {
    "cowCat": { "n": 4, "src": "breed", "lv": 2, "first": "2026-08-08T21:00:00Z" }
  },
  "fur": { "blackCat": 3, "whiteCat": 1 },
  "display": ["calicoCat", "cowCat", null],
  "stats": { "runs": 12, "wins": 3, "bestStage": 3, "totalBreeds": 21 },
  "pendingRun": null
}
```

- `dex` 键 = `cats.csv` 的 key；`n`=发现次数，`lv`=等级（发现即 1）。
- `pendingRun` 预留局内断线重连（二期，MVP 不做局内快照）。
- 键名从简（`n`/`src`/`lv`）——明文 JSON，单机不做加密/防篡改（不值得）。

### 5.2 读写策略

| 项 | 方案 |
| --- | --- |
| 路径 | `Application.persistentDataPath/catcafe_save.json` |
| 原子写 | 写 `*.tmp` → `File.Replace`（防写一半崩溃产生坏档） |
| 坏档兜底 | 解析失败 → 备份成 `*.corrupt.<timestamp>` → 新建空档（不要静默覆盖） |
| 版本迁移 | `version` 整数 + 顺序迁移函数链 `v1→v2→…`；迁移前备份 `*.bak` |
| 写盘时机 | 首次发现（立即）、局末结算、升级/合成、`OnApplicationPause(true)`/`Quit` |
| 频率控制 | `MarkDirty()` + 上述时机 Flush，不做每帧/定时写 |

### 5.3 MetaState API（草案）

```csharp
static class MetaState
{
    bool  IsDiscovered(string key);
    int   GetLevel(string key);                 // 未发现 = 0
    void  RecordDiscovery(string key, DiscoverySource src, out bool isFirst);
    int   Cans { get; }  void AddCans(int v);  bool SpendCans(int v);
    int   GetFur(string key);  void AddFur(string key, int v);  bool SpendFur(...);
    IReadOnlyList<string> UnlockedRewardKeys(); // 供奖励池过滤
    event Action<string>  OnFirstDiscovery;     // 局内发现演出订阅
}
```

---

## 6. 局内改造点清单（逐条，带现行行号）

| # | 位置 | 改造 | 规模 |
| --- | --- | --- | --- |
| 1 | `BuildDefinitions()` L253 | 删除，改从 `CatCatalog` 取只读定义 | 中 |
| 2 | `CalculateEvents()` L467 | key-switch → effect_id 分发（§4.2） | 中，唯一有回归风险的点 |
| 3 | `rewardsByRarity` L143 | 静态数组 → `CatCatalog.RewardPool(rarity, MetaState)` 动态过滤 | 小 |
| 4 | `KittenFor()` L603 | 配方表查询 + 变异掷骰（§4.3） | 小 |
| 5 | `SettleBreeding()` L549 | 产仔处调 `MetaState.RecordDiscovery`；首次发现播放演出（订阅事件，复用 Toast/RewardFx 风格） | 小 |
| 6 | `FinishStage()` L804 / `HandleResultAction()` L839 | 失败与通关路径都先进 **RunSummary 结算页**（罐头明细、绒毛、发现回顾），按钮改「返回猫咖」→ `LoadScene("CatCafeHome")`；"重新开店"保留为第二按钮 | 中 |
| 7 | `BuildBoard()` L353 | 均匀洗牌 → 支持 `board_weight` perk 的加权抽样 | 小，可后置到 M2 |
| 8 | `RewardOptions()` L660 | `round==1` 强塞育儿窝的新手引导保留不动 | 无 |

不改的东西（明确）：老虎机动画、金币特效、UI 布局契约、三阶段数值、物品系统。

---

## 7. 里程碑

| 里程碑 | 内容 | 规模估计 | 验收 |
| --- | --- | --- | --- |
| **M0 地基** | Core + Persistence + 表落地；改造点 1/2/4；`cats.csv` 用现有 27 元素填满 | 1–1.5 天 | 玩法与现版**完全一致**；杀进程数据不丢 |
| **M1 闭环** | 改造点 3/5/6；CatCafeHome 场景（占位 UI）+ DexPanel；折算公式 | 2 天 | 设计文档 §10 的"3 局闭环判据" |
| **M2 养成** | 猫咪招募面板、亲密度（改造点 7）、展示位 | 2 天 | 呼朋唤友/亲密度链路可用 |
| **M3 二期** | 每日营业、事件、成就、装修、特殊配方 | 另行排期 | — |

前置事项（进 M0 前顺手做）：`SampleScene` 移出 Build Settings；删除孤儿资源
`Assets/Art/Board/Start_BG.png`、`Start_UI.png`；重写 `readme.md`（当前描述的还是
变脸解谜项目）。

---

## 8. 风险与明确不做

| 风险 | 应对 |
| --- | --- |
| effect_id 重构引入回归 | M0 独立验收：现表数值下逐元素对照现版行为；改造点 2 单独成 commit |
| CSV 中文编码坑（Excel 存 GBK） | 解析器容错 BOM；规范要求 UTF-8（用 WPS/Numbers/VSCode 编辑）；解析失败报 文件:行:列 |
| "未配置配对不产仔"的手感变化 | 一级表已覆盖基础三色全组合；测试时确认育儿窝空转不会过于频繁，必要时给"无果"配对一个 Toast 提示 |
| 包解析问题未解决（UnityProjectContext 记录） | M0 开工前先在 Editor 里确认工程可进 Play Mode；这是所有验收的前提 |
| 存档被手改 | 接受。单机明文 JSON，不做加密 |

**明确不做**：Addressables（Resources 在此规模完全够用）、服务器/云存档、
UI Toolkit 迁移、prefab 化、局内战斗快照（`pendingRun` 只留字段）、多存档位。
