import bpy
import os
import sys

source, destination = sys.argv[-2:]
bpy.ops.import_scene.fbx(filepath=source)
tree = next(obj for obj in bpy.data.objects if obj.type == 'MESH' and 'island_tree_02_LOD0' in obj.name)

for obj in list(bpy.data.objects):
    if obj != tree:
        bpy.data.objects.remove(obj, do_unlink=True)

tree.name = 'IslandTree02_LOD0'
source_mesh = tree.data.copy()
for index, ratio in enumerate((0.18, 0.06, 0.018)):
    obj = tree if index == 0 else tree.copy()
    if index:
        bpy.context.collection.objects.link(obj)
    obj.data = source_mesh.copy()
    obj.name = f'IslandTree02_LOD{index}'
    modifier = obj.modifiers.new('PrototypeDecimation', 'DECIMATE')
    modifier.ratio = ratio
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)
    count = sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons)
    print(f'VEG_LOD {obj.name} triangles={count}')

os.makedirs(os.path.dirname(destination), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=destination, use_selection=False, apply_unit_scale=True,
                         axis_forward='-Z', axis_up='Y', add_leaf_bones=False)
