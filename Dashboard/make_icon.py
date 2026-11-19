"""Render app.svg into a multi-size app.ico.

Requires: pip install resvg-py pillow
Usage:    python make_icon.py
"""
import io
from pathlib import Path

import resvg_py
from PIL import Image

HERE = Path(__file__).parent
SIZES = [16, 24, 32, 48, 64, 256]


def render(size: int) -> Image.Image:
    png = resvg_py.svg_to_bytes(svg_path=str(HERE / "app.svg"), width=size, height=size)
    return Image.open(io.BytesIO(bytes(png))).convert("RGBA")


# Render each size from the vector source rather than downscaling one bitmap.
images = [render(s) for s in SIZES]
images[-1].save(HERE / "app.ico", format="ICO", append_images=images[:-1],
                sizes=[(s, s) for s in SIZES])
print(f"wrote app.ico ({', '.join(map(str, SIZES))})")
