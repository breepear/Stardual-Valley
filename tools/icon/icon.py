"""Draw the Stardual Valley pixel-art icon on a 24x24 grid and export it.

Usage: python3 tools/icon/icon.py <out_dir>   (needs Pillow: pip install pillow)
Writes preview.png (24x24 scaled 16x) and mipmap-<density>/ic_launcher.png for
every Android density. Copy the mipmaps into src/main/res/ to update the app.
"""
import os
import sys

from PIL import Image

PAL = {
    ".": None,
    "K": (52, 28, 18),      # outline
    "d": (124, 66, 30),     # wood dark
    "W": (176, 104, 48),    # wood
    "w": (219, 150, 76),    # wood light
    "h": (240, 190, 110),   # wood highlight
    "N": (30, 38, 92),      # night sky deep
    "n": (52, 70, 140),     # night sky
    "Y": (255, 205, 60),    # star
    "y": (255, 243, 168),   # star highlight
    "o": (214, 140, 30),    # star shade
    "s": (200, 215, 255),   # sparkle
    "B": (128, 196, 240),   # day sky
    "b": (176, 224, 250),   # day sky light
    "G": (92, 168, 60),     # grass
    "g": (58, 118, 40),     # grass dark
    "L": (150, 214, 90),    # leaf light
    "S": (120, 74, 40),     # soil
}

ART = [
    "........................",
    "...KKKKKKKKKKKKKKKKKK...",
    "..KhwwwwwwwwwwwwwwwwWK..",
    "..KwKKKKKKKKKKKKKKKKdK..",
    "..KwKNsNNNNNYNNNNsNKdK..",
    "..KwKNNNNNNYyYNNNNNKdK..",
    "..KwKNNYYYYYyYYYYNNKdK..",
    "..KwKNNNYYYYYYYYNNNKdK..",
    "..KwKnnnnYYYoYYnnsnKdK..",
    "..KwKnsnnYYonoYYnnnKdK..",
    "..KwKnnnYYonnnoYYnnKdK..",
    "..KwKKKKKKKKKKKKKKKKdK..",
    "..KWddddddKKddddddddWK..",
    "..KwKKKKKKKKKKKKKKKKdK..",
    "..KwKbbbbbbbbbbbbbbKdK..",
    "..KwKBBBBLLBLLBBBBBKdK..",
    "..KwKBBBLLLgLLLBBBBKdK..",
    "..KwKBBBBBLgLBBBBBBKdK..",
    "..KwKGGGGGGgGGGGGGGKdK..",
    "..KwKGgGGGSSSGGGgGGKdK..",
    "..KwKSSSSSSSSSSSSSSKdK..",
    "..KWKKKKKKKKKKKKKKKKdK..",
    "...KKKKKKKKKKKKKKKKKK...",
    "........................",
]


def render() -> Image.Image:
    assert len(ART) == 24 and all(len(r) == 24 for r in ART), "art must be 24x24"
    img = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
    for y, row in enumerate(ART):
        for x, ch in enumerate(row):
            c = PAL[ch]
            if c:
                img.putpixel((x, y), c + (255,))
    return img


def main(out: str) -> None:
    os.makedirs(out, exist_ok=True)
    base = render()
    base.resize((384, 384), Image.NEAREST).save(os.path.join(out, "preview.png"))
    for dens, size in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
        d = os.path.join(out, f"mipmap-{dens}")
        os.makedirs(d, exist_ok=True)
        base.resize((size, size), Image.NEAREST).save(os.path.join(d, "ic_launcher.png"))


if __name__ == "__main__":
    main(sys.argv[1])
