"""Create the System Pulse icon from simple vector geometry."""

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "XinweiManager" / "Assets" / "Brand"
OUT.mkdir(parents=True, exist_ok=True)
S = 1024
im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(im)

# Angular shell, asymmetric neon accents, and a single readable pulse.
d.polygon([(126, 66), (848, 66), (958, 176), (958, 848),
           (848, 958), (176, 958), (66, 848), (66, 126)], fill="#0B1422")
d.line([(126, 66), (848, 66), (958, 176), (958, 848),
        (848, 958), (176, 958), (66, 848), (66, 126), (126, 66)],
       fill="#00E5FF", width=38, joint="curve")
d.polygon([(66, 126), (126, 66), (416, 66), (354, 143), (143, 143),
           (143, 354), (66, 416)], fill="#FCEE0A")
d.polygon([(958, 608), (958, 848), (848, 958), (608, 958),
           (680, 881), (826, 881), (881, 826), (881, 680)], fill="#FF4AAE")
d.line([(146, 557), (315, 557), (408, 367), (500, 729),
        (600, 461), (675, 557), (870, 557)], fill="#06121F", width=105,
       joint="curve")
d.line([(146, 557), (315, 557), (408, 367), (500, 729),
        (600, 461), (675, 557), (870, 557)], fill="#FCEE0A", width=66,
       joint="curve")
d.ellipse((271, 520, 344, 593), fill="#00E5FF")
d.ellipse((830, 520, 903, 593), fill="#00E5FF")

im.save(OUT / "SystemPulse.png")
im.save(OUT / "SystemPulse.ico", format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64),
               (128, 128), (256, 256)])
