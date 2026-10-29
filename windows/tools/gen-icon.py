#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从 Assets/AppIcon.svg 的几何数据生成多尺寸 Windows 图标（纯标准库实现，无第三方依赖）。

用法：
    python3 windows/tools/gen-icon.py

输出：windows/Assets/DashKey.ico（16/20/24/32/40/48/64/128/256，PNG 压缩条目）

几何数据直接抄自 AppIcon.svg，改动图标时同步修改本文件即可。
"""

import os
import struct
import zlib

# ---------------------------------------------------------------- 几何数据

CANVAS = 1024.0

# 背景圆角矩形：x0, y0, x1, y1, r
BG_BOX = (100.0, 100.0, 924.0, 924.0, 185.0)
BG_TOP = (0x3D, 0x4C, 0x60)
BG_BOTTOM = (0x1C, 0x26, 0x32)

GLYPH_COLOR = (0xE8, 0xF0, 0xF8)

# 键盘键帽（SVG 里是纯 h/v 矩形）
GLYPH_RECTS = [
    (292.3, 341.4, 408.1, 399.3),
    (466.7, 341.4, 582.5, 399.3),
    (639.8, 341.4, 755.6, 399.3),
    (292.3, 457.3, 408.1, 515.2),
    (466.7, 457.3, 582.5, 515.2),
    (639.8, 457.3, 755.6, 515.2),
    (234.8, 631.0, 813.4, 688.9),
]

# 外框（矩形环）
FRAME_OUTER = (106.3, 223.2, 920.3, 807.1)
FRAME_INNER = (166.3, 283.2, 860.3, 747.1)


def _glyph_transform(x, y):
    """SVG 里的 translate(512 512) scale(.66) translate(-513.3 -515.15)"""
    return ((x - 513.3) * 0.66 + 512.0, (y - 515.15) * 0.66 + 512.0)


def _transform_rect(r):
    x0, y0 = _glyph_transform(r[0], r[1])
    x1, y1 = _glyph_transform(r[2], r[3])
    return (x0, y0, x1, y1)


GLYPH_RECTS_T = [_transform_rect(r) for r in GLYPH_RECTS]
FRAME_OUTER_T = _transform_rect(FRAME_OUTER)
FRAME_INNER_T = _transform_rect(FRAME_INNER)

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 4  # 每像素超采样边长


# ---------------------------------------------------------------- 光栅化

def _in_rect(x, y, r):
    return r[0] <= x <= r[2] and r[1] <= y <= r[3]


def _in_round_rect(x, y, box):
    x0, y0, x1, y1, rad = box
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + rad), x1 - rad)
    cy = min(max(y, y0 + rad), y1 - rad)
    dx, dy = x - cx, y - cy
    return dx * dx + dy * dy <= rad * rad


def _in_glyph(x, y):
    for r in GLYPH_RECTS_T:
        if _in_rect(x, y, r):
            return True
    o, i = FRAME_OUTER_T, FRAME_INNER_T
    return _in_rect(x, y, o) and not _in_rect(x, y, i)


def _lerp(a, b, t):
    return a + (b - a) * t


def render(size):
    """返回 RGBA bytearray（未预乘）。"""
    scale = size / CANVAS
    inv = 1.0 / (SS * SS)
    out = bytearray(size * size * 4)

    for py in range(size):
        for px in range(size):
            cov_bg = 0
            cov_gl = 0
            for sy in range(SS):
                for sx in range(SS):
                    ux = (px + (sx + 0.5) / SS) / scale
                    uy = (py + (sy + 0.5) / SS) / scale
                    if _in_round_rect(ux, uy, BG_BOX):
                        cov_bg += 1
                    if _in_glyph(ux, uy):
                        cov_gl += 1

            a_bg = cov_bg * inv
            a_gl = cov_gl * inv
            if a_bg <= 0 and a_gl <= 0:
                continue

            # 背景竖向渐变（objectBoundingBox）
            t = min(1.0, max(0.0, ((py + 0.5) / scale - BG_BOX[1]) / (BG_BOX[3] - BG_BOX[1])))
            bg = (_lerp(BG_TOP[0], BG_BOTTOM[0], t),
                  _lerp(BG_TOP[1], BG_BOTTOM[1], t),
                  _lerp(BG_TOP[2], BG_BOTTOM[2], t))

            a_out = a_gl + a_bg * (1.0 - a_gl)
            if a_out <= 0:
                continue
            o = (py * size + px) * 4
            for c in range(3):
                v = (GLYPH_COLOR[c] * a_gl + bg[c] * a_bg * (1.0 - a_gl)) / a_out
                out[o + c] = max(0, min(255, int(round(v))))
            out[o + 3] = max(0, min(255, int(round(a_out * 255))))
    return out


# ---------------------------------------------------------------- 编码

def png_bytes(w, h, rgba):
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)  # filter: none
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data
                + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', ihdr)
            + chunk(b'IDAT', zlib.compress(bytes(raw), 9))
            + chunk(b'IEND', b''))


def build_ico(images):
    count = len(images)
    header = struct.pack('<HHH', 0, 1, count)
    offset = 6 + 16 * count
    entries = b''
    payload = b''
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(data), offset)
        payload += data
        offset += len(data)
    return header + entries + payload


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.normpath(os.path.join(here, '..', 'Assets', 'DashKey.ico'))
    os.makedirs(os.path.dirname(out_path), exist_ok=True)

    images = []
    for size in SIZES:
        rgba = render(size)
        images.append((size, png_bytes(size, size, rgba)))
        print('  rendered %dx%d' % (size, size))

    data = build_ico(images)
    with open(out_path, 'wb') as f:
        f.write(data)
    print('wrote %s (%d bytes)' % (out_path, len(data)))

    # 回读校验结构
    with open(out_path, 'rb') as f:
        blob = f.read()
    reserved, kind, cnt = struct.unpack('<HHH', blob[:6])
    assert reserved == 0 and kind == 1 and cnt == len(SIZES), 'ICO 头异常'
    for i in range(cnt):
        e = blob[6 + 16 * i:6 + 16 * (i + 1)]
        dim, _, _, _, planes, bpp, length, off = struct.unpack('<BBBBHHII', e)
        assert blob[off:off + 8] == b'\x89PNG\r\n\x1a\n', '第 %d 项不是 PNG' % i
        w, h = struct.unpack('>II', blob[off + 16:off + 24])
        expect = 256 if dim == 0 else dim
        assert (w, h) == (expect, expect), '第 %d 项尺寸不匹配' % i
        assert off + length <= len(blob), '第 %d 项越界' % i
        assert bpp == 32 and planes == 1
    print('ok: %d entries, sizes %s' % (cnt, SIZES))


if __name__ == '__main__':
    main()
