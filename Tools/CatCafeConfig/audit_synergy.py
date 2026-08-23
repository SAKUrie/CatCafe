#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""机制互相触发体检：点名了伙伴的规则，那个伙伴是不是真的凑得到。

棋子是无放回随机上桌、玩家不能摆位（幸运房东 like），所以"A 相邻 B 时生效"
这类规则能不能兑现，取决于三件事：

  1 伙伴存不存在、拿不拿得到（不可达 = 这条规则永远不触发）
  2 两者在不在同一个可获得档次（common 棋子依赖 special 伙伴，等于纸面效果）
  3 相邻要求有多苛刻（要几个、要不要同时满足多个条件）

只报"点名具体棋子"的规则。按 kind / 稀有度过滤的（相邻猫、相邻道具）
基数大得多，不在此列。

用法：python Tools/CatCafeConfig/audit_synergy.py [--verbose]
"""

import argparse
import json
import os
import sys
from collections import defaultdict

CONFIG = os.path.join('Assets', 'Resources', 'GameData', 'cat_cafe_config.json')
RARITY_ORDER = ['common', 'uncommon', 'rare', 'special']
# filter 里写的是棋子 key 的作用域（其余写的是 kind 或稀有度）
KEY_SCOPES = ('_key', '_keys', 'board_key', 'same_row_key')


def tokens(value):
    if not value:
        return []
    return [t.strip() for t in str(value).split('|') if t.strip() and t.strip() != '*']


def reachability(cfg, elements):
    """每枚棋子的获取途径。返回 key -> 途径集合，空集合 = 拿不到。"""
    reach = defaultdict(set)
    for row in cfg['initialDeck']:
        if row.get('enabled'):
            reach[row['element_key']].add('初始牌组')
    for key, element in elements.items():
        if element.get('pool_rarity') and element.get('unlock') == 'base':
            reach[key].add('奖励池')
    for rule in cfg['rules']:
        if not rule.get('enabled') or rule.get('operation') not in ('generate', 'transform'):
            continue
        for token in tokens(rule.get('result_key')):
            reach[token].add('规则产出')
    for key, element in elements.items():
        if element.get('grown_form'):
            reach[element['grown_form']].add('幼崽长大')
    for row in cfg['breeding']:
        if not row.get('enabled'):
            continue
        for field in ('child', 'mutation_child'):
            if row.get(field):
                reach[row[field]].add('繁殖')
    for row in cfg['invites']:
        if row.get('enabled') and row.get('child'):
            reach[row['child']].add('招募')
    return reach


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--verbose', action='store_true')
    args = parser.parse_args()

    with open(CONFIG, encoding='utf-8') as handle:
        cfg = json.load(handle)
    elements = {e['key']: e for e in cfg['elements']}
    items = {i['key']: i for i in cfg['items']}
    reach = reachability(cfg, elements)

    unreachable, gap, ok = [], [], 0
    for rule in cfg['rules']:
        if not rule.get('enabled'):
            continue
        owner = rule.get('owner_key')
        partners = []
        for field in ('primary_filter', 'secondary_filter', 'remove_filter', 'target_filter'):
            scope = rule.get(field.replace('_filter', '_scope')) or ''
            if not any(hint in scope for hint in KEY_SCOPES):
                continue
            for token in tokens(rule.get(field)):
                if token in elements:
                    partners.append((field, token))
        if not partners:
            continue

        owner_def = elements.get(owner) or items.get(owner)
        owner_rarity = (owner_def or {}).get('rarity', '?')
        for field, partner in partners:
            ways = reach.get(partner) or set()
            if not ways:
                unreachable.append((rule['rule_id'], owner, owner_rarity, partner,
                                    elements[partner].get('name'), field))
                continue
            ok += 1
            p_rarity = elements[partner].get('rarity', '?')
            if owner_rarity in RARITY_ORDER and p_rarity in RARITY_ORDER:
                step = RARITY_ORDER.index(p_rarity) - RARITY_ORDER.index(owner_rarity)
                if step >= 2:
                    gap.append((rule['rule_id'], owner, owner_rarity, partner,
                                elements[partner].get('name'), p_rarity, step))

    print('规则总数 %d，其中点名了具体伙伴的引用 %d 处'
          % (len([r for r in cfg['rules'] if r.get('enabled')]), ok + len(unreachable)))

    print('\n── 1 点名了拿不到的伙伴（%d）——这条规则永远不会触发 ──' % len(unreachable))
    for rid, owner, orar, partner, pname, field in unreachable:
        print('   %-40s %s(%s) 需要 %s %s' % (rid, owner, orar, partner, pname))
    if not unreachable:
        print('   无')

    print('\n── 2 稀有度落差 ≥2 档（%d）——低档棋子依赖高档伙伴，实战基本凑不齐 ──' % len(gap))
    limit = len(gap) if args.verbose else 20
    for rid, owner, orar, partner, pname, prar, step in gap[:limit]:
        print('   %-38s %-18s(%s) 需要 %-16s(%s) 差%d档'
              % (rid, owner, orar, partner + ' ' + str(pname), prar, step))
    if len(gap) > limit:
        print('   …还有 %d 条（--verbose）' % (len(gap) - limit))
    if not gap:
        print('   无')

    # 3 被依赖最多的棋子：这些是联动网络的枢纽，一旦抽不到会连累一片
    hub = defaultdict(set)
    for rule in cfg['rules']:
        if not rule.get('enabled'):
            continue
        for field in ('primary_filter', 'secondary_filter', 'remove_filter', 'target_filter'):
            scope = rule.get(field.replace('_filter', '_scope')) or ''
            if not any(hint in scope for hint in KEY_SCOPES):
                continue
            for token in tokens(rule.get(field)):
                if token in elements and rule.get('owner_key') != token:
                    hub[token].add(rule.get('owner_key'))
    print('\n── 3 联动枢纽：被最多其他棋子点名的伙伴 ──')
    for key, owners in sorted(hub.items(), key=lambda kv: -len(kv[1]))[:12]:
        e = elements[key]
        print('   %-18s %-12s %-9s 被 %2d 个对象依赖   获取途径: %s'
              % (key, e.get('name'), e.get('rarity'), len(owners),
                 '/'.join(sorted(reach.get(key) or {'拿不到'}))))

    bad = bool(unreachable)
    print('\n结论：%s' % ('点名的伙伴全部可达' if not bad else '有规则点名了拿不到的伙伴，见上'))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
