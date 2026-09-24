from pathlib import Path
import hashlib, json
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageCms
from fixed_number_overlay import render_adidas_front, render_fixed_number

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'docs/jerseys/refs'
TMP=ROOT/'docs/jerseys/kit-masks'
TMP.mkdir(exist_ok=True)
ICC=Image.open(OUT/'home_body_color.png').info['icc_profile']

def polygon(points,size=1024):
    im=Image.new('L',(size,size)); ImageDraw.Draw(im).polygon(points,fill=255)
    return im

def finish(name,source,mask,target):
    a=np.asarray(source.convert('RGB')).copy(); m=np.asarray(mask)>0
    result=a.copy(); result[m]=np.clip(target[m],0,255).astype('uint8')
    path=OUT/(name+'_color.png')
    Image.fromarray(result).save(path,icc_profile=ICC)
    check=np.asarray(Image.open(path))
    assert check.shape==a.shape and np.array_equal(check[~m],a[~m])
    mask.save(TMP/(name+'_editable_mask.png'))
    Image.fromarray(result).resize((1024,1024)).save(TMP/(name+'_preview.png'))
    return {'file':path.name,'size':source.size,'mode':'RGB','icc':'sRGB','changed_pixels':int(np.any(check!=a,axis=2).sum()),'unchanged_outside_mask':bool(np.array_equal(check[~m],a[~m])),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}

body=Image.open(OUT/'uvref_body_2048.png').convert('RGB')
# Coordinates are authored against a half-size inspection view, then scaled
# with nearest-neighbour. No source pixels are resampled or moved.
bm=polygon([(0,0),(815,0),(815,190),(840,204),(840,458),(791,503),(820,567),(821,668),(810,741),(747,729),(672,715),(400,722),(351,736),(216,722),(92,734),(0,734)])
d=ImageDraw.Draw(bm)
for pts in [ [(0,57),(15,42),(41,34),(62,54),(81,111),(75,152),(40,176),(0,192)], [(397,0),(467,0),(483,144),(473,181),(443,201),(410,185),(390,153),(394,70)], [(0,443),(53,440),(77,459),(72,507),(55,532),(0,539)] ]:
    d.polygon(pts,fill=0)
bm=bm.resize(body.size,Image.Resampling.NEAREST)
a=np.asarray(body).astype(float)
lum=np.asarray(body.convert('L')).astype(float)
# Compress baked-in plaid contrast without inventing or moving surface detail.
smooth=np.asarray(body.convert('L').filter(ImageFilter.GaussianBlur(12))).astype(float)
shade=np.clip(.94+(smooth-95)/650+(lum-smooth)/1100,.72,1.13)
target=np.array([198,25,37])[None,None,:]*shade[:,:,None]
# Black side panels and asymmetric, unequal-length sleeve bars.
design=Image.new('L',(1024,1024)); p=ImageDraw.Draw(design)
cfg=json.loads((ROOT/'docs/jerseys/kit-painter-geometry.json').read_text())
for pts in cfg['side_panels']: p.polygon(pts,fill=255)
for x,y,length,width in cfg['bars']:
    p.polygon([(x,y),(x+length,y-17),(x+length+3,y-17+width),(x+3,y+width)],fill=255)
black=np.asarray(design.resize(body.size,Image.Resampling.NEAREST))>0
target[black]=np.array([20,23,27])*shade[black,None]
number_mask, number_layer = render_fixed_number(
    body.size, '8', (255,245,225), (20,23,27), label='Home',
    label_colour=(255,245,225), label_stroke=(20,23,27),
)
logo_mask, logo_layer = render_adidas_front(body.size, (255,245,225), (20,23,27))
body_with_markings = Image.fromarray(np.maximum.reduce((np.asarray(bm), np.asarray(number_mask), np.asarray(logo_mask))))
target_with_markings = Image.fromarray(np.clip(target, 0, 255).astype('uint8'), 'RGB').convert('RGBA')
target_with_markings = Image.alpha_composite(target_with_markings, number_layer)
target_with_markings = Image.alpha_composite(target_with_markings, logo_layer)
report=[finish('home_body',body,body_with_markings,np.asarray(target_with_markings.convert('RGB')))]

