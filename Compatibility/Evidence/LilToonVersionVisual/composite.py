"""Build the repository comparison image (synthetic materials only) and metrics subset.
usage: composite.py <capture dir> <metrics.json> <out dir>
"""
import json
import textwrap
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

SRC, METRICS, OUT = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
OUT.mkdir(parents=True, exist_ok=True)
m = json.loads(METRICS.read_text(encoding='utf-8'))
NUM = {'2.0.0': '200', '2.1.0': '210', '2.2.1': '221'}
ROWS = [
    ('2.2.1', 'm02_backlight_colorA050__back', 'Backlight, _BacklightColor alpha 0.5 (2.3.0)'),
    ('2.1.0', 'm07_outline_lit__front', 'Outline Lit (per pixel from 2.1.10)'),
    ('2.0.0', 'm08_matcap_uv1__back', 'MatCap blend UV1 outside 0-1 (2.1.0)'),
    ('2.0.0', 'm01_control__noskybox', 'No valid environment / skybox (2.1.0 fix)'),
    ('2.2.1', 'm01_control__front', 'Control: default lilToon (no difference)'),
]
P = 240
HEAD = 26


def font(size):
    for f in ('C:/Windows/Fonts/arial.ttf', 'C:/Windows/Fonts/segoeui.ttf'):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def crop_panel(path, bxs):
    im = Image.open(path).convert('RGB')
    s = P / im.width
    im = im.resize((P, P), Image.LANCZOS)
    dr = ImageDraw.Draw(im)
    for (x0, y0, x1, y1) in bxs:
        dr.rectangle([x0 * s - 2, y0 * s - 2, x1 * s + 2, y1 * s + 2], outline=(230, 0, 0), width=3)
    return im


W = 2 * P + 3 * 8 + 300
H = HEAD + len(ROWS) * (P + 8) + 8
sheet = Image.new('RGB', (W, H), (255, 255, 255))
dr = ImageDraw.Draw(sheet)
f, fs = font(15), font(13)
dr.text((8, 5), 'Made in lilToon X (author view)', fill=(0, 0, 0), font=f)
dr.text((8 + P + 8, 5), 'Same project upgraded to 2.3.4', fill=(0, 0, 0), font=f)
subset = {}
for i, (ver, item, title) in enumerate(ROWS):
    e = m['items'][item][ver]
    bxs = e['boxes']
    y = HEAD + i * (P + 8)
    sheet.paste(crop_panel(SRC / f'A_{NUM[ver]}__{item}.png', bxs), (8, y))
    sheet.paste(crop_panel(SRC / f'B_{NUM[ver]}__{item}.png', bxs), (8 + P + 8, y))
    tx = 8 + 2 * (P + 8) + 4
    dr.text((tx, y + 6), f'lilToon {ver}', fill=(0, 0, 0), font=f)
    lines = textwrap.wrap(title, 36)
    for j, line in enumerate(lines):
        dr.text((tx, y + 30 + j * 18), line.strip(), fill=(40, 40, 40), font=fs)
    dr.text((tx, y + 80), f"mean |diff| on object: {e['mean_abs_object']}", fill=(40, 40, 40), font=fs)
    dr.text((tx, y + 98), f"max |diff|: {e['max_abs']} / 255", fill=(40, 40, 40), font=fs)
    dr.text((tx, y + 116), f"changed pixels: {e['changed_pct_object']}% of object", fill=(40, 40, 40), font=fs)
sheet.save(OUT / 'comparison.png', optimize=True)

# metrics subset: synthetic materials only
syn = {k: {v: ({kk: vv for kk, vv in d.items() if kk != 'boxes'} if isinstance(d, dict) else d) for v, d in e.items()}
       for k, e in m['items'].items() if not k.startswith('avatar')}
(OUT / 'metrics.json').write_text(json.dumps({'threshold_8bit': m['threshold'], 'items': syn}, indent=1) + '\n',
                                  encoding='utf-8')
print((OUT / 'comparison.png').stat().st_size, 'bytes')
