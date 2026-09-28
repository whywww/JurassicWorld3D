import bpy
import os
import sys

source, destination = sys.argv[-2:]
bpy.ops.import_scene.fbx(filepath=source)
tree = next(obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.name != 'Cube')
source_mesh = tree.data.copy()
objects = []
for index, ratio in enumerate((1.0, 0.4, 0.16)):
    obj = tree if index == 0 else tree.copy()
    if index:
        obj.data = source_mesh.copy()
        bpy.context.collection.objects.link(obj)
    obj.name = f'{os.path.splitext(os.path.basename(source))[0]}_LOD{index}'
    if index:
        modifier = obj.modifiers.new('LODDecimation', 'DECIMATE')
        modifier.ratio = ratio
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
    triangles = sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons)
    print(f'VEG_FAB_LOD {obj.name} ratio={ratio} triangles={triangles}')
    objects.append(obj)

bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.select_set(True)
os.makedirs(os.path.dirname(destination), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=destination, use_selection=True, apply_unit_scale=True,
                         axis_forward='-Z', axis_up='Y', add_leaf_bones=False)
print(f'VEG_FAB_LOD_EXPORTED {destination}')
