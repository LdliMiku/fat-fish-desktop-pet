"""Read native frame buffers and save numeric motion fields; does not edit or write artwork."""
import gzip
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / '.build' / 'motion-tools'))
import cv2
import numpy as np

W, H = 340, 360
PAIRS = [(0,3),(3,4),(0,4),(0,5),(5,6),(0,6),(0,7),(0,9),(9,10),
         (10,11),(11,12),(12,14),(14,15),(0,15),(9,12),(10,12),
         (9,14),(10,14),(11,14),(0,12)]
GAZE = [0,16,17,18,19,21,22,23,24]+list(range(34,42))
DIRECTIONS=[16,17,18,19,21,22,23,24]
def ring_frame(r,d):
    return 0 if r==0 else 34+d if r==1 else DIRECTIONS[d]
def active(x,y):
    ax,ay=abs(x),abs(y);r=max(ax,ay)
    if r==0:return [0]
    cardinal=(3 if x<0 else 4) if ax>=ay else (1 if y<0 else 6)
    diagonal=(0 if x<0 else 2) if y<0 else (5 if x<0 else 7)
    a=min(ax,ay)/r;q=r*2;inner=min(1,int(q));u=q-inner
    pairs=[(ring_frame(inner,cardinal),(1-u)*(1-a)),(ring_frame(inner,diagonal),(1-u)*a),(ring_frame(inner+1,cardinal),u*(1-a)),(ring_frame(inner+1,diagonal),u*a)]
    return list({i for i,w in pairs if w>1e-8})
for ix in range(-24,25):
    for iy in range(-24,25):
        ids=active(ix/24,iy/24)
        PAIRS += [(a,b) for a in ids for b in ids if a<b]
PAIRS += [(0,a) for a in GAZE if a]
PAIRS += [(a,2 if a==0 else a+8 if a>=34 else a+9) for a in GAZE]
PAIRS += [(a,b) for a in GAZE if a for b in [9,12,14] if a!=b]
PAIRS = sorted(set(tuple(sorted(pair)) for pair in PAIRS))
frames = []
for index in range(50):
    raw = np.fromfile(ROOT / '.build' / 'motion-inputs' / f'{index:02}.bgra', dtype=np.uint8).reshape(H,W,4)
    gray = cv2.cvtColor(raw, cv2.COLOR_BGRA2GRAY)
    frames.append(np.clip(gray.astype(np.float32) * .82 + raw[:,:,3] * .18, 0, 255).astype(np.uint8))

target = ROOT / 'assets' / 'animations' / 'v6' / 'motion-fields.gz'
stats = []
with gzip.open(target, 'wb', compresslevel=6) as output:
    output.write(struct.pack('<5i', 0x46504D31, W, H, 2, len(PAIRS)))
    for a,b in PAIRS:
        output.write(struct.pack('<2i',a,b))
        for first,second in [(a,b),(b,a)]:
            if first in GAZE and second in GAZE:
                dis=cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
                dis.setVariationalRefinementIterations(10)
                flow=dis.calc(frames[first],frames[second],None)
            else:
                flow = cv2.calcOpticalFlowFarneback(frames[first],frames[second],None,.5,4,25,5,7,1.5,0)
            flow = np.nan_to_num(flow)
            quantized = np.round(np.clip(flow[::2,::2,:],-96,96)*32).astype('<i2')
            output.write(quantized.tobytes())
        stats.append({'from':a,'to':b,'max_displacement':float(np.max(np.linalg.norm(flow,axis=2)))})
print(json.dumps({'file':str(target),'bytes':target.stat().st_size,'pairs':len(PAIRS),'opencv':cv2.__version__,'analysis':'numeric flow only; original generated PNG unchanged'},ensure_ascii=False))
