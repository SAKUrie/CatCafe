#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 tune_curve.py 标定出的目标曲线写回策划表。

数值源头是 GameDesign/CatCafeGameConfig.xlsx 的 Stages 表，只改 JSON 会在下次
导出时被覆盖，所以这里改表、再跑 export_config.py 重新导出。

用法：
    python Tools/CatCafeConfig/apply_target_curve.py --targets 23,37,74,150,179,241
"""
import argparse
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_patch import Workbook  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
BOOK = ROOT / "GameDesign" / "CatCafeGameConfig.xlsx"

NOTE = "按 tune_curve.py 逐关标定：前三关 100%、后三关 90% 条件通关率（casual 策略）"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--targets', required=True, help='六关目标，逗号分隔')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()

    targets = [int(x) for x in args.targets.split(',')]
    book = Workbook(str(BOOK))
    for index, value in enumerate(targets):
        stage_id = str(index + 1)
        book.set_cell('Stages', stage_id, 'target', value)
        print(f'  第 {stage_id} 关 target → {value}')
    if args.dry_run:
        print(f'\n--dry-run：{book.edits} 处改动未写入')
        return 0
    book.save(backup=False)
    print(f'\n已写入 {BOOK.name}（{book.edits} 处）。记得跑 export_config.py 重新导出。')
    print(f'口径：{NOTE}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
