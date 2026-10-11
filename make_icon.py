# -*- coding: utf-8 -*-
# 生成 AutoDateShift 应用图标 app.ico（多尺寸，风格与软件内矢量时钟图标一致）
# 设计：白底表盘 + 蓝色表环(#2F6FB2) + 深色指针(#333) + 橙色换日弧箭(#D88600)
import math
from PIL import Image, ImageDraw

S = 1024  # 超采样画布，最后缩小保证边缘平滑
BLUE = (0x2F, 0x6F, 0xB2, 255)
DARK = (0x33, 0x33, 0x33, 255)
ORANGE = (0xD8, 0x86, 0x00, 255)
WHITE = (255, 255, 255, 255)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
c = S / 2.0

# 表盘（白色填充 + 蓝色环）
ring_w = S * 0.075
face_r = S * 0.40
d.ellipse([c - face_r, c - face_r, c + face_r, c + face_r], fill=WHITE)
d.ellipse([c - face_r, c - face_r, c + face_r, c + face_r], outline=BLUE, width=int(ring_w))

# 刻度（12/3/6/9 四个主刻度，蓝色）
tick_w = int(S * 0.018)
tick_len = S * 0.055
for ang in (0, 90, 180, 270):
    a = math.radians(ang - 90)
    r1 = face_r - ring_w * 0.5 - tick_len
    r2 = face_r - ring_w * 0.5 - S * 0.008
    d.line([c + r1 * math.cos(a), c + r1 * math.sin(a),
            c + r2 * math.cos(a), c + r2 * math.sin(a)], fill=BLUE, width=tick_w)

# 指针（与运行时钟表图标一致：12 点 + 约 4:30 方向），圆角端点用圆点模拟
hand_w = int(S * 0.045)
hx1, hy1 = c, c - face_r * 0.58          # 分针指向 12
hx2, hy2 = c + face_r * 0.38, c + face_r * 0.22  # 时针指向约 4:30
d.line([c, c, hx1, hy1], fill=DARK, width=hand_w)
d.line([c, c, hx2, hy2], fill=DARK, width=hand_w)
d.ellipse([c - hand_w / 2, c - hand_w / 2, c + hand_w / 2, c + hand_w / 2], fill=DARK)

# 换日弧箭：表环外侧橙色圆弧（约 200°->320°），末端箭头，寓意"日期循环切换"
arc_r = face_r + ring_w * 1.1
arc_w = int(S * 0.045)
d.arc([c - arc_r, c - arc_r, c + arc_r, c + arc_r], start=195, end=325, fill=ORANGE, width=arc_w)
# 箭头（在 325° 处沿切线方向）
a = math.radians(325)
tip_x, tip_y = c + arc_r * math.cos(a), c + arc_r * math.sin(a)
tang = a + math.radians(90)  # 切线方向
aw = S * 0.075  # 箭头长度
ah = S * 0.055  # 箭头半宽
p1 = (tip_x + aw * math.cos(tang), tip_y + aw * math.sin(tang))
p2 = (tip_x - aw * 0.55 * math.cos(tang) + ah * math.cos(a), tip_y - aw * 0.55 * math.sin(tang) + ah * math.sin(a))
p3 = (tip_x - aw * 0.55 * math.cos(tang) - ah * math.cos(a), tip_y - aw * 0.55 * math.sin(tang) - ah * math.sin(a))
d.polygon([p1, p2, p3], fill=ORANGE)

sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
img.save("app.ico", format="ICO", sizes=sizes)
# 顺手导出一张 256 PNG 供 GitHub 仓库/Release 展示
img.resize((256, 256), Image.LANCZOS).save("app.png", format="PNG")
print("app.ico / app.png generated")
