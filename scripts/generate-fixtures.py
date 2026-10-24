"""Generate explicitly synthetic HP crops; requires Pillow. Never overwrites user samples."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json, random
root = Path(__file__).resolve().parents[1] / 'tests/fixtures/synthetic'
root.mkdir(parents=True, exist_ok=True)
font_path = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
font = ImageFont.truetype(font_path, 20)
manifest = []
for name, text, hp in [('healthy','800 / 1200',800),('threshold','350 / 1200',350),
                       ('low','349 / 1200',349),('critical','25 / 1200',25),
                       ('zero','0 / 1200',0),('four-digit','2345 / 3000',2345),
                       ('single','320',320),('menu','SETTINGS',None),
                       ('bad-fraction','800 / 100',None),('blank','',None),('noise','',None)]:
    image = Image.new('RGB', (180,32))
    draw = ImageDraw.Draw(image)
    for y in range(32): draw.line((0,y,179,y),fill=(15,70-y,24))
    if text:
        bbox = draw.textbbox((0,0), text, font=font)
        draw.text(((180-(bbox[2]-bbox[0]))//2,1), text, font=font, fill='white', stroke_width=0)
    if name == 'noise':
        rng = random.Random(42)
        for _ in range(100): draw.point((rng.randrange(180),rng.randrange(32)), fill=(190,190,190))
    image.save(root / (name+'.png'))
    manifest.append({'File':'synthetic/'+name+'.png','Hp':hp})
manifest_path = root.parent/'manifest.json'
if manifest_path.exists():
    manifest += [entry for entry in json.loads(manifest_path.read_text()) if not entry['File'].startswith('synthetic/')]
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
