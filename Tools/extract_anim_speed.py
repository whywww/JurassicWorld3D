"""
FBX binary parser: extract T-Rex walk animation and compute ground speed
via forward kinematics (FK) on the bone skeleton.
"""
import argparse
from pathlib import Path
import struct
import math
import zlib

# ─── FBX Binary Parser ───────────────────────────────────────────────

def read_properties(f, count):
    props = []
    for _ in range(count):
        tc = chr(struct.unpack('<B', f.read(1))[0])
        if tc == 'Y': props.append(('Y', struct.unpack('<h', f.read(2))[0]))
        elif tc == 'C': props.append(('C', struct.unpack('<B', f.read(1))[0]))
        elif tc == 'I': props.append(('I', struct.unpack('<i', f.read(4))[0]))
        elif tc == 'F': props.append(('F', struct.unpack('<f', f.read(4))[0]))
        elif tc == 'D': props.append(('D', struct.unpack('<d', f.read(8))[0]))
        elif tc == 'L': props.append(('L', struct.unpack('<q', f.read(8))[0]))
        elif tc in ('f','d','i','b','l'):
            arr_len = struct.unpack('<I', f.read(4))[0]
            enc = struct.unpack('<I', f.read(4))[0]
            raw_len = struct.unpack('<I', f.read(4))[0]
            raw = f.read(raw_len)
            if enc == 1: raw = zlib.decompress(raw)
            if tc == 'f': arr = struct.unpack('<%df' % arr_len, raw[:arr_len*4])
            elif tc == 'd': arr = struct.unpack('<%dd' % arr_len, raw[:arr_len*8])
            elif tc in ('i','l'): arr = struct.unpack('<%di' % arr_len, raw[:arr_len*4]) if tc=='i' else struct.unpack('<%dq' % arr_len, raw[:arr_len*8])
            elif tc == 'b': arr = list(raw[:arr_len])
            props.append((tc, arr))
        elif tc == 'S':
            slen = struct.unpack('<I', f.read(4))[0]
            props.append(('S', f.read(slen).decode('ascii', errors='replace').rstrip('\x00')))
        elif tc == 'R':
            rlen = struct.unpack('<I', f.read(4))[0]
            props.append(('R', f.read(rlen)))
        else:
            raise ValueError(f"Unknown prop type {tc} at {f.tell()}")
    return props

class Node:
    __slots__ = ('name','props','children')
    def __init__(self, name, props, children):
        self.name = name; self.props = props; self.children = children
    def find(self, name):
        for c in self.children:
            if c.name == name: return c
        return None
    def finds(self, name):
        return [c for c in self.children if c.name == name]
    def pv(self, i=0):
        return self.props[i][1] if i < len(self.props) else None

def read_node(f):
    end = struct.unpack('<I', f.read(4))[0]
    if end == 0: return None
    np = struct.unpack('<I', f.read(4))[0]
    _pl = struct.unpack('<I', f.read(4))[0]
    nl = struct.unpack('<B', f.read(1))[0]
    name = f.read(nl).decode('ascii', errors='replace') if nl else ''
    props = read_properties(f, np) if np else []
    kids = []
    while f.tell() < end:
        c = read_node(f)
        if c is None: break
        kids.append(c)
    f.seek(end)
    return Node(name, props, kids)

def parse_fbx(path):
    with open(path, 'rb') as f:
        f.read(23)
        ver = struct.unpack('<I', f.read(4))[0]
        kids = []
        while True:
            n = read_node(f)
            if n is None: break
            kids.append(n)
    return Node('Root', [], kids), ver

def eval_curve(times, values, t):
    n = len(times)
    if n == 0: return 0.0
    if n == 1: return values[0]
    if t <= times[0]: return values[0]
    if t >= times[-1]: return values[-1]
    lo, hi = 0, n-1
    while lo < hi-1:
        mid = (lo+hi)//2
        if times[mid] <= t: lo = mid
        else: hi = mid
    t0, t1 = times[lo], times[hi]
    if abs(t1-t0) < 1e-12: return values[lo]
    a = (t - t0) / (t1 - t0)
    return values[lo] + a * (values[hi] - values[lo])

