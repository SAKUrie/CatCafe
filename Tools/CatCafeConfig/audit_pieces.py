#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""棋子闭包体检：每一枚棋子能不能进场、进场后做不做事、有没有图。

三个方向各查一遍：

  入口  这枚棋子有没有任何途径出现在盘面上？
        初始牌组 / 三选一奖励池 / 规则 generate·transform 产出 /
        幼崽长大(grown_form) / 繁殖表 child·mutation_child / 招募表 child

  出口  它进场之后有没有任何效果？
        自己带规则，或者被别的规则当成来源·过滤器·目标点名

  美术  asset 列指向的图在不在 Resources 下

再反查引用完整性：规则、繁殖、招募、grown_form 里提到的 key 是不是都真实存在。

用法：python Tools/CatCafeConfig/audit_pieces.py [--verbose]
"""

import argparse
import json
import os
import sys
from collections import defaultdict

CONFIG = os.path.join('Assets', 'Resources', 'GameData', 'cat_cafe_config.json')
# 代码里是 Resources.Load("CatCafe/" + asset)，见 CatCafeGameController.LoadConfiguredSprite
ART_ROOT = os.path.join('Assets', 'Resources', 'CatCafe')
ART_EXT = ('.png', '.jpg', '.jpeg', '.psd', '.asset')

# 这些 scope 的 filter 里写的是棋子 key（其余写的是 kind 或稀有度）
KEY_FILTER_HINTS = ('_key', '_keys', 'board_key', 'same_row_key')


def tokens(value):
    """拆 a|b|c。'*' 是通配符（任意棋子），不是具体 key，直接滤掉。"""
    if not value:
        return []
    return [t.strip() for t in str(value).split('|') if t.strip() and t.strip() != '*']


def load():
    with open(CONFIG, encoding='utf-8') as handle:
        return json.load(handle)


def art_exists(asset):
    if not asset:
        return False
    base = os.path.join(ART_ROOT, asset.replace('/', os.sep))
    return any(os.path.exists(base + ext) for ext in ART_EXT)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--verbose', action='store_true', help='把每一类的完整名单都列出来')
    args = parser.parse_args()

    cfg = load()
    elements = {e['key']: e for e in cfg['elements']}
    rules = [r for r in cfg['rules'] if r.get('enabled')]
    items = {i['key'] for i in cfg['items']}

    # ── 入口 ──
    entries = defaultdict(set)          # key -> {入口说明}
    for row in cfg['initialDeck']:
        if row.get('enabled'):
            entries[row['element_key']].add('初始牌组')

    for key, element in elements.items():
        if not element.get('pool_rarity'):
            continue
        if element.get('unlock') == 'base':
            entries[key].add('奖励池(base)')
        else:
            entries[key].add('奖励池(需先解锁)')

    for rule in rules:
        if rule.get('operation') in ('generate', 'transform'):
            for token in tokens(rule.get('result_key')):
                entries[token].add('规则 %s(%s)' % (rule['rule_id'], rule['operation']))

    for key, element in elements.items():
        grown = element.get('grown_form')
        if grown:
            entries[grown].add('%s 长大' % key)

    for row in cfg['breeding']:
        if not row.get('enabled'):
            continue
        for field in ('child', 'mutation_child'):
            if row.get(field):
                entries[row[field]].add('繁殖 %s+%s' % (row['parent_a'], row['parent_b']))

    for row in cfg['invites']:
        if row.get('enabled') and row.get('child'):
            entries[row['child']].add('招募表')

    # ── 出口 ──
    effects = defaultdict(set)          # key -> {它参与了什么}
    for rule in rules:
        rid = rule['rule_id']
        if rule.get('owner_type') == 'element' and rule.get('owner_key') in elements:
            effects[rule['owner_key']].add('自带规则 ' + rid)
        for field in ('source_keys', 'result_key'):
            for token in tokens(rule.get(field)):
                if token in elements:
                    effects[token].add('被 %s 的 %s 点名' % (rid, field))
        for field in ('primary_filter', 'secondary_filter', 'remove_filter', 'target_filter'):
            scope = rule.get(field.replace('_filter', '_scope')) or ''
            if not any(hint in scope for hint in KEY_FILTER_HINTS):
                continue
            for token in tokens(rule.get(field)):
                if token in elements:
                    effects[token].add('被 %s 的 %s 点名' % (rid, field))

    # ── 引用完整性 ──
    dangling = []
    for rule in rules:
        for field in ('source_keys', 'result_key'):
            for token in tokens(rule.get(field)):
                if token not in elements and token not in ('common', 'uncommon', 'rare', 'special'):
                    dangling.append(('规则 ' + rule['rule_id'], field, token))
    for key, element in elements.items():
        if element.get('grown_form') and element['grown_form'] not in elements:
            dangling.append(('棋子 ' + key, 'grown_form', element['grown_form']))
    for row in cfg['breeding']:
        if not row.get('enabled'):
            continue
        for field in ('parent_a', 'parent_b', 'child', 'mutation_child'):
            value = row.get(field)
            if value and value != '*' and value not in elements:
                dangling.append(('繁殖 %s+%s' % (row['parent_a'], row['parent_b']), field, value))
    for row in cfg['invites']:
        if not row.get('enabled'):
            continue
        for field in ('child', 'inviter_a', 'inviter_b'):
            value = row.get(field)
            if value and value != '*' and value not in elements:
                dangling.append(('招募 ' + str(row.get('child')), field, value))

    # ── 汇总 ──
    no_entry = sorted(k for k in elements if k not in entries)
    no_effect = sorted(k for k in elements if k not in effects)
    no_art = sorted(k for k in elements if not art_exists(elements[k].get('asset')))
    orphan = sorted(set(no_entry) & set(no_effect))

    def show(title, keys, extra=None):
        print('\n── %s（%d / %d）──' % (title, len(keys), len(elements)))
        if not keys:
            print('  无')
            return
        limit = len(keys) if args.verbose else 40
        for key in keys[:limit]:
            element = elements[key]
            line = '  %-22s %-10s %-8s %s' % (
                key, element.get('name', ''), element.get('kind', ''),
                element.get('unlock', ''))
            if extra:
                line += '  ' + extra(key)
            print(line)
        if len(keys) > limit:
            print('  …还有 %d 个（--verbose 看全部）' % (len(keys) - limit))

    print('棋子总数 %d（enabled）' % len(elements))
    kinds = defaultdict(int)
    for element in elements.values():
        kinds[element.get('kind', '?')] += 1
    print('按种类：' + '  '.join('%s %d' % (k, v) for k, v in sorted(kinds.items())))

    show('进不了场：没有任何入口', no_entry)
    show('进场了不做事：没有任何规则关联', no_effect,
         lambda k: '(rule_text: %s)' % (elements[k].get('rule_text') or '空')[:24])
    show('缺图：asset 指向的文件不存在', no_art,
         lambda k: 'asset=' + (elements[k].get('asset') or '(空)'))
    show('孤儿：既进不了场也不做事', orphan)

    print('\n── 悬空引用（%d）──' % len(dangling))
    for where, field, token in sorted(dangling):
        print('  %-40s %s = %s' % (where, field, token))
    if not dangling:
        print('  无')

    closed = not (no_entry or no_art or dangling)
    print('\n结论：%s' % ('闭包完整' if closed else '未闭包，见上'))
    return 0 if closed else 1


if __name__ == '__main__':
    sys.exit(main())
