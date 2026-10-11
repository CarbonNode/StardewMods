# Makes the main farm's farmstead for the valley map: the homestead of the set (assets/farms/home.png)
# with a tall mast on its roof and a gold banner with a blue stripe, after the generated design kept
# in art/farmsteads/main.png. The house, its shading and its ground are home.png's, untouched, so the
# picture matches the other nine; only the mast and the banner are drawn here, pixel by pixel.
# python3 main-banner.py home.png out.png
import math, sys
from PIL import Image
home = Image.open(sys.argv[1]).convert('RGBA')
UP, WIDE = 14, 40                       # room above the roof for the mast, and to the right for the banner
out = Image.new('RGBA', (WIDE, home.height + UP), (0, 0, 0, 0))
out.alpha_composite(home, (0, UP))
px = out.load()
GOLD_HI, GOLD, GOLD_LO = (255, 214, 64), (242, 168, 18), (196, 112, 12)
BLUE_HI, BLUE, BLUE_LO = (58, 104, 214), (24, 60, 176), (14, 34, 122)
EDGE = (96, 50, 14)
WOOD_HI, WOOD, WOOD_LO = (196, 130, 62), (150, 86, 34), (84, 44, 18)
MAST_X, FOOT = 22, UP + 9               # the mast stands on the ridge of the house's roof
for y in range(2, FOOT + 1):
    px[MAST_X, y] = WOOD_HI if y % 5 == 3 else WOOD
    px[MAST_X + 1, y] = WOOD_LO
px[MAST_X, 0] = px[MAST_X + 1, 0] = GOLD_HI   # the knob on top
px[MAST_X, 1], px[MAST_X + 1, 1] = GOLD, GOLD_LO
# The banner: 16 wide, 10 tall, in two slow waves; a swallow tail at its free end.
bands = [GOLD_HI, GOLD_HI, GOLD, GOLD, BLUE_HI, BLUE, BLUE_LO, GOLD, GOLD, GOLD_LO]
shade = {GOLD_HI: GOLD, GOLD: GOLD_LO, GOLD_LO: EDGE, BLUE_HI: BLUE, BLUE: BLUE_LO, BLUE_LO: (8, 20, 84)}
LONG = 16
for c in range(LONG):
    x = MAST_X + 2 + c
    turn = c * 2 * math.pi / 9
    wave = 0 if c < 2 else round(1.2 * math.sin(turn))
    fold = c >= 2 and math.cos(turn) < -0.3          # the side of each wave that is turned from the light
    tail = c - (LONG - 4)                            # 0..3 over the last four columns: the notch opens
    for r, colour in enumerate(bands):
        if tail >= 0 and abs(r - 4.5) <= tail + 0.5 - 1: continue
        px[x, 2 + r + wave] = shade[colour] if fold else colour
    if tail < 0: px[x, 2 + len(bands) + wave] = EDGE   # the shadowed lower hem
out.save(sys.argv[2])
print(sys.argv[2], out.size, len(out.getcolors(100000)), 'colours')
