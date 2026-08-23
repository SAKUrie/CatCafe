#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""棋子收益总表：把每张牌的裸收益、条件收益、离场收益和实测期望拉成一张表。

裸收益  = trigger=round / operation=income 且无条件（scope=none、comparator=always）的 base_value 之和
条件收益 = 其余 income 规则，按公式展开
实测期望 = 把这张牌放进随机棋盘量 N 次的平均单格收益（含相邻协同，不含道具乘区）
        中局盘面 = 12 张随机 base 牌，接近第 4 天的实际构成
离场收益 = trigger=on_dismiss 的回报

用法：
    python Tools/CatCafeConfig/piece_income_table.py            # 打印
    python Tools/CatCafeConfig/piece_income_table.py --md 输出.md
"""

import argparse
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from balance_sim import (BOARD_SIZE, Config, Piece, Run, contains_token,  # noqa: E402
                         passes, rule_value)

RARITY_ORDER = ['common', 'uncommon', 'rare', 'special']
KIND_LABEL = {'cat': '猫', 'kitten': '幼崽', 'prop': '物件', 'staff': '员工', 'guest': '客人'}
SCOPE_LABEL = {
    'adjacent_cats': '相邻猫数', 'adjacent_kind': '相邻同类数', 'adjacent_key': '相邻指定牌数',
    'board_cats': '场上猫数', 'board_kind': '场上同类数', 'board_key': '场上指定牌数',
    'same_row_key': '同排指定牌数', 'board_distinct_cat_color': '场上不同毛色数',
    'connected_same': '同名连通数', 'adjacent_empty': '相邻空位数', 'round_number': '波次序号',
    'instance_rounds': '本实例营业次数', 'pool_cats': '名册猫数', 'owned_items': '持有物件数',
    'round_income': '本波收入', 'consumed_total': '累计消耗数',
}


def is_flat(rule):
    return (rule.get('primary_scope') in ('none', '', None)
            and rule.get('primary_comparator') in ('always', '', None))


def formula(rule):
    """把一条 income 规则写成人能读的式子。"""
    parts = []
    base = int(rule.get('base_value') or 0)
    if base:
        parts.append(str(base))
    pf = int(rule.get('primary_factor') or 0)
    if pf:
        scope = SCOPE_LABEL.get(rule.get('primary_scope'), rule.get('primary_scope'))
        filt = rule.get('primary_filter')
        if filt:
            scope += f'({filt})'
        div = int(rule.get('divisor') or 1)
        term = f'{scope}' + (f'÷{div}' if div > 1 else '')
        parts.append(f'{term}×{pf}')
    sf = int(rule.get('secondary_factor') or 0)
    if sf:
        scope = SCOPE_LABEL.get(rule.get('secondary_scope'), rule.get('secondary_scope'))
        parts.append(f'{scope}×{sf}')
    cf = int(rule.get('cross_factor') or 0)
    if cf:
        parts.append(f'(主×次)×{cf}')
    body = ' + '.join(parts) or '0'
    cond = []
    cmp_map = {'ge': '≥', 'gt': '>', 'le': '≤', 'lt': '<', 'eq': '=', 'ne': '≠'}
    pc = rule.get('primary_comparator')
    if pc and pc != 'always':
        scope = SCOPE_LABEL.get(rule.get('primary_scope'), rule.get('primary_scope'))
        if pc == 'modulo_zero':
            cond.append(f'{scope}是{rule.get("primary_threshold")}的倍数')
        else:
            cond.append(f'{scope}{cmp_map.get(pc, pc)}{rule.get("primary_threshold")}')
    sc = rule.get('secondary_comparator')
    if sc and sc != 'always':
        scope = SCOPE_LABEL.get(rule.get('secondary_scope'), rule.get('secondary_scope'))
        cond.append(f'{scope}{cmp_map.get(sc, sc)}{rule.get("secondary_threshold")}')
    return body + ('（' + '且'.join(cond) + '）' if cond else '')


def dismiss_text(cfg, key):
    out = []
    for rule in cfg.dismiss_rules.get(key, []):
        op = rule.get('operation')
        base = int(rule.get('base_value') or 0)
        if op == 'income':
            out.append(f'{base}金币')
        elif op == 'add_removal':
            out.append(f'+{base}下班券')
        elif op == 'add_reroll':
            out.append(f'+{base}招呼券')
        elif op == 'generate':
            child = cfg.elements.get(rule.get('result_key'), {}).get('name', rule.get('result_key'))
            out.append(f'→{child}×{max(1, int(rule.get("result_count") or 1))}')
    return '、'.join(out)


def realistic_filler(cfg, seed=20260818, day=4):
    """用真实对局第 N 天的名册当填充，别拿 44 张 base 牌等概率糊弄。"""
    import balance_sim as sim
    rng = random.Random(seed)
    run = sim.Run(cfg, rng, sim.greedy_policy)
    for stage_index, stage in enumerate(cfg.stages[:day]):
        run.day = stage_index + 1
        for r in range(int(stage['rounds'])):
            run.play_round()
            if r < int(stage['rounds']) - 1:
                choice = sim.greedy_policy(run, run.reward_options(stage['rarity_context']),
                                           stage['rarity_context'])
                if choice:
                    run.add_piece(choice)
        run.money = max(0, run.money - int(stage['target']))
    return [p.key for p in run.pool]


def measure(cfg, key, filler_keys, rng, samples=400):
    """把这张牌塞进随机中局盘面，量它自己那一格的收益。"""
    element = cfg.elements[key]
    rules = cfg.round_rules.get(key, []) + cfg.round_rules.get('*', [])
    if not any(r.get('operation') in ('income', 'remove_targets') for r in rules):
        return 0.0, 0.0
    run = Run.__new__(Run)          # 只借用 scope/neighbors，不跑完整局
    run.cfg = cfg
    run.pool = []
    run.items = []
    run.consumed = 0
    run.round_index = 3
    run.unsupported = __import__('collections').Counter()
    probe = Piece(key, element['kind'], -1)
    values = []
    for _ in range(samples):
        filler = [Piece(k, cfg.elements[k]['kind'], i)
                  for i, k in enumerate(rng.choices(filler_keys, k=11))]
        board = filler + [None] * (BOARD_SIZE - 1 - len(filler))
        rng.shuffle(board)
        index = rng.randrange(BOARD_SIZE)
        board.insert(index, probe)
        board = board[:BOARD_SIZE]
        run.pool = [p for p in board if p is not None]
        nearby = run.neighbors(board, index)
        amount = 0
        for rule in rules:
            if rule.get('operation') not in ('income', 'remove_targets'):
                continue
            if rule.get('owner_type') == 'element':
                owner = rule.get('owner_key')
                if owner and owner != '*' and owner != key:
                    continue
            if not (contains_token(rule.get('source_kinds'), probe.kind)
                    and contains_token(rule.get('source_keys'), probe.key)):
                continue
            primary = run.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                                probe, index, board, nearby, 0)
            secondary = run.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                                  probe, index, board, nearby, 0)
            if not passes(rule.get('primary_comparator'), primary, int(rule.get('primary_threshold') or 0)):
                continue
            if not passes(rule.get('secondary_comparator'), secondary, int(rule.get('secondary_threshold') or 0)):
                continue
            if rule.get('operation') == 'income':
                amount += rule_value(rule, primary, secondary)
            else:
                # remove_targets：拆掉相邻目标，按条数或目标裸收益倍数结算
                targets = [n for n in nearby if n is not None
                           and contains_token(rule.get('target_filter'), n.key)]
                limit = int(rule.get('target_limit') or 0) or len(targets)
                targets = targets[:limit]
                if targets:
                    amount += (int(rule.get('base_value') or 0)
                               + len(targets) * int(rule.get('primary_factor') or 0))
                    if rule.get('target_value_mode') == 'base_income':
                        factor = float(rule.get('multiplier') or 0) or 1.0
                        base_sum = sum(
                            sum(int(x.get('base_value') or 0)
                                for x in cfg.round_rules.get(t.key, [])
                                if x.get('operation') == 'income' and is_flat(x))
                            for t in targets)
                        amount += round(base_sum * factor)
        values.append(amount)
    return statistics.mean(values), max(values)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--md', default=None, help='额外写出 markdown 文件')
    parser.add_argument('--all', action='store_true', help='含未解锁（breed/recipe/mutation/invite）棋子')
    args = parser.parse_args()

    cfg = Config()
    rng = random.Random(20260818)
    filler_keys = realistic_filler(cfg)
    print('# 填充盘面取自真实对局第 4 天的名册（%d 张）' % len(filler_keys))
    print()

    rows = []
    for key, element in cfg.elements.items():
        if not args.all and element.get('unlock') != 'base':
            continue
        rules = [r for r in cfg.round_rules.get(key, []) if r.get('operation') == 'income']
        flat = sum(int(r.get('base_value') or 0) for r in rules if is_flat(r))
        conds = [formula(r) for r in rules if not is_flat(r)]
        mean, top = measure(cfg, key, filler_keys, rng)
        rows.append({
            'key': key,
            'name': element.get('name', key),
            'kind': KIND_LABEL.get(element.get('kind'), element.get('kind')),
            'rarity': element.get('pool_rarity') or element.get('rarity') or '—',
            'unlock': element.get('unlock'),
            'flat': flat,
            'cond': '；'.join(conds),
            'ev': mean,
            'sd': top,
            'dismiss': dismiss_text(cfg, key),
        })

    order = {r: i for i, r in enumerate(RARITY_ORDER)}
    rows.sort(key=lambda r: (order.get(r['rarity'], 9), -r['ev'], r['key']))

    header = f"{'名称':<12}{'类':<5}{'池':<9}{'裸':>3}{'期望':>7}{'上限':>6}  {'离场':<14}条件收益"
    lines = [header, '─' * 120]
    current = None
    for r in rows:
        if r['rarity'] != current:
            current = r['rarity']
            lines.append(f'── {current} ──')
        name = r['name'][:11]
        pad = 12 - sum(2 if ord(c) > 127 else 1 for c in name)
        lines.append(f"{name}{' ' * max(1, pad)}{r['kind']:<4}{r['rarity']:<9}"
                     f"{r['flat']:>3}{r['ev']:>7.2f}{r['sd']:>6.2f}  {r['dismiss'] or '—':<14}{r['cond']}")
    print('\n'.join(lines))

    ev_by = {}
    for r in rows:
        ev_by.setdefault(r['rarity'], []).append(r['ev'])
    print('\n── 稀有度均值 ──')
    for rarity in RARITY_ORDER:
        vals = ev_by.get(rarity)
        if not vals:
            continue
        print(f'{rarity:<10} n={len(vals):<3} 期望均值 {statistics.mean(vals):.2f}  '
              f'中位 {statistics.median(vals):.2f}  区间 [{min(vals):.2f}, {max(vals):.2f}]')

    if args.md:
        md = ['| 名称 | 类 | 池 | 裸收益 | 实测期望 | 上限 | 离场收益 | 条件收益 |',
              '|---|---|---|---:|---:|---:|---|---|']
        for r in rows:
            md.append(f"| {r['name']} | {r['kind']} | {r['rarity']} | {r['flat']} | "
                      f"{r['ev']:.2f} | {r['sd']:.2f} | {r['dismiss'] or '—'} | {r['cond'] or '—'} |")
        with open(args.md, 'w', encoding='utf-8') as handle:
            handle.write('\n'.join(md) + '\n')
        print(f'\n已写出 {args.md}')


if __name__ == '__main__':
    main()
