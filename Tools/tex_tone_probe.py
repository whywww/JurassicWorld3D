# -*- coding: utf-8 -*-
"""探测 NSk2 地面/草地纹理的平均色调，判断哪些偏绿（草）哪些偏棕（泥土）。"""
import argparse
from pathlib import Path
import os
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--textures", type=Path,
                    default=Path(__file__).resolve().parent.parent /
                    "Assets/ThirdParty/NatureStarterKit2/Textures",
                    help="Texture directory (default: project NatureStarterKit2 textures)")
base = parser.parse_args().textures
names = ["ground01.tga", "ground02.tga", "ground03.tga",
         "grass01.tga", "grass02.tga",
         "bark01.tga", "bark02.tga", "bush01.tga"]

for n in names:
    p = os.path.join(base, n)
    if not os.path.exists(p):
        print(f"{n:14s} MISSING"); continue
    im = Image.open(p).convert("RGB")
    w, h = im.size
    # 中央 50% 区域采样，避免边缘 alpha/留白干扰
    box = (w // 4, h // 4, w * 3 // 4, h * 3 // 4)
    crop = im.crop(box)
    px = list(crop.getdata())
    npx = len(px)
    r = sum(p[0] for p in px) / npx
    g = sum(p[1] for p in px) / npx
    b = sum(p[2] for p in px) / npx
    # 色调判定：绿色主导->草系；红>绿,蓝低->棕色/泥土系
    if g > r * 1.08 and g > b * 1.25:
        tone = "GREEN(草系)"
    elif r > g * 1.05 and b < r * 0.8:
        tone = "BROWN(棕/泥土系)"
    elif abs(r - g) < 0.08 * max(r, g) and abs(g - b) < 0.08 * max(g, b):
        tone = "GRAY(灰系)"
    else:
        tone = "MIXED(混合)"
    print(f"{n:14s} {w}x{h}  avgRGB=({r:5.1f},{g:5.1f},{b:5.1f})  -> {tone}")

print("done")
