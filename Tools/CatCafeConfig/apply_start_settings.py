#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""开始界面：「换一家小店」改叫「存档」，并给设置按钮接上真正的设置面板。

设置项都是设备级偏好（PlayerPrefs），和存档档位无关，所以放在开始界面没有歧义。
「重看字条」没放进来——那个写的是当前存档的已读位，而开始界面上"当前存档"随时会换。

用法：python Tools/CatCafeConfig/apply_start_settings.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_patch import Workbook  # noqa: E402

BOOK = os.path.join('GameDesign', 'CatCafeGameConfig.xlsx')

NEW = [
    ('ui_start_shops_title', '存 档', '开始界面存档列表的标题'),
    ('ui_start_shops_hint', '每一家小店都是一份独立的进度，猫、罐头和图鉴各记各的。',
     '存档列表顶部的一行说明'),
    ('ui_start_settings_title', '设 置', '开始界面设置面板标题'),
    ('ui_start_music_label', '音乐音量', '开始界面设置：音乐音量分区标题'),
    ('ui_start_sfx_label', '音效音量', '开始界面设置：音效音量分区标题'),
    ('ui_start_speed_label', '忙碌演出速度', '开始界面设置：结算演出速度分区标题'),
    ('ui_start_settings_close', '关 闭', '开始界面设置面板的关闭按钮'),
    ('ui_start_shops_back', '回 去', '开始界面存档列表的返回按钮'),
    ('ui_start_confirm_title', '等一下', '开始界面二次确认弹层的标题'),
]

# 设置按钮原来只弹一句"去大厅改"，现在有真面板了，这两条不再有人引用。
RETIRED = ['ui_start_settings_hint', 'ui_start_settings_hint_accept']


def main():
    wb = Workbook(BOOK)

    for key, value, note in NEW:
        if wb.find_row('Settings', key)[0] is None:
            wb.append_row('Settings', {'key': key, 'value': value,
                                       'value_type': 'string', 'design_note': note,
                                       'enabled': '1'})
        else:
            wb.set_cell('Settings', key, 'value', value)
            wb.set_cell('Settings', key, 'design_note', note)

    for key in RETIRED:
        if wb.find_row('Settings', key)[0] is None:
            continue
        wb.set_cell('Settings', key, 'enabled', '0')
        wb.set_cell('Settings', key, 'design_note',
                    '已停用：开始界面接上真正的设置面板后不再需要"去大厅改"的提示')

    wb.save(backup=False)
    print('已写入 %s（%d 处），下一步跑 export_config.py' % (BOOK, wb.edits))


if __name__ == '__main__':
    main()
