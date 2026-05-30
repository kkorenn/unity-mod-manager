#!/usr/bin/env python3
"""Render the DMG background at the exact window size (600x400 points = pixels).
Logo + "Unity Mod Manager" (JetBrains Mono Bold) + an arrow from app to Applications.
Usage: dmg-background.py <icon_png> <out_png>
Icons sit at (150,220) and (450,220) in dmg-settings.py; the arrow lines up at y=220."""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

icon_path, out_path = sys.argv[1], sys.argv[2]
W, H = 600, 400
HERE = os.path.dirname(os.path.abspath(__file__))
JBM_BOLD = os.path.join(HERE, "dmg-assets", "JetBrainsMono-Bold.ttf")

bg = Image.new("RGB", (W, H))
top, bot = (244, 246, 249), (227, 231, 238)
for y in range(H):
    t = y / (H - 1)
    bg.paste(tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)), (0, y, W, y + 1))
d = ImageDraw.Draw(bg)

def font(size):
    for p in [JBM_BOLD, "/System/Library/Fonts/SFNS.ttf", "/System/Library/Fonts/Helvetica.ttc"]:
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
    logo = Image.open(icon_path).convert("RGBA").resize((96, 96), Image.LANCZOS)
    bg.paste(logo, (W // 2 - 48, 40), logo)
except Exception as e:
    print("logo skipped:", e)

centered("Unity Mod Manager", 162, font(26), (32, 36, 44))

# Arrow centered between the icons (icon centers at x=150 and x=450, y=220).
y = 220
x1, x2 = 240, 352
col = (150, 156, 167)
d.line([(x1, y), (x2, y)], fill=col, width=8)
d.polygon([(x2, y - 17), (x2 + 28, y), (x2, y + 17)], fill=col)

bg.save(out_path)
print("wrote", out_path)
