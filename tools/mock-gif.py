from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(r'D:\DSH\RWZ0\work-countdown')
VIS = ROOT / 'test/visual/reminder-mock'
OUT = ROOT / 'test/visual/reminder-previews'
OUT.mkdir(parents=True, exist_ok=True)

FONT = r'C:\Windows\Fonts\msyh.ttc'
LABEL = ImageFont.truetype(FONT, 20)
TITLE = ImageFont.truetype(FONT, 24)

VARIANTS = [
    ('v1', 'V1 · 圆形扩散 + 上下对擦', 200, 600, 700, 3620),
    ('v2', 'V2 · 进度条拉满 + 同向扫擦', 220, 620, 720, 3560),
    ('v3', 'V3 · 柔边光晕 + 中缝展开', 280, 660, 780, 3640),
]

def frame(v, t):
    t = int(round(t / 50.0)) * 50
    return Image.open(VIS / v / f'f{t:04d}.png').convert('RGB')

def fit(img, size):
    img = img.copy()
    img.thumbnail(size, Image.LANCZOS)
    return img

def label(draw, xy, text, font=LABEL, fill=(225, 232, 244)):
    draw.text(xy, text, font=font, fill=fill)

# ---- GIFs ----
for v, name, *_ in VARIANTS:
    frames = [Image.open(p).convert('RGB') for p in sorted((VIS / v).glob('f*.png'))]
    gif = OUT / f'reminder-{v}.gif'
    frames[0].save(gif, save_all=True, append_images=frames[1:], duration=50,
                   loop=0, optimize=True, disposal=2)
    print(gif.name, gif.stat().st_size, 'bytes', len(frames), 'frames')

# ---- 单版分镜 3x2 ----
for v, name, t_flood, t_full, t_reveal, t_exit in VARIANTS:
    tiles = [
        (0, '① 原卡片'),
        (t_flood, '② 从条子末端扩散'),
        (t_full, '③ 纯色铺满全屏'),
        (t_reveal, '④ 标题 / 色块同时擦出'),
        (1500, '⑤ 停留'),
        (t_exit, '⑥ 退场'),
    ]
    tw, th, gap, top = 480, 270, 14, 62
    sheet = Image.new('RGB', (tw * 3 + gap * 4, top + th * 2 + gap * 3), (13, 17, 25))
    d = ImageDraw.Draw(sheet)
    label(d, (gap, 16), name, TITLE)
    for i, (t, cap) in enumerate(tiles):
        img = fit(frame(v, t), (tw, th))
        x = gap + (i % 3) * (tw + gap)
        y = top + (i // 3) * (th + gap + 24)
        sheet.paste(img, (x, y))
        label(d, (x, y + th + 3), cap, LABEL)
    sheet.save(OUT / f'reminder-{v}-sheet.png')
    print('sheet', v, sheet.size)

# ---- 三版对比 3 行 x 4 列 ----
cols = [
    (200, '扩散中'), (600, '铺满'), (760, '擦出中'), (1500, '停留'),
]
tw, th, gap = 400, 225, 12
left = 150
head = 84
sheet = Image.new('RGB', (left + 4 * tw + 5 * gap, head + 3 * th + 4 * gap), (13, 17, 25))
d = ImageDraw.Draw(sheet)
label(d, (gap, 12), '三版提醒动画 · 关键帧', TITLE)
for c, (t, cap) in enumerate(cols):
    label(d, (left + gap + c * (tw + gap), 50), cap)
for r, (v, name, t_flood, t_full, t_reveal, t_exit) in enumerate(VARIANTS):
    y = head + gap + r * (th + gap)
    label(d, (gap, y + th // 2 - 12), v.upper(), TITLE)
    picks = [t_flood, t_full, t_reveal, 1500]
    for c, t in enumerate(picks):
        img = fit(frame(v, t), (tw, th))
        sheet.paste(img, (left + gap + c * (tw + gap), y))
sheet.save(OUT / 'reminder-three-versions.png')
print('compare', sheet.size)
