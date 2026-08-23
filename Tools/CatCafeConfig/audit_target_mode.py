#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""目标模式闭包审计。

默认保证所有标记为“已接入”的条目都满足：配置键存在、源表启用、目标文案与
正式 rule_text 一致、正式导出 JSON 中存在。未完成条目会列为 backlog，但默认
不让增量开发失败；发布闸门使用 --require-complete，要求 backlog 为 0。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import zipfile
from pathlib import Path

from export_config import read_rows, read_shared_strings, sheet_targets


ROOT = Path(__file__).resolve().parents[2]
BOOK = ROOT / "GameDesign" / "CatCafeGameConfig.xlsx"
CONFIG = ROOT / "Assets" / "Resources" / "GameData" / "cat_cafe_config.json"
RARITY_MAP = {
    "普通": "common", "稀有": "uncommon", "史诗": "rare",
    "传奇": "special", "特殊": "special"
}


def norm(value) -> str:
    return re.sub(r"[\s　，。；、：:（）()]", "", str(value or ""))


def truthy(value) -> bool:
    return isinstance(value, bool) and value or str(value or "").strip().lower() in {
        "true", "1", "yes", "y"
    }


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


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit Cat Cafe target-mode closure")
    parser.add_argument("--require-complete", action="store_true")
    args = parser.parse_args()

    with zipfile.ZipFile(BOOK) as book:
        targets = sheet_targets(book)
        shared = read_shared_strings(book)
        pieces = table(read_rows(book, targets["Pieces"], shared), 3, 5)
        items = table(read_rows(book, targets["Buffs"], shared), 3, 5)
        statuses = table(read_rows(book, targets["V3接入状态"], shared), 2, 3)

    source = {
        "店内对象": {str(row.get("key")): row for row in pieces if row.get("key")},
        "长期道具": {str(row.get("key")): row for row in items if row.get("key")},
    }
    runtime = json.loads(CONFIG.read_text(encoding="utf-8"))
    runtime_keys = {
        "店内对象": {row["key"] for row in runtime["elements"]},
        "长期道具": {row["key"] for row in runtime["items"]},
    }
    rules_by_owner = {}
    for rule in runtime["rules"]:
        rules_by_owner.setdefault(str(rule.get("owner_key") or ""), []).append(rule)

    false_closed = []
    semantic_gaps = []
    backlog = []
    closed = 0
    for row in statuses:
        category = str(row.get("类别") or "")
        if category not in source:
            continue
        key = str(row.get("配置键") or "")
        state = str(row.get("配置状态") or "")
        current = source[category].get(key)
        exact = current is not None and norm(row.get("原始效果")) == norm(current.get("rule_text"))
        name_exact = current is not None and norm(row.get("名称")) == norm(current.get("name"))
        rarity_exact = current is not None and \
            RARITY_MAP.get(str(row.get("稀有度") or "")) == str(current.get("rarity") or "")
        enabled = current is not None and truthy(current.get("enabled"))
        exported = key in runtime_keys[category]
        if state == "已接入":
            if current is None or not exact or not name_exact or not rarity_exact or not enabled or not exported:
                false_closed.append((row["__row__"], category, row.get("名称"), key,
                                     current is not None, exact, name_exact, rarity_exact,
                                     enabled, exported))
            else:
                closed += 1
                related = list(rules_by_owner.get(key, []))
                if category == "店内对象":
                    for candidate in runtime["rules"]:
                        haystack = "|".join(str(candidate.get(field) or "") for field in (
                            "source_keys", "primary_filter", "secondary_filter",
                            "remove_filter", "target_filter", "result_key"))
                        if key and key in re.split(r"[|;]", haystack):
                            related.append(candidate)
                text = str(row.get("原始效果") or "")
                triggers = {str(rule.get("trigger") or "") for rule in related}
                operations = {str(rule.get("operation") or "") for rule in related}
                gaps = []
                if "被移除时" in text and not ({"on_dismiss", "on_consume"} & triggers):
                    gaps.append("被移除时缺少离场触发")
                if "营业前" in text and "before_round" not in triggers:
                    gaps.append("营业前效果缺少before_round")
                if "点击" in text and "on_click" not in triggers and \
                        not ({"before_settlement", "stage_settlement"} & triggers):
                    gaps.append("点击效果缺少主动触发")
                if "概率" in text and not any(float(rule.get("chance") or 1) < 1 for rule in related) and \
                        not ({"modify_rule_chance", "rarity_weights"} & triggers):
                    gaps.append("概率效果缺少概率规则")
                if "带来" in text and not ({"generate", "generate_random", "generate_source",
                                             "generate_history_random", "transform", "choose_generate",
                                             "force_choose", "waive_payment_generate", "add_choice"} & operations) and \
                        "on_choose" not in triggers:
                    gaps.append("带来效果缺少生成/选择规则")
                multiplier_claim = "倍金币" in text or "倍收益" in text or "×" in text
                has_multiplier = ({"multiply", "multiply_income", "multiply_targets",
                                   "set_max_adjacent"} & operations) or any(
                    float(rule.get("multiplier") or 0) > 1 or
                    str(rule.get("target_value_mode") or "") in {"multiply_value", "base_income"}
                    for rule in related)
                if multiplier_claim and not has_multiplier:
                    gaps.append("倍率效果缺少乘算规则")
                if "移除自身" in text and not any(rule.get("consume_self") for rule in related) and \
                        not ({"transform", "consume_self", "consume_at_count"} & operations):
                    gaps.append("移除自身缺少consume_self")
                if "视为相邻" in text and "adjacency" not in triggers:
                    gaps.append("全局相邻缺少adjacency规则")
                active_choice_claim = "额外选择" in text or \
                    ("可以点击移除" in text and "选择" in text)
                if active_choice_claim and category == "长期道具" and \
                        not ({"choose_generate", "add_choice", "add_item_choice"} & operations):
                    gaps.append("选择效果缺少选择规则")
                if gaps:
                    semantic_gaps.append((row["__row__"], category, row.get("名称"), key, gaps))
        elif state == "目标保留禁用":
            if current is None or not exact or not name_exact or not rarity_exact or enabled or exported:
                false_closed.append((row["__row__"], category, row.get("名称"), key,
                                     current is not None, exact, name_exact, rarity_exact,
                                     not enabled, not exported))
            else:
                closed += 1
        else:
            backlog.append((row["__row__"], category, row.get("V3编号"),
                            row.get("名称"), key, state))

    print(f"目标模式：closed={closed}, backlog={len(backlog)}, false_closed={len(false_closed)}")
    if false_closed:
        print("\n── 错误标记为已接入 ──")
        for entry in false_closed:
            print("  row=%s %s %s key=%s exists=%s text=%s name=%s rarity=%s enabled=%s exported=%s" % entry)
    if backlog:
        print("\n── 未闭包 backlog ──")
        for number, category, source_number, name, key, state in backlog:
            print(f"  row={number:<3} {category:<4} #{source_number!s:<10} {name} key={key or '-'} [{state}]")

    if semantic_gaps:
        print("\n── 已接入条目的语义缺口 ──")
        for number, category, name, key, gaps in semantic_gaps:
            print(f"  row={number:<3} {category:<4} {name} key={key}: {'；'.join(gaps)}")

    if false_closed or semantic_gaps or (args.require_complete and backlog):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
