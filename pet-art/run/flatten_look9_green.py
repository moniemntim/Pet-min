from pathlib import Path
from PIL import Image

p = Path(__file__).parent / "decoded" / "look-row-9.png"
im = Image.open(p).convert("RGB")
pix = im.load()
for y in range(im.height):
    for x in range(im.width):
        r, g, b = pix[x, y]
        if g >= 100 and g > r * 1.32 and g > b * 1.32:
            pix[x, y] = (0, 255, 0)
im.save(p)
