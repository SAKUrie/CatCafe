# 猫咖 UI 素材清单与重绘建议

> 盘点日期：2026-08-19  
> 固定验收画布：1536×864（16:9）  
> 范围：开始界面、整备大厅、局内界面、共用纸艺控件

## 结论

- 三套主界面的美术语言已经统一，背景、账本、立体书场景、金币和设置按钮不需要整体推翻重绘。
- 当前最需要处理的不是背景质量，而是**烘焙在图片里的业务文案**、**历史版本与正式版本混放**、以及**参考源文件不是真透明源图**。
- 本次不移动、不改名、不删除任何现有 Unity 资源。运行时大量使用 `Resources.Load`，直接整理目录会改变路径；同时项目协作规则要求修改或删除他人既有素材前先确认归属。
- 建议下一轮先制作“去文字、透明底、原尺寸”的非破坏性版本，再从源表添加文字位置和资源路径，最后替换运行时引用。

## 目录职责

| 目录 | 当前职责 | 处理原则 |
| --- | --- | --- |
| `Assets/Resources/CatCafe/StartUI/` | 开始界面运行时图层 | 正式运行时目录；保留路径和 `.meta` |
| `Assets/Resources/CatCafe/HomeUI/` | 大厅运行时图层与三组序列帧 | 正式运行时目录；布局来自工作簿导出数据 |
| `Assets/Resources/CatCafe/InGameUI/` | 局内整画布图层、旧皮肤、特效图 | 先按下文状态分类，未确认前不清理旧版 |
| `Assets/Resources/CatCafe/PaperSkin/` | 弹窗、按钮、卡片、稀有度徽章 | `*-v2` 为当前主版本；旧版仍需归属确认 |
| `Assets/Art/CatCafe/Incoming_2026-08-17/` | 局内 v3 高分辨率母版来稿 | 当前 v3 的可追溯母版；后续应转入稳定 Source 目录 |
| `Assets/Art/UI/CatCafeInGame/Reference/` | 早期拆图参考 | 不能全部视为母版，部分背景色已烘焙 |

## 运行时素材

### 开始界面

代码入口：`Assets/Scripts/CatCafe/CatCafeStartController.cs`。六张图均为 4800×2700 RGBA 整画布图层，全部正在使用。

| 素材 | 用途 | 状态 | 重绘判断 |
| --- | --- | --- | --- |
| `start-backdrop.png` | 主背景、标题、故事字条 | 正在显示 | 主视觉保留；故事字条含烘焙文案，若文案需走表则要出无字版 |
| `start-glow.png` | 全屏高光粒子 | 正在显示 | 保留 |
| `start-button-play.png` | 开始游戏按钮层 | 正在显示 | 建议出无字版，按钮文案改由表格驱动 |
| `start-button-shops.png` | 故事/存档入口按钮层 | 正在显示 | 建议出无字版；当前图上“故事”与运行时入口语义可能不一致 |
| `start-button-settings.png` | 设置按钮层 | 正在显示 | 建议出无字版 |
| `start-button-quit.png` | 退出按钮层 | 正在显示 | 建议出无字版 |

注意：六张 4800×2700 RGBA 图在解码后理论上约占 296.6 MiB（不含 Unity 额外开销）。这是性能问题，不是画质问题；项目禁止降采样，未获得明确授权前不得制作低分辨率替代图。

### 整备大厅

代码入口：`Assets/Scripts/CatCafe/Meta/CatCafeHomeController.cs`。位置和尺寸来自 `GameDesign/CatCafeGameConfig.xlsx` 的正式导出数据。

