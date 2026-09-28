import bpy
import sys

bpy.ops.import_scene.fbx(filepath=sys.argv[-1])
for obj in bpy.data.objects:
    if obj.type != 'MESH' or obj.name == 'Cube':
        continue
    mesh = obj.data
    for slot, material in enumerate(mesh.materials):
        indices = {v for face in mesh.polygons if face.material_index == slot for v in face.vertices}
        if not indices:
            continue
        points = [obj.matrix_world @ mesh.vertices[i].co for i in indices]
        minimum = tuple(round(min(p[axis] for p in points), 3) for axis in range(3))
        maximum = tuple(round(max(p[axis] for p in points), 3) for axis in range(3))
        triangles = sum(len(face.vertices) - 2 for face in mesh.polygons if face.material_index == slot)
        radial = []
        for face in mesh.polygons:
            if face.material_index != slot or face.center.xy.length < 0.05:
                continue
            radial.append(face.normal.xy.normalized().dot(face.center.xy.normalized()))
        negative = sum(1 for value in radial if value < -0.1)
        positive = sum(1 for value in radial if value > 0.1)
        print(f'VEG_PART object={obj.name} slot={slot} material={material.name if material else None} tris={triangles} min={minimum} max={maximum} radialNormalsOut={positive} in={negative}')
