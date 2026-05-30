#!/usr/bin/env python3
"""Render the DMG background: logo + title + an arrow from the app to Applications.
Usage: dmg-background.py <icon_png> <out_png>
Canvas is 1200x800 (2x of a 600x400 DMG window)."""
import sys
from PIL import Image, ImageDraw, ImageFont

icon_path, out_path = sys.argv[1], sys.argv[2]
W, H = 1200, 800

# Soft vertical gradient background.
bg = Image.new("RGB", (W, H))
top, bot = (244, 246, 249), (227, 231, 238)
for y in range(H):
    t = y / (H - 1)
    bg.paste(tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)), (0, y, W, y + 1))
d = ImageDraw.Draw(bg)

def font(size, bold=False):
    for p in (["/System/Library/Fonts/SFNS.ttf"] if not bold else
              ["/System/Library/Fonts/SFNS.ttf"]) + ["/System/Library/Fonts/Helvetica.ttc"]:
        try:
            return ImageFont.truetype(p, size)
        except Exception:
            continue
    return ImageFont.load_default()

def centered(text, cy, fnt, fill):
    l, t, r, b = d.textbbox((0, 0), text, font=fnt)
    d.text(((W - (r - l)) / 2 - l, cy - (b - t) / 2 - t), text, font=fnt, fill=fill)

# Logo, top-center.
try:
    logo = Image.open(icon_path).convert("RGBA").resize((200, 200), Image.LANCZOS)
    bg.paste(logo, (W // 2 - 100, 70), logo)
except Exception as e:
    print("logo skipped:", e)

centered("Unity Mod Manager", 320, font(52), (40, 44, 52))
centered("Drag the app onto Applications to install", 372, font(28), (120, 126, 138))

# Arrow between the two icons (icons sit at y=220pt -> 440px).
y = 440
x1, x2 = 486, 700
col = (150, 156, 167)
d.line([(x1, y), (x2, y)], fill=col, width=16)
d.polygon([(x2, y - 34), (x2 + 56, y), (x2, y + 34)], fill=col)

bg.save(out_path)
print("wrote", out_path)
