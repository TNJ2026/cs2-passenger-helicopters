"""Exclude animated blur states from the parked static Mi-26 preview.

Keep all OBJ attribute declarations so original positive face indices stay
valid. Remove only faces belonging to the two blur-state groups. Do not alter
the source geometry or confuse this parked model with an animated vehicle.
"""
from pathlib import Path
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]
BLURRED = {'rotor_b_top', 'rotor_b_bck'}

def parked_obj(text):
    group = None
    removed = {}
    out = []
    retained = {}
    for line in text.splitlines(keepends=True):
        if line.startswith('g '):
            group = line.strip()[2:]
        if line.startswith('f '):
            counts = removed if group in BLURRED else retained
            counts[group] = counts.get(group, 0) + 1
            if group in BLURRED:
                continue
        out.append(line)
    return ''.join(out), removed, retained

def prepare():
    source = ROOT/'assets_game_import/special-materials-v3/CWMi26BodyV3'
    dest = ROOT/'assets_game_import/rotor-v4/CWMi26ParkedBodyV4'
    if dest.exists():
        raise FileExistsError(dest)
    dest.mkdir(parents=True)
    for p in source.iterdir():
        if p.is_file() and not p.name.startswith(('CWMilMi26tcPart07', 'CWMilMi26tcPart08')):
            shutil.copy2(p, dest/p.name)
    obj = dest/'CWMilMi26tcPart01.obj'
    original = obj.read_text()
    result, removed, retained = parked_obj(original)
    assert set(removed) == BLURRED
    assert retained['rotor_top'] > 0 and retained['rotor_bck'] > 0
    assert [s for s in result.splitlines() if s.startswith(('v ', 'vt ', 'vn '))] == [s for s in original.splitlines() if s.startswith(('v ', 'vt ', 'vn '))]
    obj.write_text(result, encoding='utf-8')
    report = {'removed_faces':removed,'retained_faces':retained,
              'omitted_material_parts':[7,8], 'state':'parked static preview',
              'limitation':'No animated rotor motion or runtime blur switching.'}
    (dest.parent/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))

if __name__ == '__main__':
    sample='v 0 0 0\ng rotor_top\nf 1/1/1 1/1/1 1/1/1\ng rotor_b_top\nf 1/1/1 1/1/1 1/1/1\n'
    output, removed, retained = parked_obj(sample)
    assert output.count('f ') == 1 and removed == {'rotor_b_top':1} and retained == {'rotor_top':1}
    prepare()
