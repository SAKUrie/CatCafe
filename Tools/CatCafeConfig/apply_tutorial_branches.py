#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""接回 / 清掉几根断掉的教程枝。

1 main_08_new_cat 恢复：文案与 main_07_summary 高度重复，改写成只讲"住下了"这件事，
  别把局末账本那段规则说明再抄一遍。调用点在 CatCafeHomeController。
2 context_close_shop 移除：文案假设"提前打烊"是解锁功能，可现在结束本局从第一轮
  起就一直在，前提不成立。整行删掉。
3 新增 context_cans：罐头条一直没人解释，左上角那个数字对新玩家是个谜。
4 （代码侧）下班券那条延后到第二次点开棋子小窗，不和"点图标看收益"抢同一拍。

用法：python Tools/CatCafeConfig/apply_tutorial_branches.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_patch import Workbook  # noqa: E402

BOOK = os.path.join('GameDesign', 'CatCafeGameConfig.xlsx')

# main_07_summary 已经在局末讲过"点亮收集册、下一局起进奖励池、不进初始牌组"，
# 隔一次场景切换再抄一遍就是废话。这条只留"它住下了、去哪儿看"。
NEW_CAT_COPY = ('新朋友已经住下了，我把名字记进收集册了。'
                '想看它的资料，就去左边的收集册。')

CANS_COPY = ('罐头已经记在左上角。它和绒毛一起使用，可以在猫咪招募中邀请新朋友。')

CANS_ROW = {
    'id': 'context_cans',
    'trigger_key': 'home_cans_first',
    'copy': CANS_COPY,
    'spotlight_target': 'home_cans_hud',
    'once': '1',
    'enabled': '1',
    'appear_note': '第一次带着罐头回到大厅时',
}


def main():
    wb = Workbook(BOOK)

    # ── 1 新猫住下：改文案，enabled 保持 1 ──
    wb.set_cell('Tutorial', 'main_08_new_cat', 'copy', NEW_CAT_COPY)
    wb.set_cell('Tutorial', 'main_08_new_cat', 'appear_note',
                '打完一局、带着新点亮的猫回到大厅时')

    # ── 2 提前打烊：整行删掉 ──
    if wb.delete_row('Tutorial', 'context_close_shop'):
        print('已删除 Tutorial.context_close_shop')

    # 所有字条统一使用房东奶奶口吻。
    wb.set_cell('Settings', 'tutorial_system_voice_ids', 'value', '')

    # ── 3 罐头条 ──
    if wb.find_row('Tutorial', CANS_ROW['id'])[0] is None:
        wb.append_row('Tutorial', CANS_ROW)
    else:
        for field in ('copy', 'spotlight_target', 'appear_note'):
            wb.set_cell('Tutorial', CANS_ROW['id'], field, CANS_ROW[field])

    # ── 4 下班券那条延后到第几次点开棋子小窗 ──
    if wb.find_row('Settings', 'tutorial_dismiss_note_after_opens')[0] is None:
        wb.append_row('Settings', {
            'key': 'tutorial_dismiss_note_after_opens',
            'value': '2',
            'value_type': 'int',
            'design_note': '下班券可用后，第二次点开内容详情时再讲精简名册，避免与首次查看收益挤在一起',
            'enabled': '1',
        })
    else:
        wb.set_cell('Settings', 'tutorial_dismiss_note_after_opens', 'value', '2')
        wb.set_cell('Settings', 'tutorial_dismiss_note_after_opens', 'design_note',
                    '下班券可用后，第二次点开内容详情时再讲精简名册，避免与首次查看收益挤在一起')

    wb.save(backup=False)
    print('已写入 %s（%d 处），下一步跑 export_config.py' % (BOOK, wb.edits))


if __name__ == '__main__':
    main()
