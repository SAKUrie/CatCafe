#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 v3 数值方案写进 CatCafeGameConfig.xlsx。

方案来源：Tools/CatCafeConfig/balance_sim.py 的蒙特卡洛标定
  - 道具品质梯度：common +1~2.5 / uncommon +2.5~3.5 / rare +3~5 / special +8（每波，基准 30）
  - 目标曲线按通关率标定：第 1 关 100%（三种策略零失败），整局 casual 50%
  - 送走＝浓缩：离场时把身价按稀有度永久转移给同类最强者，突破 16 格产出天花板

跑完记得导出：python Tools/CatCafeConfig/export_config.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_patch import Workbook  # noqa: E402

BOOK = os.path.join('GameDesign', 'CatCafeGameConfig.xlsx')

# ── Rules：道具数值与触发条件 ──
RULE_EDITS = [
    ('v3item_107_income', {'base_value': '8'}, '特殊档：每波 +4 → +8'),
    ('v3item_083_income', {'base_value': '5'}, '稀有档：每波 +3 → +5'),
    ('v3item_066_income', {'base_value': '3'}, '少见档：每波 +2 → +3'),
    ('v3item_049_income', {'base_value': '6'}, '普通档：3 的倍数时 +3 → +6（生效率仅 34%）'),
    ('golden_register', {'primary_factor': '2'}, '每件物件 +1 → +2；随收集成长，第 4 天约 +8/波'),
    ('house_special', {'primary_threshold': '2', 'secondary_threshold': '2'},
     '3 猫 3 客 → 2 猫 2 客；生效率 57% → 87%'),
    ('matching_cushions', {'primary_threshold': '2'},
     '3 连同名 → 2 连；实测 +0.66 → +3.14'),
    ('quiet_bell', {'primary_threshold': '28', 'base_value': '6'},
     '收入 ≤12 时 +4 → ≤28 时 +6；原阈值第 3 天起永久失效'),
    ('snack_shelf', {'source_keys': 'pastry|coffee|candy|cherryCake|americanCoffee|championBlend|driedCheese'},
     '原引用 milk|pastry，而 milk 在 Pieces 表里根本不存在（全表唯一悬空引用）'),
    ('panoramic_cats', {'source_kinds': ''},
     '斜角相邻改为对所有棋子生效；原本只给猫/幼崽，而 base 池的猫没有任何相邻规则'),
    ('panoramic_coffee', {'enabled': '0'}, '与 panoramic_cats 同义，去重'),
    ('double_tray', {'primary_scope': 'none', 'primary_comparator': 'always',
                     'primary_threshold': '0', 'source_kinds': 'Prop'},
     '原挂 consume_self，而可消耗牌几乎不存在，且双券礼袋是负收益（翻倍＝加倍惩罚）；'
     '改为本波第一件结算的物件翻倍，once_per_round 天然成立'),
    ('lucky_paw', {'multiplier': '0.12'}, '每只猫 8% → 12% 稀有权重'),
    ('recycling_income', {'base_value': '12'}, '每送走 3 位 8 → 12 金币'),
    ('final_double_coupon_bag_base', {'base_value': '-4'},
     '每波 −12 → −4；第 1 天每波总收入才 9，抽到它直接输，是第 1 关唯一的失败来源'),
]

# ── Rules：新增一行通用离场价值转移 ──
TRANSFER_RULE = {
    'rule_id': 'dismiss_transfer_permanent',
    'design_note': '玩家主动送走任意棋子时，把它的身价按稀有度永久转移给名册里同类最强的一位'
                   '（普通1/少见2/稀有3/特殊4）。盘面只有 16 格，名册涨过 16 之后加牌不再提高'
                   '每波收入、只稀释出场率——这是唯一能突破产出天花板的成长通道。',
    'owner_type': 'element',
    'owner_key': '*',
    'trigger': 'on_dismiss',
    'priority': '50',
    'source_kinds': '',
    'source_keys': '',
    'operation': 'transfer_permanent',
    'primary_scope': 'self_rarity',
    'primary_filter': '',
    'primary_comparator': 'always',
    'primary_threshold': '0',
    'secondary_scope': 'none',
    'secondary_filter': '',
    'secondary_comparator': 'always',
    'secondary_threshold': '0',
    'base_value': '1',
    'primary_factor': '1',
    'secondary_factor': '0',
    'cross_factor': '0',
    'divisor': '1',
    'multiplier': '0',
    'consume_self': '0',
    'once_per_round': '0',
    'reason': '离场价值转移',
    'enabled': '1',
}

