# Cuts a generated farmstead off its flat key colour and brings it down to map size.
# python3 refine.py source.png out.png width [colours]
import sys
from PIL import Image
src, out, width = sys.argv[1], sys.argv[2], int(sys.argv[3])
colours = int(sys.argv[4]) if len(sys.argv) > 4 else 28
im = Image.open(src).convert('RGB')
px = im.load()
W, H = im.size
def key(c):  # magenta, and the pink haze around it
    r, g, b = c
    return r > 150 and b > 150 and g < 110 and abs(r - b) < 90
mask = Image.new('L', (W, H), 0)
mp = mask.load()
for y in range(H):
    for x in range(W):
        if not key(px[x, y]): mp[x, y] = 255
box = mask.getbbox()
im, mask = im.crop(box), mask.crop(box)
h = round(im.height * width / im.width)
# Each output pixel is the most common solid colour of its cell: crisp edges, no blended fringe.
small = Image.new('RGBA', (width, h), (0, 0, 0, 0))
sp = small.load(); ip = im.load(); kp = mask.load()
q = im.quantize(colours, method=Image.MEDIANCUT, dither=Image.NONE).convert('RGB'); qp = q.load()
for oy in range(h):
    for ox in range(width):
        x0, x1 = int(ox * im.width / width), max(int((ox + 1) * im.width / width), int(ox * im.width / width) + 1)
        y0, y1 = int(oy * im.height / h), max(int((oy + 1) * im.height / h), int(oy * im.height / h) + 1)
        counts, solid, total = {}, 0, 0
        for y in range(y0, min(y1, im.height)):
            for x in range(x0, min(x1, im.width)):
                total += 1
                if kp[x, y]:
                    solid += 1
                    c = qp[x, y]; counts[c] = counts.get(c, 0) + 1
        if total and solid * 2 >= total:
            sp[ox, oy] = max(counts, key=counts.get) + (255,)
small.save(out)
print(out, small.size)