legs=Image.open(OUT/'uvref_legs_1024.png').convert('RGB')
lm=Image.new('L',legs.size)
panels=[[(23,309),(218,315),(217,447),(232,489),(205,573),(188,637),(198,738),(198,929),(182,980),(191,989),(155,1001),(113,984),(78,990),(26,1023),(10,973),(10,900),(15,829),(21,722),(14,632),(28,537),(21,423)],[(237,316),(440,317),(447,451),(435,550),(428,611),(439,688),(445,809),(449,934),(447,985),(431,1006),(345,985),(310,994),(288,1010),(276,991),(264,951),(272,910),(269,790),(270,667),(263,582),(256,533),(240,489)],[(466,333),(530,345),(579,346),(636,334),(648,399),(665,441),(690,461),(685,543),(670,606),(668,703),(659,785),(663,823),(648,866),(654,924),(657,984),(642,999),(574,982),(540,996),(500,1020),(487,1000),(484,944),(495,894),(493,819),(491,720),(482,654),(465,612),(470,535)],[(754,331),(792,337),(839,347),(932,331),(940,371),(924,442),(919,526),(912,599),(911,702),(917,807),(911,911),(915,969),(932,1005),(906,1021),(864,1005),(818,982),(787,993),(738,1014),(730,1004),(712,947),(721,889),(719,800),(720,706),(718,617),(710,550),(700,466),(723,440),(740,401)]]
ld=ImageDraw.Draw(lm)
for pts in panels: ld.polygon(pts,fill=255)
# The validated gearless mesh still uses the upper waistband faces of the
# lower-body atlas. Replace the stock belt/cuff strip there with the team's
# shorts colour so no tactical waist gear remains visible.
for pts in [
    [(0,270),(220,270),(224,360),(0,360)],
    [(230,270),(450,270),(454,360),(230,360)],
    [(460,270),(695,270),(700,360),(460,360)],
    [(700,270),(945,270),(950,360),(700,360)],
]:
    ld.polygon(pts, fill=255)
la=np.asarray(legs).astype(float); ll=np.asarray(legs.convert('L')).astype(float)
ls=np.clip(.98+(ll-120)/340,.62,1.28)
lt=np.zeros_like(la)
lt[:]=np.array([25,27,31])*ls[:,:,None]
# Red lower-leg fabric; this is a pant recolour, not new shorts geometry.
lt[700:]=np.array([190,24,35])*ls[700:,:,None]
lt[717:731]=np.array([25,27,31])*ls[717:731,:,None]
lt[746:754]=np.array([25,27,31])*ls[746:754,:,None]
boot=Image.new('L',legs.size); bd=ImageDraw.Draw(boot)
for pts in [[(604,31),(630,42),(651,66),(645,113),(620,155),(638,180),(668,198),(680,228),(692,243),(674,265),(641,282),(594,305),(565,295),(547,269),(545,226),(558,178),(573,128),(584,72)],[(740,23),(771,18),(821,28),(862,36),(878,63),(876,91),(859,110),(839,119),(838,151),(863,177),(884,195),(895,223),(891,251),(879,263),(857,258),(830,239),(807,217),(786,184),(777,154),(758,133),(737,97),(726,57)]]:
    bd.polygon(pts,fill=255)
b=np.asarray(boot)>0
# Preserve dark seams/laces and sole pixels; colour leather/cloth uppers only.
b &= (ll>35)
weight=np.clip((ll[b]-35)/45,0,1)[:,None]
pink=np.array([235,66,142])*np.clip(.90+(ll[b]-90)/210,.58,1.22)[:,None]
lt[b]=pink*weight+la[b]*(1-weight)
combined=np.asarray(lm).copy(); combined[b]=255
report.append(finish('home_legs',legs,Image.fromarray(combined),lt))
print(json.dumps(report,indent=2))