| 素材组 | 文件 | 状态 | 重绘判断 |
| --- | --- | --- | --- |
| 场景 | `home-backdrop.png`、`home-popup-book.png`、`home-clouds-front.png` | 正在显示 | 风格和清晰度一致，保留 |
| 主入口 | `home-details-stand.png`、`home-relations-stand.png`、`home-start-ribbon.png` | 正在显示 | 建议出无字版；“猫咪详情 / DETAILS”“猫咪招募”“开始营业 / START”目前烘焙在图内 |
| HUD | `home-cans-bar.png`、`home-dex-bar.png`、`home-settings.png` | 正在显示 | 保留；空白区域适合承载表格文字 |
| 动画 | `sprite_sheet.png`、`sprite_sheet_cat_top.png`、`sprite_sheet_cat_right.png` | 正在加载 | 保留画面；建议以后改用语义化文件名，但不能直接改现有路径 |
| 单帧猫 | `home-cat-mid.png`、`home-cat-top.png`、`home-cat-right.png` | 不被 `PlaceAnimatedCat` 加载 | 作为动画参考帧保留，不能当作当前运行时图层 |

`home-popup-book.png` 上的 `Cat Cafe` 更接近品牌标识，可在品牌定稿后保留为美术文字；三个功能入口属于业务文案，应优先去字。

### 局内界面

代码入口：`Assets/Scripts/CatCafe/CatCafeGameController.cs`。

| 分类 | 文件 | 说明 |
| --- | --- | --- |
| 当前可见 | `background-v3.png`、`book-v3.png`、`round-sign-v3.png`、`goal-banner-v2.png`、`coin-hud-v2.png`、`start-button.png`、`settings-v3.png` | 当前分层皮肤直接显示 |
| 当前辅助 | `coin.png`、`settings.png` | `coin.png` 用于金币特效；`settings.png` 被透明交互反馈层加载 |
| 启动检查 | `background-v2.png`、`book-v2.png` | 只用于判断是否启用分层皮肤；删掉任一张会回退旧 UI |
| 暂无运行时引用 | `background.png`、`book-grid.png`、`buff-panel.png`、`clock.png`、`goal-banner.png`、`start-label.png` | 历史拆图/旧皮肤，不可仅凭“未引用”直接删除 |
| 表中遗留 | `paper-composite-straightened-v1.png` | `ui_paper_composite_resource` 仍指向它，但当前控制器没有读取该键 |

重绘判断：

- `background-v3.png`、`book-v3.png`、`round-sign-v3.png`、`settings-v3.png` 均有高分辨率母版，不需要重绘。
- `goal-banner-v2.png`、`coin-hud-v2.png` 的透明边缘和风格正常，不需要重绘。
- `start-button.png` 含“开始营业”烘焙文字，建议出无字版，再用表格文字覆盖。
- `coin.png` 本身质量足够；问题是 1254×1254 图只以约 54 px 显示，属于资源规格/内存问题，不是重绘问题。
- `book-v3.png` 上“咖吉米”属于品牌标题；是否保留为美术字应由品牌规范决定，不与普通 UI 文案混为一类。

### 共用纸艺控件

当前实际使用的 10 张：

- `badge-brown-v2.png`、`badge-green-v2.png`、`badge-orange-v2.png`、`badge-purple-v2.png`
- `button-primary-v2.png`、`button-secondary-v2.png`、`button-leave-v2.png`
- `modal-main-v2.png`、`reward-card-v2.png`、`title-ribbon-v2.png`

当前没有找到运行时引用、先按历史素材保留的 14 张：

- 全部非 v2 徽章、按钮、标题条
- `modal-panel.png`、`paw-tab.png`、`paw-tab-v2.png`
- `reward-card.png`、`stamp-coffee.png`、`stamp-paw.png`

`badge-orange-v2.png` 与 `badge-purple-v2.png` 已在项目任务板注明为程序推导占位，属于明确的正式重绘项；文件尺寸、九宫格边界和透明通道必须保持现有契约。

## 母版血缘

以下运行时文件与 2026-08-17 来稿逐像素验证为“高分辨率母版按 Lanczos 缩放到 1536×864”的结果，差异为 0：

