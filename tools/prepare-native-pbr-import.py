"""Prepare isolated LOD0 PBR candidates; never edit source or installed assets.

Source MGA: R metal, G gloss, B AO (Transport Fever material documentation).
CS2 MaskMap: R metal, G coat, B unused, A gloss (MW CS2 PBR exporter).
AO is retained separately for authoring, NOT written into CS2's coat channel.
These candidates still require native import and visual acceptance.
"""
from pathlib import Path
import json
import shutil
import importlib.util
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('native_converter', ROOT/'tools/convert-native-assets.py')
converter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(converter)


def pack_mask(mga):
    out = np.zeros((*mga.shape[:2], 4), dtype=np.uint8)
    out[..., 0] = mga[..., 0]
    out[..., 3] = mga[..., 1]
    return out


def flip_obj_v(text):
    """TF DDS uses top-down V; OBJ/native PNG pipeline retains OBJ UV values."""
    lines = []
    for line in text.splitlines(keepends=True):
        if line.startswith('vt '):
            words = line.split()
            words[2] = format(1.0-float(words[2]), '.9g')
            line = ' '.join(words)+'\n'
        lines.append(line)
    return ''.join(lines)


def prepare():
    dest = ROOT/'assets_game_import/pbr-v2-uv'
    if dest.exists():
        raise FileExistsError('Inspect existing PBR candidates instead of overwriting: '+str(dest))
    audit = json.loads((ROOT/'artifacts/native-import-20261027/material-source-audit.json').read_text())
    safe = json.loads((ROOT/'assets_game_import/diagnostics/TerminalRoofMatteMappedAB/settings.json').read_text())
    safe['importSettings'].pop('#cwMatte.Surface')
    safe['assetMapping'].pop('*')
    linear = dict(safe['importSettings']['#cwSafe.Texture'], linearTexture=True)
    safe['importSettings']['#cwLinear.Texture'] = linear
    safe['assetMapping']['*_MaskMap{Texture}'] = ['#cwLinear.Texture']
    report = {'status': 'prepared; import and visual QA pending', 'materials': [], 'groups': [],
              'sources': ['https://www.wiki.transportfever2.com/doku.php?id=modding:resourcetypes:mtl',
                          'https://github.com/Macwelshman/MW-CS2-PBR-Material'],
              'limitations': ['AO archived separately, not baked into albedo',
                             'No source layered dirt/rust/operations or emissive conversion',
                             'No LOD, animation or gameplay configuration']}
    report['uv_conversion'] = 'v -> 1-v; geometry positions/normals/faces unchanged; normal handedness still requires visual QA'
    groups = {}
    for row in audit['materials']:
        model = 'CW'+''.join(s.capitalize() for s in row['model'].split('_'))
        part = model+'Part'+str(row['part']).zfill(2)
        group = model+'PBRSection'+str((row['part']-1)//8+1)
        folder = dest/group
        folder.mkdir(parents=True, exist_ok=True)
        settings = groups.setdefault(group, json.loads(json.dumps(safe)))
        source = ROOT/'assets_game_import'/model
        for p in source.glob(part+'*'):
            if p.suffix == '.png':
                with Image.open(p) as im:
                    im.convert('RGBA').save(folder/p.name)
            elif p.suffix == '.obj':
                (folder/p.name).write_text(flip_obj_v(p.read_text(encoding='utf-8')), encoding='utf-8')
            else:
                shutil.copy2(p, folder/p.name)
        assert (folder/(part+'.obj')).is_file()
        material = ROOT/row['source_material']
        definition = converter.parse(material)
        params = definition.get('params', {})
        refs = list(converter.values(params.get('map_metal_gloss_ao', {}), 'fileName'))
        mask_source = None
        if refs:
            package = ROOT/'assets'/row['package']
            dds = converter.resolve(material.parent, refs[0], package)
            png = ROOT/'assets_converted'/row['package']/'textures'/dds.relative_to(package).with_suffix('.png')
            with Image.open(png) as im:
                mga = np.array(im.convert('RGBA'))
            Image.fromarray(pack_mask(mga)).save(folder/(part+'_MaskMap.png'))
            ao = dest/'authoring-ao'/group
            ao.mkdir(parents=True, exist_ok=True)
            Image.fromarray(mga[..., 2]).save(ao/(part+'_SourceAO.png'))
            mask_source = str(png.relative_to(ROOT))
        report['materials'].append({'part': part, 'group': group, 'source_mga': mask_source,
                                    'source_shader': row['source_shader']})
    for group, settings in groups.items():
        folder = dest/group
        (folder/'settings.json').write_text(json.dumps(settings, indent=2), encoding='utf-8')
        parts = len(list(folder.glob('*.obj')))
        assert 1 <= parts <= 8
        report['groups'].append({'directory': str(folder), 'parts': parts})
    (dest/'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'groups': len(groups), 'materials': len(report['materials']),
                      'masks': sum(bool(r['source_mga']) for r in report['materials']), 'directory': str(dest)}))


if __name__ == '__main__':
    probe = np.array([[[11, 33, 77, 255], [0, 255, 0, 255]]], dtype=np.uint8)
    assert pack_mask(probe).tolist() == [[[11, 0, 0, 33], [0, 0, 0, 255]]]
    assert flip_obj_v('v 1 2 3\nvt 0.2 -0.5\nvt 0.3 1.75\nf 1/1 2/2 3/3\n') == 'v 1 2 3\nvt 0.2 1.5\nvt 0.3 -0.75\nf 1/1 2/2 3/3\n'
    prepare()
