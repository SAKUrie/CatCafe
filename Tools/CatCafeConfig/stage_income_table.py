#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按每轮期望收入砍关卡门槛。

为什么不用通关率二分（tune_curve.py 那套）：二分是在完整对局里搜目标，于是
两个东西会混进结果——

  1 死亡截断：目标定高了，弱局在第 3 关就死了，第 4 关的样本只剩强局，
    分布被削掉左尾，看上去"第 4 关很好过"。
  2 余额红利：清关是 money -= target，上一关攒下的余额会顶下一关的目标，
    于是门槛能被抬得比"这一天真能赚到的钱"更高。

这里换成无截断量法：把所有目标压到 1，让每一局都活到最后一关，量出每关
「当天收入」的真实分布，再直接从分位数砍门槛。门槛取第 X 百分位，理论上
就有 100-X% 的局靠当天收入独立达标——不吃余额红利，是保守下界。

用法：
    python Tools/CatCafeConfig/stage_income_table.py --runs 500
    python Tools/CatCafeConfig/stage_income_table.py --goals 1,1,1,0.8,0.8,0.8
"""
import argparse
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import balance_sim as sim


def quantile(values, p):
    """values 需已排序。p=0 取最差局。"""
    if not values:
        return 0
    return values[min(len(values) - 1, int(len(values) * p))]


def measure(config, seeds, policy):
    """把目标压到 1 跑满全程，返回每关的当天收入样本（升序）。"""
    def flatten(cfg):
        for stage in cfg.stages:
            stage['target'] = 1

    cfg = sim.Config(config, flatten)
    runs = [sim.simulate(cfg, seed, policy) for seed in seeds]
    alive = [r for r in runs if r.failed_day is None]
    samples = []
    for index in range(len(cfg.stages)):
        samples.append(sorted(r.day_income[index] for r in alive
                              if len(r.day_income) > index))
    return cfg, alive, samples


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runs', type=int, default=500)
    parser.add_argument('--seed', type=int, default=606060)
    parser.add_argument('--policy', default='casual',
                        choices=['greedy', 'random', 'hoard', 'casual', 'naive', 'synergy'])
    parser.add_argument('--goals', default='1,1,1,0.8,0.8,0.8',
                        help='每关期望通关率；门槛取第 (1-goal) 百分位')
    parser.add_argument('--round-to', type=int, default=5, help='门槛下取整粒度')
    parser.add_argument('--config', default=sim.CONFIG)
    args = parser.parse_args()

    probe = sim.Config(args.config)
    missing = {k: v for k, v in probe.unsupported_protocol.items() if v}
    if missing:
        print('模拟器未覆盖当前协议，拒绝出数：')
        for key, values in missing.items():
            print(f'  {key}: {values}')
        return 2

    goals = [float(x) for x in args.goals.split(',')]
    seeds = [args.seed + i for i in range(args.runs)]
    cfg, alive, samples = measure(args.config, seeds, sim.POLICIES[args.policy])
    print(f'{len(alive)}/{args.runs} 局全程存活（目标压到 1），分布无截断\n')

    print(' 关  轮 |        每轮收入           |          当天累计收入')
    print('        |  中位   均值   P10   P90  |  最差   P10   P25   中位   P75')
    for index, stage in enumerate(cfg.stages):
        rounds = int(stage['rounds'])
        total = samples[index]
        per = sorted(v / rounds for v in total)
        print(f'  {index + 1}  {rounds} | {statistics.median(per):>5.1f} {statistics.mean(per):>6.1f} '
              f'{quantile(per, .1):>5.1f} {quantile(per, .9):>5.1f}  | '
              f'{total[0]:>5} {quantile(total, .1):>5} {quantile(total, .25):>5} '
              f'{statistics.median(total):>5.0f} {quantile(total, .75):>5}')

    step = max(1, args.round_to)
    targets = []
    print(f'\n按目标曲线 {[f"{g:.0%}" for g in goals]} 砍门槛：')
    for index, goal in enumerate(goals):
        percentile = max(0.0, 1.0 - goal)
        raw = quantile(samples[index], percentile)
        value = int(raw) // step * step
        targets.append(value)
        print(f'  第 {index + 1} 关：P{percentile * 100:.0f} = {raw} → 门槛 {value}')
    print(f'\n门槛曲线：{targets}')
    print('（口径：只用当天收入达标，不吃上一关的余额结转，是保守下界）')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