| 高分辨率母版 | 当前运行时图 |
| --- | --- |
| `ingame-background-3200x1800.png` | `background-v3.png` |
| `ingame-book-3200x1800.png` | `book-v3.png` |
| `ingame-wave-sign-3200x1800.png` | `round-sign-v3.png` |
| `global-settings-2816x1584.png` | `settings-v3.png` |

`Assets/Art/UI/CatCafeInGame/Reference/` 中三张所谓 source 不能作为最终透明母版：

- `buff-panel-source.png` 为 RGB，绿色背景已烘焙；真正透明版本反而是运行时 `buff-panel.png`。
- `coin-source.png` 为 RGB，棋盘格已烘焙；真正透明版本是运行时 `coin.png`。
- `goal-banner-source.png` 为 RGB，棋盘格已烘焙；透明版本是运行时 `goal-banner.png` / `goal-banner-v2.png`。

在拿到可编辑原稿前，以上运行时透明图应作为视觉参考基准，不能从 RGB “source” 再次抠图覆盖正式资产。

## 建议重绘顺序

### P0：去除业务文案

先制作以下原尺寸、透明通道不变的无字版本：

1. 四张开始界面按钮层。
2. 大厅的详情立牌、招募立牌、开始营业缎带。
3. 局内开始营业按钮。
4. 如故事字条文案仍会迭代，再制作 `start-backdrop` 的无字条版本。

对应文字、字号、颜色、位置和点击区必须先进入源工作簿，再走既有导出流程；不能在代码里补常量。只改文字承载区，其他 UI 坐标、尺寸、锚点和遮挡关系保持不动。

### P1：正式徽章与缺失图标

1. 重绘 `badge-orange-v2`、`badge-purple-v2`，保持 220×85、透明底和现有九宫格安全区。
2. 确认大厅是否仍需要独立“罐头”和“绒毛”图标；当前没有可复用的独立 `can-icon` / `fur-icon` 正式资源。若需要，应按大厅现有纸雕风格新增，不从别的图层裁切放大。
3. 补齐真正透明、可编辑的 buff panel / coin / goal banner 母版；这是源文件治理，不需要覆盖当前运行时图。

### P2：版本与体积治理

1. 获得素材归属人确认后，把历史素材移出 `Resources`，并同步更新 `.meta` 和全部引用。
2. 将日期化 Incoming 母版迁入稳定的 `Source` 层级；日期只写在变更记录，不写进长期目录职责。
3. 对 4800×2700 全画布按钮层和超采样金币做显存预算。任何裁切、降分辨率或改变 Unity Max Size 的方案都必须单独获得授权。

## 清理前必须先修的引用问题

这些不是重绘问题，但会阻止安全整理：

1. 分层皮肤的可用性检查读取 `background-v2` / `book-v2`，实际显示却是 `background-v3` / `book-v3`。在修正前不能清掉 v2。
2. 可见设置按钮使用 `settings-v3`，透明交互反馈层仍加载 `settings`，两张图边界略有差异。
3. 开始页、首页和局内主要资源名仍有硬编码；动画序列帧资源路径已由表驱动，但其余资源路径尚未统一进表。
4. `ui_paper_composite_resource` 仍在导出数据中，当前控制器无读取点；应由源工作簿决定退役，而不是只改导出 JSON。

## 交付验收清单

- 固定在 1536×864、16:9 下检查位置、尺寸、遮挡与点击区。
- 对每张 PNG 检查原尺寸、RGBA/透明通道和 alpha 边缘；禁止把棋盘格或纯色抠图底烘焙进正式素材。
- Unity 导入保持 Uncompressed、关闭 Crunch、关闭 mipmap，Max Size 不得造成缩放。
- 所有业务文案、资源路径、坐标、字号、颜色和点击区来自源工作簿及正式导出数据。
- 文字与实际承载背景必须有明显对比；不能靠新增遮挡层掩盖原图问题。
- 新版使用新文件名并并行验证；除非明确要求替换，不直接覆盖旧素材。

