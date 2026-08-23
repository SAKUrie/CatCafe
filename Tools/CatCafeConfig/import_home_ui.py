"""把大厅分层 PNG 裁成紧致精灵并算出摆放坐标。

源图是 2816x1584 的全画布分层导出（每层都是满画布、其余透明）。直接进 Unity
每层要吃 17.9MB 显存，17 层约 300MB，绝大部分是透明像素——所以这里按 alpha
包围盒裁掉空白，只留内容。

坐标以项目参考分辨率 1536x864 为准（PlaceTopLeft 用的左上原点），
贴图像素按 1920 基准导出（相对参考分辨率 1.25 倍超采样，1080p 屏上刚好清晰）。
背景层例外：模糊底图放大没有收益，按 1x 导出省显存。
"""
import io
import os
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
# 源目录＝美术给的分层导出。默认是本次那一版的下载路径，
# 换一批素材时用命令行第一个参数指过去即可：python import_home_ui.py <目录>
DEFAULT_SRC = Path(r'C:\Users\tengfei\Downloads\莉莉丝飞书20260815-115053\大厅')
SRC = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_SRC
DST = ROOT / 'Assets' / 'Resources' / 'CatCafe' / 'HomeUI'

CANVAS_W, CANVAS_H = 2816, 1584
REF_W, REF_H = 1536, 864
TO_REF = REF_W / float(CANVAS_W)          # 坐标：画布 → 参考分辨率
TEX_SCALE = 1920 / float(CANVAS_W)        # 贴图：画布 → 1920 基准

# 源文件 → 输出名。带文字的版本不导入（数字由运行时文本叠上去）。
# 顺序即绘制顺序（自底向上）。
LAYERS = [
    ('云.png',           'home-backdrop',       'fullscreen'),
    ('板子png.png',      'home-popup-book',     'crop'),
    ('猫上.png',         'home-cat-top',        'crop'),
    ('猫中.png',         'home-cat-mid',        'crop'),
    ('猫右.png',         'home-cat-right',      'crop'),
    ('前景云.png',       'home-clouds-front',   'crop'),
    ('猫咪详情.png',     'home-details-stand',  'crop'),
    ('关系图鉴.png',     'home-relations-stand', 'crop'),
    ('开始营业.png',     'home-start-ribbon',   'crop'),
    ('猫罐头去文字.png', 'home-cans-bar',       'crop'),
    ('进度条去文字.png', 'home-dex-bar',        'crop'),
    ('设置.png',         'home-settings',       'crop'),
]


def main() -> int:
    if not SRC.is_dir():
        print('找不到源目录：%s' % SRC)
        return 1
    DST.mkdir(parents=True, exist_ok=True)

    report = io.open(ROOT / 'Docs' / '_home_ui.txt', 'w', encoding='utf-8')
    report.write('%-22s %-14s %-22s %s\n' % ('输出', '贴图像素', '摆放 (x,y,w,h) @1536x864', '显存'))
    total_mb = 0.0
    placements = []

    for source, name, mode in LAYERS:
        im = Image.open(SRC / source).convert('RGBA')
        if mode == 'fullscreen':
            box = (0, 0, CANVAS_W, CANVAS_H)
            target = (REF_W, REF_H)
        else:
            box = im.getbbox()
            target = (max(1, int(round((box[2] - box[0]) * TEX_SCALE))),
                      max(1, int(round((box[3] - box[1]) * TEX_SCALE))))
        cropped = im.crop(box).resize(target, Image.LANCZOS)
        cropped.save(DST / (name + '.png'), optimize=True)

        x, y = box[0] * TO_REF, box[1] * TO_REF
        w, h = (box[2] - box[0]) * TO_REF, (box[3] - box[1]) * TO_REF
        placements.append((name, x, y, w, h))
        mb = target[0] * target[1] * 4 / 1048576.0
        total_mb += mb
        report.write('%-22s %-14s %-22s %.1f MB\n' % (
            name + '.png', '%dx%d' % target,
            '%.0f,%.0f %.0fx%.0f' % (x, y, w, h), mb))

    report.write('\n合计未压缩显存：%.1f MB（源图直接导入约 300 MB）\n' % total_mb)
    report.write('\n--- Settings 表待填行（key / value / value_type）---\n')
    for name, x, y, w, h in placements:
        prefix = 'ui_' + name.replace('-', '_')
        for suffix, value in (('_x', x), ('_y', y), ('_width', w), ('_height', h)):
            report.write('%-34s %8.1f  float\n' % (prefix + suffix, value))
    report.close()
    print('导出 %d 张到 %s' % (len(LAYERS), DST))
    return 0


if __name__ == '__main__':
    sys.exit(main())
