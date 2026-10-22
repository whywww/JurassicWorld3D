"""Blender --background --python Tools/Protoceratops/convert_model.py."""
import json
import pathlib
import zipfile
import bpy
import numpy as np
from mathutils import Matrix

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets/Dinosaurs/Protoceratops/Source'
PREPARED = SOURCE.parent / 'Prepared'
SOURCE.mkdir(parents=True, exist_ok=True)
PREPARED.mkdir(parents=True, exist_ok=True)
archive_path = ROOT / 'SourceArchives/Dinosaurs/Protoceratops/Protoceratops.zip'
if not archive_path.exists():
    archive_path.parent.mkdir(parents=True, exist_ok=True)
    archive_path.write_bytes((ROOT / 'Protoceratops.zip').read_bytes())
with zipfile.ZipFile(archive_path) as archive:
    for name in archive.namelist():
        if name.endswith(('.glb', '.gltf', '.png', '.json', '.txt')) or name == 'model.bin':
            target = SOURCE / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read(name))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(SOURCE / 'Protoceratops_Andrewsi.glb'))
for obj in list(bpy.context.scene.objects):
    # Only the importer-created custom bone shape is disposable. The unskinned
    # eyes and corneas are authored meshes, not rig helpers.
    if obj.type == 'MESH' and obj.name == 'Icosphere' and not obj.data.materials:
        bpy.data.objects.remove(obj, do_unlink=True)
armature = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
head_bone = 'protoceratops_head.M_32'
assert head_bone in armature.data.bones
bpy.context.scene.frame_set(12)
# The unskinned eyes were authored in the first animation pose, rather than the
# skeleton's rest pose. Undo that pose before adding the rigid head weights.
head_to_bind = armature.data.bones[head_bone].matrix_local @ armature.pose.bones[head_bone].matrix.inverted()
eye_parts = []
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH':
        continue
    materials = {m.name for m in obj.data.materials if m}
    if not materials.intersection({'eyes', 'shinny lens'}):
        continue
    # Preserve the original visible position at the first WalkCycle frame and UVs.
    world = obj.matrix_world.copy()
    original_points = [world @ obj.data.vertices[i].co for i in (0, len(obj.data.vertices) - 1)]
    obj.data.transform(head_to_bind @ armature.matrix_world.inverted() @ world)
    obj.parent = armature
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)
    group = obj.vertex_groups.new(name=head_bone)
    group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    modifier = obj.modifiers.new(name='Head attachment', type='ARMATURE')
    modifier.object = armature
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    for i, expected in zip((0, len(obj.data.vertices) - 1), original_points):
        assert (evaluated.matrix_world @ evaluated.data.vertices[i].co - expected).length < 0.0001, 'Eye reference pose shifted'
    eye_parts.append(obj.name)
assert len(eye_parts) == 2, 'Both the original eye and cornea meshes are required'
bpy.context.scene.frame_start = 12
bpy.context.scene.frame_end = 60
bpy.context.scene.render.fps = 24

# Unity's specular workflow stores smoothness in alpha; never use the damaged ORM.
rough = bpy.data.images.load(str(SOURCE / 'textures/protoceratops_rghnss.png'))
spec = bpy.data.images.load(str(SOURCE / 'textures/protoceratops_spclr.png'))
rough.colorspace_settings.name = spec.colorspace_settings.name = 'Non-Color'
if tuple(rough.size) != tuple(spec.size):
    rough.scale(*spec.size)
pixels = np.empty(len(spec.pixels), dtype=np.float32)
spec.pixels.foreach_get(pixels)
rough_pixels = np.empty(len(rough.pixels), dtype=np.float32)
rough.pixels.foreach_get(rough_pixels)
pixels[3::4] = 1.0 - rough_pixels[0::4]
packed = bpy.data.images.new('SpecularSmoothness', width=spec.size[0], height=spec.size[1], alpha=True)
packed.colorspace_settings.name = 'Non-Color'
packed.pixels.foreach_set(pixels)
packed.filepath_raw = str(PREPARED / 'SpecularSmoothness.png')
packed.file_format = 'PNG'
packed.save()
report = {'actions': [{'name': a.name, 'frames': list(a.frame_range)} for a in bpy.data.actions],
          'objects': [{'name': o.name, 'type': o.type, 'dimensions': list(o.dimensions)} for o in bpy.context.scene.objects],
          'head_attached_eye_meshes': eye_parts,
          'bones': [b.name for o in bpy.context.scene.objects if o.type == 'ARMATURE' for b in o.data.bones]}
(ROOT / 'Tools/Protoceratops/conversion.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
bpy.ops.export_scene.fbx(filepath=str(PREPARED / 'Protoceratops.fbx'),
    object_types={'ARMATURE', 'MESH', 'EMPTY'}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0.0, axis_forward='-Z', axis_up='Y', path_mode='STRIP')
print('PROTOCERATOPS_CONVERTED', json.dumps(report))
