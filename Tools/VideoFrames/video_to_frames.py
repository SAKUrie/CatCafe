#!/usr/bin/env python3
"""把视频抽成固定数量的序列帧。

用途：Seedance 之类生成的短视频 → 定长序列帧 → 导进 Unity 当逐帧动画或图集。

管线：
    均匀采样 N 帧 → 统一裁掉四周空白 → 统一缩放对齐 → 导出单帧 PNG（可选图集）

「统一」是这个脚本的重点：逐帧各裁各的会让主体在帧间抖动，所以裁剪框取所有帧的
并集，缩放后再居中补齐到同一尺寸。这样导进 Unity 直接就是对齐好的序列。

依赖：
    pip install opencv-python pillow numpy

用法：
    python video_to_frames.py input.mp4 -o output_frames -n 12 --height 512
    python video_to_frames.py input.mp4 -o out --alpha --sheet --columns 6

资产规范提醒：项目禁止有损压缩与降采样。--height 会缩放画面，给游戏用的正式素材
请按目标尺寸一次到位，不要先缩小再放大。
"""

from __future__ import annotations

import argparse
import math
import os
import sys

try:
    import cv2
    import numpy as np
    from PIL import Image
except ImportError as error:  # 缺依赖时给出能直接照抄的安装命令，而不是一句 ImportError
    sys.exit(
        "缺少依赖：{0}\n请先安装：pip install opencv-python pillow numpy".format(error.name)
    )


def ensure_dir(path: str) -> None:
    os.makedirs(path, exist_ok=True)


def sample_video_frames(video_path: str, num_frames: int) -> list:
    """均匀取 num_frames 帧。

    刻意顺序解码而不是 seek：CAP_PROP_POS_FRAMES 在多数编码上不准，seek 到的位置
    和请求的帧号能差好几帧，抽出来的序列就不均匀了。短视频顺序读一遍代价可以接受。

    也刻意不信任 CAP_PROP_FRAME_COUNT——它对可变帧率和某些容器会给出错误值。
    先按它估算目标下标，读完之后如果实际帧数对不上，用真实帧数重新均匀取一次。
    """
    if num_frames < 1:
        raise ValueError("帧数必须 >= 1")

    capture = cv2.VideoCapture(video_path)
    if not capture.isOpened():
        raise RuntimeError("打不开视频：{0}".format(video_path))

    try:
        reported = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
        wanted = set()
        if reported > 0:
            wanted = set(np.linspace(0, reported - 1, num_frames).astype(int).tolist())

        picked = {}
        everything = []
        index = 0
        while True:
            ok, frame = capture.read()
            if not ok:
                break
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            if index in wanted:
                picked[index] = rgb
            # 帧数不多时留一份全量，供下面重采样兜底；太长的视频只留命中的，别把内存吃光。
            if reported <= 0 or len(everything) < 4096:
                everything.append(rgb)
            index += 1
    finally:
        capture.release()

    actual = index
    if actual == 0:
        raise RuntimeError("视频里读不出任何一帧：{0}".format(video_path))
    if actual < num_frames:
        raise RuntimeError(
            "视频只有 {0} 帧，取不出 {1} 帧".format(actual, num_frames)
        )

    # 报的帧数准，且目标帧都读到了：直接用。
    if reported == actual and len(picked) == num_frames:
        return [Image.fromarray(picked[i]) for i in sorted(picked)]

    # 否则按真实帧数重来一遍。
    if len(everything) < actual:
        raise RuntimeError(
            "视频帧数({0})与容器声明({1})不符，且长度超出兜底缓存；"
            "请改用较短的片段，或先用 ffmpeg 转成固定帧率。".format(actual, reported)
        )
    indices = np.linspace(0, actual - 1, num_frames).astype(int)
    return [Image.fromarray(everything[i]) for i in indices]


