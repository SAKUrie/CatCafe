#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把首次营业引导改成按行动分拍的最短链路：

  1 大厅只提示去营业                         main_01_home
  2 进场先点猫查看每波次收益                 main_02_inspect
  3 关闭详情后再提示目标、波次与拉杆         main_02_run
  4 首次真实相邻结算时解释联动               main_03_synergy
  5 第一次选择前解释收取、跳过与名册稀释     main_04_reward

“看收益”和“开始营业”由玩家关闭详情这个真实操作隔开，不在同一静默点连续弹出。

第 5 拍要求盘面上真的出现「猫咪＋猫砂盆相邻」，所以：
  - 猫砂盆进初始牌组（原来只在奖励池里，首局盘面根本没有）
  - 首转钉住的那一对从「猫＋客人」改成「猫＋猫砂盆」——客人是固定小费，
    挨着谁都一样，拿它讲相邻联动是讲错了

用法：python Tools/CatCafeConfig/apply_tutorial_flow.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_patch import Workbook  # noqa: E402

BOOK = os.path.join('GameDesign', 'CatCafeGameConfig.xlsx')

RUN_COPY = ('今天的目标营业额和波次都写在上方。准备好后，拉一下右下角的猫爪，'
            '开始第 1 波次。')
INSPECT = {
    'id': 'main_02_inspect',
    'trigger_key': 'run_first_inspect',
    'copy': '先点一下框中的猫咪。详情里会写明它每个波次的基础收益。'
            '看完关掉详情，我们再开始营业。',
    'spotlight_target': 'run_synergy_cells',
    'once': '1',
    'enabled': '1',
    'appear_note': '第一次营业、开始第 1 波次前，先查看一只猫的收益',
}
SYNERGY_COPY = ('看，这只猫挨着猫砂盆，本波次会多赚一份金币。相邻位置合适，收益就会更高。')
REWARD_COPY = ('每个波次结束后，可以挑一张加入店内名册；没有合适的，也可以先不收。'
               '名册越长，单张上场的机会越少。')


def main():
    wb = Workbook(BOOK)

    # ── Tutorial：一个动作只配一张字条，用实际查看详情隔开前两拍 ──
    if wb.find_row('Tutorial', INSPECT['id'])[0] is None:
        _, home_row = wb.find_row('Tutorial', 'main_01_home')
        wb.insert_row('Tutorial', home_row, INSPECT)
    else:
        for field, value in INSPECT.items():
            if field != 'id':
                wb.set_cell('Tutorial', INSPECT['id'], field, value)
    wb.delete_row('Tutorial', 'main_05_reward_chosen')
    wb.set_cell('Tutorial', 'main_02_run', 'copy', RUN_COPY)
    wb.set_cell('Tutorial', 'main_02_run', 'appear_note', '第一次营业、开始第 1 波次前')
    wb.set_cell('Tutorial', 'main_03_synergy', 'copy', SYNERGY_COPY)
    wb.set_cell('Tutorial', 'main_03_synergy', 'appear_note',
                '第一次完成相邻联动时')
    wb.set_cell('Tutorial', 'main_04_reward', 'copy', REWARD_COPY)
    wb.set_cell('Tutorial', 'main_04_reward', 'appear_note',
                '第 1 波次结束、第一次挑选名册内容时')

    # ── InitialDeck：拿一位客人换一只猫砂盆，牌组仍是 6 张 ──
    # 开局 3 猫 3 客人里一件道具都没有，玩家要等到第一次挑牌才见得到道具；
    # 而客人是固定小费，挨着谁都一样，本来也撑不起"相邻还能多挣"这一课。
    # 直接加第 7 张会把刚定好的目标曲线整条抬上去（casual 56%→70%），所以是换不是加。
    wb.set_cell('InitialDeck', 'guest', 'count', '2')
    if wb.find_row('InitialDeck', 'litterBox')[0] is None:
        wb.append_row('InitialDeck', {'element_key': 'litterBox', 'count': '1', 'enabled': '1'})

    # ── Settings：首转钉住的那一对改成可配 ──
    if wb.find_row('Settings', 'tutorial_first_roll_pair')[0] is None:
        wb.append_row('Settings', {
            'key': 'tutorial_first_roll_pair',
            'value': 'cat,litterBox',
            'value_type': 'string',
            'design_note': '首转钉在前两个槽位的一对，逗号分隔；每项可以是棋子种类'
                           '（cat/guest/prop/staff/kitten）或具体棋子键。这一对必须真的有'
                           '相邻联动，否则 main_03_synergy 会指着两张没关系的牌讲联动',
            'enabled': '1',
        })
    else:
        wb.set_cell('Settings', 'tutorial_first_roll_pair', 'value', 'cat,litterBox')
    wb.set_cell('Settings', 'tutorial_first_roll_slots', 'design_note',
                '首局首转前四枚棋子落位槽位，0-based；前两个必须相邻，用来钉 '
                'tutorial_first_roll_pair，其余棋子随后随机')

    wb.save(backup=False)
    print('已写入 %s（%d 处），下一步跑 export_config.py' % (BOOK, wb.edits))


if __name__ == '__main__':
    main()
