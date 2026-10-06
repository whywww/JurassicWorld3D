"""Prepare one complete tree and three bounded LODs per Poly Haven source FBX."""
import bpy
import json
import os
import sys
from mathutils import Vector

project = os.path.abspath(sys.argv[-1])
assets = [
    ('island_tree_01', 'IslandTree01'),
    ('island_tree_03', 'IslandTree03'),
    ('tree_small_02', 'TreeSmall02'),
    ('jacaranda_tree', 'JacarandaTree'),
    ('fir_tree_01', 'FirTree01'),
]
reports = []
for asset, name in assets:
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    source = os.path.join(project, 'Assets/ThirdParty/VegetationDiversity/PolyHaven', name, asset + '_1k.fbx')
    bpy.ops.import_scene.fbx(filepath=source)
    meshes = [obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.name != 'Cube']
    print('PREVIEW_SOURCE', asset, [(obj.name, len(obj.data.polygons)) for obj in meshes], flush=True)
    # These exports contain assembled trees. Choose the lowest supplied complete LOD.
    lods = sorted({int(obj.name.rsplit('LOD', 1)[1].split('.')[0]) for obj in meshes if 'LOD' in obj.name})
    if lods:
        level = max(lods)
        meshes = [obj for obj in meshes if 'LOD' + str(level) in obj.name]
    if asset == 'fir_tree_01':
        # A/B/C are alternative trees, not parts of the same tree.
        variants = [obj for obj in meshes if '_a_' in obj.name.lower() or obj.name.lower().endswith('_a')]
        if variants:
            meshes = variants
        elif len(meshes) > 1:
            meshes = [sorted(meshes, key=lambda obj: obj.name)[0]]
    if not meshes:
        raise RuntimeError('No complete tree found: ' + asset)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    tree = bpy.context.object
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # Root at the ground under the wooden trunk, keeping native dimensions.
    wood_slots = {i for i, material in enumerate(tree.data.materials)
                  if material and not any(word in material.name.lower() for word in ('leaves', 'leaf', 'twig', 'branch'))}
    wood_ids = {index for polygon in tree.data.polygons if polygon.material_index in wood_slots for index in polygon.vertices}
    vertices = [tree.data.vertices[index] for index in wood_ids] or list(tree.data.vertices)
    base = min(vertex.co.z for vertex in vertices)
    bottom = [vertex.co for vertex in vertices if vertex.co.z <= base + .25]
    center = sum(bottom, Vector()) / len(bottom)
    offset = Vector((center.x, center.y, base))
    for vertex in tree.data.vertices:
        vertex.co -= offset
    tree.location = Vector()
    source_mesh = tree.data.copy()
    source_triangles = sum(len(face.vertices) - 2 for face in source_mesh.polygons)
    objects, counts = [], []
    for level, budget in enumerate((180000, 65000, 20000)):
        obj = tree if level == 0 else tree.copy()
        obj.data = source_mesh.copy()
        if level:
            bpy.context.collection.objects.link(obj)
        obj.name = name + '_LOD' + str(level)
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        if source_triangles > budget:
            modifier = obj.modifiers.new('PreviewLOD', 'DECIMATE')
            modifier.ratio = budget / source_triangles
            modifier.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
        count = sum(len(face.vertices) - 2 for face in obj.data.polygons)
        counts.append(count)
        objects.append(obj)
        print('PREVIEW_LOD', obj.name, count, flush=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    destination = os.path.join(project, 'Assets/ThirdParty/VegetationDiversity/Prepared', name + '_LODs.fbx')
    bpy.ops.export_scene.fbx(filepath=destination, use_selection=True, apply_unit_scale=True,
                             axis_forward='-Z', axis_up='Y', add_leaf_bones=False)
    reports.append({'asset': asset, 'name': name, 'source_triangles': source_triangles,
                    'lod_triangles': counts, 'dimensions_m': list(objects[0].dimensions),
                    'materials': [m.name for m in objects[0].data.materials if m]})
    print('PREVIEW_PREPARED', name, flush=True)
with open(os.path.join(project, 'Tools/VegetationDiversity/five-tree-model-report.json'), 'w', encoding='utf-8') as output:
    json.dump(reports, output, indent=2)