def estimate_background(frames: list, border: int = 8) -> tuple:
    """量出底色，并顺带量出「底有多花」，用来自动定容差。

    三段素材实测：平涂底的噪声 p99 只有 4~5，实拍纸纹底能到 20。容差写死一个值
    必然顾此失彼——小了扣不干净，大了会把奶白描边也算成底色，描边一旦被算成底，
    主体内部就会经描边连通到外面，整只猫被灌成筛子。

    取边缘像素距中位底色的 p99（不用 max 或 p99.9：主体的尾巴/耳朵有时会碰到画面
    边缘，那些离群值会把容差顶上天）。
    """
    bg = estimate_bg_color(frames, border)
    samples = []
    for frame in frames:
        arr = np.array(frame.convert("RGB")).astype(np.float32)
        for band in (arr[:border, :, :], arr[-border:, :, :],
                     arr[:, :border, :], arr[:, -border:, :]):
            samples.append(band.reshape(-1, 3))
    border_pixels = np.concatenate(samples, axis=0)
    noise = float(np.percentile(np.sqrt(((border_pixels - bg) ** 2).sum(axis=1)), 99))
    return bg, noise


def estimate_bg_color(frames: list, border: int = 8) -> np.ndarray:
    """从画面四周一圈像素估出底色。

    不用「亮度高于阈值就是背景」那套：这批素材的底是暖米色 (236,228,217)，
    而猫身上的白胸白爪比底还亮，按亮度扣会把猫扣掉、把底留下。
    改成先量出底色，再按颜色距离判定，白色主体和暖米底才分得开。

    取所有帧边框像素的中位数：中位数不怕主体偶尔碰到边缘，多帧合并也更稳。
    """
    samples = []
    for frame in frames:
        arr = np.array(frame.convert("RGB"))
        samples.append(arr[:border, :, :].reshape(-1, 3))
        samples.append(arr[-border:, :, :].reshape(-1, 3))
        samples.append(arr[:, :border, :].reshape(-1, 3))
        samples.append(arr[:, -border:, :].reshape(-1, 3))
    return np.median(np.concatenate(samples, axis=0), axis=0).astype(np.float32)


def bg_distance(image, bg_color: np.ndarray) -> np.ndarray:
    arr = np.array(image.convert("RGB")).astype(np.float32)
    return np.sqrt(((arr - bg_color) ** 2).sum(axis=2))


def outer_bg_mask(distance: np.ndarray, tolerance: float, feather: float) -> np.ndarray:
    """只把「与画面边缘连通」的那片底色算作背景。

    不能光看颜色距离：这批猫身上的奶白胸毛 (240,232,222) 和底色 (234,227,216)
    几乎一样，纯按距离抠会在胸口和嘴周打出一片碎洞。
    先取颜色接近底色的像素，再做连通域，只有碰到画面边缘的那些才是真背景；
    主体内部就算颜色撞上底色，也因为不连通而被保住。
    """
    similar = (distance <= tolerance + feather).astype(np.uint8)
    count, labels = cv2.connectedComponents(similar, connectivity=4)
    if count <= 1:
        return np.zeros(distance.shape, dtype=bool)

    edge_labels = set()
    edge_labels.update(np.unique(labels[0, :]).tolist())
    edge_labels.update(np.unique(labels[-1, :]).tolist())
    edge_labels.update(np.unique(labels[:, 0]).tolist())
    edge_labels.update(np.unique(labels[:, -1]).tolist())
    edge_labels.discard(0)  # 0 是非 similar 的像素，不参与
    if not edge_labels:
        return np.zeros(distance.shape, dtype=bool)

    return np.isin(labels, list(edge_labels))


