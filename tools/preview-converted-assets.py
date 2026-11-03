from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
root=Path(__file__).resolve().parents[1]/'assets_converted'
fig=plt.figure(figsize=(15,10))
for i,folder in enumerate(sorted(root.glob('*/import/*/LOD0'))):
    ax=fig.add_subplot(2,3,i+1,projection='3d');triangles=[]
    for file in folder.glob('*.obj'):
        verts=[]
        for line in file.read_text().splitlines():
            a=line.split()
            if a and a[0]=='v':verts.append([float(x) for x in a[1:]])
            if a and a[0]=='f':triangles.append([verts[int(x.split('/')[0])-1] for x in a[1:]])
    t=np.array(triangles)[:,:,[0,2,1]]
    ax.add_collection3d(Poly3DCollection(t,facecolor='#8eaebc',edgecolor='none',alpha=1,rasterized=True))
    lo=t.min(axis=(0,1));hi=t.max(axis=(0,1));center=(lo+hi)/2;r=max(hi-lo)/2
    ax.set_xlim(center[0]-r,center[0]+r);ax.set_ylim(center[1]-r,center[1]+r);ax.set_zlim(center[2]-r,center[2]+r)
    ax.set_box_aspect((1,1,1));ax.view_init(elev=25,azim=-45);ax.set_axis_off();ax.set_title(folder.parent.name)
fig.suptitle('Converted LOD0 geometry - untextured inspection',fontsize=18)
fig.tight_layout();fig.savefig(root/'geometry-preview.png',dpi=120)
