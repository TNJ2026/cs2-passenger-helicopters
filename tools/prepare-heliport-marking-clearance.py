"""Separate source airport markings from the coplanar apron; retain UVs/materials."""
from pathlib import Path
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets_game_import/heliport-clearance-v7/CWHeliportTerminalPBRSection1Clearance'
OUT = ROOT / 'assets_game_import/heliport-markings-v8/CWHeliportTerminalMarkingsV8'

def prepare():
    source = SOURCE / 'CWHeliportTerminalPart01.obj'
    lines = source.read_text().splitlines()
    vertices = [list(map(float, line.split()[1:])) for line in lines if line.startswith('v ')]
    assert len(vertices) == 1094 and all(len(v) == 3 and abs(v[1] - .2) < .00001 for v in vertices)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(SOURCE, OUT)  # Deliberately refuses an existing output.
    result = []
    for line in lines:
        if line.startswith('v '):
            parts = line.split()
            parts[2] = '.24'
            line = ' '.join(parts)
        result.append(line)
    (OUT / source.name).write_text('\n'.join(result) + '\n')
    # Only vertex height may change; UVs, normals, face indices and texture bytes stay intact.
    assert [x for x in lines if not x.startswith('v ')] == [x for x in result if not x.startswith('v ')]
    for file in SOURCE.iterdir():
        if file.is_file() and file.name != source.name:
            assert file.read_bytes() == (OUT / file.name).read_bytes()
    report = {'source': str(source.relative_to(ROOT)), 'output': str(OUT.relative_to(ROOT)),
              'vertices': len(vertices), 'original_y_m': .2, 'corrected_y_m': .24,
              'apron_y_m': .2, 'marking_clearance_m': .04, 'uv_and_other_assets_unchanged': True,
              'note': 'Airport terminal markings only; standalone helipad unchanged. Needs live verification.'}
    (OUT.parent / 'validation.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))

if __name__ == '__main__':
    prepare()
