# 猫咖配置工作流

`CatCafeGameConfig.xlsx` 是玩法数据的唯一编辑入口。不要手改 Unity 中生成的 JSON。

1. 在 Unity 顶部选择 `Tools → Cat Cafe → 配置表 → 打开 Excel`。
2. 编辑后关闭 Excel。
3. 选择 `Tools → Cat Cafe → 配置表 → 导出 Excel 到 JSON`。
4. 工具会校验主键与跨表引用，并生成：
   `Assets/Resources/GameData/cat_cafe_config.json`。
5. Unity 会自动刷新生成的 JSON。

Unity 菜单使用项目内置 C# 直接读取 `.xlsx`，无需安装 Python 或 Excel 导表插件。

自动化环境也可以运行 `Tools/CatCafeConfig/export_config.py`；该命令行方式需要 Python 3。

`Tutorial` 页维护房东奶奶字条，`Intimacy` 页维护猫咪亲密度里程碑；两者和其他玩法表一起导出到统一 JSON。

表格前四行分别是：标题、中文列名、运行时字段名、字段类型。数据从第5行开始。
`enabled=FALSE` 的行不会进入运行时 JSON。

TA 的同名素材替换接口仍是 `Assets/Resources/CatCafe/InGameUI`，与本配置流程相互独立。

## 最终版内容接入与闭包

- `Pieces`、`Buffs` 和 `Rules` 仍是运行时唯一正式数据源；代码只提供通用规则解释能力。
- **目标模式是唯一闭包口径**：对象与长期道具统一以 `V3接入状态` 的名称、稀有度和原始效果为准；`V3道具接入状态` 仅保留历史映射参考，不作为发布闸门。
- 目标模式与当前项目配置发生数值或机制冲突时，当前配置必须迁移到目标效果；不得再用“沿用当前项目机制”标记为完成。
- `Tools/CatCafeConfig/audit_target_mode.py` 保证所有“已接入”行与源表、运行时 JSON 完全一致；发布前使用 `--require-complete`，backlog 必须为 0。
- 暂缓的复杂机制不得用不准确的近似逻辑替代；对象仍可启用，但必须在状态表中明确记录未实现部分。
- 所有启用规则的拥有者、生成物、变形目标和被引用对象都必须存在且启用；导出前必须通过跨表引用与闭包校验。
- 新增或调整内容时，先修改 `CatCafeGameConfig.xlsx`，再使用项目导出器生成 JSON；禁止直接编辑生成的 JSON。
