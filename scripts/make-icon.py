"""生成 src/CampusLogin/app.ico（纯 Python，无第三方依赖）。

图案：蓝色圆角方块 + 白色 Wi-Fi 标志。ICO 内嵌 PNG（Vista+ 支持）。
用法：python3 scripts/make-icon.py
"""
import math
import struct
import zlib
from pathlib import Path

BG = (37, 99, 235)  # #2563EB
FG = (255, 255, 255)
SS = 4  # 超采样倍数，用于抗锯齿


def coverage(x, y, size):
    """返回 (背景覆盖率, 前景覆盖率)，坐标归一化到 0..1。"""
    bg = fg = 0
    for sy in range(SS):
        for sx in range(SS):
            u = (x + (sx + 0.5) / SS) / size
            v = (y + (sy + 0.5) / SS) / size
            if in_rounded_square(u, v):
                bg += 1
                if in_wifi(u, v):
                    fg += 1
    n = SS * SS
    return bg / n, fg / n


def in_rounded_square(u, v, r=0.22):
    du = max(r - u, 0, u - (1 - r))
    dv = max(r - v, 0, v - (1 - r))
    return du * du + dv * dv <= r * r


def in_wifi(u, v):
    cx, cy = 0.5, 0.74
    dx, dy = u - cx, v - cy
    d = math.hypot(dx, dy)
    if d <= 0.075:
        return True  # 底部圆点
    if dy > 0:
        return False
    ang = math.degrees(math.atan2(-dy, abs(dx)))  # 0 = 水平, 90 = 正上
    if ang < 42:
        return False
    for r in (0.20, 0.33, 0.46):
        if abs(d - r) <= 0.045:
            return True
    return False


def render_png(size):
    rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            bg, fg = coverage(x, y, size)
            if bg == 0:
                row += bytes(4)
                continue
            t = fg / bg
            rgb = [round(BG[i] * (1 - t) + FG[i] * t) for i in range(3)]
            row += bytes(rgb + [round(255 * bg)])
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    sizes = [16, 24, 32, 48, 64, 256]
    images = [render_png(s) for s in sizes]
    header = struct.pack("<HHH", 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries = b""
    for s, img in zip(sizes, images):
        dim = 0 if s >= 256 else s
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(img), offset)
        offset += len(img)
    out = Path(__file__).resolve().parent.parent / "src" / "CampusLogin" / "app.ico"
    out.write_bytes(header + entries + b"".join(images))
    print(f"wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