def keep_largest_island(content: np.ndarray) -> np.ndarray:
    """只留最大的那一块，其余全部透明。

    抠完总会剩些零星碎块：压缩噪点、投影的边角、被切断的胡须。它们各自独立，
    面积阈值又不好定——定小了留噪点，定大了误伤。主体只有一块且远大于其他，
    直接取最大连通块最干净。代价是与主体断开的细节（个别游离的胡须尖）会一起没掉。
    """
    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        content.astype(np.uint8), connectivity=8)
    if count <= 2:
        return content

    areas = stats[1:, cv2.CC_STAT_AREA]
    return labels == (int(np.argmax(areas)) + 1)


def drop_speckles(content: np.ndarray, min_area: int) -> np.ndarray:
    """去掉面积过小的孤立内容块。

    视频压缩噪点会让背景里少数像素的颜色距离超过容差，抠完之后就是满地白点。
    这些噪点各自孤立且只有几个像素，而主体（连胡须都连着头）是一大块，
    按面积一刀切最省事也最不会误伤。
    """
    if min_area <= 1:
        return content

    count, labels, stats, _ = cv2.connectedComponentsWithStats(
        content.astype(np.uint8), connectivity=8)
    if count <= 1:
        return content

    keep = np.zeros(count, dtype=bool)
    for i in range(1, count):
        keep[i] = stats[i, cv2.CC_STAT_AREA] >= min_area
    return keep[labels]


def content_mask(
    image, bg_color: np.ndarray, tolerance: float, feather: float, min_area: int = 0
) -> np.ndarray:
    """内容 = 不属于外部背景、且面积够大的像素。"""
    distance = bg_distance(image, bg_color)
    return drop_speckles(~outer_bg_mask(distance, tolerance, feather), min_area)


