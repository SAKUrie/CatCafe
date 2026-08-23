#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""营业道具（Buff）价值表 + 全棋子离场收益表。

道具分两类，量法不同：
  盘面类（modify_income / round_end / adjacency）——在同一批随机盘面上开关道具做
      配对比较，直接给出「每波多少金币」，噪声极低。
  对局类（rarity_weights / reward_options / on_choose / on_consume /
      stage_deadline）——它们不改单波结算，只改奖励流或券流，必须整局 A/B：
      同一批种子跑有/无两组，比总收入。

用法：
    python Tools/CatCafeConfig/item_value_table.py
    python Tools/CatCafeConfig/item_value_table.py --runs 80 --md 输出.md
"""

import argparse
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import balance_sim as sim  # noqa: E402
from balance_sim import BOARD_SIZE, Config, Piece, Run  # noqa: E402

BOARD_TRIGGERS = {'modify_income', 'round_end', 'adjacency'}


def item_triggers(cfg, key):
    out = set()
    for rule in cfg.modify_rules:
        if rule.get('owner_key') == key:
            out.add('modify_income')
    for rule in cfg.round_end_rules:
        if rule.get('owner_key') == key:
            out.add('round_end')
    for trigger, rules in cfg.other_rules.items():
        if any(r.get('owner_key') == key for r in rules):
            out.add(trigger)
    return out


def sample_boards(cfg, seed=20260818, day=4, count=600):
    """取真实对局第 day 天的名册，抽 count 张盘面固定下来，供配对比较。"""
    rng = random.Random(seed)
    run = Run(cfg, rng, sim.greedy_policy)
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
    roster = [p.key for p in run.pool]
    boards = []
    picker = random.Random(seed + 1)
    for _ in range(count):
        keys = picker.sample(roster, min(BOARD_SIZE, len(roster)))
        board = [Piece(k, cfg.elements[k]['kind'], i) for i, k in enumerate(keys)]
        board += [None] * (BOARD_SIZE - len(board))
        picker.shuffle(board)
        boards.append(board)
    return roster, boards


def board_income(cfg, board, items):
    """复用 Run 的结算路径，只算一波的总收入。"""
    probe = Run.__new__(Run)
    probe.cfg = cfg
    probe.rng = random.Random(0)
    probe.pool = [p for p in board if p is not None]
    probe.items = list(items)
    probe.consumed = 0
    probe.round_index = 3
    probe.money = 0
    probe.unsupported = __import__('collections').Counter()
    probe.round_income = []
    probe.removal = 0
    probe.reroll = 0
    probe.dismissals = 0
    probe.transfers = 0
    probe.day = 4
    probe.uid = 999
    probe._fixed_board = board
    original = Run.build_board
    Run.build_board = lambda self: [
        Piece(p.key, p.kind, p.uid) if p is not None else None for p in self._fixed_board]
    try:
        return probe.play_round()
    finally:
        Run.build_board = original


def measure_board_items(cfg, boards, keys):
    base = [board_income(cfg, b, []) for b in boards]
    rows = []
    for key in keys:
        with_item = [board_income(cfg, b, [key]) for b in boards]
        deltas = [w - b for w, b in zip(with_item, base)]
        rows.append((key, statistics.mean(deltas), statistics.mean(base),
                     sum(1 for d in deltas if d > 0) / len(deltas)))
    return rows, statistics.mean(base)


def measure_run_items(cfg, keys, runs, seed=20260818):
    """整局 A/B：同一批种子，强制持有该道具 vs 不持有。"""
    def run_with(forced, n):
        out = []
        for i in range(n):
            rng = random.Random(seed + i)
            run = Run(cfg, rng, sim.greedy_policy)
            if forced:
                run.items.append(forced)
            for stage_index, stage in enumerate(cfg.stages):
                run.day = stage_index + 1
                rounds = int(stage['rounds'])
                for r in range(rounds):
                    run.play_round()
                    sim.dismiss_policy(run, int(stage['target']), rounds - r - 1)
                    if r < rounds - 1:
                        choice = sim.greedy_policy(
                            run, run.reward_options(stage['rarity_context']),
                            stage['rarity_context'])
                        if choice:
                            run.add_piece(choice)
                            for rule in cfg.other_rules.get('on_choose', []):
                                if rule.get('owner_key') in run.items and rule.get('operation') == 'income':
                                    run.money += int(rule.get('base_value') or 0)
                if run.money < int(stage['target']):
                    break
                run.money -= int(stage['target'])
            out.append(sum(run.round_income) + run.money * 0)
        return out

    base = run_with(None, runs)
    rows = []
    for key in keys:
        got = run_with(key, runs)
        deltas = [g - b for g, b in zip(got, base)]
        rows.append((key, statistics.mean(deltas),
                     statistics.pstdev(deltas) / max(1, len(deltas) ** 0.5)))
    return rows, statistics.mean(base)


def dismiss_rows(cfg):
    out = []
    for key, rules in cfg.dismiss_rules.items():
        element = cfg.elements.get(key)
        if not element:
            continue
        gains = []
        for rule in rules:
            op = rule.get('operation')
            base = int(rule.get('base_value') or 0)
            if op == 'income':
                gains.append(f'{base} 金币')
            elif op == 'add_removal':
                gains.append(f'+{base} 下班券')
            elif op == 'add_reroll':
                gains.append(f'+{base} 招呼券')
            elif op == 'generate':
                child = cfg.elements.get(rule.get('result_key'), {}).get(
                    'name', rule.get('result_key'))
                gains.append(f'→ {child}×{max(1, int(rule.get("result_count") or 1))}')
        out.append((element['name'], key, element.get('rarity'),
                    element.get('unlock'), '、'.join(gains)))
    out.sort(key=lambda r: (r[3] != 'base', r[0]))
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runs', type=int, default=80)
    parser.add_argument('--boards', type=int, default=600)
    parser.add_argument('--md', default=None)
    parser.add_argument('--variant', default='current', choices=list(sim.VARIANTS))
    args = parser.parse_args()

    cfg = Config(variant=sim.VARIANTS[args.variant])
    roster, boards = sample_boards(cfg, count=args.boards)
    print(f'# 盘面样本：真实对局第 4 天名册 {len(roster)} 张，抽 {len(boards)} 张盘面\n')

    board_keys, run_keys = [], []
    for key in cfg.items:
        triggers = item_triggers(cfg, key)
        (board_keys if triggers & BOARD_TRIGGERS else run_keys).append(key)

    rows, baseline = measure_board_items(cfg, boards, board_keys)
    rows.sort(key=lambda r: -r[1])
    print(f'── 盘面类道具（基准每波 {baseline:.1f} 金币）──')
    print(f"{'道具':<14}{'品质':<9}{'每波增益':>9}{'相对':>7}{'生效率':>7}  效果")
    for key, delta, base, hit in rows:
        item = cfg.items[key]
        name = item['name'][:12]
        pad = 14 - sum(2 if ord(c) > 127 else 1 for c in name)
        print(f"{name}{' ' * max(1, pad)}{item['rarity']:<9}{delta:>+9.2f}"
              f"{delta / baseline:>+6.0%}{hit:>7.0%}  {item['rule_text'][:34]}")

    run_rows, run_base = measure_run_items(cfg, run_keys, args.runs)
    run_rows.sort(key=lambda r: -r[1])
    print(f'\n── 对局类道具（{args.runs} 局配对 A/B，基准全程 {run_base:.0f} 金币）──')
    print(f"{'道具':<14}{'品质':<9}{'全程增益':>9}{'标准误':>8}  效果")
    for key, delta, se in run_rows:
        item = cfg.items[key]
        name = item['name'][:12]
        pad = 14 - sum(2 if ord(c) > 127 else 1 for c in name)
        print(f"{name}{' ' * max(1, pad)}{item['rarity']:<9}{delta:>+9.1f}{se:>8.1f}  "
              f"{item['rule_text'][:34]}")

    print('\n── 全棋子离场收益 ──')
    print(f"{'名称':<14}{'品质':<9}{'解锁':<10}离场回报")
    for name, key, rarity, unlock, gains in dismiss_rows(cfg):
        pad = 14 - sum(2 if ord(c) > 127 else 1 for c in name[:12])
        print(f"{name[:12]}{' ' * max(1, pad)}{rarity:<9}{unlock:<10}{gains}")

    if args.md:
        md = ['## 盘面类道具', '',
              f'基准每波 {baseline:.1f} 金币（第 4 天名册）', '',
              '| 道具 | 品质 | 每波增益 | 相对 | 生效率 | 效果 |', '|---|---|---:|---:|---:|---|']
        for key, delta, base, hit in rows:
            item = cfg.items[key]
            md.append(f"| {item['name']} | {item['rarity']} | {delta:+.2f} | "
                      f"{delta / baseline:+.0%} | {hit:.0%} | {item['rule_text']} |")
        md += ['', '## 对局类道具', '',
               f'{args.runs} 局配对 A/B，基准全程 {run_base:.0f} 金币', '',
               '| 道具 | 品质 | 全程增益 | 标准误 | 效果 |', '|---|---|---:|---:|---|']
        for key, delta, se in run_rows:
            item = cfg.items[key]
            md.append(f"| {item['name']} | {item['rarity']} | {delta:+.1f} | {se:.1f} | "
                      f"{item['rule_text']} |")
        md += ['', '## 全棋子离场收益', '',
               '| 名称 | 品质 | 解锁 | 离场回报 |', '|---|---|---|---|']
        for name, key, rarity, unlock, gains in dismiss_rows(cfg):
            md.append(f'| {name} | {rarity} | {unlock} | {gains} |')
        with open(args.md, 'w', encoding='utf-8') as handle:
            handle.write('\n'.join(md) + '\n')
        print(f'\n已写出 {args.md}')


if __name__ == '__main__':
    main()
