"""Preserve the LOD0 trunk at every distance and thin complete Jacaranda leaf cards."""
import bpy
import bmesh
import json
import os
import sys

project = os.path.abspath(sys.argv[-1])
report_path = os.path.join(project, 'Tools/VegetationDiversity/five-tree-model-report.json')
with open(report_path, encoding='utf-8') as source:
    reports = json.load(source)

def filter_faces(mesh, retain):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[face for face in bm.faces if not retain(face)], context='FACES')
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

def merge_material_uvs(mesh):
    # Poly Haven FBXs store branch cards in another named UV layer, with zero UV0.
    # Built-in Unity shaders consume UV0, so copy the populated layer per material.
    if len(mesh.uv_layers) < 2:
        return
    for slot in range(len(mesh.materials)):
        loops = [index for face in mesh.polygons if face.material_index == slot for index in face.loop_indices]
        if not loops:
            continue
        sums = [sum(layer.data[index].uv.length_squared for index in loops) for layer in mesh.uv_layers]
        if sums[0] > .00001:
            continue
        selected = max(range(len(sums)), key=lambda index: sums[index])
        if selected:
            for index in loops:
                mesh.uv_layers[0].data[index].uv = mesh.uv_layers[selected].data[index].uv
            print('PREVIEW_UV0', mesh.materials[slot].name, mesh.uv_layers[selected].name, flush=True)

for report in reports:
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    path = os.path.join(project, 'Assets/Environment/Vegetation/Trees/Prepared/Models', report['name'] + '_LODs.fbx')
    bpy.ops.import_scene.fbx(filepath=path)
    objects = sorted([obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.name != 'Cube'], key=lambda obj: obj.name)
    high = objects[0]
    wood = {i for i, material in enumerate(high.data.materials)
            if material and not any(word in material.name.lower() for word in ('leaves', 'leaf', 'twig', 'branch'))}
    if not any(face.material_index in wood for face in high.data.polygons):
        raise RuntimeError('LOD0 trunk missing: ' + report['name'])
    trunk_mesh = high.data.copy()
    filter_faces(trunk_mesh, lambda face: face.material_index in wood)
    for level, obj in enumerate(objects):
        if level == 0:
            continue
        filter_faces(obj.data, lambda face: face.material_index not in wood)
        trunk = bpy.data.objects.new('Preview preserved trunk', trunk_mesh.copy())
        bpy.context.collection.objects.link(trunk)
        trunk.matrix_world = high.matrix_world.copy()
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        trunk.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.join()
        if report['name'] == 'JacarandaTree' and level == 2:
            mesh = obj.data
            parents = list(range(len(mesh.vertices)))
            def find(index):
                while parents[index] != index:
                    parents[index] = parents[parents[index]]
                    index = parents[index]
                return index
            for edge in mesh.edges:
                a, b = map(find, edge.vertices)
                if a != b:
                    parents[b] = a
            leaves = {i for i, material in enumerate(mesh.materials) if material and 'leaves' in material.name}
            groups = {}
            for face in mesh.polygons:
                if face.material_index in leaves:
                    groups.setdefault(find(face.vertices[0]), []).append(face.index)
            removed = {face for key, faces in groups.items() if ((key * 2654435761) & 0xffffffff) < 0x80000000 for face in faces}
            filter_faces(mesh, lambda face: face.index not in removed)
        report['lod_triangles'][level] = sum(len(face.vertices) - 2 for face in obj.data.polygons)
        print('PREVIEW_PRESERVED_TRUNK', obj.name, report['lod_triangles'][level], flush=True)
    for obj in objects:
        merge_material_uvs(obj.data)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
                             axis_forward='-Z', axis_up='Y', add_leaf_bones=False)
    report['trunk_preservation'] = 'LOD0 wood geometry reused at every distance'
    report['uv_mapping'] = 'Populated material-specific source UV layers merged into Unity UV0'
    if report['name'] == 'JacarandaTree':
        report['foliage_reduction'] = 'Stable sample of half the complete leaf cards in LOD2; branches retained'
with open(report_path, 'w', encoding='utf-8') as output:
    json.dump(reports, output, indent=2)
