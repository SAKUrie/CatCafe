#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""显示模式（窗口 / 全屏）的配置项。

窗口尺寸也走表：窗口档退回来时要显式给一次尺寸，写死 1280×720 的话
以后改默认窗口大小还得动代码。

用法：python Tools/CatCafeConfig/apply_fullscreen.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from xlsx_patch import Workbook  # noqa: E402

BOOK = os.path.join('GameDesign', 'CatCafeGameConfig.xlsx')

NEW = [
    ('fullscreen_default', 'FALSE', 'bool',
     '首次启动是否全屏；之后以玩家在设置里选的为准（存 PlayerPrefs）'),
    ('window_width', '1280', 'int', '窗口档的宽；从全屏退回窗口时用这个尺寸'),
    ('window_height', '720', 'int', '窗口档的高'),
    ('ui_settings_screen_title', '显示模式', 'string', '设置面板：显示模式分区标题'),
    ('ui_settings_screen_windowed_label', '窗口', 'string', '显示模式：窗口档文案'),
    ('ui_settings_screen_fullscreen_label', '全屏', 'string', '显示模式：全屏档文案'),
]


def main():
    wb = Workbook(BOOK)
    for key, value, value_type, note in NEW:
        if wb.find_row('Settings', key)[0] is None:
            wb.append_row('Settings', {'key': key, 'value': value, 'value_type': value_type,
                                       'design_note': note, 'enabled': '1'})
        else:
            wb.set_cell('Settings', key, 'value', value)
            wb.set_cell('Settings', key, 'value_type', value_type)
            wb.set_cell('Settings', key, 'design_note', note)
    wb.save(backup=False)
    print('已写入 %s（%d 处），下一步跑 export_config.py' % (BOOK, wb.edits))


if __name__ == '__main__':
    main()