# ── Buffs：品质与说明文案 ──
BUFF_EDITS = [
    ('catApron', {'rarity': 'rare'}, '实测 +5.34/波，是全表最强盘面道具，不该挂 common'),
    ('luckyPaw', {'rarity': 'uncommon',
                  'rule_text': '店内每只猫咪使少见和稀有伙伴来店里的机会提高12%。'}, ''),
    ('v3Item107', {'rule_text': '每次营业获得8金币。'}, ''),
    ('v3Item083', {'rule_text': '每次营业获得5金币。'}, ''),
    ('v3Item066', {'rule_text': '每次营业获得3金币。'}, ''),
    ('v3Item049', {'rule_text': '本次营业金币为3的倍数时，额外获得6金币。'}, ''),
    ('goldenRegister', {'rule_text': '每波客人散去时，每有1件店里的物件就多2金币。'}, ''),
    ('houseSpecial', {'rule_text': '场上同时有至少2只猫咪和2名客人时，咖啡、点心和咖啡师的收益翻倍。'}, ''),
    ('matchingCushions', {'rule_text': '2位或更多同名伙伴上下左右连成一组时，它们获得的金币翻倍。'}, ''),
    ('quietBell', {'rule_text': '若这波客人的收入不超过28金币，这波客人散去时获得6金币。'}, ''),
    ('snackShelf', {'rule_text': '咖啡、点心、糖果和蛋糕这类吃食每次产生收益时，额外获得2金币。'}, ''),
    ('panoramicWindow', {'rule_text': '所有伙伴计算相邻时都会把四个斜角算进去。'}, ''),
    ('doubleTray', {'rule_text': '每波第一件结算的物件收益翻倍。'}, ''),
    ('recyclingBin', {'rule_text': '每送走3位伙伴，获得12金币和1张下班券。'}, ''),
]

# ── Pieces ──
PIECE_EDITS = [
    ('doubleCouponBag',
     {'rule_text': '每次营业失去4金币。移除自身并获得1张清理券和1张换货券。'},
     '配合 final_double_coupon_bag_base 的 −12 → −4'),
]

# ── Stages：按通关率标定的目标曲线 ──
STAGE_TARGETS = {'1': '17', '2': '58', '3': '116', '4': '198', '5': '288', '6': '400'}

# ── Settings ──
SETTING_EDITS = [
    ('stage_clear_removal_reward', '1',
     '保底 1 张；实际按名册规模追加，见 stage_clear_removal_per_excess'),
]
SETTING_NEW = [
    ('stage_clear_removal_per_excess', '3', 'int',
     '通关时名册每超出盘面（16 格）N 张就多给 1 张下班券，让瘦身能力跟膨胀速度同构'),
    ('ui_dismiss_transfer_format', '{0}接过了{1}的活儿，每次营业永久 +{2}', 'string',
     '离场价值转移的提示；0=接班者 1=离场者 2=转移点数'),
]


def main():
    wb = Workbook(BOOK)
    log = []

    for rule_id, fields, note in RULE_EDITS:
        for field, value in fields.items():
            wb.set_cell('Rules', rule_id, field, value)
        log.append(f'Rules  {rule_id:<32}{fields}' + (f'  # {note}' if note else ''))

    number = wb.append_row('Rules', TRANSFER_RULE)
    log.append(f'Rules  + 新增第 {number} 行 {TRANSFER_RULE["rule_id"]}')

    for key, fields, note in BUFF_EDITS:
        for field, value in fields.items():
            wb.set_cell('Buffs', key, field, value)
        log.append(f'Buffs  {key:<32}{list(fields)}' + (f'  # {note}' if note else ''))

    for key, fields, note in PIECE_EDITS:
        for field, value in fields.items():
            wb.set_cell('Pieces', key, field, value)
        log.append(f'Pieces {key:<32}{list(fields)}' + (f'  # {note}' if note else ''))

    for stage_id, target in STAGE_TARGETS.items():
        wb.set_cell('Stages', stage_id, 'target', target)
    log.append(f'Stages 目标曲线 → {list(STAGE_TARGETS.values())}')

    for key, value, note in SETTING_EDITS:
        wb.set_cell('Settings', key, 'value', value)
        wb.set_cell('Settings', key, 'design_note', note)
        log.append(f'Settings {key} = {value}')
    for key, value, kind, note in SETTING_NEW:
        wb.append_row('Settings', {'key': key, 'value': value, 'value_type': kind,
                                   'design_note': note, 'enabled': 'TRUE'})
        log.append(f'Settings + 新增 {key} = {value}')

    wb.save()
    print('\n'.join(log))
    print(f'\n共 {wb.edits} 处改动，已写入 {BOOK}（原文件备份为 .bak）')
    print('接着跑：python Tools/CatCafeConfig/export_config.py')


if __name__ == '__main__':
    main()
