#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""联动体检：收入构成拆解 + 完美摆位的天花板。

要回答的是「联动太少」还是「联动够不着」——处方完全相反：
  太少   → 加联动牌、加强联动数值
  够不着 → 给玩家摆位控制权，加牌只会加大方差

三个量：
  1 收入构成：一波收入里，无条件固定项 / 条件项（相邻·场上计数）/ 道具乘区各占多少
  2 摆位天花板：同一批棋子，随机落座 vs 贪心最优落座，收入差多少
  3 两张牌相邻的概率：4×4 盘面上任意两张同时在场的牌正好相邻的几率

用法：python Tools/CatCafeConfig/synergy_report.py
"""

import itertools
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import balance_sim as sim  # noqa: E402
from balance_sim import BOARD_COLUMNS, BOARD_ROWS, BOARD_SIZE, Config, Piece, Run  # noqa: E402


def is_flat(rule):
    return (rule.get('primary_scope') in ('none', '', None)
            and rule.get('primary_comparator') in ('always', '', None)
            and rule.get('secondary_scope') in ('none', '', None)
            and rule.get('secondary_comparator') in ('always', '', None))


def make_probe(cfg):
    run = Run.__new__(Run)
    run.cfg = cfg
    run.rng = random.Random(0)
    run.pool = []
    run.items = []
    run.consumed = 0
    run.round_index = 3
    run.unsupported = __import__('collections').Counter()
    return run


def cell_income(run, board, index, only=None):
    """算某一格的收益。only='flat' 只算无条件项，'cond' 只算条件项，None 全算。"""
    piece = board[index]
    if piece is None:
        return 0
    nearby = run.neighbors(board, index)
    total = 0
    for rule in run.cfg.round_rules.get(piece.key, []) + run.cfg.round_rules.get('*', []):
        if rule.get('operation') != 'income' or not run.matches_source(rule, piece):
            continue
        if only == 'flat' and not is_flat(rule):
            continue
        if only == 'cond' and is_flat(rule):
            continue
        primary = run.scope(rule.get('primary_scope'), rule.get('primary_filter'),
                            piece, index, board, nearby, 0)
        secondary = run.scope(rule.get('secondary_scope'), rule.get('secondary_filter'),
                              piece, index, board, nearby, 0)
        if not sim.passes(rule.get('primary_comparator'), primary,
                          int(rule.get('primary_threshold') or 0)):
            continue
        if not sim.passes(rule.get('secondary_comparator'), secondary,
                          int(rule.get('secondary_threshold') or 0)):
            continue
        total += sim.rule_value(rule, primary, secondary)
    return total


def board_total(run, board, only=None):
    return sum(cell_income(run, board, i, only) for i in range(BOARD_SIZE))


def best_arrangement(run, pieces, restarts=6, sweeps=40):
    """贪心 + 多次重启的换位爬山，逼近「完美摆位」的收入上限。"""
    best = None
    for attempt in range(restarts):
        board = list(pieces) + [None] * (BOARD_SIZE - len(pieces))
        run.rng.shuffle(board)
        current = board_total(run, board)
        for _ in range(sweeps):
            improved = False
            for a, b in itertools.combinations(range(BOARD_SIZE), 2):
                if board[a] is None and board[b] is None:
                    continue
                board[a], board[b] = board[b], board[a]
                value = board_total(run, board)
                if value > current:
                    current = value
                    improved = True
                else:
                    board[a], board[b] = board[b], board[a]
            if not improved:
                break
        if best is None or current > best:
            best = current
    return best


def main():
    cfg = Config()
    run = make_probe(cfg)
    rng = random.Random(20260818)

    # 取真实对局第 4 天的名册
    play = Run(cfg, random.Random(20260818), sim.greedy_policy)
    for i, stage in enumerate(cfg.stages[:4]):
        play.day = i + 1
        for r in range(int(stage['rounds'])):
            play.play_round()
            if r < int(stage['rounds']) - 1:
                choice = sim.greedy_policy(play, play.reward_options(stage['rarity_context']),
                                           stage['rarity_context'])
                if choice:
                    play.add_piece(choice)
        play.money = max(0, play.money - int(stage['target']))
    roster = [p.key for p in play.pool]
    run.items = list(play.items)
    print(f'样本：真实对局第 4 天名册 {len(roster)} 张，持有道具 {len(run.items)} 件\n')

    # ── 1 收入构成 ──
    flats, conds, totals = [], [], []
    boards = []
    for _ in range(400):
        keys = rng.sample(roster, min(BOARD_SIZE, len(roster)))
        board = [Piece(k, cfg.elements[k]['kind'], i) for i, k in enumerate(keys)]
        board += [None] * (BOARD_SIZE - len(board))
        rng.shuffle(board)
        run.pool = [p for p in board if p is not None]
        boards.append(board)
        flats.append(board_total(run, board, 'flat'))
        conds.append(board_total(run, board, 'cond'))
        totals.append(board_total(run, board))
    flat, cond, total = statistics.mean(flats), statistics.mean(conds), statistics.mean(totals)
    print('── 1 收入构成（每波，未计道具乘区）──')
    print(f'  无条件固定项  {flat:6.1f}  {flat / total:5.0%}')
    print(f'  条件项（相邻/场上计数/连锁）  {cond:6.1f}  {cond / total:5.0%}')
    print(f'  合计          {total:6.1f}')

    # ── 2 摆位天花板 ──
    print('\n── 2 摆位天花板（同一批棋子，随机落座 vs 最优落座）──')
    gains = []
    for board in boards[:30]:
        pieces = [p for p in board if p is not None]
        run.pool = pieces
        actual = board_total(run, board)
        best = best_arrangement(run, pieces)
        gains.append((actual, best))
    mean_actual = statistics.mean(a for a, _ in gains)
    mean_best = statistics.mean(b for _, b in gains)
    print(f'  随机落座  {mean_actual:6.1f}')
    print(f'  最优落座  {mean_best:6.1f}')
    print(f'  可争取的空间  +{mean_best - mean_actual:.1f}  （+{(mean_best / mean_actual - 1):.0%}）')

    # ── 3 两张牌相邻的概率 ──
    pairs = 0
    for row in range(BOARD_ROWS):
        for col in range(BOARD_COLUMNS):
            if col + 1 < BOARD_COLUMNS:
                pairs += 1
            if row + 1 < BOARD_ROWS:
                pairs += 1
    total_pairs = BOARD_SIZE * (BOARD_SIZE - 1) // 2
    n = len(roster)
    appear = min(1.0, BOARD_SIZE / n)
    print('\n── 3 联动能不能撞上 ──')
    print(f'  4×4 盘面相邻对 {pairs} / 总对数 {total_pairs} → 两张都在场时相邻概率 {pairs / total_pairs:.0%}')
    print(f'  名册 {n} 张时单张出场率 {appear:.0%}，两张同时出场 {appear * (BOARD_SIZE - 1) / (n - 1):.0%}')
    print(f'  → 指定的两张牌，某一波正好相邻的概率 '
          f'{appear * (BOARD_SIZE - 1) / (n - 1) * pairs / total_pairs:.1%}')

    # ── 4 有条件项的牌占比 ──
    base = [k for k, e in cfg.elements.items()
            if e.get('unlock') == 'base' and e.get('pool_rarity')]
    with_cond = [k for k in base
                 if any(r.get('operation') == 'income' and not is_flat(r)
                        for r in cfg.round_rules.get(k, []))]
    print(f'\n── 4 奖励池里带条件项的牌 {len(with_cond)}/{len(base)} = '
          f'{len(with_cond) / len(base):.0%} ──')


if __name__ == '__main__':
    main()
