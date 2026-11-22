#!/usr/bin/env python3
"""墨案 / InkCase 的像素图标。

图标是 16x16 的像素网格（下面 GRID 里逐字符写死），再按整数倍放大成
16/32/48/64/128/256 —— 全部都是 16 的整数倍，所以每一档放大都不会出现
半像素重采样。改图标就改 GRID，然后：

    python tools/icon/make-icon.py

会覆盖 src/InkCase.App/app.ico（exe 图标 + 窗口图标，csproj 里 applicationIcon
和 Resource 都指向它）和 assets/icon-256.png（README 预览用）。

调色板：
  .  透明（只切掉四个角，让外框看起来是圆角）
  G  品牌绿 #0D5A53
  C  米白 #FFFEFB（对勾）
  R  朱红 #B23A2C（红笔划线）
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

PALETTE = {
    ".": (0x0D, 0x5A, 0x53, 0),   # 角上的透明像素
    "G": (0x0D, 0x5A, 0x53, 0xFF),
    "C": (0xFF, 0xFE, 0xFB, 0xFF),
    "R": (0xB2, 0x3A, 0x2C, 0xFF),
}

# 16x16，逐像素。对勾两笔各 2px 粗，底部顶点落在第 11 行，下面空一行再压红线。
GRID = [
    ".GGGGGGGGGGGGGG.",
    "GGGGGGGGGGGGGGGG",
    "GGGGGGGGGGGGGGGG",
    "GGGGGGGGGGGGGGGG",
    "GGGGGGGGGGGGGGGG",
    "GGGGGGGGGGGGCCGG",
    "GGGCCGGGGGGGCCGG",
    "GGGGCCGGGGGCCGGG",
    "GGGGGCCGGGCCGGGG",
    "GGGGGGCCGCCGGGGG",
    "GGGGGGCCCCGGGGGG",
    "GGGGGGCCCGGGGGGG",
    "GGGGGGGGGGGGGGGG",
    "GGGRRRRRRRRRRGGG",
    "GGGGGGGGGGGGGGGG",
    ".GGGGGGGGGGGGGG.",
]

SIZES = [16, 32, 48, 64, 128, 256]


def build_base():
    assert len(GRID) == 16, len(GRID)
    img = Image.new("RGBA", (16, 16), PALETTE["."])
    px = img.load()
    for y, row in enumerate(GRID):
        assert len(row) == 16, (y, len(row))
        for x, ch in enumerate(row):
            px[x, y] = PALETTE[ch]
    return img


def main():
    base = build_base()
    imgs = [base.resize((s, s), Image.NEAREST) for s in SIZES]

    ico = os.path.join(ROOT, "src", "InkCase.App", "app.ico")
    imgs[-1].save(ico, format="ICO", sizes=[(s, s) for s in SIZES], append_images=imgs[:-1])

    prev = os.path.join(ROOT, "assets", "icon-256.png")
    os.makedirs(os.path.dirname(prev), exist_ok=True)
    imgs[-1].save(prev)

    print("wrote %s (%d bytes)" % (ico, os.path.getsize(ico)))
    print("wrote %s" % prev)


if __name__ == "__main__":
    main()
