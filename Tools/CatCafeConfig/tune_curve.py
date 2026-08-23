#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按「每关条件通关率」标定目标曲线。

和 tune_targets.py 的区别：那个脚本只盯整局通关率，用一个统一缩放系数拉曲线；
这里是逐关标定——第 N 关的目标只影响第 N 关往后，所以从前往后依次二分即可，
每关都能精确命中各自的通关率，而不是让一个系数去凑六个数。

条件通关率 = 打到这一关的局里，清掉这一关的比例。第 1 关的条件通关率就是
绝对通关率；后面几关都是「已经活到这」的条件概率，和策划口径一致。

用法：
    python Tools/CatCafeConfig/tune_curve.py --runs 300
    python Tools/CatCafeConfig/tune_curve.py --goals 1,1,1,0.7,0.9,0.9
"""
import argparse
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import balance_sim as sim


def build(targets):
    """把一组目标塞进配置，其余数值原样不动。"""
    def variant(cfg):
        for index, stage in enumerate(cfg.stages):
            if index < len(targets):
                stage['target'] = int(targets[index])
    return variant


def play(targets, seeds, policy, config):
    cfg = sim.Config(config, build(targets))
    return [sim.simulate(cfg, seed, policy) for seed in seeds]


def stage_rates(runs, count):
    """每关：(打到这关的局数, 清掉这关的局数, 条件通关率)。"""
    out = []
    for index in range(count):
        reached = [r for r in runs if r.failed_day is None or r.failed_day > index]
        cleared = [r for r in reached if r.cleared_days > index]
        rate = len(cleared) / len(reached) if reached else 0.0
        out.append((len(reached), len(cleared), rate))
    return out


def calibrate(goals, seeds, policy, config, ceiling):
    """逐关二分。第 N 关的目标不影响前 N-1 关，所以从前往后一次过。"""
    targets = [1] * len(goals)
    for index, goal in enumerate(goals):
        low, high = 1, ceiling[index]
        best = 1
        for _ in range(12):
            mid = (low + high) // 2
            probe = list(targets)
            probe[index] = mid
            runs = play(probe, seeds, policy, config)
            reached, cleared, rate = stage_rates(runs, len(goals))[index]
            if rate >= goal:
                best = mid           # 还清得掉，继续往上抬
                low = mid + 1
            else:
                high = mid - 1
            if low > high:
                break
        targets[index] = best
        print(f'  第 {index + 1} 关 → 目标 {best}')
    return targets


def report(targets, seeds, config, goals):
    print(f'\n标定结果：{targets}')
    header = f'\n{"关":>3} {"目标":>6} {"轮":>3} {"到达":>5} {"通关":>5} {"条件通关率":>11} {"目标曲线":>9} {"收入中位":>9}'
    for name in ('casual', 'greedy', 'naive'):
        runs = play(targets, seeds, sim.POLICIES[name], config)
        full = sum(1 for r in runs if r.failed_day is None) / len(runs)
        print(f'\n════════ {name}｜{len(runs)} 局｜整局通关率 {full:.0%} ════════')
        print(header)
        for index, (reached, cleared, rate) in enumerate(stage_rates(runs, len(targets))):
            stage = runs[0].cfg.stages[index]
            vals = [r.day_income[index] for r in runs if len(r.day_income) > index]
            median = statistics.median(vals) if vals else 0
            print(f'{index + 1:>3} {targets[index]:>6} {int(stage["rounds"]):>3} '
                  f'{reached:>5} {cleared:>5} {rate:>10.0%} {goals[index]:>9.0%} {median:>9.0f}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runs', type=int, default=300)
    parser.add_argument('--seed', type=int, default=20260819)
    parser.add_argument('--policy', default='casual',
                        choices=['greedy', 'random', 'hoard', 'casual', 'naive', 'synergy'])
    parser.add_argument('--goals', default='1,1,1,0.7,0.9,0.9',
                        help='每关条件通关率，逗号分隔')
    parser.add_argument('--ceiling', default='200,400,700,1100,1600,2400',
                        help='每关二分上界')
    parser.add_argument('--config', default=sim.CONFIG)
    args = parser.parse_args()

    goals = [float(x) for x in args.goals.split(',')]
    ceiling = [int(x) for x in args.ceiling.split(',')]
    seeds = [args.seed + i for i in range(args.runs)]
    policy = sim.POLICIES[args.policy]

    probe = sim.Config(args.config)
    missing = {k: v for k, v in probe.unsupported_protocol.items() if v}
    if missing:
        print('模拟器未覆盖当前协议，拒绝标定：')
        for key, values in missing.items():
            print(f'  {key}: {values}')
        return 2

    print(f'按 {args.policy} 策略逐关标定（{args.runs} 局/次）：')
    targets = calibrate(goals, seeds, policy, args.config, ceiling)
    report(targets, seeds, args.config, goals)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
