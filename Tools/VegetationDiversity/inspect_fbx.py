import bpy
import sys

source = sys.argv[-1]
bpy.ops.import_scene.fbx(filepath=source)
for obj in bpy.data.objects:
    if obj.type == 'MESH':
        triangles = sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons)
        print(f'VEG_MESH name={obj.name!r} parent={obj.parent.name if obj.parent else None} verts={len(obj.data.vertices)} tris={triangles} materials={[m.name if m else None for m in obj.data.materials]} location={tuple(obj.location)} dimensions={tuple(obj.dimensions)} scale={tuple(obj.scale)} rotation={tuple(obj.rotation_euler)}')
