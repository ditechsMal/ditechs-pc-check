#!/usr/bin/env python3
"""
Draws app.ico for Ditechs PC Check.

Kept in the repo so the icon is reproducible rather than a binary nobody can
regenerate. Colours are the Ditechs palette, read off ditechs.co.uk:

    #0091D0  the brand blue   #0A3B52  a dark tint of it, for contrast

Run from the project folder:   python3 make-icon.py
"""
import math
import struct

BRAND = (0, 145, 208)       # #0091D0 - the brand blue
SHADE = (10, 59, 82)        # #0A3B52 - a dark tint of the brand blue
WHITE = (255, 255, 255)


def dist_seg(px, py, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    length2 = vx * vx + vy * vy
    t = 0.0 if length2 == 0 else max(0.0, min(1.0, (wx * vx + wy * vy) / length2))
    return math.hypot(px - (ax + t * vx), py - (ay + t * vy))


def sample(x, y, size):
    # rounded square, so the corners are transparent
    margin = size * 0.045
    radius = size * 0.22
    dx = max(abs(x - size / 2) - (size / 2 - margin - radius), 0.0)
    dy = max(abs(y - size / 2) - (size / 2 - margin - radius), 0.0)
    if math.hypot(dx, dy) - radius > 0:
        return (0, 0, 0, 0)

    # the brand blue, dark at the top left through to the full brand colour
    t = max(0.0, min(1.0, x / size * 0.55 + y / size * 0.65))
    bg = tuple(SHADE[i] + (BRAND[i] - SHADE[i]) * t for i in range(3))

    # a magnifying glass over a tick: "we looked, and here is the answer"
    if abs(math.hypot(x - 0.44 * size, y - 0.425 * size) - 0.225 * size) <= 0.055 * size / 2:
        return WHITE + (255,)
    if dist_seg(x, y, 0.605 * size, 0.590 * size, 0.780 * size, 0.765 * size) <= 0.060 * size / 2:
        return WHITE + (255,)
    if dist_seg(x, y, 0.350 * size, 0.430 * size, 0.415 * size, 0.505 * size) <= 0.048 * size / 2:
        return WHITE + (255,)
    if dist_seg(x, y, 0.415 * size, 0.505 * size, 0.555 * size, 0.325 * size) <= 0.048 * size / 2:
        return WHITE + (255,)

    return (int(bg[0]), int(bg[1]), int(bg[2]), 255)


def render(size, supersample=4):
    big = size * supersample
    acc = [[0, 0, 0, 0] for _ in range(size * size)]
    for sy in range(big):
        row = (sy // supersample) * size
        for sx in range(big):
            r, g, b, a = sample(sx + 0.5, sy + 0.5, big)
            p = acc[row + (sx // supersample)]
            p[0] += r
            p[1] += g
            p[2] += b
            p[3] += a
    n = supersample * supersample
    out = bytearray()
    for p in acc:
        out += bytes((p[0] // n, p[1] // n, p[2] // n, p[3] // n))
    return bytes(out)


def dib(size, rgba):
    """One 32bpp icon image in the format .ico expects."""
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, size * size * 4, 0, 0, 0, 0)
    pixels = bytearray()
    for y in range(size - 1, -1, -1):            # bottom-up
        row = rgba[y * size * 4:(y + 1) * size * 4]
        for x in range(size):
            r, g, b, a = row[x * 4:x * 4 + 4]
            pixels += bytes((b, g, r, a))        # BGRA
    mask = bytes(((size + 31) // 32) * 4 * size)  # 1bpp AND mask, all clear
    return header + bytes(pixels) + mask


def main():
    sizes = [16, 32, 48, 64, 128, 256]
    blobs = [(s, dib(s, render(s, 4 if s <= 128 else 2))) for s in sizes]

    out = struct.pack('<HHH', 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    entries, data = b'', b''
    for size, blob in blobs:
        entries += struct.pack('<BBBBHHII', 0 if size >= 256 else size,
                               0 if size >= 256 else size, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        data += blob

    with open('app.ico', 'wb') as fh:
        fh.write(out + entries + data)

    print(f"   app.ico written: {len(out + entries + data):,} bytes, sizes {sizes}")
    print(f"   brand blue #0091D0, shade #{SHADE[0]:02X}{SHADE[1]:02X}{SHADE[2]:02X}")


if __name__ == '__main__':
    main()
