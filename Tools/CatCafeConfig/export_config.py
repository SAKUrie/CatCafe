#!/usr/bin/env python3
"""Export CatCafeGameConfig.xlsx into Unity's single runtime JSON.

No third-party package is required: .xlsx is read as Open XML with Python's stdlib.
The workbook is the source of truth; do not edit the generated JSON by hand.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_INPUT = ROOT / "GameDesign" / "CatCafeGameConfig.xlsx"
DEFAULT_OUTPUT = ROOT / "Assets" / "Resources" / "GameData" / "cat_cafe_config.json"
SHEET_MAP = {
    "Settings": "settings",
    "Rarities": "rarities",
    "Pieces": "elements",
    "Buffs": "items",
    "Stages": "stages",
    "Weights": "weights",
    "InitialDeck": "initialDeck",
    "Rules": "rules",
    "Breeding": "breeding",
    "Levels": "levels",
    "Tutorial": "tutorials",
    "Intimacy": "intimacy",
    "Invite": "invites",
    "Archetypes": "archetypes",
}
NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
REL_NS = {"r": "http://schemas.openxmlformats.org/package/2006/relationships"}
DOC_REL = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"


def column_index(reference: str) -> int:
    match = re.match(r"([A-Z]+)", reference)
    if not match:
        raise ValueError(f"无效单元格地址：{reference}")
    result = 0
    for char in match.group(1):
        result = result * 26 + ord(char) - 64
    return result - 1


def text_of(node: ET.Element | None) -> str:
    if node is None:
        return ""
    return "".join(part.text or "" for part in node.iterfind(".//m:t", NS))


def read_shared_strings(book: zipfile.ZipFile) -> list[str]:
    try:
        root = ET.fromstring(book.read("xl/sharedStrings.xml"))
    except KeyError:
        return []
    return [text_of(item) for item in root.findall("m:si", NS)]


def sheet_targets(book: zipfile.ZipFile) -> dict[str, str]:
    workbook = ET.fromstring(book.read("xl/workbook.xml"))
    rels = ET.fromstring(book.read("xl/_rels/workbook.xml.rels"))
    target_by_id = {
        relation.attrib["Id"]: relation.attrib["Target"]
        for relation in rels.findall("r:Relationship", REL_NS)
    }
    result: dict[str, str] = {}
    for sheet in workbook.findall("m:sheets/m:sheet", NS):
        target = target_by_id[sheet.attrib[DOC_REL]].replace("\\", "/")
        if target.startswith("/"):
            target = target.lstrip("/")
        elif not target.startswith("xl/"):
            target = "xl/" + target
        result[sheet.attrib["name"]] = target
    return result


def cell_value(cell: ET.Element, shared: list[str]):
    kind = cell.attrib.get("t", "")
    if kind == "inlineStr":
        return text_of(cell.find("m:is", NS))
    value_node = cell.find("m:v", NS)
    value = "" if value_node is None else (value_node.text or "")
    if kind == "s":
        return shared[int(value)] if value else ""
    if kind == "b":
        return value == "1"
    if kind in ("str", "e"):
        return value
    if value == "":
        return ""
    try:
        number = float(value)
        return int(number) if number.is_integer() else number
    except ValueError:
        return value


def read_rows(book: zipfile.ZipFile, target: str, shared: list[str]) -> dict[int, list]:
    root = ET.fromstring(book.read(target))
    rows: dict[int, list] = {}
    for row in root.findall("m:sheetData/m:row", NS):
        row_number = int(row.attrib["r"])
        values: list = []
        for cell in row.findall("m:c", NS):
            index = column_index(cell.attrib["r"])
            while len(values) <= index:
                values.append("")
            values[index] = cell_value(cell, shared)
        rows[row_number] = values
    return rows


def convert(value, value_type: str):
    if value is None or value == "":
        if value_type == "bool":
            return False
        if value_type in ("int", "float"):
            return 0
        return ""
    if value_type == "int":
        return int(float(value))
    if value_type == "float":
        return float(value)
    if value_type == "bool":
        if isinstance(value, bool):
            return value
        return str(value).strip().lower() in ("true", "1", "yes", "y")
    return str(value).replace("\r\n", "\n")


def parse_sheet(rows: dict[int, list], name: str) -> list[dict]:
    fields = [str(value).strip() for value in rows.get(3, [])]
    types = [str(value).strip().lower() or "string" for value in rows.get(4, [])]
    if not fields or not any(fields):
        raise ValueError(f"{name} 第3行缺少英文字段名")
    result: list[dict] = []
    for row_number in sorted(number for number in rows if number >= 5):
        values = rows[row_number]
        if not any(value not in (None, "") for value in values):
            continue
        item = {}
        for index, field in enumerate(fields):
            if not field:
                continue
            value = values[index] if index < len(values) else ""
            value_type = types[index] if index < len(types) else "string"
            item[field] = convert(value, value_type)
        if "enabled" in item and not item["enabled"]:
            continue
        result.append(item)
    return result


def ensure_unique(rows: list[dict], key: str, sheet: str):
    seen = set()
    for row in rows:
        value = row.get(key)
        if value in (None, ""):
            raise ValueError(f"{sheet} 存在空 {key}")
        if value in seen:
            raise ValueError(f"{sheet} 存在重复 {key}={value}")
        seen.add(value)


def validate(data: dict):
    ensure_unique(data["settings"], "key", "Settings")
    ensure_unique(data["rarities"], "key", "Rarities")
    ensure_unique(data["elements"], "key", "Pieces")
    ensure_unique(data["items"], "key", "Buffs")
    ensure_unique(data["stages"], "id", "Stages")
    ensure_unique(data["weights"], "context", "Weights")
    ensure_unique(data["rules"], "rule_id", "Rules")
    ensure_unique(data["tutorials"], "id", "Tutorial")
    ensure_unique(data["intimacy"], "milestone", "Intimacy")
    ensure_unique(data["invites"], "child", "Invite")
    ensure_unique(data["archetypes"], "key", "Archetypes")

    element_keys = {row["key"] for row in data["elements"]}
    element_by_key = {row["key"]: row for row in data["elements"]}
    item_keys = {row["key"] for row in data["items"]}
    rarity_keys = {row["key"] for row in data["rarities"]}
    weight_keys = {row["context"] for row in data["weights"]}
    settings = {row["key"]: row["value"] for row in data["settings"]}

    required_presentation_settings = (
        "ui_run_wave_label_format",
        "ui_reward_choice_title",
        "ui_reward_choice_special_title_format",
        "ui_reward_reroll_available_format",
        "ui_reward_reroll_unavailable",
        "ui_item_choice_title",
        "ui_item_reroll_unavailable",
        "ui_item_added_toast_format",
        "ui_card_detail_dismiss_format",
        "tutorial_spotlight_padding",
        "tutorial_spotlight_edge_thickness",
        "tutorial_note_portrait_resource",
        "tutorial_note_portrait_x",
        "tutorial_note_portrait_y",
        "tutorial_note_portrait_width",
        "tutorial_note_portrait_height",
        "tutorial_note_text_left_inset",
        "tutorial_note_text_right_inset",
        "meta_fur_natural_interval_minutes",
        "meta_fur_natural_amount_per_interval",
        "meta_fur_natural_cap_per_breed",
        "ui_home_invite_title",
        "ui_home_fur_natural_gain_format",
        "ui_card_detail_header",
        "ui_card_detail_meta_format",
        "ui_card_detail_no_effect",
        "ui_card_detail_close_label",
        "ui_card_detail_width",
        "ui_card_detail_height",
        "ui_card_detail_content_width",
        "ui_card_detail_content_height",
        "ui_card_detail_icon_size",
        "ui_card_detail_content_spacing",
        "ui_card_detail_fallback_font_size",
        "ui_card_detail_fallback_color",
        "ui_card_detail_meta_font_size",
        "ui_card_detail_meta_height",
        "ui_card_detail_income_font_size",
        "ui_card_detail_rule_font_size",
        "ui_card_detail_rule_height",
        "ui_card_detail_rule_color",
        "ui_card_detail_close_width",
        "ui_card_detail_close_height",
        "ui_card_detail_backdrop_color",
        "ui_card_detail_income_format",
        "ui_card_detail_preview_income_format",
        "ui_card_detail_income_breakdown_format",
        "ui_card_detail_income_separator",
        "ui_card_detail_income_source_format",
        "ui_card_detail_not_on_board",
        "ui_card_detail_horizontal_offset",
        "ui_card_detail_vertical_offset",
        "ui_card_detail_paper_layer_x",
        "ui_card_detail_show_backing",
        "ui_symbol_link_color",
        "ui_symbol_reference_width",
        "ui_symbol_reference_height",
        "ui_symbol_reference_screen_padding",
        "ui_symbol_reference_horizontal_gap",
        "ui_symbol_reference_vertical_offset",
        "ui_symbol_reference_panel_color",
        "ui_symbol_reference_panel_padding",
        "ui_symbol_reference_content_spacing",
        "ui_symbol_reference_title_font_size",
        "ui_symbol_reference_title_height",
        "ui_symbol_reference_icon_size",
        "ui_symbol_reference_meta_font_size",
        "ui_symbol_reference_meta_height",
        "ui_symbol_reference_rule_font_size",
        "ui_symbol_reference_rule_height",
        "ui_symbol_reference_text_color",
        "ui_symbol_reference_close_hint",
        "ui_symbol_reference_hint_font_size",
        "ui_symbol_reference_hint_height",
        "ui_symbol_reference_hint_color",
        "settlement_reaction_plain_seconds",
        "settlement_reaction_linked_seconds",
        "settlement_reaction_high_seconds",
        "settlement_reaction_plain_scale",
        "settlement_reaction_linked_scale",
        "settlement_reaction_high_scale",
        "settlement_reaction_plain_marker_alpha",
        "settlement_reaction_linked_marker_alpha",
        "settlement_reaction_high_marker_alpha",
        "ui_settlement_normal_color",
        "ui_settlement_linked_color",
        "ui_settlement_high_color",
        "settlement_reaction_group_gap_seconds",
        "settlement_collect_intro_seconds",
        "settlement_payout_batch_pulse_seconds",
        "settlement_payout_batch_hold_seconds",
        "settlement_payout_batch_gap_seconds",
        "settlement_total_hold_seconds",
        "settlement_payout_marker_alpha",
        "settlement_payout_peak_scale",
        "ui_settlement_payout_color",
        "settlement_rare_chain_coin_count",
        "settlement_rare_chain_coin_lifetime_multiplier",
        "settlement_rare_chain_coin_min_scale",
        "settlement_rare_chain_coin_max_scale",
        "settlement_rare_chain_coin_min_speed",
        "settlement_rare_chain_coin_max_speed",
        "settlement_rare_chain_coin_fade_stagger",
        "settlement_rare_chain_coin_fade_center",
        "settlement_rare_chain_coin_angle_jitter",
        "settlement_rare_chain_coin_base_size",
        "settlement_rare_chain_coin_gravity_min",
        "settlement_rare_chain_coin_gravity_max",
        "settlement_rare_chain_coin_rotation_min",
        "settlement_rare_chain_coin_rotation_max",
        "settlement_rare_chain_coin_resource",
        "settlement_rare_chain_coin_sort_order",
        "ui_settlement_batch_text_color",
        "ui_settlement_plain_group_label",
        "ui_settlement_single_link_label_format",
        "ui_settlement_multi_link_label_format",
        "ui_settlement_reaction_format",
        "ui_settlement_collect_start",
        "ui_settlement_batch_format",
        "ui_settlement_total_format",
    )
    missing_presentation = [
        key for key in required_presentation_settings
        if key not in settings or str(settings[key]).strip() == ""
    ]
    if missing_presentation:
        raise ValueError(
            "Settings 缺少局内结算/棋子介绍参数：" + ", ".join(missing_presentation)
        )
    for key in (
        "tutorial_spotlight_padding",
        "tutorial_spotlight_edge_thickness",
        "meta_fur_natural_interval_minutes",
        "meta_fur_natural_amount_per_interval",
        "meta_fur_natural_cap_per_breed",
        "tutorial_note_portrait_width",
        "tutorial_note_portrait_height",
        "tutorial_note_text_left_inset",
        "tutorial_note_text_right_inset",
    ):
        if float(settings[key]) <= 0:
            raise ValueError(f"{key} 必须大于0")
    for key in (
        "settlement_reaction_plain_seconds",
        "settlement_reaction_linked_seconds",
        "settlement_reaction_high_seconds",
        "settlement_reaction_group_gap_seconds",
        "settlement_collect_intro_seconds",
        "settlement_payout_batch_pulse_seconds",
        "settlement_payout_batch_hold_seconds",
        "settlement_payout_batch_gap_seconds",
        "settlement_total_hold_seconds",
        "settlement_rare_chain_coin_lifetime_multiplier",
        "settlement_rare_chain_coin_fade_stagger",
        "settlement_rare_chain_coin_fade_center",
        "settlement_rare_chain_coin_angle_jitter",
    ):
        if float(settings[key]) < 0:
            raise ValueError(f"{key} 不能小于0")
    for key in (
        "settlement_rare_chain_coin_count",
        "settlement_rare_chain_coin_min_scale",
        "settlement_rare_chain_coin_max_scale",
        "settlement_rare_chain_coin_min_speed",
        "settlement_rare_chain_coin_max_speed",
        "settlement_rare_chain_coin_base_size",
        "settlement_rare_chain_coin_gravity_min",
        "settlement_rare_chain_coin_gravity_max",
        "settlement_rare_chain_coin_sort_order",
    ):
        if float(settings[key]) <= 0:
            raise ValueError(f"{key} 必须大于0")
    if float(settings["settlement_rare_chain_coin_max_scale"]) < float(settings["settlement_rare_chain_coin_min_scale"]):
        raise ValueError("settlement_rare_chain_coin_max_scale 不能小于最小大小比例")
    if float(settings["settlement_rare_chain_coin_max_speed"]) < float(settings["settlement_rare_chain_coin_min_speed"]):
        raise ValueError("settlement_rare_chain_coin_max_speed 不能小于最小发射速度")
    if float(settings["settlement_rare_chain_coin_gravity_max"]) < float(settings["settlement_rare_chain_coin_gravity_min"]):
        raise ValueError("settlement_rare_chain_coin_gravity_max 不能小于最小下落加速度")
    if float(settings["settlement_rare_chain_coin_fade_stagger"]) > 1:
        raise ValueError("settlement_rare_chain_coin_fade_stagger 不能大于1")
    if not str(settings["settlement_rare_chain_coin_resource"]).strip():
        raise ValueError("settlement_rare_chain_coin_resource 不能为空")
    for key in (
        "settlement_reaction_plain_scale",
        "settlement_reaction_linked_scale",
        "settlement_reaction_high_scale",
        "settlement_payout_peak_scale",
    ):
        if float(settings[key]) <= 0:
            raise ValueError(f"{key} 必须大于0")
    for key in (
        "settlement_reaction_plain_marker_alpha",
        "settlement_reaction_linked_marker_alpha",
        "settlement_reaction_high_marker_alpha",
        "settlement_payout_marker_alpha",
    ):
        alpha = float(settings[key])
        if alpha < 0 or alpha > 1:
            raise ValueError(f"{key} 必须在0到1之间")

    final_stage_indexes = [index for index, row in enumerate(data["stages"]) if row["is_final"]]
    if len(final_stage_indexes) != 1 or final_stage_indexes[0] != len(data["stages"]) - 1:
        raise ValueError("Stages 必须且只能有一个最终关，并且它必须是最后一个启用阶段")

    endless_enabled = str(settings.get("endless_enabled", "false")).strip().lower() in ("true", "1", "yes", "y")
    if endless_enabled:
        required_endless = (
            "endless_rounds",
            "endless_target_growth_rate",
            "endless_target_flat_increment",
            "endless_target_round_to",
            "endless_rarity_context",
        )
        missing = [key for key in required_endless if key not in settings or str(settings[key]).strip() == ""]
        if missing:
            raise ValueError("Settings 缺少无尽模式参数：" + ", ".join(missing))
        if int(float(settings["endless_rounds"])) <= 0:
            raise ValueError("endless_rounds 必须大于0")
        if float(settings["endless_target_growth_rate"]) < 0:
            raise ValueError("endless_target_growth_rate 不能小于0")
        if int(float(settings["endless_target_flat_increment"])) < 0:
            raise ValueError("endless_target_flat_increment 不能小于0")
        if int(float(settings["endless_target_round_to"])) <= 0:
            raise ValueError("endless_target_round_to 必须大于0")
        if settings["endless_rarity_context"] not in weight_keys:
            raise ValueError(
                f"endless_rarity_context 不存在对应的 Weights 上下文：{settings['endless_rarity_context']}"
            )

    for row in data["initialDeck"]:
        if row["element_key"] not in element_keys:
            raise ValueError(f"InitialDeck 引用了不存在的棋子：{row['element_key']}")
        if row["count"] <= 0:
            raise ValueError(f"InitialDeck 数量必须大于0：{row['element_key']}")
    for row in data["elements"]:
        if row["rarity"] not in rarity_keys:
            raise ValueError(f"Pieces {row['key']} 的 rarity 不存在：{row['rarity']}")
    for row in data["items"]:
        if row["rarity"] not in rarity_keys:
            raise ValueError(f"Buffs {row['key']} 的 rarity 不存在：{row['rarity']}")
    for row in data["stages"]:
        if row["rarity_context"] not in weight_keys:
            raise ValueError(f"Stages {row['id']} 的 rarity_context 不存在：{row['rarity_context']}")
    for row in data["rules"]:
        owner_type, owner_key = row["owner_type"], row["owner_key"]
        if owner_type == "element" and owner_key != "*" and owner_key not in element_keys:
            raise ValueError(f"Rules {row['rule_id']} 引用了不存在的棋子：{owner_key}")
        if owner_type == "item" and owner_key not in item_keys:
            raise ValueError(f"Rules {row['rule_id']} 引用了不存在的道具：{owner_key}")
        result_key = row.get("result_key", "")
        # generate_random 的结果池与运行时 ChooseRuleResultKey 一致，使用竖线分隔候选。
        # 校验每个候选，而不是把整串误当成一个棋子 key。
        result_keys = [key.strip() for key in result_key.split("|") if key.strip()]
        result_is_element = row.get("operation", "") in ("generate", "generate_random", "transform")
        missing_result_keys = (
            [key for key in result_keys if key not in element_keys]
            if result_is_element else []
        )
        if missing_result_keys:
            raise ValueError(
                f"Rules {row['rule_id']} 的 result_key 不存在：{'|'.join(missing_result_keys)}"
            )
        if row.get("operation", "") in ("generate", "transform") and not result_key:
            raise ValueError(f"Rules {row['rule_id']} 的 {row['operation']} 操作必须配置 result_key")
        chance = row.get("chance", 1)
        if chance < 0 or chance > 1:
            raise ValueError(f"Rules {row['rule_id']} 的 chance 必须在 0~1：{chance}")
        if row.get("repeat_on_success", False) and row.get("max_triggers", 0) <= 0:
            raise ValueError(f"Rules {row['rule_id']} 开启连续触发时，max_triggers 必须大于0")
    breeding_pairs = set()
    wildcard_rows = 0
    for row in data["breeding"]:
        parent_a = row.get("parent_a", "")
        parent_b = row.get("parent_b", "")
        child = row.get("child", "")
        result_mode = row.get("result_mode", "") or "fixed"
        pair = (parent_a, parent_b)
        if pair in breeding_pairs:
            raise ValueError(f"Breeding 存在重复父母组合：{parent_a}+{parent_b}")
        breeding_pairs.add(pair)

        wildcard = parent_a == "*" and parent_b == "*"
        if (parent_a == "*") != (parent_b == "*"):
            raise ValueError("Breeding 通配配方必须同时使用 parent_a=*、parent_b=*")
        if wildcard:
            wildcard_rows += 1
            if result_mode != "rarity_random":
                raise ValueError("Breeding 通配配方必须配置 result_mode=rarity_random")
            if child:
                raise ValueError("Breeding rarity_random 通配配方的 child 必须留空")
            context = row.get("rarity_context", "")
            if context not in weight_keys:
                raise ValueError(f"Breeding rarity_context 不存在对应的 Weights 上下文：{context}")
        else:
            for field in ("parent_a", "parent_b"):
                if row[field] not in element_keys:
                    raise ValueError(f"Breeding 引用了不存在的棋子：{field}={row[field]}")
                if element_by_key[row[field]].get("kind") != "cat":
                    raise ValueError(f"Breeding 父母必须是成年猫：{field}={row[field]}")
            if parent_a > parent_b:
                raise ValueError(f"Breeding 父母必须按 key 字典序填写：{parent_a}+{parent_b}")
            if result_mode != "fixed":
                raise ValueError(f"Breeding 精确配方仅支持 result_mode=fixed：{parent_a}+{parent_b}")
            if child not in element_keys:
                raise ValueError(f"Breeding 引用了不存在的幼猫：child={child}")
            if element_by_key[child].get("kind") != "kitten":
                raise ValueError(f"Breeding child 必须是幼猫：{child}")
            if parent_a == parent_b and element_by_key[child].get("grown_form") != parent_a:
                raise ValueError(f"Breeding 同品种必须生出同品种幼猫：{parent_a} -> {child}")

        if result_mode not in ("fixed", "rarity_random"):
            raise ValueError(f"Breeding 未知 result_mode：{result_mode}")
        mutation = row.get("mutation_child", "")
        if mutation and mutation not in element_keys:
            raise ValueError(f"Breeding 引用了不存在的突变幼崽：{mutation}")
        if mutation and element_by_key[mutation].get("kind") != "kitten":
            raise ValueError(f"Breeding mutation_child 必须是幼猫：{mutation}")
        mutation_rate = row.get("mutation_rate", 0)
        if mutation_rate < 0 or mutation_rate > 1:
            raise ValueError(f"Breeding mutation_rate 必须在 0~1：{mutation_rate}")

    if wildcard_rows > 1:
        raise ValueError("Breeding 只能配置一条 *+* 通配配方")
    if wildcard_rows:
        adult_keys = {row["key"] for row in data["elements"] if row.get("kind") == "cat"}
        kitten_grown_forms = {
            row.get("grown_form") for row in data["elements"]
            if row.get("kind") == "kitten" and row.get("grown_form") in adult_keys
        }
        missing_kittens = sorted(adult_keys - kitten_grown_forms)
        if missing_kittens:
            raise ValueError("Breeding rarity_random 缺少对应幼猫：" + ", ".join(missing_kittens))
    for row in data["invites"]:
        for field in ("child", "inviter_a"):
            if row[field] not in element_keys:
                raise ValueError(f"Invite 引用了不存在的猫：{field}={row[field]}")
        if row["fur_a"] <= 0:
            raise ValueError(f"Invite 的 fur_a 必须大于0：{row['child']}")
        if row["cans"] < 0:
            raise ValueError(f"Invite 的 cans 不能为负：{row['child']}")
        inviter_b = row.get("inviter_b", "")
        if not inviter_b:
            continue
        if inviter_b not in element_keys:
            raise ValueError(f"Invite 引用了不存在的猫：inviter_b={inviter_b}")
        if row["fur_b"] <= 0:
            raise ValueError(f"Invite 配了 inviter_b 就必须配 fur_b：{row['child']}")
    for row in data["levels"]:
        if row["cat_key"] not in element_keys:
            raise ValueError(f"Levels 引用了不存在的猫：{row['cat_key']}")


def export(workbook_path: Path, output_path: Path, check_only: bool):
    with zipfile.ZipFile(workbook_path) as book:
        shared = read_shared_strings(book)
        targets = sheet_targets(book)
        data = {}
        for sheet_name, json_key in SHEET_MAP.items():
            if sheet_name not in targets:
                raise ValueError(f"工作簿缺少工作表：{sheet_name}")
            data[json_key] = parse_sheet(read_rows(book, targets[sheet_name], shared), sheet_name)
    validate(data)

    if not check_only:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with output_path.open("w", encoding="utf-8", newline="\n") as stream:
            stream.write(json.dumps(data, ensure_ascii=False, indent=2) + "\n")

    counts = ", ".join(f"{name}={len(data[key])}" for name, key in SHEET_MAP.items())
    action = "校验通过" if check_only else f"已导出 {output_path}"
    print(f"{action}\n{counts}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Export CatCafe Excel config to Unity JSON")
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        export(args.input.resolve(), args.output.resolve(), args.check)
        return 0
    except Exception as exception:
        print(f"配置导出失败：{exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
