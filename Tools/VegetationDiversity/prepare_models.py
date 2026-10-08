import bpy
import os
import sys

kind, source, destination = sys.argv[-3:]
bpy.ops.import_scene.fbx(filepath=source)

def export(objects, target):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=target, use_selection=True, apply_unit_scale=True,
                             axis_forward='-Z', axis_up='Y', add_leaf_bones=False)
    print(f'VEG_PREPARED {target} meshes={[obj.name for obj in objects if obj.type == "MESH"]}')

if kind == 'pachira':
    for letter in 'abcd':
        bark = bpy.data.objects[f'pachira_aquatica_01_bark_{letter}']
        leaf = bpy.data.objects[f'pachira_aquatica_01_leaves_{letter}']
        offset = bark.location.x
        bark.location.x -= offset
        leaf.location.x -= offset
        export([bark, leaf], os.path.join(destination, f'Pachira_{letter.upper()}.fbx'))
elif kind == 'single':
    meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.name != 'Cube']
    export(meshes, destination)
elif kind == 'banana':
    meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.name != 'Cube']
    for obj in meshes:
        mesh = obj.data
        bark_slots = {i for i, material in enumerate(mesh.materials)
                      if material and 'bark' in material.name.lower()}
        bark_vertices = {index for face in mesh.polygons if face.material_index in bark_slots
                         for index in face.vertices}
        for index in bark_vertices:
            mesh.vertices[index].co.x *= 1.7
            mesh.vertices[index].co.y *= 1.7
    export(meshes, destination)
else:
    raise ValueError(kind)
