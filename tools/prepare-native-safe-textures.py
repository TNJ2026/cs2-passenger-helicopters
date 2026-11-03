"""Isolate PNG encoding and native texture compression for an import diagnostic."""
from pathlib import Path
from PIL import Image
import json,shutil,sys
root=Path(__file__).resolve().parents[1]
name=sys.argv[1] if len(sys.argv)>1 else 'CWHelipad'
source=root/'assets_game_import'/name
suffix=sys.argv[2] if len(sys.argv)>2 else 'SafeTextures'
dest=root/'assets_game_import/diagnostics'/(name+suffix)
dest.mkdir(parents=True,exist_ok=False)
for p in source.iterdir():
    if p.suffix=='.png':
        with Image.open(p) as im: im.convert('RGBA').save(dest/p.name)
    else:shutil.copy2(p,dest/p.name)
settings={
 'importSettings':{'#cwSafe.Texture':{
 '@type':'Colossal.AssetPipeline.Importers.DefaultTextureImporter+ImportSettings',
 'normalMap':False,'linearTexture':False,'computeMips':True,'singleChannel':False,
 'keep16Bit':False,'compressBC':False,'compressionEffort':0,'filterMode':'Trilinear',
 'wrapMode':'Repeat','anisoLevel':16,'alphaIsTransparency':False,'overrideCompressionFormat':'None'},
 '#cwSafeNormal.Texture':{
 '@type':'Colossal.AssetPipeline.Importers.DefaultTextureImporter+ImportSettings',
 'normalMap':True,'linearTexture':False,'computeMips':True,'singleChannel':False,
 'keep16Bit':False,'compressBC':False,'compressionEffort':0,'filterMode':'Trilinear',
 'wrapMode':'Repeat','anisoLevel':16,'alphaIsTransparency':False,'overrideCompressionFormat':'None'}},
 'assetMapping':{'*{Texture}':['#cwSafe.Texture'],'*{_Normal}{Texture}':['#cwSafeNormal.Texture']}}
(dest/'settings.json').write_text(json.dumps(settings,indent=2),encoding='utf-8')
print(dest)