# ─── 3x3 rotation helpers (degrees) ─────────────────────────────────

def rot_x(a):
    r = math.radians(a); c = math.cos(r); s = math.sin(r)
    return [[1,0,0],[0,c,-s],[0,s,c]]

def rot_y(a):
    r = math.radians(a); c = math.cos(r); s = math.sin(r)
    return [[c,0,s],[0,1,0],[-s,0,c]]

def rot_z(a):
    r = math.radians(a); c = math.cos(r); s = math.sin(r)
    return [[c,-s,0],[s,c,0],[0,0,1]]

def mat_mul(a, b):
    r = [[0]*3 for _ in range(3)]
    for i in range(3):
        for j in range(3):
            r[i][j] = sum(a[i][k]*b[k][j] for k in range(3))
    return r

def mat_vec(m, v):
    return [sum(m[i][j]*v[j] for j in range(3)) for i in range(3)]

def euler_to_mat(rx, ry, rz, order='XYZ'):
    m = [[1,0,0],[0,1,0],[0,0,1]]
    for axis, angle in zip(order, [rx, ry, rz]):
        if axis == 'X': r = rot_x(angle)
        elif axis == 'Y': r = rot_y(angle)
        elif axis == 'Z': r = rot_z(angle)
        else: continue
        m = mat_mul(m, r)
    return m

# FBX unit conversion: FBX uses cm, Unity uses m
CM_TO_M = 0.01

