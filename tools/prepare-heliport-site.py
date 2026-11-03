"""Author a reversible airport site module around the retained source coordinates.

Reference: assets/heliport-native/rs.png. This reconstructs the missing layout,
not the original game's facility data. Operational lanes/terrain are separate.
"""
from pathlib import Path
import json
import shutil
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets_game_import/heliport-v7/CWHeliportSiteV7'


def prepare():
    OUT.mkdir(parents=True, exist_ok=False)
    report = {'reference': 'assets/heliport-native/rs.png',
              'road_frontage': 'positive X, boundary x=62m',
              'bounds_xz': [-50, -50, 62, 52],
              'limitations': ['Visual layout only; parking bays have no simulation lanes.',
                              'Flat ground sheet requires terrain integration before operational use.'],
              'parts': {}}
    for name, color, height in [('Asphalt', (94, 97, 98, 255), -.04),
                                ('ParkingWhite', (218, 216, 202, 255), .025)]:
        vertices, faces = [], []

        def rect(x0, z0, x1, z1):
            i = len(vertices)
            vertices.extend([(x0, height, z0), (x1, height, z0),
                             (x1, height, z1), (x0, height, z1)])
            faces.extend([(i, i+2, i+1), (i, i+3, i+2)])

        if name == 'Asphalt':
            rect(-50, -50, 62, 52)
        else:
            # Two banks leave an entrance crossing at the terminal centre.
            for start in (-36, 12):
                for j in range(9):
                    z = start + j * 3
                    rect(51, z, 57, z+.14)
                rect(51, start, 51.14, start+24)
            for z in np.arange(-5, 5, 1.5):
                rect(50, float(z), 61, float(z)+.65)
        triangles = np.array(vertices)[np.array(faces)]
        assert np.isfinite(triangles).all()
        assert (np.cross(triangles[:, 1]-triangles[:, 0],
                         triangles[:, 2]-triangles[:, 0])[:, 1] > 0).all()
        part = 'CWHeliportSite' + name
        obj = [f'mtllib {part}.mtl', f'usemtl {part}']
        obj += ['v ' + ' '.join(map(str, v)) for v in vertices]
        obj += ['vt 0.5 0.5', 'vn 0 1 0']
        obj += ['f ' + ' '.join(f'{i+1}/1/1' for i in face) for face in faces]
        (OUT / (part+'.obj')).write_text('\n'.join(obj)+'\n')
        (OUT / (part+'.mtl')).write_text(f'newmtl {part}\nKd 1 1 1\nmap_Kd {part}_BaseColorMap.png\n')
        for suffix, value in [('BaseColorMap', color), ('MaskMap', (0, 0, 0, 15)),
                              ('NormalMap', (128, 128, 255, 255))]:
            Image.new('RGBA', (32, 32), value).save(OUT/(part+'_'+suffix+'.png'))
        report['parts'][name] = {'triangles': len(faces), 'height_m': height}
    shutil.copy2(ROOT/'assets_game_import/pbr-v2-uv/CWHeliportPBRSection1/settings.json', OUT/'settings.json')
    (OUT.parent/'validation.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    prepare()
