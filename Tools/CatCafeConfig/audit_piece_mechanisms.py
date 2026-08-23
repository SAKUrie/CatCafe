#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""目标模式棋子导入与交互闭包审计（只读）。"""

from __future__ import annotations

import json
import re
import sys
import zipfile
from collections import Counter, defaultdict
from pathlib import Path

from export_config import read_rows, read_shared_strings, sheet_targets


ROOT = Path(__file__).resolve().parents[2]
BOOK = ROOT / "GameDesign" / "CatCafeGameConfig.xlsx"
CONFIG = ROOT / "Assets" / "Resources" / "GameData" / "cat_cafe_config.json"
ASSET_ROOT = ROOT / "Assets" / "Resources" / "CatCafe"
RARITY_MAP = {
    "普通": "common", "稀有": "uncommon", "史诗": "rare",
    "传奇": "special", "特殊": "special",
}
REFERENCE_FIELDS = (
    "owner_key", "source_keys", "primary_filter", "secondary_filter",
    "remove_filter", "target_filter", "result_key",
)


def norm(value) -> str:
    return re.sub(r"[\s　，。；、：:（）()]", "", str(value or ""))


def table(rows: dict[int, list], header_row: int, data_row: int) -> list[dict]:
    headers = [str(value or "").strip() for value in rows.get(header_row, [])]
    result = []
    for number in sorted(key for key in rows if key >= data_row):
        values = rows[number]
        if not any(value not in (None, "") for value in values):
            continue
        row = {header: values[index] if index < len(values) else ""
               for index, header in enumerate(headers) if header}
        row["__row__"] = number
        result.append(row)
    return result


def tokens(value):
    return [token.strip() for token in re.split(r"[|;]", str(value or "")) if token.strip()]