def main():
    import sys
    sys.stdout.reconfigure(encoding='utf-8')
    
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fbx", type=Path,
                        default=Path(__file__).resolve().parent.parent /
                        "Assets/Dinosaurs/TRex/Source/source/T-Rex.fbx",
                        help="FBX input (default: project T-Rex asset)")
    fbx_path = parser.parse_args().fbx
    print("Parsing FBX...")
    root, ver = parse_fbx(fbx_path)
    print(f"FBX Version: {ver}")
    
    objs = root.find('Objects')
    conns_node = root.find('Connections')
    
    # Build connection maps
    oo_parent = {}  # parent -> [child]
    op_parent = {}  # parent -> [(child, prop)]
    op_child = {}   # child -> [(parent, prop)]
    
    if conns_node:
        for c in conns_node.finds('C'):
            ct = c.pv(0); cid = c.pv(1); pid = c.pv(2)
            prop = c.pv(3) if len(c.props) > 3 else ''
            if ct == 'OO':
                oo_parent.setdefault(pid, []).append(cid)
            elif ct == 'OP':
                op_parent.setdefault(pid, []).append((cid, prop))
                op_child.setdefault(cid, []).append((pid, prop))
    
    # Index objects
    models = {}       # uid -> {name, tx, ty, tz, rx, ry, rz, pre_x, pre_y, pre_z, post_x, post_y, post_z, ro, props}
    anim_stacks = {}
    anim_layers = {}
    anim_cns = {}     # curveNode uid -> {name, target_model_id, target_prop}
    anim_curves = {}  # curve uid -> (times_s, values)
    
    if objs:
        for child in objs.children:
            uid = child.pv(0)
            if uid is None: continue
            name = child.pv(1) if len(child.props) > 1 else ''
            name = name.split('\x00')[0].strip()  # clean null bytes
            
            if child.name == 'Model':
                # Extract Properties70 for transform values
                name = name.split('\x00')[0].strip()  # clean null bytes
                props70 = child.find('Properties70')
                tx = ty = tz = rx = ry = rz = 0.0
                pre_x = pre_y = pre_z = 0.0
                post_x = post_y = post_z = 0.0
                rot_order = 'XYZ'
                
                if props70:
                    for p in props70.finds('P'):
                        pname = p.pv(0) if len(p.props) > 0 else ''
                        ptype = p.pv(1) if len(p.props) > 1 else ''
                        pval = p.pv(4) if len(p.props) > 4 else (p.pv(3) if len(p.props) > 3 else None)
                        if pval is None: continue
                        
                        if pname == 'Lcl Translation' and ptype == 'Lcl Translation':
                            tx = p.pv(4) if len(p.props) > 4 else 0
                            ty = p.pv(5) if len(p.props) > 5 else 0
                            tz = p.pv(6) if len(p.props) > 6 else 0
                        elif pname == 'Lcl Rotation' and ptype == 'Lcl Rotation':
                            rx = p.pv(4) if len(p.props) > 4 else 0
                            ry = p.pv(5) if len(p.props) > 5 else 0
                            rz = p.pv(6) if len(p.props) > 6 else 0
                        elif pname == 'PreRotation':
                            pre_x = p.pv(4) if len(p.props) > 4 else 0
                            pre_y = p.pv(5) if len(p.props) > 5 else 0
                            pre_z = p.pv(6) if len(p.props) > 6 else 0
                        elif pname == 'PostRotation':
                            post_x = p.pv(4) if len(p.props) > 4 else 0
                            post_y = p.pv(5) if len(p.props) > 5 else 0
                            post_z = p.pv(6) if len(p.props) > 6 else 0
                        elif pname == 'RotationOrder':
                            if isinstance(pval, str):
                                rot_order = pval
                
                models[uid] = {
                    'name': name, 'tx': tx, 'ty': ty, 'tz': tz,
                    'rx': rx, 'ry': ry, 'rz': rz,
                    'pre_x': pre_x, 'pre_y': pre_y, 'pre_z': pre_z,
                    'post_x': post_x, 'post_y': post_y, 'post_z': post_z,
                    'rot_order': rot_order
                }
            
            elif child.name == 'AnimationStack':
                anim_stacks[uid] = name
            elif child.name == 'AnimationLayer':
                anim_layers[uid] = name
            elif child.name == 'AnimationCurveNode':
                # Find target model via OP connection
                target_id = None
                target_prop = ''
                if uid in op_child:
                    for pid, prop in op_child[uid]:
                        if pid in models:
                            target_id = pid
                            target_prop = prop
                anim_cns[uid] = {'name': name, 'target': target_id, 'prop': target_prop}
            elif child.name == 'AnimationCurve':
                tn = child.find('KeyTime')
                vn = child.find('KeyValueFloat')
                if tn and vn:
                    rt = tn.pv(0); rv = vn.pv(0)
                    if rt and rv:
                        anim_curves[uid] = ([t/46186158000.0 for t in rt], list(rv))
    
    print(f"Models: {len(models)}, Stacks: {len(anim_stacks)}, Curves: {len(anim_curves)}")
    
    # Build bone hierarchy from OO connections (Model -> Model)
    bone_children = {}  # parent_model_id -> [child_model_ids]
    bone_parent = {}    # child_model_id -> parent_model_id
    for pid, cids in oo_parent.items():
        if pid not in models: continue
        for cid in cids:
            if cid in models:
                bone_children.setdefault(pid, []).append(cid)
                bone_parent[cid] = pid
    
    # Find root bone (Hips or the one with no parent that's in the skeleton)
    # The skeleton root is typically "Hips" or the first bone with no model parent
    skeleton_bones = set()
    for uid in models:
        name = models[uid]['name']
        if name.startswith('jt_') or name in ('Hips',):
            skeleton_bones.add(uid)
    
    # Find the skeleton root (bone with no parent among skeleton bones)
    skel_root = None
    for uid in skeleton_bones:
        if uid not in bone_parent or bone_parent[uid] not in skeleton_bones:
            skel_root = uid
            break
    
    print(f"Skeleton root: {models.get(skel_root, {}).get('name', '?')}")
    
    # Print hierarchy from skeleton root (first few levels)
    def print_hier(uid, depth=0, max_depth=4):
        if depth > max_depth: return
        name = models.get(uid, {}).get('name', f'?{uid}')
        print(f"  {'  '*depth}{name}")
        for cid in bone_children.get(uid, []):
            if cid in skeleton_bones:
                print_hier(cid, depth+1, max_depth)
    
    print("\nBone hierarchy (first 7 levels):")
    print_hier(skel_root, max_depth=7)
    
    # ─── Find walk animation and collect per-bone animation data ────
    walk_stack_id = None
    for uid, name in anim_stacks.items():
        if 'walk slow' in name.lower():
            walk_stack_id = uid; break
    
    if not walk_stack_id:
        print("ERROR: No walk slow loop stack!"); return
    
    # Get layers and curve nodes
    stack_layers = oo_parent.get(walk_stack_id, [])
    layer_cns = []
    for lid in stack_layers:
        layer_cns.extend(oo_parent.get(lid, []))
    
    # Build per-bone animation data: bone_uid -> {axis: (times, values)}
    # axis is like "Lcl Translation|d|X"
    bone_anim = {}
    for cn_id in layer_cns:
        if cn_id not in anim_cns: continue
        cn = anim_cns[cn_id]
        target_id = cn['target']
        if target_id is None: continue
        
        # Get child curves
        for cv_id, cv_prop in op_parent.get(cn_id, []):
            if cv_id in anim_curves and anim_curves[cv_id]:
                times, values = anim_curves[cv_id]
                key = f"{cn['prop']}|{cv_prop}"
                bone_anim.setdefault(target_id, {})[key] = (times, values)
    
    # Get clip duration
    clip_dur = 0
    for uid, props in bone_anim.items():
        for key, (times, vals) in props.items():
            if len(times) > 1:
                d = times[-1] - times[0]
                if d > clip_dur: clip_dur = d
    
    print(f"\nClip duration: {clip_dur:.4f}s")
    print(f"Bones with animation: {len(bone_anim)}")
    
    # ─── Forward Kinematics ─────────────────────────────────────────
    # For each frame, compute world position of foot bones
    # FK chain: for each bone, local_transform = T * Rpre * R * Rpost * S
    # world_transform = parent_world * local_transform
    # Position = world_transform * (0,0,0) = world translation
    
    # Find hip bone (body reference)
    hip_id = None
    for uid in skeleton_bones:
        name = models[uid]['name']
        if 'Hips_C' in name or name == 'Hips':
            hip_id = uid; break
    
    # Foot bones to track
    foot_names = ['jt_Foot_L', 'jt_Foot_R', 'jt_ToeMiddle_L', 'jt_ToeMiddle_R']
    foot_ids = []
    for uid in skeleton_bones:
        if models[uid]['name'] in foot_names:
            foot_ids.append(uid)
    
    print(f"Hip: {models.get(hip_id, {}).get('name', '?')}")
    print(f"Foot bones: {[models.get(fid, {}).get('name', '?') for fid in foot_ids]}")
    
    # Debug: print Properties70 for a sample bone
    sample_bone = hip_id
    if sample_bone:
        print(f"\n=== Properties70 for {models[sample_bone]['name']} ===")
        # Re-parse to get raw Properties70 data
        if objs:
            for child in objs.children:
                uid = child.pv(0)
                if uid == sample_bone:
                    props70 = child.find('Properties70')
                    if props70:
                        for p in props70.children:
                            if p.name == 'P':
                                pname = p.pv(0) if len(p.props) > 0 else ''
                                ptype = p.pv(1) if len(p.props) > 1 else ''
                                flags = p.pv(2) if len(p.props) > 2 else ''
                                vals = [p.pv(i) for i in range(3, len(p.props))]
                                print(f"  {pname:30s} type={ptype:20s} flags={flags} vals={vals}")
                    break
    
    # Also check jt_Thigh_R
    for uid in skeleton_bones:
        if models[uid]['name'] == 'jt_Thigh_R':
            print(f"\n=== Properties70 for jt_Thigh_R ===")
            if objs:
                for child in objs.children:
                    cuid = child.pv(0)
                    if cuid == uid:
                        props70 = child.find('Properties70')
                        if props70:
                            for p in props70.children:
                                if p.name == 'P':
                                    pname = p.pv(0) if len(p.props) > 0 else ''
                                    ptype = p.pv(1) if len(p.props) > 1 else ''
                                    flags = p.pv(2) if len(p.props) > 2 else ''
                                    vals = [p.pv(i) for i in range(3, len(p.props))]
                                    print(f"  {pname:30s} type={ptype:20s} flags={flags} vals={vals}")
                        break
            break
    
    # Build ordered bone list (parent before children) for FK
    ordered_bones = []
    def collect_bones(uid):
        ordered_bones.append(uid)
        for cid in bone_children.get(uid, []):
            if cid in skeleton_bones:
                collect_bones(cid)
    if skel_root:
        collect_bones(skel_root)
    
    # For each bone, precompute bind pose transform
    # local_bind = T_bind * Rpre * R_bind * Rpost * S_bind (S=1 for rotation purposes)
    bone_bind = {}
    for uid in ordered_bones:
        m = models[uid]
        T = [m['tx']*CM_TO_M, m['ty']*CM_TO_M, m['tz']*CM_TO_M]
        Rpre = euler_to_mat(m['pre_x'], m['pre_y'], m['pre_z'])
        Rbind = euler_to_mat(m['rx'], m['ry'], m['rz'])
        Rpost = euler_to_mat(m['post_x'], m['post_y'], m['post_z'])
        bone_bind[uid] = {'T': T, 'Rpre': Rpre, 'Rbind': Rbind, 'Rpost': Rpost}
    
    # Sample at N frames
    N = 64
    foot_positions = {fid: [] for fid in foot_ids}
    hip_positions = []
    
    for fi in range(N + 1):
        t = clip_dur * fi / N if clip_dur > 0 else 0
        
        # For each bone, compute animated local transform
        # animated: get dR from animation curves (delta rotation)
        # local = T_bind * Rpre * (R_bind + dR) * Rpost
        # Actually: animated rotation = Rbind + dR (euler add)
        
        bone_world_pos = {}
        bone_world_rot_full = {}
        bone_world_rot = {}  # not needed for position, but let's keep it simple
        
        for uid in ordered_bones:
            m = models[uid]
            parent_uid = bone_parent.get(uid)
            
            # Bind translation (from Properties70)
            bind_tx = m['tx'] * CM_TO_M
            bind_ty = m['ty'] * CM_TO_M
            bind_tz = m['tz'] * CM_TO_M
            
            # Animated translation: FBX curves store ABSOLUTE values
            has_anim_t = False
            if uid in bone_anim:
                for key in bone_anim[uid]:
                    if 'Translation' in key:
                        has_anim_t = True
                        break
            
            if has_anim_t:
                anim = bone_anim[uid]
                atx = aty = atz = 0.0
                for key, (times, vals) in anim.items():
                    if 'Translation' in key:
                        val = eval_curve(times, vals, t) * CM_TO_M
                        if 'd|X' in key: atx = val
                        elif 'd|Y' in key: aty = val
                        elif 'd|Z' in key: atz = val
                total_tx = atx
                total_ty = aty
                total_tz = atz
            else:
                total_tx = bind_tx
                total_ty = bind_ty
                total_tz = bind_tz
            
            # Bind rotation (from Properties70 - static default)
            rx = m['rx']; ry = m['ry']; rz = m['rz']
            
            # Animated rotation: FBX curves store ABSOLUTE values (not deltas)
            # Check if animation has rotation curves for this bone
            has_anim_rot = False
            if uid in bone_anim:
                anim = bone_anim[uid]
                for key in anim:
                    if 'Rotation' in key and 'Translation' not in key:
                        has_anim_rot = True
                        break
            
            if has_anim_rot:
                # Animation curves provide absolute rotation values
                arx = ary = arz = 0.0
                anim = bone_anim[uid]
                for key, (times, vals) in anim.items():
                    if 'Rotation' in key and 'Translation' not in key:
                        val = eval_curve(times, vals, t)
                        if 'd|X' in key: arx = val
                        elif 'd|Y' in key: ary = val
                        elif 'd|Z' in key: arz = val
                total_rx = arx
                total_ry = ary
                total_rz = arz
            else:
                # No animation: use bind pose rotation
                total_rx = rx
                total_ry = ry
                total_rz = rz
            
            # Compute local rotation matrix
            Rpre = bone_bind[uid]['Rpre']
            Ranim = euler_to_mat(total_rx, total_ry, total_rz)
            Rpost = bone_bind[uid]['Rpost']
            Rlocal = mat_mul(Rpre, mat_mul(Ranim, Rpost))
            
            # World position
            if parent_uid is not None and parent_uid in bone_world_pos:
                pw = bone_world_pos[parent_uid]
                # Get parent world rotation for transforming local offset
                if parent_uid in bone_world_rot_full:
                    pR = bone_world_rot_full[parent_uid]
                    local_off = mat_vec(pR, [total_tx, total_ty, total_tz])
                    wx = pw[0] + local_off[0]
                    wy = pw[1] + local_off[1]
                    wz = pw[2] + local_off[2]
                else:
                    wx = pw[0] + total_tx
                    wy = pw[1] + total_ty
                    wz = pw[2] + total_tz
                # World rotation
                bone_world_rot_full[uid] = mat_mul(pR, Rlocal) if parent_uid in bone_world_rot_full else Rlocal
            else:
                wx = total_tx; wy = total_ty; wz = total_tz
                bone_world_rot_full[uid] = Rlocal
            
            bone_world_pos[uid] = [wx, wy, wz]
            
            if uid == hip_id:
                hip_positions.append((wx, wy, wz))
            if uid in foot_ids:
                foot_positions[uid].append((wx, wy, wz))
    
    # ─── Compute step length (PCA method, same as baker) ───────────
    print("\n=== Computing step length via FK ===")
    
    # Convert to relative positions (foot - hip) in XZ
    tracks = []
    for fid in foot_ids:
        fname = models[fid]['name']
        track = []
        for i in range(N + 1):
            rel_x = foot_positions[fid][i][0] - hip_positions[i][0]
            rel_z = foot_positions[fid][i][2] - hip_positions[i][2]
            track.append((rel_x, rel_z))
        tracks.append(track)
        
        # Print some diagnostics
        xs = [p[0] for p in track]
        zs = [p[1] for p in track]
        print(f"  {fname}: X range=[{min(xs):.4f}, {max(xs):.4f}] Z range=[{min(zs):.4f}, {max(zs):.4f}]")
    
    # PCA step length
    ranges = []
    for fi, samples in enumerate(tracks):
        if len(samples) < 4: continue
        mx = sum(s[0] for s in samples)/len(samples)
        mz = sum(s[1] for s in samples)/len(samples)
        cxx = sum((s[0]-mx)**2 for s in samples)
        czz = sum((s[1]-mz)**2 for s in samples)
        cxz = sum((s[0]-mx)*(s[1]-mz) for s in samples)
        theta = 0.5 * math.atan2(2*cxz, cxx - czz)
        axis = (math.cos(theta), math.sin(theta))
        projs = [(s[0]-mx)*axis[0] + (s[1]-mz)*axis[1] for s in samples]
        rng = max(projs) - min(projs)
        if rng > 0:
            ranges.append(rng)
            print(f"  {models[foot_ids[fi]]['name']}: step range={rng:.4f}m (axis {math.degrees(theta):.1f}deg)")
    
    if ranges:
        ranges.sort()
        step = ranges[len(ranges)//2]
        speed = 2.0 * step / clip_dur if clip_dur > 0 else 0
        print(f"\n  Step length (median): {step:.4f} m")
        print(f"  Clip duration: {clip_dur:.4f} s")
        print(f"  Native ground speed: {speed:.4f} m/s")
        print(f"\n  *** moveAnimationGroundSpeed = {speed:.2f} ***")
    else:
        print("  No valid step length!")
    
    # Also check: hip displacement over the cycle
    if hip_positions:
        dx = hip_positions[-1][0] - hip_positions[0][0]
        dz = hip_positions[-1][2] - hip_positions[0][2]
        print(f"\n  Hip displacement: dx={dx:.6f} dz={dz:.6f} total={math.sqrt(dx*dx+dz*dz):.6f} m")


if __name__ == '__main__':
    main()
