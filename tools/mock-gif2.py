from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(r'D:\DSH\RWZ0\work-countdown')
VIS = ROOT / 'test/visual/reminder-mock2'
OUT = ROOT / 'test/visual/reminder-previews2'
OUT.mkdir(parents=True, exist_ok=True)

FONT = r'C:\Windows\Fonts\msyh.ttc'
LABEL = ImageFont.truetype(FONT, 19)
TITLE = ImageFont.truetype(FONT, 23)

MODES = [
    ('full-circle', '全屏 · 圆形柔光', 400, 225),
    ('full-bar', '全屏 · 进度条拉伸', 400, 225),
    ('card-circle', '卡片内 · 圆形柔光（放大 2×）', 400, 300),
    ('card-bar', '卡片内 · 进度条拉伸（放大 2×）', 400, 300),
]
TIMES = {'full-circle': (250, 600, 780, 1500), 'full-bar': (250, 650, 800, 1500),
         'card-circle': (250, 520, 650, 1500), 'card-bar': (350, 520, 650, 1500)}

def frame(mode, t):
    t = int(round(t / 50.0)) * 50
    return Image.open(VIS / mode / f'f{t:04d}.png').convert('RGB')

def fit(img, size):
    img = img.copy()
    img.thumbnail(size, Image.LANCZOS)
    return img

def label(draw, xy, text, font=LABEL, fill=(225, 232, 244)):
    draw.text(xy, text, font=font, fill=fill)

# GIFs
for mode, name, w, h in MODES:
    frames = [Image.open(p).convert('RGB') for p in sorted((VIS / mode).glob('f*.png'))]
    gif = OUT / f'{mode}.gif'
    frames[0].save(gif, save_all=True, append_images=frames[1:], duration=50,
                   loop=0, optimize=True, disposal=2)
    print(gif.name, gif.stat().st_size, 'bytes', len(frames), 'frames')

# 全屏对比 2x4
tw, th, gap, head, left = 400, 225, 12, 74, 96
sheet = Image.new('RGB', (left + gap + tw * 4 + gap * 4, head + th * 2 + gap * 3), (13, 17, 25))
d = ImageDraw.Draw(sheet)
label(d, (gap, 10), '全屏扩散 · 两种样式', TITLE)
for c, cap in enumerate(['扩散中', '铺满', '擦出中', '停留']):
    label(d, (left + gap + c * (tw + gap), 44), cap)
for r, mode in enumerate(['full-circle', 'full-bar']):
    name = '圆形柔光' if mode == 'full-circle' else '进度条拉伸'
    y = head + r * (th + gap)
    label(d, (gap, y + 8), name, LABEL)
    # 用左边留白放样式名，图从 gap*? 开始
    for c, t in enumerate(TIMES[mode]):
        x = left + gap + c * (tw + gap)
        sheet.paste(fit(frame(mode, t), (tw, th)), (x, y))
sheet.save(OUT / 'full-compare.png')
print('full-compare', sheet.size)

# 卡片内对比 2x4（图 400x300）
tw2, th2, head2, left2 = 400, 300, 74, 110
sheet = Image.new('RGB', (left2 + gap + tw2 * 4 + gap * 4, head2 + th2 * 2 + gap * 3), (13, 17, 25))
d = ImageDraw.Draw(sheet)
label(d, (gap, 10), '卡片内扩散 · 两种样式（放大 2×）', TITLE)
for c, cap in enumerate(['扩散中', '铺满', '擦出中', '停留']):
    label(d, (left2 + gap + c * (tw2 + gap), 44), cap)
for r, mode in enumerate(['card-circle', 'card-bar']):
    name = '圆形柔光' if mode == 'card-circle' else '进度条拉伸'
    y = head2 + r * (th2 + gap)
    label(d, (gap, y + 8), name, LABEL)
    for c, t in enumerate(TIMES[mode]):
        x = left2 + gap + c * (tw2 + gap)
        sheet.paste(fit(frame(mode, t), (tw2, th2)), (x, y))
sheet.save(OUT / 'card-compare.png')
print('card-compare', sheet.size)

for mode in ['card-circle', 'card-bar']:
    src = VIS / f'{mode}-full.png'
    if src.exists():
        Image.open(src).save(OUT / f'{mode}-full.png')
print('done')
