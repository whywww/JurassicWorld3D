"""Run with Blender --background --python Tools/Pteradactal/convert_model.py."""
import bpy
import json
import pathlib
import zipfile
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets/Dinosaurs/Pteradactal/Source'
SOURCE.mkdir(parents=True, exist_ok=True)
DEST = ROOT / 'Assets/Dinosaurs/Pteradactal/Prepared'
DEST.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(ROOT / 'SourceArchives/Dinosaurs/Pteradactal/animated-flying-pteradactal-dinosaur-loop.zip') as archive:
    (SOURCE / 'Pteradactal.glb').write_bytes(archive.read('source/Pteradactal.glb'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(SOURCE / 'Pteradactal.glb'))
# The glTF importer creates an Icosphere as a custom bone shape. It is not model geometry.
for obj in list(bpy.context.scene.objects):
    if obj.type == 'MESH' and not any(m.type == 'ARMATURE' for m in obj.modifiers):
        bpy.data.objects.remove(obj, do_unlink=True)
bpy.context.scene.frame_start = 0
bpy.context.scene.frame_end = 257
for index, image in enumerate(bpy.data.images):
    if image.type != 'IMAGE':
        continue
    image.filepath_raw = str(DEST / ('Texture%d.png' % index))
    image.file_format = 'PNG'
    image.save()
roughness = next(n.image for m in bpy.data.materials if m.use_nodes
                 for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.label == 'METALLIC ROUGHNESS')
pixels = np.empty(len(roughness.pixels), dtype=np.float32)
roughness.pixels.foreach_get(pixels)
pixels = pixels.reshape(-1, 4)
packed_pixels = np.ones_like(pixels)
packed_pixels[:, 0] = pixels[:, 2]
packed_pixels[:, 3] = 1.0 - pixels[:, 1]
packed = bpy.data.images.new('MetallicSmoothness', width=roughness.size[0], height=roughness.size[1], alpha=True)
packed.colorspace_settings.name = 'Non-Color'
packed.pixels.foreach_set(packed_pixels.ravel())
packed.filepath_raw = str(DEST / 'MetallicSmoothness.png')
packed.file_format = 'PNG'
packed.save()
materials = []
for material in bpy.data.materials:
    textures = {}
    if material.use_nodes:
        for node in material.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image:
                textures[node.label or node.name] = pathlib.Path(node.image.filepath_raw).name
    materials.append({'name': material.name, 'textures': textures})
actions = []
for action in bpy.data.actions:
    actions.append({'name': action.name, 'frames': list(action.frame_range)})
print('PTERADACTAL_SOURCE', json.dumps({'actions': actions, 'materials': materials,
    'objects': [{'name': o.name, 'type': o.type, 'dimensions': list(o.dimensions)} for o in bpy.context.scene.objects]}))
bpy.ops.export_scene.fbx(filepath=str(DEST / 'Pteradactal.fbx'),
    use_selection=False, object_types={'ARMATURE', 'MESH', 'EMPTY'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.0,
    axis_forward='-Z', axis_up='Y', path_mode='STRIP')
(ROOT / 'Tools/Pteradactal/conversion.json').write_text(json.dumps({
    'actions': actions, 'materials': materials}, indent=2), encoding='utf-8')
