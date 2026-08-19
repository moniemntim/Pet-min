from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
refs = [root / 'references' / f'reference-{i:02d}.jpg' for i in range(1, 10)]
thumb_w, thumb_h = 640, 420
label_h = 34
canvas = Image.new('RGB', (thumb_w * 3, (thumb_h + label_h) * 3), (245, 245, 245))
draw = ImageDraw.Draw(canvas)
for idx, path in enumerate(refs):
    im = Image.open(path).convert('RGB')
    im.thumbnail((thumb_w - 12, thumb_h - 12), Image.Resampling.LANCZOS)
    x = (idx % 3) * thumb_w + (thumb_w - im.width) // 2
    y = (idx // 3) * (thumb_h + label_h) + (thumb_h - im.height) // 2
    canvas.paste(im, (x, y))
    draw.rectangle(((idx % 3) * thumb_w, (idx // 3) * (thumb_h + label_h), (idx % 3 + 1) * thumb_w, (idx // 3 + 1) * (thumb_h + label_h)), outline=(180, 180, 180), width=2)
    draw.text(((idx % 3) * thumb_w + 12, (idx // 3) * (thumb_h + label_h) + thumb_h + 7), path.stem, fill=(20, 20, 20))
out = root / 'references' / 'reference-boards-all.jpg'
canvas.save(out, quality=94, optimize=True)
print(out)
