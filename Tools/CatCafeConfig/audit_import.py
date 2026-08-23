#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""策划原表 → 项目配置 的导入完整性体检。

比的是 Assets/Art/CatCafe/Incoming_2026-08-12/source.xlsx 的「店内对象」表
（153 条 V3 原始设计）和运行时 cat_cafe_config.json 的 Pieces。

查四件事：
  1 有没有漏导 / 多出（按名称匹配）
  2 稀有度对不对（策划表是 普通/稀有/史诗/传奇 四档，
    项目是 common/uncommon/rare/special，按档位序号对应，不是按字面）
  3 「金币」列声明的固定收益，和引擎里那条无条件 income 规则给的数对不对
  4 效果文案有没有跟着策划表走

第 3 条是重点：金币列是策划对这枚棋子的基础定价，rule_text 只是给玩家看的字，
真正决定收益的是 Rules 表。三者对不上就说明导入时漏了一环。

用法：python Tools/CatCafeConfig/audit_import.py [--verbose]
"""

import argparse
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_patch import Workbook  # noqa: E402

DESIGN = os.path.join('Assets', 'Art', 'CatCafe', 'Incoming_2026-08-12', 'source.xlsx')
CONFIG = os.path.join('Assets', 'Resources', 'GameData', 'cat_cafe_config.json')
SHEET = '店内对象'
HEADER_ROW = 4

# 策划表四档 → 项目四档，按档位序号对应。字面不一样是历史遗留：
# 策划叫「稀有」的是第 2 档，项目第 2 档叫 uncommon(少见)。
RARITY = {'普通': 'common', '稀有': 'uncommon', '史诗': 'rare', '传奇': 'special'}


def norm(text):
    """比文案时忽略空白与全角半角标点差异。"""
    text = str(text or '')
    for a, b in (('\\n', ''), ('\n', ''), ('　', ''), (' ', ''),
                 ('，', ','), ('。', '.'), ('；', ';'), ('：', ':')):
        text = text.replace(a, b)
    return text.strip()


def read_design():
    book = Workbook(DESIGN)
    header = None
    rows = {}
    for row in book.rows(SHEET):
        number = int(re.search(r'r="(\d+)"', row).group(1))
        values = book.row_values(row)
        if number == HEADER_ROW:
            header = {v: k for k, v in values.items() if v}
            continue
        if header is None or number <= HEADER_ROW:
            continue
        name = values.get(header.get('名称', ''), '')
        if not name:
            continue
        rows[name.strip()] = {
            'no': values.get(header.get('编号', ''), ''),
            'type': values.get(header.get('类型', ''), ''),
            'rarity': values.get(header.get('稀有度', ''), '').strip(),
            'coins': values.get(header.get('金币', ''), '').strip(),
            'effect': values.get(header.get('效果', ''), ''),
        }
    return rows


def base_income(rules, key):
    """引擎里这枚棋子的无条件每波收益：trigger=round、operation=income、
    主副作用域都为空且比较符恒真的那条规则的 base_value 之和。

    带 consume_self 的不算——那种是"结算一次就离场"的一次性收益（现金礼袋 +10、
    斯芬克斯猫离场 +8），策划表的「金币」列记的是每波固定值，两者不是一回事。
    一条无条件规则都没有 ≡ 每波固定 0 金币，按 0 处理。
    """
    total = 0
    for rule in rules:
        if not rule.get('enabled') or rule.get('owner_key') != key:
            continue
        if rule.get('trigger') != 'round' or rule.get('operation') != 'income':
            continue
        if rule.get('consume_self'):
            continue
        if (rule.get('primary_scope') or 'none') not in ('none', ''):
            continue
        if (rule.get('secondary_scope') or 'none') not in ('none', ''):
            continue
        if (rule.get('primary_comparator') or 'always') != 'always':
            continue
        if (rule.get('secondary_comparator') or 'always') != 'always':
            continue
        total += int(rule.get('base_value') or 0)
    return total


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--verbose', action='store_true')
    args = parser.parse_args()

    design = read_design()
    with open(CONFIG, encoding='utf-8') as handle:
        cfg = json.load(handle)
    elements = {e['name']: e for e in cfg['elements'] if e.get('name')}
    rules = cfg['rules']

    missing = sorted(n for n in design if n not in elements)
    extra = sorted(n for n in elements if n not in design)
    bad_rarity, bad_coins, bad_effect = [], [], []

    for name, row in sorted(design.items()):
        element = elements.get(name)
        if element is None:
            continue
        want = RARITY.get(row['rarity'])
        got = element.get('rarity')
        if want and got and want != got:
            bad_rarity.append((name, row['rarity'], want, got))

        if row['coins'] not in ('', None):
            try:
                want_coins = int(float(row['coins']))
            except ValueError:
                want_coins = None
            if want_coins is not None:
                got_coins = base_income(rules, element['key'])
                if got_coins != want_coins:
                    bad_coins.append((name, element['key'], want_coins, got_coins))

        if row['effect'] and norm(row['effect']) != norm(element.get('rule_text')):
            bad_effect.append((name, row['effect'], element.get('rule_text') or ''))

    print('策划表「%s」%d 条  ·  项目 Pieces %d 条' % (SHEET, len(design), len(elements)))

    print('\n── 1 漏导：策划表有、项目没有（%d）──' % len(missing))
    for n in missing:
        print('   %-16s 编号%s %s' % (n, design[n]['no'], design[n]['type']))
    if not missing:
        print('   无')

    print('\n── 2 稀有度对不上（%d）──' % len(bad_rarity))
    for name, raw, want, got in bad_rarity:
        print('   %-16s 策划=%s(应为%s)  项目=%s' % (name, raw, want, got))
    if not bad_rarity:
        print('   无')

    print('\n── 3 固定金币对不上（%d）──' % len(bad_coins))
    for name, key, want, got in bad_coins:
        print('   %-16s %-16s 策划=%s  引擎实际=%s' % (name, key, want, got))
    if not bad_coins:
        print('   无')

    print('\n── 4 效果文案与策划表不一致（%d）──' % len(bad_effect))
    limit = len(bad_effect) if args.verbose else 12
    for name, want, got in bad_effect[:limit]:
        print('   %s' % name)
        print('     策划: %s' % want.replace('\n', ' / ')[:88])
        print('     项目: %s' % got.replace('\n', ' / ')[:88])
    if len(bad_effect) > limit:
        print('   …还有 %d 条（--verbose 看全部）' % (len(bad_effect) - limit))
    if not bad_effect:
        print('   无')

    print('\n── 5 项目多出、策划表没有的（%d）──' % len(extra))
    if args.verbose:
        for n in extra:
            print('   %-16s %s' % (n, elements[n].get('key')))
    else:
        print('   ' + '、'.join(extra[:20]) + ('…' if len(extra) > 20 else ''))

    bad = bool(missing or bad_rarity or bad_coins)
    print('\n结论：%s' % ('导入完整' if not bad else '有偏差，见上'))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
