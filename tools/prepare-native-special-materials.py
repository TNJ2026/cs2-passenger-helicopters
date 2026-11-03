"""Isolated source-faithful light/glass probes; no installed asset overwrites."""
from pathlib import Path
import json
import shutil
import importlib.util
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('converter', ROOT/'tools/convert-native-assets.py')
converter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(converter)

def prepare():
    dest = ROOT/'assets_game_import/special-materials-v3'
    if dest.exists():
        raise FileExistsError(dest)
    audit = json.loads((ROOT/'artifacts/native-import-20261027/material-source-audit.json').read_text())
    probes = [('helipad', 14, 'CWHelipadLightV3'), ('mil_mi26tc', 4, 'CWMi26GlassV3')]
    records = []
    for model, number, name in probes:
        row = next(r for r in audit['materials'] if r['model'] == model and r['part'] == number)
        prefix = 'CW'+''.join(s.capitalize() for s in model.split('_'))
        part = prefix+'Part'+str(number).zfill(2)
        source = ROOT/'assets_game_import/pbr-v2-uv'/(prefix+'PBRSection'+str((number-1)//8+1))
        folder = dest/name
        folder.mkdir(parents=True)
        for p in source.glob(part+'*'):
            shutil.copy2(p, folder/p.name)
        settings = json.loads((source/'settings.json').read_text())
        definition = converter.parse(ROOT/row['source_material'])
        if model == 'helipad':
            material = ROOT/row['source_material']
            ref = next(converter.values(definition['params']['map_emissive'], 'fileName'))
            package = ROOT/'assets'/row['package']
            dds = converter.resolve(material.parent, ref, package)
            png = ROOT/'assets_converted'/row['package']/'textures'/dds.relative_to(package).with_suffix('.png')
            with Image.open(png) as im:
                base = im.convert('RGBA')
            base.putalpha(255)
            base.save(folder/(part+'_BaseColorMap.png'))
            # A=1 is native emissive light-group ID, not source transparency.
            emission = base.copy()
            emission.putalpha(1)
            emission.save(folder/(part+'_Emissive0.png'))
            Image.new('RGBA', base.size, (0, 0, 0, 0)).save(folder/(part+'_MaskMap.png'))
            Image.new('RGBA', base.size, (128, 128, 255, 255)).save(folder/(part+'_NormalMap.png'))
            (folder/(part+'.mtl')).write_text('newmtl '+part+'\nKd 1 1 1\nmap_Kd '+part+'_BaseColorMap.png\n', encoding='utf-8')
            settings['importSettings']['#cwEmission.Model'] = {'@type':'Colossal.AssetPipeline.PostProcessors.Emissive.EmissiveBakingPostProcessor+PostProcessSettings'}
            settings['assetMapping'][part] = ['#cwEmission.Model']
            records.append({'name':name,'part':part,'source_emission':str(png),'source_scale':definition['params']['emissive_scale'],'note':'Native emissive baking probe. Source intensity/time behavior is recorded, not mapped to runtime light logic.'})
        else:
            settings['importSettings']['#cwGlass.Surface'] = {'@type':'Colossal.AssetPipeline.PostProcessors.SurfacePostProcessor+PostProcessSettings','materialTemplate':'DefaultGlass','needsAlpha':True,'needsAlphaClip':False}
            settings['assetMapping'][part] = ['#cwGlass.Surface']
            records.append({'name':name,'part':part,'template':'DefaultGlass','source_alpha':row['alpha_settings']})
        (folder/'settings.json').write_text(json.dumps(settings,indent=2),encoding='utf-8')
    # Rebuild the parent groups without the isolated material to avoid double
    # geometry when composing the corrected full models.
    for source_name, excluded, target in [
        ('CWHelipadPBRSection2', 'CWHelipadPart14', 'CWHelipadBodyV3'),
        ('CWMilMi26tcPBRSection1', 'CWMilMi26tcPart04', 'CWMi26BodyV3'),
    ]:
        folder = dest/target
        folder.mkdir()
        for p in (ROOT/'assets_game_import/pbr-v2-uv'/source_name).iterdir():
            if p.is_file() and not p.name.startswith(excluded):
                shutil.copy2(p, folder/p.name)
        records.append({'name':target,'source_group':source_name,'excluded':excluded})
    (dest/'manifest.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
    print(json.dumps(records,indent=2))

if __name__ == '__main__':
    prepare()
