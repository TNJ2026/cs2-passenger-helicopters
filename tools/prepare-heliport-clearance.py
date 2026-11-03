"""Create isolated airport authoring copies with a 20 cm pavement clearance.

This is a preview asset preparation step, not terrain-following or a building
placement policy. Keep source module origins and all texture references intact.
"""
from pathlib import Path
import json
import shutil
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'assets_game_import/heliport-clearance-v7'

def prepare():
    OUT.mkdir(parents=True, exist_ok=False)
    sources = sorted((ROOT/'assets_game_import/pbr-v2-uv').glob('CWHeliportPBRSection*'))
    sources += sorted((ROOT/'assets_game_import/pbr-v2-uv').glob('CWHeliportTerminalPBRSection*'))
    sources += [ROOT/'assets_game_import/heliport-v7/CWHeliportSiteV7']
    rows = []
    for source in sources:
        dest = OUT/(source.name+'Clearance')
        shutil.copytree(source, dest)
        count = 0
        for obj in dest.glob('*.obj'):
            lines = []
            for line in obj.read_text().splitlines():
                if line.startswith('v '):
                    xyz = np.array([float(v) for v in line.split()[1:]])
                    assert len(xyz) == 3 and np.isfinite(xyz).all()
                    xyz[1] += .2
                    line = 'v '+' '.join(format(v, '.10g') for v in xyz)
                    count += 1
                lines.append(line)
            obj.write_text('\n'.join(lines)+'\n')
        assert count > 0
        rows.append({'source': str(source.relative_to(ROOT)), 'output': str(dest.relative_to(ROOT)),
                     'vertices': count, 'vertical_translation_m': .2})
    (OUT/'validation.json').write_text(json.dumps(rows, indent=2))
    print(json.dumps(rows, indent=2))

if __name__ == '__main__':
    prepare()
