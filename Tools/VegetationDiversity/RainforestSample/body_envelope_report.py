"""Compare the baked T-Rex envelopes without changing Unity assets."""
from pathlib import Path
import re, math, json, hashlib
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Tools/VegetationDiversity/RainforestSample'
before = (ROOT / 'Logs/body-envelope-before-side.asset').read_text()
after = (ROOT / 'Assets/Scenes/TRexFrontTestAssets/SideInteractionProfile.asset').read_text()
def motions(text):
    return {int(b.splitlines()[0]): b for b in re.split(r'(?m)^  - role: ', text)[1:]}
def bands(text):
    pattern = r'm_Center: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}\s+m_Extent: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}'
    return [tuple(map(float, m)) for m in re.findall(pattern, text)]
def frames(block):
    section = block.split('    footFrames:')[1].split('    blendDuration:')[0]
    result = []
    for f in re.split(r'(?m)^    - time: ', section)[1:]:
        root = re.search(r'rootOffset: \{x: ([^,]+), y: ([^}]+)\}\s+yaw: ([^\n]+)', f)
        result.append((float(f.splitlines()[0]), *map(float, root.groups())))
    return result

def pose_at(fs, t):
    i = min(int(t / fs[-1][0] * (len(fs)-1)), len(fs)-1)
    a, b = fs[i], fs[min(i+1,len(fs)-1)]
    u = 0 if a[0] == b[0] else (t-a[0])/(b[0]-a[0])
    return tuple(a[j]+(b[j]-a[j])*u for j in range(1,4))
def polygon(band, pose, margin=0):
    x,y,z,ex,ey,ez = band; rx,rz,angle = pose
    c,s = math.cos(math.radians(angle)),math.sin(math.radians(angle))
    return [(rx+c*px+s*pz, rz-s*px+c*pz) for px,pz in [(x-ex-margin,z-ez-margin),(x+ex+margin,z-ez-margin),(x+ex+margin,z+ez+margin),(x-ex-margin,z+ez+margin)]]
old, new = motions(before), motions(after)
fs = frames(new[16]); old_bands = bands(old[16].split('    bodyBands:')[1].split('    playerPoses:')[0])
old_polys = [polygon(b,pose_at(fs,fs[-1][0]*i/5),.3) for i in range(6) for b in old_bands]
pose_sections = re.split(r'(?m)^    - time: ',new[16].split('    obstaclePoses:')[1])[1:]
new_polys = [polygon(b,pose_at(fs,float(p.splitlines()[0]))) for p in pose_sections for b in bands(p)]
def extent(polys):
    xs=[x for p in polys for x,z in p];zs=[z for p in polys for x,z in p]
    return [min(xs),min(zs),max(xs),max(zs)]
old_ext,new_ext=extent(old_polys),extent(new_polys)
img=Image.new('RGB',(1280,770),'#f5f7fb');d=ImageDraw.Draw(img)
fontpath='C:/Windows/Fonts/arial.ttf'
font=lambda n: ImageFont.truetype(fontpath,n)
d.text((35,20),'T-Rex turn clearance: before / after',font=font(30),fill='#14253b')
d.text((35,62),'Top view, same scale. Boxes are hard-obstacle envelopes; dark line is sampled root travel.',font=font(18),fill='#49586d')
for panel,(polys,ext,title,color) in enumerate([(old_polys,old_ext,'Before: whole clip + tail + 0.30 m margin','#bf4564'),(new_polys,new_ext,'After: pose torso/head + 0.05 m margin','#007e76')]):
    ox=320+panel*640;oy=410;scale=18
    proj=lambda p:(ox+p[0]*scale,oy-p[1]*scale)
    for v in range(-16,17,2):
        d.line([proj((v,-16)),proj((v,16))],fill='#dde3ec')
        d.line([proj((-16,v)),proj((16,v))],fill='#dde3ec')
    overlay=Image.new('RGBA',img.size,(0,0,0,0)); od=ImageDraw.Draw(overlay)
    rgb=(191,69,100) if panel==0 else (0,126,118)
    # Union per interval without repeatedly darkening overlapping height layers.
    for p in polys: od.polygon([proj(q) for q in p],fill=rgb+(64,))
    img=Image.alpha_composite(img.convert('RGBA'),overlay).convert('RGB');d=ImageDraw.Draw(img)
    d.line([proj((f[1],f[2])) for f in fs],fill='#182b44',width=3)
    x,z=proj((0,0));d.ellipse((x-5,z-5,x+5,z+5),fill='#182b44')
    d.text((35+panel*640,110),title,font=font(20),fill=color)
    d.text((35+panel*640,145),f'Swept extent: {ext[2]-ext[0]:.2f} x {ext[3]-ext[1]:.2f} m',font=font(21),fill='#14253b')
    for tick in [-12,-8,-4,0,4,8,12]:
        x,z=proj((tick,-16));d.text((x-10,z+3),str(tick),font=font(12),fill='#49586d')
d.text((35,742),'Tail and limbs intentionally excluded. Torso, hips, neck and head still reject hard trunks.',font=font(18),fill='#49586d')
img.save(OUT/'BodyHeadEnvelopeComparison.png')

def preserved(block):
    a=block.index('    bodyBands:'); b=block.index('    playerPoses:',a)
    value=(block[:a]+block[b:]).split('    obstaclePoses:')[0]
    return re.sub(r'(?m)^\s+part: 0\n','',value)
calibration=all(preserved(old[k])==preserved(new[k]) for k in old)
result={'motionCount':len(new),'originalCalibrationUnchanged':calibration,'oldTurnSweptExtent':old_ext,'newTurnSweptExtent':new_ext}
(ROOT/'Logs/body-envelope-comparison.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
