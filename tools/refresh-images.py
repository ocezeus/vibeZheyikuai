from pathlib import Path
from PIL import Image
import shutil
root = Path('D:/DSH/RWZ0/work-countdown')
vis = root/'test/visual'
# 由 VisualCheck / DocShots / Palette 生成的画面
for src, dst in [('compact.png', 'final-collapsed.png'), ('expanded.png', 'final-expanded.png'),
                 ('kind-event.png', 'reminder.png'), ('kind-meeting.png', 'reminder-meeting.png'),
                 ('kind-lunch.png', 'reminder-lunch.png'), ('kind-evening.png', 'reminder-evening.png'),
                 ('tab-policy.png', 'settings-dialog.png'),
                 ('tab-fixed.png', 'settings-schedules.png'),
                 ('doc-fixed.png', 'settings-schedule.png'),
                 ('doc-events.png', 'settings-events.png'),
                 ('doc-look.png', 'settings-themes.png'),
                 ('doc-color.png', 'settings-color.png')]:
    p = vis/src
    if p.exists():
        shutil.copyfile(p, root/dst)
frames = [Image.open(p).convert('RGB') for p in sorted(vis.glob('demo-*.png'))]
if frames:
    frames[0].save(root/'design-preview.gif', save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
    for f in frames:
        f.close()
print('images refreshed:', len(list(vis.glob('*.png'))))
