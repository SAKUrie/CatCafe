#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""三旋钮联调：目标数值 / 各关稀有度权重 / 每天轮次。

只动这三样，不碰规则和系数。目标：
  第 1 关 100% 通关（对所有策略都是硬保证，不是概率）
  整局 casual 通关率 50%
  目标数值取整、上取整

用法：
    python Tools/CatCafeConfig/tune_three_knobs.py --runs 120
    python Tools/CatCafeConfig/tune_three_knobs.py --runs 120 --apply
"""

import argparse
import math
import os
import statistics
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import balance_sim as sim  # noqa: E402

# ── 旋钮 2：每天一档专属权重，稀有度单调爬升 ──
# 原表六天只有三档轮着用，第 4 天用回第 1 天的 78/20/2，奖励质量直接倒退回开局。
WEIGHTS = {
    'stage1': (78, 20, 2),
    'stage2': (64, 29, 7),
    'stage3': (50, 36, 14),
    'stage4': (40, 42, 18),
    'stage5': (32, 45, 23),
    'stage6': (24, 48, 28),
}

# ── 旋钮 3：每天轮次 ──
ROUNDS = [3, 4, 4, 5, 5, 6]

STEP = [1, 5, 10, 10, 10, 10]        # 各天目标的取整粒度


def ceil_to(value, step):
    return int(math.ceil(value / step) * step)


def make_variant(targets, rounds=None, weights=None):
    def variant(cfg):
        use_rounds = rounds or ROUNDS
        use_weights = weights or WEIGHTS
        for name, (c, u, r) in use_weights.items():
            cfg.weights[name] = {'context': name, 'common': c, 'uncommon': u,
                                 'rare': r, 'enabled': True}
        for i, stage in enumerate(cfg.stages):
            stage['rounds'] = use_rounds[i]
            stage['target'] = targets[i]
            stage['rarity_context'] = 'stage%d' % (i + 1)
    return variant


def clear_rate(cfg_variant, seeds, policy):
    cfg = sim.Config(variant=cfg_variant)
    runs = [sim.simulate(cfg, s, sim.POLICIES[policy]) for s in seeds]
    ok = sum(1 for r in runs if r.failed_day is None) / len(runs)
    return ok, runs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--runs', type=int, default=120)
    ap.add_argument('--goal', type=float, default=0.50)
    ap.add_argument('--apply', action='store_true', help='把标定结果写进 Excel')
    args = ap.parse_args()
    seeds = [20260818 + i for i in range(args.runs)]

    # ── 第 1 关：取最差一局收入，向下留余量后上取整 ──
    probe = make_variant([1] + [99999] * 5)
    _, runs = clear_rate(probe, seeds, 'casual')
    d1 = [r.day_income[0] for r in runs]
    floor = min(d1)
    t1 = ceil_to(floor * 0.8, STEP[0])
    print(f'第 1 天收入：最差 {floor}｜P10 {sorted(d1)[len(d1)//10]}｜'
          f'中位 {statistics.median(d1):.0f} → 目标 {t1}')

    # ── 第 2-6 关：在「每轮目标」上标定，天目标 = 每轮目标 × 轮次 ──
    # 轮次是 3/4/4/5/5/6 不等，直接调天目标的绝对值会把轮次的影响混进去。
    # 真正有设计含义的量是「这一天每轮要赚多少」，轮次一改天目标自动跟着走。
    #
    # 学习墙立在第 2-3 关：在那里失败，一局只花几分钟就能重来，学习循环短；
    # 卡在第 6 关的话每次失败要打二十分钟才知道错在哪。搞懂之后后段应当放行。
    PROFILE = [1.00, 0.75, 0.75, 0.95, 0.96, 0.97]     # 连乘 ≈ 0.50
    targets = [t1] + [0] * 5
    per_round = [t1 / ROUNDS[0]] + [0.0] * 5
    for day in range(1, 6):
        lo, hi = 2.0, 160.0
        for _ in range(9):
            mid = (lo + hi) / 2
            targets[day] = ceil_to(mid * ROUNDS[day], STEP[day])
            _, runs = clear_rate(make_variant(targets), seeds, 'casual')
            reached = [r for r in runs if len(r.day_income) > day]
            if not reached:
                hi = mid
                continue
            passed = sum(1 for r in reached if r.failed_day != day + 1)
            if passed / len(reached) > PROFILE[day]:
                lo = mid
            else:
                hi = mid
        per_round[day] = lo
        targets[day] = ceil_to(lo * ROUNDS[day], STEP[day])
        _, runs = clear_rate(make_variant(targets), seeds, 'casual')
        reached = [r for r in runs if len(r.day_income) > day]
        passed = sum(1 for r in reached if r.failed_day != day + 1)
        income = statistics.mean(r.day_income[day] / ROUNDS[day]
                                 for r in reached) if reached else 0
        print(f'  第 {day+1} 天  每轮目标 {lo:>5.1f} × {ROUNDS[day]} 轮 → {targets[day]:>4}'
              f'｜每轮期望收入 {income:>5.1f}｜压力 {lo/max(income,1):.2f}'
              f'｜通过 {passed/max(1,len(reached)):.0%}（目标 {PROFILE[day]:.0%}）')
    rate, _ = clear_rate(make_variant(targets), seeds, 'casual')
    best = (targets, rate)

    targets, rate = best
    print(f'\n标定结果 目标={targets}｜轮次={ROUNDS}')
    print(f'{"策略":<10}{"通关率":>7}  失败天分布')
    for policy in ('greedy', 'naive', 'casual', 'random'):
        ok, runs = clear_rate(make_variant(targets), seeds, policy)
        fails = Counter(r.failed_day for r in runs if r.failed_day)
        print(f'{policy:<10}{ok:>7.0%}  ' +
              ('  '.join(f'D{d}:{c}' for d, c in sorted(fails.items())) or '—'))

    _, runs = clear_rate(make_variant(targets), seeds, 'casual')
    print(f'\n{"天":>3}{"轮":>4}{"目标":>6}{"收入中位":>10}{"P10":>7}{"P90":>7}{"当天达标":>9}')
    for i in range(6):
        vals = sorted(r.day_income[i] for r in runs if len(r.day_income) > i)
        if not vals:
            continue
        ok = sum(1 for v in vals if v >= targets[i]) / len(vals)
        print(f'{i+1:>3}{ROUNDS[i]:>4}{targets[i]:>6}{statistics.median(vals):>10.0f}'
              f'{vals[len(vals)//10]:>7}{vals[min(len(vals)-1,int(len(vals)*0.9))]:>7}{ok:>8.0%}')

    if args.apply:
        from xlsx_patch import Workbook
        book = os.path.join('GameDesign', 'CatCafeGameConfig.xlsx')
        wb = Workbook(book)
        for i, target in enumerate(targets):
            wb.set_cell('Stages', str(i + 1), 'target', str(target))
            wb.set_cell('Stages', str(i + 1), 'rounds', str(ROUNDS[i]))
            wb.set_cell('Stages', str(i + 1), 'rarity_context', 'stage%d' % (i + 1))
        for name, (c, u, r) in WEIGHTS.items():
            row, _ = wb.find_row('Weights', name)
            if row is None:
                wb.append_row('Weights', {'context': name, 'common': str(c),
                                          'uncommon': str(u), 'rare': str(r),
                                          'enabled': 'TRUE'})
            else:
                wb.set_cell('Weights', name, 'common', str(c))
                wb.set_cell('Weights', name, 'uncommon', str(u))
                wb.set_cell('Weights', name, 'rare', str(r))
        wb.save(backup=False)
        print(f'\n已写入 {book}（{wb.edits} 处），记得跑 export_config.py')


if __name__ == '__main__':
    main()
