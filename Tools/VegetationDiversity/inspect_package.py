import bpy
import glob
import os
import sys

for source in glob.glob(os.path.join(sys.argv[-1], '**', '*.fbx'), recursive=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)
    meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH']
    summary = []
    for obj in meshes:
        triangles = sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons)
        summary.append((obj.name, triangles, tuple(round(x, 2) for x in obj.dimensions), [m.name if m else None for m in obj.data.materials]))
    print(f'VEG_PACKAGE {os.path.basename(source)} meshes={len(meshes)} total_tris={sum(row[1] for row in summary)} details={summary}')
