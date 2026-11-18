"""Codex 로 만든 아이콘 원본을 WinDeck 버튼용 128px PNG 로 다듬는다.

usage: python process_icons.py <원본 폴더> <assets/icons 폴더> [--suffix -2d] [--sheet 미리보기.png]

- 원본 폴더의 <name>.png 를 assets/icons/manifest.tsv 의 이름 순서대로 처리 (원본이 있는 것만)
- --suffix -2d: manifest 에서 이름이 -2d 로 끝나는 줄만, 원본은 접미사를 뺀 이름(folder-2d → folder.png)
- 투명 여백을 잘라 정사각형 가운데에 맞추고(여백 4%), 반투명 잡티를 지운 뒤 128px 로 축소
- --sheet: 여러 배경색 위에 올린 대조용 미리보기 이미지
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

SIZE = 128
MARGIN = 0.04
ALPHA_FLOOR = 24  # 이보다 옅은 픽셀은 배경 제거 잔여물로 보고 투명 처리
STYLE_SUFFIXES = ["-2d"]  # 입체(기본) 외 스타일 세트의 이름 접미사


def load_manifest(icon_dir: Path):
    rows = []
    for line in (icon_dir / "manifest.tsv").read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        name, label, category = (f.strip() for f in line.split("\t")[:3])
        rows.append((name, label, category))
    return rows


def process(src: Path) -> Image.Image:
    im = Image.open(src).convert("RGBA")
    alpha = im.getchannel("A").point(lambda v: 0 if v < ALPHA_FLOOR else v)
    im.putalpha(alpha)
    bbox = alpha.point(lambda v: 255 if v > 0 else 0).getbbox()
    if bbox:
        im = im.crop(bbox)
    side = int(max(im.size) * (1 + MARGIN * 2))
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(im, ((side - im.width) // 2, (side - im.height) // 2), im)
    return canvas.resize((SIZE, SIZE), Image.LANCZOS)


def contact_sheet(items, path: Path):
    backs = [(52, 52, 56), (0, 99, 177), (16, 124, 16), (92, 45, 145), (196, 43, 28)]
    cell, cols = 104, 9
    rows = (len(items) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 18)), (28, 28, 30))
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("malgun.ttf", 12)
    except OSError:
        font = ImageFont.load_default()
    for i, (name, label, img) in enumerate(items):
        x, y = (i % cols) * cell, (i // cols) * (cell + 18)
        draw.rounded_rectangle([x + 6, y + 6, x + cell - 6, y + cell - 6], 14, fill=backs[i % len(backs)])
        icon = img.resize((60, 60), Image.LANCZOS)
        sheet.paste(icon, (x + (cell - 60) // 2, y + (cell - 60) // 2), icon)
        draw.text((x + cell // 2, y + cell + 2), label, fill=(230, 230, 235), font=font, anchor="mt")
    sheet.save(path)


def main():
    src_dir, out_dir = Path(sys.argv[1]), Path(sys.argv[2])
    sheet = Path(sys.argv[sys.argv.index("--sheet") + 1]) if "--sheet" in sys.argv else None
    suffix = sys.argv[sys.argv.index("--suffix") + 1] if "--suffix" in sys.argv else ""
    done, missing = [], []
    for name, label, _ in load_manifest(out_dir):
        if suffix and not name.endswith(suffix):
            continue
        if not suffix and any(name.endswith(x) for x in STYLE_SUFFIXES):
            continue
        src = src_dir / f"{name[:len(name) - len(suffix)] if suffix else name}.png"
        if not src.exists():
            missing.append(name)
            continue
        img = process(src)
        img.save(out_dir / f"{name}.png", optimize=True)
        done.append((name, label, img))
    print(f"processed {len(done)} icons" + (f", missing: {', '.join(missing)}" if missing else ""))
    if sheet:
        contact_sheet(done, sheet)
        print(f"contact sheet: {sheet}")


if __name__ == "__main__":
    main()
