from pathlib import Path
import json, math
from PIL import Image, ImageDraw, ImageFont
root=Path('Tools/VegetationDiversity/RainforestSample')
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18)
small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',14)
canvas=Image.new('RGB',(1400,1140),'#f3f6f9');d=ImageDraw.Draw(canvas)
titles=['Rear trunk / forward exit','Advance before turning','Blind alley / safe wait','Side exit','Straight departure','Ending invalidates old exit','Temporary peer / resume']
for n in range(7):
    data=json.loads((root/f'exit-case-{n}.json').read_text(encoding='utf-8-sig'))
    col=n%3;row=n//3;x=20+col*460;y=45+row*360
    d.rounded_rectangle((x,y,x+440,y+340),12,fill='white',outline='#cbd5e1')
    d.text((x+12,y+10),titles[n],font=font,fill='#18334f')
    pts=data.get('path',[])+data.get('trace',[])+[data['root'],data['connection']]+data.get('trunks',[])
    xs=[p['x'] for p in pts];zs=[p['z'] for p in pts]
    for b in data.get('solids',[]):
        xs.extend([b['m_Center']['x']-b['m_Extent']['x'],b['m_Center']['x']+b['m_Extent']['x']]);zs.extend([b['m_Center']['z']-b['m_Extent']['z'],b['m_Center']['z']+b['m_Extent']['z']])
    xmin,xmax=min(xs)-2,max(xs)+2;zmin,zmax=min(zs)-2,max(zs)+2
    scale=min(402/max(1,xmax-xmin),260/max(1,zmax-zmin));cx=x+220;cy=y+180
    def xy(p):return (cx+(p['x']-(xmin+xmax)/2)*scale,cy-(p['z']-(zmin+zmax)/2)*scale)
    def dot(p,r,fill):
        a,b=xy(p);d.ellipse((a-r,b-r,a+r,b+r),fill=fill)
    for b in data.get('solids',[]):
        c=b['m_Center'];e=b['m_Extent'];a=xy({'x':c['x']-e['x'],'z':c['z']+e['z']});v=xy({'x':c['x']+e['x'],'z':c['z']-e['z']});d.rectangle((*a,*v),fill='#84917e')
    for p in data.get('trunks',[]):dot(p,max(3,scale*.25),'#7e5736')
    if len(data.get('path',[]))>1:d.line([xy(p) for p in data['path']],fill='#008ca8',width=2)
    dot(data['root'],5,'#1b4da5');dot(data['connection'],5,'#e7a000')
    d.text((x+12,y+314),f"Exit direction {data['angle']:.0f} deg | scale: {10/scale:.1f} m / 10 px",font=small,fill='#42536b')
d.text((20,12),'Verified exit snapshots: blue actor | cyan route | yellow connection | grey hard obstacles',font=font,fill='#18334f')
d.text((960,920),'Snapshots from 30 FPS fixtures.\nNot a rendering of the Main forest.\nThe final actor position is shown.',font=font,fill='#42536b')
canvas.save(root/'ExitPaths.png')
