# 猫咖玩法配置规范

## 1. 唯一数据源

策划唯一编辑入口：`GameDesign/CatCafeGameConfig.xlsx`。

Unity 运行时文件：`Assets/Resources/GameData/cat_cafe_config.json`。

运行时 JSON 是自动生成文件，不允许手工修改。编辑 Excel 后关闭文件，双击
`Tools/CatCafeConfig/ExportConfig.cmd`。导出工具使用 Python 标准库，不依赖第三方包；
写入 JSON 前会检查主键重复、棋子/道具/繁育/升级引用和关卡权重引用。

## 2. 通用格式

| 行 | 内容 |
| --- | --- |
| 1 | 表标题 |
| 2 | 中文列名，供策划阅读 |
| 3 | JSON 字段名，不要随意修改 |
| 4 | 字段类型：`string` / `int` / `float` / `bool` |
| 5起 | 配置数据 |

带 `enabled` 的行设置为 `FALSE` 后不会导出。

## 3. 工作表职责

| 工作表 | 职责 |
| --- | --- |
| `Settings` | 棋盘尺寸、初始资源、奖励数、结算阈值、繁育参数、局末与局外经济参数 |
| `Rarities` | 品质顺序、中文显示名、颜色 |
| `Elements` | 游戏所有棋子：猫、幼崽、客人、餐饮、用品和员工 |
| `Items` | 经营道具的身份、品质、资源和说明 |
| `Stages` | 关卡回合、目标、品质权重上下文及通关奖励档 |
| `Weights` | 普通/少见/稀有抽取权重 |
| `InitialDeck` | 新一局开始时牌池里有哪些棋子及数量 |
| `Rules` | 所有棋子效果和经营道具机制 |
| `Breeding` | **仅局内育儿窝**：无序父母组合、幼崽、突变。`craft_fur` / `craft_cans` 两列已作废（见 §3.1） |
| `Invite` | **局外呼朋唤友**：谁能把谁请进猫咖，以及要花多少绒毛与罐头（见 §3.1） |
| `Levels` | 猫咪升级消耗与 perk。购买式升级已被亲密度取代（决策 D4/D5），该表当前为空 |
| `Tutorial` | 房东奶奶字条：触发器、文案、聚光目标。`appear_note` 列用玩家能读的话写明这张字条什么时候出现——局外「设置 → 房东奶奶的字条 → 回看字条内容」会原样展示该列与文案，改动时两边口径要一致 |

### 3.1 Breeding 与 Invite 的分工

两张表各管一条解锁轨道，互不引用：

| | `Breeding`（局内） | `Invite`（局外） |
| --- | --- | --- |
| 触发 | 棋盘上育儿窝旁凑齐两只成年猫 | 大厅「猫咪招募」面板点「呼朋唤友」 |
| 语义 | 父母生幼崽 | 已入住的猫出门把朋友请回来（**不是**父母，也不是合成） |
| 产物 | 幼崽落到棋盘上，长大后点亮图鉴 | 直接点亮图鉴，不产幼崽 |
| 消耗 | 无 | `inviter_a` 的绒毛（`fur_a`）＋ 可选 `inviter_b` 的绒毛（`fur_b`）＋ `cans` 罐头 |

`Invite` 列义：

| 列 | 含义 |
| --- | --- |
| `child` | 想请来的新猫，必须是成年品种（`kind=cat`）且全表唯一 |
| `inviter_a` / `fur_a` | 发起邀请的猫与所需绒毛，必填，`fur_a > 0` |
| `inviter_b` / `fur_b` | 第二位邀请者；留空表示一位就够。**越稀有的猫配两位** |
| `cans` | 路上带的罐头 |

两位邀请者允许填同一只猫，运行时会把绒毛要求相加，不会让一份绒毛当两份花。
变异猫（异瞳、德文卷毛）**不进这张表**——它们只能在营业时偶遇，保持稀缺。

> 2026-08-15 改造前，局外解锁复用的是 `Breeding` 的 `craft_fur`/`craft_cans`（"两只猫合成一只猫"）。
> 该玩法已删除，两列在 Excel 里保留数据但中文列名已标注「（作废）」，运行时不再读取。

## 4. Rules 通用规则表

### 4.1 拥有者与触发时机

