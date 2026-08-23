#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按通关率标定目标曲线：第 1 关 100%，整局 50%（挂在 casual 策略上）。

第 1 关取「N 局里最差那局的第 1 天收入」再留一点余量，保证零失败；
第 2-6 关按一个统一缩放系数搜索，命中整局目标通关率。
"""
import argparse
import statistics
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import balance_sim as sim


def play(cfg, seeds, policy):
    return [sim.simulate(cfg, s, policy) for s in seeds]


def build(targets):
    def variant(cfg):
        cfg.tuned_targets = targets
        sim.variant_v3(cfg)
    return variant


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--runs', type=int, default=120)
    ap.add_argument('--goal', type=float, default=0.50)
    ap.add_argument('--shape', default='176,262,366')
    args = ap.parse_args()
    seeds = [20260818 + i for i in range(args.runs)]
    casual = sim.POLICIES['casual']

    # ── 第 1 关：找收入下限 ──
    probe = sim.Config(variant=build([1, 9999, 9999, 9999, 9999, 9999]))
    runs = play(probe, seeds, casual)
    d1 = [r.day_income[0] for r in runs]
    floor = min(d1)
    t1 = max(4, int(floor * 0.85))
    print(f'第 1 天收入：最差 {floor}｜P10 {sorted(d1)[len(d1)//10]}｜中位 '
          f'{statistics.median(d1):.0f} → 目标定 {t1}（最差局的 85%）')

    # ── 第 2-6 关：搜索缩放系数命中整局通关率 ──
    shape = [52, 104] + [int(x) for x in args.shape.split(',')]
    lo, hi = 0.5, 2.5
    best = None
    for _ in range(7):
        k = (lo + hi) / 2
        targets = [t1] + [max(1, int(round(v * k / 2) * 2)) for v in shape]
        cfg = sim.Config(variant=build(targets))
        runs = play(cfg, seeds, casual)
        rate = sum(1 for r in runs if r.failed_day is None) / len(runs)
        d1_fail = sum(1 for r in runs if r.failed_day == 1)
        print(f'  k={k:.3f} 目标={targets} → 通关 {rate:.0%}（第1天失败 {d1_fail} 局）')
        best = (targets, rate, runs)
        if rate > args.goal:
            lo = k
        else:
            hi = k
    targets, rate, runs = best
    print(f'\n标定结果：{targets}｜casual 通关率 {rate:.0%}')
    for name in ('greedy', 'casual', 'random'):
        cfg = sim.Config(variant=build(targets))
        rs = play(cfg, seeds, sim.POLICIES[name])
        ok = sum(1 for r in rs if r.failed_day is None) / len(rs)
        from collections import Counter
        fails = Counter(r.failed_day for r in rs if r.failed_day)
        print(f'  {name:<8}{ok:>5.0%}  失败天 ' +
              ('  '.join(f'D{d}:{c}' for d, c in sorted(fails.items())) or '—'))


if __name__ == '__main__':
    main()
