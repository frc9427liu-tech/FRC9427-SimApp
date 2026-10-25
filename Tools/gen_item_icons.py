"""Draw original cartoon item icons (MK-style pictograms) -> Assets/Resources/Racing/Textures/ui_item_<kind>.png
Run: python -I Tools/gen_item_icons.py   (supersampled 4x for smooth edges; all shapes drawn from scratch)"""
import math, os
from PIL import Image, ImageDraw, ImageFilter

S = 1024            # work size; downscaled to 256
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Resources', 'Racing', 'Textures')
DARK = (40, 28, 48, 255)


def new():
    return Image.new('RGBA', (S, S), (0, 0, 0, 0))


def outline(img, color=DARK, w=22):
    """Dark outline around the opaque shape (cartoon look)."""
    a = img.split()[3]
    grown = a.filter(ImageFilter.MaxFilter(2 * (w // 2) + 1))
    base = Image.new('RGBA', img.size, color)
    base.putalpha(grown)
    base.alpha_composite(img)
    return base


def shine(img, box, alpha=150):
    d = ImageDraw.Draw(img)
    d.ellipse(box, fill=(255, 255, 255, alpha))


def poly_star(cx, cy, r_out, r_in, n=5, rot=-90):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rot + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def banana():
    im = new(); d = ImageDraw.Draw(im)
    d.ellipse((150, 200, 850, 900), fill=(255, 214, 40, 255))
    cut = Image.new('L', (S, S), 0); ImageDraw.Draw(cut).ellipse((330, 120, 1010, 820), fill=255)
    a = im.split()[3]; a = Image.composite(Image.new('L', (S, S), 0), a, cut); im.putalpha(a)
    d = ImageDraw.Draw(im)
    d.polygon([(205, 330), (270, 250), (300, 330)], fill=(110, 80, 30, 255))
    d.polygon([(700, 800), (790, 820), (770, 740)], fill=(110, 80, 30, 255))
    d.arc((200, 280, 800, 880), 110, 170, fill=(255, 246, 150, 255), width=26)
    return outline(im)


def shell(col, rim=(250, 235, 190, 255), wings=False):
    im = new(); d = ImageDraw.Draw(im)
    d.ellipse((170, 560, 850, 780), fill=rim)
    d.pieslice((140, 130, 880, 870), 180, 360, fill=col)
    d.rectangle((140, 490, 880, 540), fill=rim)
    d.pieslice((140, 130, 880, 870), 180, 360, fill=col)
    lighter = tuple(min(255, int(c * 1.25 + 30)) for c in col[:3]) + (255,)
    for (x, y) in [(512, 330), (360, 400), (664, 400)]:
        pts = [(x + 90 * math.cos(math.radians(60 * i + 30)), y + 90 * math.sin(math.radians(60 * i + 30))) for i in range(6)]
        d.polygon(pts, fill=lighter)
    d.ellipse((330, 220, 460, 310), fill=(255, 255, 255, 120))
    im = outline(im)
    if wings:
        d = ImageDraw.Draw(im)
        for sx in (-1, 1):
            cx = 512 + sx * 380
            d.polygon([(cx, 380), (cx + sx * 150, 250), (cx + sx * 120, 420), (cx + sx * 160, 470), (cx, 520)], fill=(255, 255, 255, 255), outline=DARK)
    return im


def mushroom(cap=(235, 50, 50, 255), spot=(255, 255, 255, 255)):
    im = new(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((340, 520, 690, 880), 90, fill=(250, 232, 200, 255))
    d.pieslice((100, 140, 920, 900), 180, 360, fill=cap)
    d.rectangle((100, 520, 920, 570), fill=cap)
    d.ellipse((120, 470, 900, 640), fill=cap)
    for (x, y, r) in [(512, 270, 105), (260, 430, 85), (770, 430, 85)]:
        d.ellipse((x - r, y - r, x + r, y + r), fill=spot)
    d.ellipse((410, 650, 460, 720), fill=DARK); d.ellipse((570, 650, 620, 720), fill=DARK)
    return outline(im)


def triple(draw_fn):
    base = draw_fn().resize((560, 560), Image.LANCZOS)
    im = new()
    for (x, y) in [(40, 330), (424, 330), (232, 40)]:
        im.alpha_composite(base, (x, y))
    return im


def star():
    im = new(); d = ImageDraw.Draw(im)
    d.polygon(poly_star(512, 540, 470, 200), fill=(255, 215, 40, 255))
    d.polygon(poly_star(512, 540, 330, 140), fill=(255, 238, 120, 255))
    d.ellipse((400, 470, 460, 570), fill=DARK); d.ellipse((565, 470, 625, 570), fill=DARK)
    d.arc((430, 560, 600, 660), 20, 160, fill=DARK, width=18)
    return outline(im)


def lightning():
    im = new(); d = ImageDraw.Draw(im)
    d.polygon([(620, 60), (250, 560), (470, 560), (370, 960), (790, 400), (560, 400)], fill=(255, 224, 40, 255))
    d.polygon([(600, 150), (340, 520), (500, 520), (440, 790), (700, 430), (520, 430)], fill=(255, 245, 160, 255))
    return outline(im)


def bomb():
    im = new(); d = ImageDraw.Draw(im)
    d.ellipse((140, 270, 880, 960), fill=(45, 45, 60, 255))
    d.rounded_rectangle((420, 200, 600, 310), 25, fill=(120, 120, 135, 255))
    d.line([(520, 210), (620, 110), (720, 150)], fill=(160, 110, 60, 255), width=34)
    d.polygon(poly_star(740, 140, 110, 45, 8, 0), fill=(255, 150, 30, 255))
    d.polygon(poly_star(740, 140, 60, 25, 8, 0), fill=(255, 235, 100, 255))
    d.ellipse((250, 380, 400, 500), fill=(255, 255, 255, 130))
    d.ellipse((330, 600, 400, 680), fill=(255, 255, 255, 255)); d.ellipse((500, 600, 570, 680), fill=(255, 255, 255, 255))
    d.ellipse((350, 625, 385, 665), fill=DARK); d.ellipse((520, 625, 555, 665), fill=DARK)
    return outline(im)


def bullet():
    im = new(); d = ImageDraw.Draw(im)
    d.rounded_rectangle((200, 280, 760, 800), 260, fill=(40, 40, 52, 255))
    d.rectangle((200, 280, 420, 800), fill=(40, 40, 52, 255))
    d.ellipse((330, 360, 560, 620), fill=(255, 255, 255, 255))
    d.ellipse((440, 440, 520, 560), fill=DARK)
    d.polygon([(330, 360), (580, 400), (330, 410)], fill=DARK)
    d.rectangle((120, 340, 200, 740), fill=(255, 120, 40, 255))
    return outline(im)


def itembox(tint=(255, 255, 255)):
    im = new(); d = ImageDraw.Draw(im)
    cols = [(255, 90, 90), (255, 190, 60), (110, 220, 110), (90, 170, 255)]
    d.rounded_rectangle((130, 130, 890, 890), 120, fill=(255, 255, 255, 235))
    d.polygon([(140, 140), (880, 140), (880, 240), (140, 560)], fill=cols[3] + (200,))
    d.polygon([(140, 560), (880, 240), (880, 400), (140, 800)], fill=cols[2] + (200,))
    d.polygon([(140, 800), (880, 400), (880, 520), (260, 880)], fill=cols[1] + (200,))
    f = [(512, 270), (700, 270), (700, 460), (560, 540), (560, 600), (460, 600), (460, 480), (600, 410), (600, 360), (450, 360), (450, 440), (330, 440), (330, 270)]
    d.polygon(f, fill=(255, 255, 255, 255), outline=DARK)
    d.rectangle((460, 650, 560, 740), fill=(255, 255, 255, 255), outline=DARK)
    return outline(im)


def ink():
    im = new(); d = ImageDraw.Draw(im)
    d.ellipse((220, 120, 800, 620), fill=(150, 90, 220, 255))
    for i, x in enumerate([260, 390, 520, 650, 740]):
        d.rounded_rectangle((x - 45, 520, x + 45, 920 - (i % 2) * 90), 45, fill=(150, 90, 220, 255))
    d.ellipse((330, 300, 460, 450), fill=(255, 255, 255, 255)); d.ellipse((560, 300, 690, 450), fill=(255, 255, 255, 255))
    d.ellipse((380, 350, 440, 430), fill=DARK); d.ellipse((600, 350, 660, 430), fill=DARK)
    d.ellipse((300, 180, 420, 250), fill=(255, 255, 255, 110))
    return outline(im)


def feather():
    im = new(); d = ImageDraw.Draw(im)
    d.polygon([(860, 90), (260, 330), (140, 700), (300, 820), (620, 640), (880, 300)], fill=(255, 255, 255, 255))
    d.polygon([(860, 90), (620, 640), (880, 300)], fill=(160, 215, 255, 255))
    d.line([(170, 880), (840, 120)], fill=(120, 170, 230, 255), width=30)
    for t in (0.25, 0.45, 0.65):
        x = 170 + (840 - 170) * t; y = 880 + (120 - 880) * t
        d.line([(x, y), (x - 150, y + 20)], fill=(200, 225, 250, 255), width=16)
    return outline(im)


def coin():
    im = new(); d = ImageDraw.Draw(im)
    d.ellipse((130, 130, 890, 890), fill=(255, 205, 30, 255))
    d.ellipse((220, 220, 800, 800), fill=(255, 232, 100, 255))
    d.rounded_rectangle((455, 300, 570, 720), 50, fill=(235, 170, 20, 255))
    d.ellipse((300, 270, 460, 330), fill=(255, 255, 255, 140))
    return outline(im)


def fireflower():
    im = new(); d = ImageDraw.Draw(im)
    d.rectangle((470, 520, 550, 900), fill=(80, 190, 90, 255))
    d.polygon([(550, 760), (770, 640), (720, 800)], fill=(80, 190, 90, 255))
    for i in range(6):
        a = math.radians(60 * i); x = 512 + 230 * math.cos(a); y = 400 + 230 * math.sin(a)
        d.ellipse((x - 130, y - 130, x + 130, y + 130), fill=(255, 140, 30, 255))
    d.ellipse((380, 270, 640, 530), fill=(255, 235, 90, 255))
    d.ellipse((440, 340, 480, 390), fill=DARK); d.ellipse((545, 340, 585, 390), fill=DARK)
    return outline(im)


def horn():
    im = new(); d = ImageDraw.Draw(im)
    d.polygon([(180, 480), (760, 180), (900, 360), (760, 720), (180, 560)], fill=(255, 205, 40, 255))
    d.ellipse((730, 150, 960, 400), fill=(255, 230, 110, 255))
    d.ellipse((60, 430, 260, 610), fill=(255, 150, 40, 255))
    for (x, y) in [(300, 640), (360, 760)]:
        d.ellipse((x - 70, y - 70, x + 70, y + 70), fill=(255, 120, 60, 255))
    d.line([(930, 120), (990, 60)], fill=(255, 250, 200, 255), width=22); d.line([(960, 260), (1020, 250)], fill=(255, 250, 200, 255), width=22)
    return outline(im)


def boo():
    im = new(); d = ImageDraw.Draw(im)
    d.ellipse((160, 150, 860, 800), fill=(245, 248, 255, 255))
    d.polygon([(200, 720), (360, 900), (460, 760), (600, 920), (700, 760), (840, 880), (860, 600)], fill=(245, 248, 255, 255))
    d.ellipse((330, 330, 450, 480), fill=DARK); d.ellipse((560, 330, 680, 480), fill=DARK)
    d.pieslice((340, 520, 690, 760), 0, 180, fill=(120, 40, 70, 255)); d.polygon([(400, 600), (440, 600), (420, 650)], fill=(255, 255, 255, 255))
    return outline(im)


def boomerang():
    im = new(); d = ImageDraw.Draw(im)
    d.polygon([(120, 700), (330, 180), (500, 260), (400, 520), (780, 520), (860, 700), (700, 820), (160, 800)], fill=(255, 160, 40, 255))
    d.polygon([(220, 700), (340, 300), (420, 330), (340, 600), (780, 600)], fill=(255, 205, 100, 255))
    return outline(im)


def shield():
    im = new(); d = ImageDraw.Draw(im)
    d.polygon([(512, 90), (880, 220), (840, 640), (512, 940), (180, 640), (140, 220)], fill=(70, 150, 255, 255))
    d.polygon([(512, 190), (770, 280), (740, 600), (512, 810), (290, 600), (260, 280)], fill=(130, 195, 255, 255))
    d.polygon(poly_star(512, 500, 160, 70), fill=(255, 255, 255, 255))
    return outline(im)


def mega():
    im = mushroom(cap=(255, 215, 60, 255), spot=(255, 90, 90, 255))
    d = ImageDraw.Draw(im)
    for (x, y) in [(110, 120), (930, 200)]:
        d.polygon(poly_star(x, y, 90, 30, 4, -90), fill=(255, 255, 255, 255))
    return im


ICONS = {
    'boost': mushroom, 'tripleboost': lambda: triple(mushroom), 'shield': shield, 'oil': banana, 'triplebanana': lambda: triple(banana),
    'bumper': lambda: shell((60, 190, 80, 255)), 'triplebumper': lambda: triple(lambda: shell((60, 190, 80, 255))),
    'homing': lambda: shell((235, 60, 60, 255)), 'bluesell': None,
    'bluesehll': None,
    'blueshell': lambda: shell((60, 130, 245, 255), wings=True),
    'star': star, 'lightning': lightning, 'bomb': bomb, 'mega': mega, 'bullet': bullet, 'fakebox': lambda: itembox(),
    'inkblast': ink, 'feather': feather, 'coin': coin, 'fireflower': fireflower, 'goldboost': lambda: mushroom(cap=(255, 205, 40, 255), spot=(255, 255, 255, 255)),
    'superhorn': horn, 'boo': boo, 'boomerang': boomerang,
}

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    for k, fn in ICONS.items():
        if fn is None:
            continue
        im = fn().resize((256, 256), Image.LANCZOS)
        im.save(os.path.join(OUT, 'ui_item_' + k + '.png'))
        print('ok', k)