- `owner_type=element`：棋子自身规则。`owner_key=*` 时用 `source_kinds` 过滤一组棋子。
- `owner_type=item`：玩家持有对应经营道具后规则生效。
- 多个过滤值使用 `|` 分隔，留空表示不过滤。

| trigger | 含义 |
| --- | --- |
| `round` | 计算每个棋子的本回合收益 |
| `modify_income` | 在基础收益后进行加法或乘法修正 |
| `adjacency` | 修改相邻范围，例如加入斜对角 |
| `round_end` | 回合收益全部结算后触发 |
| `on_choose` | 玩家选择棋子后触发 |
| `on_consume` | 一个棋子被消耗后触发 |
| `on_dismiss` / `on_any_dismiss` | 指定棋子离场 / 任意棋子离场后触发 |
| `on_click` / `on_skip` / `on_removal_spent` | 主动使用道具 / 跳过选择 / 使用下班券后触发 |
| `before_round` | 洗名册与转动前触发 |
| `cycle` / `modify_rule_chance` / `modify_target_limit` | 修改周期、概率或动作目标上限 |
| `modify_round_events` / `modify_money_loss` / `modify_dismiss_income` | 修改本轮事件、负金币变化或离场金币 |
| `pool_limit` / `pool_rarity` / `element_enter` | 修改名册数量上限、奖励池品质或进场变形 |
| `prevent_remove` / `suppress_rules` | 阻止移除或抑制指定负面规则 |
| `rarity_weights` | 抽奖励前修正品质权重 |
| `reward_options` | 修正奖励选项数量 |
| `stage_deadline` | 关卡到期且金币不足时触发 |

### 4.2 原子操作

除基础的 `income`、`add`、`multiply` 外，当前执行器还支持概率/随机收益、生成、
随机生成、按来源再生、历史带回、变形、移除、免疫、永久成长、周期缩短、存值、
奖励品质改写、目标倍率、规则抑制等原子。完整列表由工作簿「说明」页根据当前启用
Rules 维护，并由 `Tools/CatCafeConfig/audit_rules.py` 对照控制器实际分支；不得再在文档
里维护一份与运行时脱节的手工白名单。

新增现有原子操作的组合只需要加规则行；只有出现全新原子操作时才扩展 C# 执行器。

### 4.3 计数、比较与数值

计数范围除相邻、全盘、名册和回合计数外，还包括角落/左右列、同名最大数量与最大
连通组、实例营业次数、道具持有次数、道具累计值、清理券数量等。完整列表同样以工作簿
「说明」页和 `audit_rules.py` 的实测结果为准。

比较符：`always`、`eq`、`ne`、`ge`、`gt`、`le`、`lt`、`modulo_zero`。

```text
收益 = base_value
     + floor(primary / divisor) × primary_factor
     + secondary × secondary_factor
     + primary × secondary × cross_factor
```

规则按 `priority` 从小到大执行。加法通常放在乘法前；`once_per_round=TRUE`
可用于“双层托盘”一类每回合只生效一次的效果。

## 5. 资源引用

`Elements.asset` 和 `Items.asset` 填 Resources 相对名，不含扩展名。棋子资源仍从
`Assets/Resources/CatCafe/` 加载。

TA 的同名素材替换接口保持不变：`Assets/Resources/CatCafe/InGameUI/`。

文件包括 `background.png`、`book-grid.png`、`buff-panel.png`、`goal-banner.png`、
`coin.png`、`clock.png`、`settings.png`、`start-button.png`、`start-label.png`。
导入器及运行时按名称加载逻辑不属于玩法配表，本次重构没有修改。

## 6. 稳定性约束

- `key` 会进入存档，发布后不得改名；显示名可以改。
- 百分比使用小数，例如 5% 填 `0.05`。
- `InitialDeck.element_key`、规则拥有者、繁育父母/幼崽、呼朋唤友的 `child`/`inviter_*` 和升级猫键必须存在于 `Elements`。
- 加 `Invite` 行时自查解锁链不能死锁：`inviter_a`/`inviter_b` 要么是初始猫（`unlock=base`），要么本身也能被别的行请到。
- `Stages.rarity_context` 必须存在于 `Weights.context`。
- 先导出成功，再提交 Excel、JSON 和代码。