def find_content_bbox(
    image, bg_color: np.ndarray, tolerance: float, feather: float, min_area: int = 0
) -> tuple:
    mask = content_mask(image, bg_color, tolerance, feather, min_area)
    ys, xs = np.where(mask)
    if xs.size == 0 or ys.size == 0:
        # 整帧都被当成背景，不裁，交给调用方决定
        return (0, 0, image.width, image.height)
    return (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def crop_frames_consistently(
    frames: list, padding: int, bg_color: np.ndarray, tolerance: float, feather: float,
    min_area: int = 0
) -> list:
    """所有帧用同一个裁剪框（各帧包围盒的并集），避免主体在帧间跳动。"""
    boxes = [find_content_bbox(f, bg_color, tolerance, feather, min_area) for f in frames]
    x1 = max(0, min(b[0] for b in boxes) - padding)
    y1 = max(0, min(b[1] for b in boxes) - padding)
    x2 = min(frames[0].width, max(b[2] for b in boxes) + padding)
    y2 = min(frames[0].height, max(b[3] for b in boxes) + padding)

    if x2 <= x1 or y2 <= y1:
        return list(frames)
    return [f.crop((x1, y1, x2, y2)) for f in frames]


def apply_alpha(
    frames: list, bg_color: np.ndarray, tolerance: float, feather: float,
    min_area: int = 0, edge_width: float = 1.2, subject_only: bool = False
) -> list:
    """按与底色的颜色距离扣成透明。

    给游戏用的序列帧几乎都要透明底，米色底导进 Unity 会是一块米色方块。
    距离 <= tolerance 全透明，>= tolerance + feather 完全不透明，中间线性过渡，
    这样纸片边缘的抗锯齿像素能拿到中间 alpha，不会出现硬锯齿。
    """
    out = []
    for frame in frames:
        rgb = np.array(frame.convert("RGB")).astype(np.float32)
        distance = np.sqrt(((rgb - bg_color) ** 2).sum(axis=2))

        # 连通判定只用容差，绝不能再叠加过渡宽度。
        # 叠加过 16 一次：判定线被顶到 23.8，而猫的奶白身体离底色才 20，
        # 身体整片被判成「接近底色」，再从腹部连通到画面外，整只猫被灌空。
        outer = outer_bg_mask(distance, tolerance, float(feather))
        content = drop_speckles(~outer, min_area)
        if subject_only:
            content = keep_largest_island(content)

        # alpha 按「几何边缘」算，而不是按颜色距离渐变。
        #
        # 这批美术自带一圈奶白纸边，它和底色只差 20 上下。若按颜色距离给 alpha，
        # 那圈设计上的白边会被判成半透明，边缘看起来发虚发毛。
        # 改成：先拿二值轮廓，再用距离变换在轮廓上做 1px 抗锯齿——
        # 白边保持完全不透明，边缘该硬的地方硬、只在真正的边界上过渡。
        # 试过按「底色整体压暗、色相不变」把地面投影单独识别出来给半透明，
        # 结果把猫的奶白身体一起吃掉了——浅色毛同样满足这个判据，
        # 靠距离上限也拦不住（奶白身体离底色只有 20~40）。
        # 结论：这批素材里投影和浅色毛在颜色上不可分，投影只能整块保留。
        solid = content.astype(np.uint8)
        inside = cv2.distanceTransform(solid, cv2.DIST_L2, 3)
        outside = cv2.distanceTransform(1 - solid, cv2.DIST_L2, 3)
        signed = inside - outside
        width = max(0.5, float(edge_width))
        alpha = np.clip(signed / width + 0.5, 0.0, 1.0)

        # 去底色污染：边缘像素是 alpha*前景 + (1-alpha)*底色 混出来的，
        # 直接扣完会把米色留在边上，换到深色背景就是一圈浅色光晕。
        # 按混合公式反解前景色，半透明像素才不带底色。
        safe = np.clip(alpha, 0.25, 1.0)[:, :, None]
        foreground = bg_color + (rgb - bg_color) / safe
        foreground = np.where(alpha[:, :, None] >= 0.999, rgb, foreground)
        foreground = np.clip(foreground, 0.0, 255.0)

        rgba = np.dstack([foreground, alpha * 255.0]).astype(np.uint8)
        out.append(Image.fromarray(rgba, mode="RGBA"))
    return out


def resize_frames(frames: list, target_height: int, transparent: bool) -> list:
    """按高度统一缩放，再居中补齐到相同宽度，保证每帧尺寸完全一致。"""
    scaled = []
    max_width = 0
    for frame in frames:
        scale = target_height / float(frame.height)
        width = max(1, int(round(frame.width * scale)))
        scaled.append(frame.resize((width, target_height), Image.LANCZOS))
        max_width = max(max_width, width)

    mode = "RGBA" if transparent else "RGB"
    fill = (0, 0, 0, 0) if transparent else (255, 255, 255)
    result = []
    for frame in scaled:
        canvas = Image.new(mode, (max_width, target_height), fill)
        canvas.paste(frame, ((max_width - frame.width) // 2, 0))
        result.append(canvas)
    return result


def save_frames(frames: list, out_dir: str, prefix: str) -> None:
    ensure_dir(out_dir)
    width = max(2, len(str(len(frames) - 1)))
    for i, frame in enumerate(frames):
        frame.save(os.path.join(out_dir, "{0}_{1:0{2}d}.png".format(prefix, i, width)))


def make_sprite_sheet(frames: list, out_path: str, columns: int, transparent: bool) -> None:
    if not frames:
        return

    cell_w, cell_h = frames[0].size
    columns = max(1, min(columns, len(frames)))
    rows = math.ceil(len(frames) / columns)

    mode = "RGBA" if transparent else "RGB"
    fill = (0, 0, 0, 0) if transparent else (255, 255, 255)
    sheet = Image.new(mode, (columns * cell_w, rows * cell_h), fill)
    for i, frame in enumerate(frames):
        sheet.paste(frame, ((i % columns) * cell_w, (i // columns) * cell_h))
    sheet.save(out_path)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="把视频均匀抽成固定数量的序列帧，统一裁剪与尺寸。"
    )
    parser.add_argument("video", help="输入视频路径")
    parser.add_argument("-o", "--output", default="output_frames", help="输出目录")
    parser.add_argument("-n", "--frames", type=int, default=12, help="抽多少帧，默认 12")
    parser.add_argument("--height", type=int, default=512, help="统一高度，默认 512")
    parser.add_argument("--prefix", default="frame", help="单帧文件名前缀")
    parser.add_argument("--padding", type=int, default=16, help="裁剪框外扩像素，默认 16")
    parser.add_argument(
        "--tolerance", type=float, default=None,
        help="底色容差：与底色的 RGB 距离小于它算背景。不填则按实测背景噪声自动定")
    parser.add_argument(
        "--alpha", action="store_true",
        help="把背景扣成透明并输出 RGBA。给游戏用的序列帧基本都要开")
    parser.add_argument(
        "--feather", type=float, default=None,
        help="连通判定的富余量(颜色距离)，不填取容差的 0.5 倍。调大易把浅色主体灌空")
    parser.add_argument(
        "--min-island", type=int, default=0,
        help="孤立内容块小于这个面积(像素)就当噪点去掉；0 表示按画面面积的万分之二自动定")
    parser.add_argument(
        "--edge-width", type=float, default=1.2,
        help="轮廓抗锯齿宽度(像素)。默认 1.2；调大边更软，调小更利落")
    parser.add_argument(
        "--subject-only", action="store_true",
        help="只保留最大的一块主体，其余零星碎块一律透明")
    parser.add_argument("--no-crop", action="store_true", help="不做统一裁剪")
    parser.add_argument("--sheet", action="store_true", help="额外导出一张 sprite sheet")
    parser.add_argument("--columns", type=int, default=0, help="图集列数，0 表示排成一行")
    return parser


def main() -> int:
    args = build_parser().parse_args()

    if not os.path.isfile(args.video):
        sys.exit("找不到视频：{0}".format(args.video))

    print("采样 {0} 帧...".format(args.frames))
    frames = sample_video_frames(args.video, args.frames)

    bg_color, noise = estimate_background(frames)
    tolerance = args.tolerance if args.tolerance is not None else max(6.0, noise * 1.5)
    # 这是给连通判定的富余量，不是边缘软化宽度——边缘软化由 --edge-width 负责。
    # 给大了会把浅色主体判成底色并从内部连通到画面外，把主体灌空，所以留窄。
    feather = args.feather if args.feather is not None else tolerance * 0.5
    print("底色 RGB≈({0}, {1}, {2})  背景噪声 p99={3:.1f}  容差={4:.1f}  过渡={5:.1f}".format(
        int(bg_color[0]), int(bg_color[1]), int(bg_color[2]), noise, tolerance, feather))

    min_island = args.min_island
    if min_island <= 0:
        min_island = max(16, int(frames[0].width * frames[0].height * 0.0002))
    print("噪点面积阈值 {0} 像素".format(min_island))

    if not args.no_crop:
        print("统一裁剪...")
        frames = crop_frames_consistently(
            frames, args.padding, bg_color, tolerance, feather, min_island)

    if args.alpha:
        print("扣背景为透明...")
        frames = apply_alpha(
            frames, bg_color, tolerance, feather, min_island, args.edge_width,
            args.subject_only)

    print("统一缩放到高 {0}...".format(args.height))
    frames = resize_frames(frames, args.height, args.alpha)

    print("导出单帧 PNG...")
    save_frames(frames, args.output, args.prefix)

    if args.sheet:
        columns = args.columns if args.columns > 0 else len(frames)
        print("拼图集（{0} 列）...".format(columns))
        make_sprite_sheet(
            frames,
            os.path.join(args.output, "sprite_sheet.png"),
            columns,
            args.alpha,
        )

    print("完成：{0}（{1} 帧，每帧 {2}x{3}）".format(
        os.path.abspath(args.output), len(frames), frames[0].width, frames[0].height))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
