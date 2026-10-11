# Draws the Farmstead Totem: python3 totem.py totem.png preview.png
# The sprite is kept as text so a change to it is a change anyone can read.
# 16 by 32 pixels, the size of a thing that stands in the world (a scarecrow, a statue): the
# bottom 16 by 16 is the tile it stands on, the rest rises above it.
import struct, zlib, sys

PAL = {
 '.': (0, 0, 0, 0),
 'O': (43, 24, 12, 255),     # outline
 'D': (104, 52, 22, 255),    # wood, shaded side
 'M': (158, 84, 34, 255),    # wood
 'L': (203, 122, 50, 255),   # wood, lit side
 'H': (238, 170, 86, 255),   # wood, highlight
 'k': (72, 34, 14, 255),     # carved lines
 'p': (246, 122, 92, 255),   # red paint, lit
 'r': (206, 62, 44, 255),    # red paint
 'R': (140, 34, 28, 255),    # red paint, shaded
 't': (64, 188, 172, 255),   # teal paint
 'T': (30, 122, 116, 255),   # teal paint, shaded
 'y': (250, 208, 78, 255),   # gold
 'Y': (255, 244, 176, 255),  # gold, glint
 'g': (182, 132, 24, 255),   # gold, shaded
 'w': (250, 240, 214, 255),  # eye white
 's': (150, 146, 134, 255),  # stone
 'S': (104, 100, 92, 255),   # stone, shaded
 'h': (198, 194, 180, 255),  # stone, lit
}

ROWS = [
 "................",  # 0
 "......OOOO......",  # 1  crest
 ".....OyYyrO.....",  # 2
 "....OOryyrOO....",  # 3
 "...OLHLLLMMDO...",  # 4  the bird's head
 "...OLwkLLwkDO...",  # 5  eyes
 "...OLLLyyMMDO...",  # 6  beak
 ".OOOMMyYygMDOOO.",  # 7  wings begin
 "OpprrOMyyMOrrppO",  # 8
 "OrRrRrOMMOrRrRrO",  # 9
 "OtTtTtOMMOtTtTtO",  # 10
 ".OTtTOOMMOOTtTO.",  # 11
 "..OOOOLMMDOOOO..",  # 12
 "...OLHLLLMMDO...",  # 13 the farmer's face
 "...OLkkLLkkDO...",  # 14 brows
 "...OLwkLLwkDO...",  # 15 eyes
 "...OLLLMMMMDO...",  # 16
 "...OLMrrrrMDO...",  # 17 mouth
 "...OLMRwwRDDO...",  # 18
 "..OOkkkkkkkkOO..",  # 19 a painted band
 "..OtTtTtTtTtTO..",  # 20
 "..OOkkkkkkkkOO..",  # 21
 "...OLHLLLMMDO...",  # 22 the warp stone, set in gold
 "...OLLgyygMDO...",  # 23
 "...OLgyYtygDO...",  # 24
 "...OLgytTygDO...",  # 25
 "...OLMgyygDDO...",  # 26
 "..OOOOOOOOOOOO..",  # 27 plinth
 ".OhhhhhsssssSSO.",  # 28
 ".OhsssssssssSSO.",  # 29
 ".OsSSSSSSSSSSSO.",  # 30
 "..OOOOOOOOOOOO..",  # 31
]
assert len(ROWS) == 32 and all(len(r) == 16 for r in ROWS), [(i, len(r)) for i, r in enumerate(ROWS) if len(r) != 16]

def chunk(tag, data):
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)

def write(path, width, height, pixel):
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        for x in range(width):
            raw += bytes(pixel(x, y))
    open(path, "wb").write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
                           + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))

write(sys.argv[1], 16, 32, lambda x, y: PAL[ROWS[y][x]])   # the 16x32 sprite the game loads

# An enlarged copy to look at: on grass, as it stands in the world, and small, as in a pack slot.
SCALE = 12
GRASS = [(96, 172, 60, 255), (88, 160, 54, 255)]
def preview(x, y):
    sx, sy = x // SCALE, y // SCALE
    p = PAL[ROWS[sy][sx]] if sx < 16 else (0, 0, 0, 0)
    if sx >= 18:
        # 4x, the game's own zoom
        ix, iy = (x - 18 * SCALE) // 4, (y - 8 * SCALE) // 4
        p = PAL[ROWS[iy][ix]] if 0 <= ix < 16 and 0 <= iy < 32 else (0, 0, 0, 0)
    if p[3] == 0:
        return GRASS[((x // (SCALE * 2)) + (y // (SCALE * 2))) % 2]
    return p
if len(sys.argv) > 2:
    write(sys.argv[2], 26 * SCALE, 32 * SCALE, preview)
