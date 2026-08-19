from pathlib import Path
from PIL import Image, ImageOps, ImageDraw

root = Path(__file__).parent
ref = root / "references"
items = [ref / f"reference-0{i}.jpg" for i in range(1, 10)]
items += [ref / "layout-guides" / "idle.png", ref / "canonical-base.png"]
thumb_w, thumb_h = 480, 300
cols = 3
rows = (len(items) + cols - 1) // cols
board = Image.new("RGB", (cols * thumb_w, rows * thumb_h), "#202020")
for n, path in enumerate(items):
    im = Image.open(path).convert("RGB")
    im.thumbnail((thumb_w - 16, thumb_h - 42), Image.Resampling.LANCZOS)
    tile = Image.new("RGB", (thumb_w, thumb_h), "#303030")
    tile.paste(im, ((thumb_w - im.width)//2, 6))
    d = ImageDraw.Draw(tile)
    d.text((8, thumb_h - 28), path.name, fill="white")
    x, y = (n % cols) * thumb_w, (n // cols) * thumb_h
    board.paste(tile, (x, y))
board.save(root / "references" / "all-grounding-board.jpg", quality=94, subsampling=0)
