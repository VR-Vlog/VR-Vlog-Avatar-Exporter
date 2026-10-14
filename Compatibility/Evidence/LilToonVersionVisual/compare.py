"""Compare the E145 captures: A_<v> (lilToon v) vs B_<v> (same project upgraded to 2.3.4) vs A_234.

Writes metrics.json and per-item comparison PNGs (A | B with red boxes around differing regions).
usage: analyze.py <capture dir> <out dir>
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

SRC = Path(sys.argv[1])
OUT = Path(sys.argv[2])
OUT.mkdir(parents=True, exist_ok=True)
VERSIONS = [('200', '2.0.0'), ('210', '2.1.0'), ('221', '2.2.1')]
THRESH = 8          # per-channel 8-bit difference counted as "different"
MIN_AREA = 25       # ignore isolated pixels (skinning / AA noise)


def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int16)


def boxes(diff):
    mask = diff.max(axis=2) > THRESH
    mask = ndimage.binary_opening(mask, iterations=1)
    mask = ndimage.binary_dilation(mask, iterations=6)
    lab, n = ndimage.label(mask)
    out = []
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        if sl is None:
            continue
        area = int((lab[sl] == i).sum())
        if area < MIN_AREA:
            continue
        out.append((sl[1].start, sl[0].start, sl[1].stop, sl[0].stop))
    return out


def metrics(a, b):
    d = np.abs(a - b)
    fg = (np.abs(a - 127).max(axis=2) > 3) | (np.abs(b - 127).max(axis=2) > 3)  # not background grey
    changed = d.max(axis=2) > THRESH
    return {
        'mean_abs': round(float(d.mean()), 3),
        'mean_abs_object': round(float(d[fg].mean()) if fg.any() else 0.0, 3),
        'max_abs': int(d.max()),
        'changed_px': int(changed.sum()),
        'changed_pct_object': round(100.0 * changed.sum() / max(1, fg.sum()), 2),
    }


def font(size):
    for f in ('C:/Windows/Fonts/meiryo.ttc', 'C:/Windows/Fonts/YuGothM.ttc', 'C:/Windows/Fonts/arial.ttf'):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def panel(img, bxs, label, w=384):
    im = Image.fromarray(img.astype(np.uint8)).resize((w, w), Image.LANCZOS)
    s = w / img.shape[1]
    dr = ImageDraw.Draw(im)
    for (x0, y0, x1, y1) in bxs:
        dr.rectangle([x0 * s - 2, y0 * s - 2, x1 * s + 2, y1 * s + 2], outline=(230, 0, 0), width=3)
    canvas = Image.new('RGB', (w, w + 34), (255, 255, 255))
    canvas.paste(im, (0, 34))
    ImageDraw.Draw(canvas).text((8, 6), label, fill=(20, 20, 20), font=font(18))
    return canvas


items = sorted({p.name.split('__', 1)[1][:-4] for p in SRC.glob('A_234__*.png')})
result = {'threshold': THRESH, 'items': {}}
for item in items:
    ref = load(SRC / f'A_234__{item}.png')
    entry = {}
    for n, ver in VERSIONS:
        a = load(SRC / f'A_{n}__{item}.png')
        b = load(SRC / f'B_{n}__{item}.png')
        m = metrics(a, b)
        m['upgraded_vs_234_max_abs'] = int(np.abs(b - ref).max())
        m['upgraded_vs_234_mean_abs'] = round(float(np.abs(b - ref).mean()), 4)
        bx = boxes(np.abs(a - b)) if m['changed_pct_object'] >= 0.05 else []
        m['boxes'] = bx
        entry[ver] = m
        tag = 'diff' if bx else 'same'
        row = [panel(a, bx, f'lilToon {ver}（元）'), panel(b, bx, '2.3.4 へ上げた後')]
        w = sum(p.width for p in row) + 8
        sheet = Image.new('RGB', (w, row[0].height), (255, 255, 255))
        x = 0
        for p in row:
            sheet.paste(p, (x, 0)); x += p.width + 8
        sheet.save(OUT / f'{tag}__{ver}__{item}.png')
    result['items'][item] = entry
    if 'A2_200' and (SRC / f'A2_200__{item}.png').exists():
        rep = load(SRC / f'A2_200__{item}.png')
        entry['repeat_2.0.0_max_abs'] = int(np.abs(rep - load(SRC / f'A_200__{item}.png')).max())
(OUT / 'metrics.json').write_text(json.dumps(result, indent=1, ensure_ascii=False), encoding='utf-8')

print(f"{'item':40} " + ' '.join(f'{v:>22}' for _, v in VERSIONS))
for item, e in result['items'].items():
    print(f'{item:40} ' + ' '.join(
        f"{e[v]['mean_abs_object']:7.2f}/{e[v]['max_abs']:3d}/{e[v]['changed_pct_object']:6.2f}%" for _, v in VERSIONS)
        + f"  up=234:{max(e[v]['upgraded_vs_234_max_abs'] for _, v in VERSIONS)}")