def main() -> int:
    with zipfile.ZipFile(BOOK) as book:
        targets = sheet_targets(book)
        shared = read_shared_strings(book)
        statuses = table(read_rows(book, targets["V3接入状态"], shared), 2, 3)
    target_rows = [row for row in statuses if row.get("类别") == "店内对象"]

    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    elements = {row["key"]: row for row in config["elements"]}
    rules = [row for row in config["rules"] if row.get("enabled")]
    breeding = [row for row in config["breeding"] if row.get("enabled")]
    initial_deck = [row for row in config["initialDeck"] if row.get("enabled")]
    settings = [row for row in config["settings"] if row.get("enabled")]
    target_by_key = {str(row.get("配置键") or ""): row for row in target_rows if row.get("配置键")}

    issues = []
    warnings = []
    key_counts = Counter(str(row.get("配置键") or "") for row in target_rows if row.get("配置键"))
    for key, count in key_counts.items():
        if count > 1:
            issues.append(("目标映射重复", key, f"出现{count}次"))

    for row in target_rows:
        key = str(row.get("配置键") or "")
        name = str(row.get("名称") or "")
        element = elements.get(key)
        if not key or element is None:
            issues.append(("目标棋子未导入", name, f"row={row['__row__']} key={key or '-'}"))
            continue
        if norm(name) != norm(element.get("name")):
            issues.append(("名称不一致", name, f"key={key} runtime={element.get('name')}"))
        expected_rarity = RARITY_MAP.get(str(row.get("稀有度") or ""))
        if expected_rarity != element.get("rarity"):
            issues.append(("稀有度不一致", name,
                           f"key={key} target={expected_rarity} runtime={element.get('rarity')}"))
        if norm(row.get("原始效果")) != norm(element.get("rule_text")):
            issues.append(("效果文案不一致", name, f"key={key}"))
        asset = str(element.get("asset") or "")
        png = ASSET_ROOT / (asset + ".png")
        meta = ASSET_ROOT / (asset + ".png.meta")
        if not png.exists() or not meta.exists():
            issues.append(("素材缺失", name,
                           f"key={key} png={png.exists()} meta={meta.exists()} asset={asset}"))

    rules_by_owner = defaultdict(list)
    related_rules = defaultdict(list)
    for rule in rules:
        rules_by_owner[str(rule.get("owner_key") or "")].append(rule)
        field_tokens = set()
        for field in REFERENCE_FIELDS:
            field_tokens.update(tokens(rule.get(field)))
        for token in field_tokens:
            if token in elements:
                related_rules[token].append(rule)

    breeding_keys = set()
    for row in breeding:
        breeding_keys.update(filter(None, (row.get("parent_a"), row.get("parent_b"),
                                           row.get("child"), row.get("mutation_child"))))

    for key, row in target_by_key.items():
        owned = rules_by_owner.get(key, [])
        related = owned + related_rules.get(key, [])
        if not related and key not in breeding_keys:
            issues.append(("棋子没有机制入口", row.get("名称"), f"key={key}"))

    supplemental = [key for key in elements if key not in target_by_key]
    all_references = set()
    for rule in rules:
        for field in REFERENCE_FIELDS:
            all_references.update(token for token in tokens(rule.get(field)) if token in elements)
    for row in breeding:
        all_references.update(value for value in (
            row.get("parent_a"), row.get("parent_b"), row.get("child"), row.get("mutation_child"))
            if value in elements)
    all_references.update(row.get("element_key") for row in initial_deck
                          if row.get("element_key") in elements)
    all_references.update(str(row.get("value")) for row in settings
                          if str(row.get("value")) in elements)
    for key in supplemental:
        element = elements[key]
        asset = str(element.get("asset") or "")
        png = ASSET_ROOT / (asset + ".png")
        meta = ASSET_ROOT / (asset + ".png.meta")
        if not png.exists() or not meta.exists():
            issues.append(("补充棋子素材缺失", element.get("name"), f"key={key} asset={asset}"))
        if key not in all_references and not rules_by_owner.get(key):
            issues.append(("补充棋子无调用方", element.get("name"), f"key={key}"))

    # 策划效果中明确点名的其他棋子，必须真实出现在该棋子的相关规则字段中。
    names = sorted(((str(row.get("名称") or ""), key)
                    for key, row in target_by_key.items() if row.get("名称")),
                   key=lambda item: len(item[0]), reverse=True)
    for owner_key, row in target_by_key.items():
        effect = str(row.get("原始效果") or "")
        related = rules_by_owner.get(owner_key, []) + related_rules.get(owner_key, [])
        serialized = "|".join(str(rule.get(field) or "")
                              for rule in related for field in REFERENCE_FIELDS)
        referenced_tokens = set(tokens(serialized))
        for target_name, target_key in names:
            if target_key == owner_key or target_name not in effect:
                continue
            if any(target_name != longer_name and target_name in longer_name and longer_name in effect
                   for longer_name, _ in names):
                continue
            if target_key not in referenced_tokens:
                operations = {str(rule.get("operation") or "") for rule in related}
                scopes = {str(rule.get(field) or "") for rule in related
                          for field in ("primary_scope", "secondary_scope")}
                if target_key == "finalPiece149" and "set_income" in operations:
                    continue
                if target_key == "finalPiece050" and "adjacent_empty" in scopes:
                    continue
                issues.append(("点名互动未接线", row.get("名称"),
                               f"key={owner_key} 文案点名={target_name}({target_key})"))

        numeric_values = []
        for related_rule in related:
            for field in ("base_value", "primary_factor", "secondary_factor", "cross_factor",
                          "primary_threshold", "secondary_threshold", "result_count", "multiplier",
                          "chance"):
                try:
                    numeric_values.append(float(related_rule.get(field) or 0))
                except (TypeError, ValueError):
                    pass
        for percent in re.findall(r"(\d+(?:\.\d+)?)%", effect):
            value = float(percent) / 100.0
            expected = (value, 1.0 + value)
            if not any(any(abs(actual - target) < 1e-6 for target in expected)
                       for actual in numeric_values):
                issues.append(("百分比未落到规则", row.get("名称"),
                               f"key={owner_key} 文案={percent}%"))
        for multiple in re.findall(r"(\d+(?:\.\d+)?)倍", effect):
            value = float(multiple)
            if not any(abs(actual - value) < 1e-6 or abs(actual - (value - 1)) < 1e-6
                       for actual in numeric_values):
                issues.append(("倍率未落到规则", row.get("名称"),
                               f"key={owner_key} 文案={multiple}倍"))
        for cycles in re.findall(r"(\d+)次营业", effect):
            value = float(cycles)
            if not any(abs(actual - value) < 1e-6 or abs(actual - (value - 1)) < 1e-6
                       for actual in numeric_values):
                issues.append(("周期未落到规则", row.get("名称"),
                               f"key={owner_key} 文案={cycles}次营业"))

    # 生成/变形链不能指向自身形成无退出循环；周期链允许多级但不能闭环。
    graph = defaultdict(set)
    for rule in rules:
        if rule.get("operation") != "transform":
            continue
        owner = str(rule.get("owner_key") or "")
        for result in tokens(rule.get("result_key")):
            if owner in elements and result in elements:
                graph[owner].add(result)
    visiting = set()
    visited = set()

    def visit(node, path):
        if node in visiting:
            cycle = path[path.index(node):] + [node]
            issues.append(("生成/变形循环", elements[node]["name"], " -> ".join(cycle)))
            return
        if node in visited:
            return
        visiting.add(node)
        for child in graph.get(node, ()):
            visit(child, path + [child])
        visiting.remove(node)
        visited.add(node)

    for key in graph:
        visit(key, [key])

    edge_counts = Counter()
    for rule in rules:
        owner = str(rule.get("owner_key") or "")
        if owner not in elements and owner != "*":
            continue
        operation = str(rule.get("operation") or "")
        if operation in {"generate", "generate_random", "generate_source", "transform"}:
            edge_counts["生成/变形"] += 1
        elif operation in {"remove_targets", "consume_self", "consume_at_count"}:
            edge_counts["移除/消耗"] += 1
        elif operation in {"multiply_income", "set_income", "income", "permanent_add"}:
            edge_counts["收益/成长"] += 1
        elif str(rule.get("trigger") or "") in {"prevent_remove", "modify_rule_chance",
                                                  "rarity_weights", "on_random_result"}:
            edge_counts["保护/概率/事件"] += 1

    print(f"目标棋子={len(target_rows)}, 已映射={len(target_by_key)}, 补充棋子={len(supplemental)}, runtime Pieces={len(elements)}")
    print("互动边：" + "，".join(f"{key}={value}" for key, value in sorted(edge_counts.items())))
    if issues:
        print(f"\n发现 {len(issues)} 处棋子导入/互动问题：")
        for kind, where, detail in issues:
            print(f"  [{kind}] {where}: {detail}")
    if warnings:
        print(f"\n警告 {len(warnings)}：")
        for warning in warnings:
            print("  " + warning)
    return 1 if issues else 0


if __name__ == "__main__":
    raise SystemExit(main())
