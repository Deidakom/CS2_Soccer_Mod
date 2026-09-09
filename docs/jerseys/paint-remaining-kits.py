"""Deterministic per-variant albedo painter. Python + Pillow + numpy.

Run with --stock-dir PATH. Reads stock only; writes refs and review masks.
Home is unchanged unless --include-home is explicitly supplied.
"""
import argparse, ast, hashlib, json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageCms
from fixed_number_overlay import render_adidas_front, render_fixed_number

HERE=Path(__file__).resolve().parent
OUT=HERE/'refs'
MASKS=HERE/'kit-masks'
ICC=Image.open(OUT/'home_body_color.png').info['icc_profile']

def mask(points,size=1024):
 im=Image.new('L',(size,size)); ImageDraw.Draw(im).polygon(points,fill=255); return im

def body_mask(v):
 # Explicit silhouettes inspected separately on b/c/d, half-resolution coords.
 contours={
 'b':[(0,0),(814,0),(814,185),(837,197),(834,353),(840,370),(838,455),(790,503),(809,546),(818,571),(817,642),(808,748),(737,726),(672,716),(405,722),(345,737),(220,723),(80,734),(0,734)],
 'c':[(0,0),(740,0),(740,436),(680,439),(680,450),(744,450),(750,510),(750,650),(753,656),(750,794),(677,795),(679,896),(616,905),(0,905)],
 'd':[(0,0),(814,0),(814,187),(837,199),(835,352),(840,372),(838,454),(790,505),(810,548),(818,571),(818,642),(808,748),(738,728),(672,717),(405,723),(345,736),(220,724),(80,734),(0,734)]}
 im=mask(contours[v]); d=ImageDraw.Draw(im)
 if v=='b':
  for box in [(7,50,70,154),(414,6,461,79),(402,84,477,189),(17,451,61,522)]: d.ellipse(box,fill=0)
 if v=='c':
  # Standalone second insignia island: explicitly editable, not equipment.
  d.ellipse((639,800,736,898),fill=255)
 if v=='d':
  d.polygon([(755,445),(807,446),(827,466),(820,507),(800,535),(780,546),(757,527),(744,499),(743,464)],fill=255)
 return im.resize((2048,2048),Image.Resampling.NEAREST)

def save(name,base,editable,target,source):
 original=np.asarray(base); m=np.asarray(editable)>0
 result=original.copy(); result[m]=np.clip(target[m],0,255).astype('uint8')
 path=OUT/f'{name}_color.png'; Image.fromarray(result).save(path,icc_profile=ICC)
 check=Image.open(path); check.load(); arr=np.asarray(check)
 assert check.mode=='RGB' and check.size==base.size and check.info.get('icc_profile')
 assert np.array_equal(arr[~m],original[~m])
 editable.save(MASKS/f'{name}.png')
 return dict(file=path.name,base=source.name,base_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),dimensions=list(check.size),mode=check.mode,changed_pixels=int(np.any(arr!=original,axis=2).sum()),outside_mask_changed_pixels=0,sha256=hashlib.sha256(path.read_bytes()).hexdigest())

def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--stock-dir',type=Path,required=True); ap.add_argument('--include-home',action='store_true'); args=ap.parse_args()
 MASKS.mkdir(exist_ok=True)
 # Geometry lives in a versioned JSON file, including individually editable
 # legs polygons for each variant. Motif geometry is shared intentionally.
 cfg=json.loads((HERE/'kit-painter-geometry.json').read_text())
 report=[]
 palettes={'away':('b',[25,83,207],[237,241,247],[237,241,247],[25,83,207],[246,207,35]),'gkhome':('c',[237,104,20],[20,23,27],[24,26,30],[224,91,18],[20,22,25]),'gkaway':('d',[238,241,245],[25,83,185],[232,236,243],[232,236,243],[20,22,25])}
 for kit,(v,primary,trim,upper,sock,bootcolor) in palettes.items():
  source=args.stock_dir/f'tm_leet_v2_body_variant{v}_color.png'; base=Image.open(source).convert('RGB')
  assert base.size==(2048,2048)
  bm=body_mask(v)
  lum=np.asarray(base.convert('L')).astype(float)
  smooth=np.asarray(base.convert('L').filter(ImageFilter.GaussianBlur(12))).astype(float)
  shade=np.clip(.96+(smooth-100)/800+(lum-smooth)/1200,.80,1.07)
  # Eliminate printed badges, including their albedo relief/colour, not just hue.
  badge=Image.new('L',(1024,1024)); bd=ImageDraw.Draw(badge)
  if v=='c':
   bd.ellipse((288,403,393,508),fill=255); bd.ellipse((637,797,738,900),fill=255)
  if v=='d':
   bd.rectangle((0,440,82,545),fill=255); bd.rectangle((743,441,825,543),fill=255)
  remove=np.asarray(badge.resize(base.size,Image.Resampling.NEAREST))>0
  # Replace insignia shading by surrounding fabric level; feather outside
  # the removal region so this doesn't leave a bright badge-shaped patch.
  broad=np.asarray(badge.resize(base.size,Image.Resampling.NEAREST).filter(ImageFilter.MaxFilter(25)))>0
  ring=broad & ~remove
  fill=float(np.median(shade[ring])) if ring.any() else .93
  shade[remove]=fill
  softened=np.asarray(Image.fromarray(np.clip(shade*200,0,255).astype('uint8')).filter(ImageFilter.GaussianBlur(8))).astype(float)/200
  shade[broad]=softened[broad]
  target=np.array(primary)*shade[:,:,None]
  panels=Image.new('L',(1024,1024)); pd=ImageDraw.Draw(panels)
  for pts in cfg['side_panels']: pd.polygon(pts,fill=255)
  pm=np.asarray(panels.resize(base.size,Image.Resampling.NEAREST))>0
  target[pm]=np.array(trim)*shade[pm,None]
  bars=Image.new('L',(1024,1024)); dd=ImageDraw.Draw(bars)
  for x,y,length,width in cfg['bars']: dd.polygon([(x,y),(x+length,y-17),(x+length+3,y-17+width),(x+3,y+width)],fill=255)
  am=np.asarray(bars.resize(base.size,Image.Resampling.NEAREST))>0
  target[am]=np.array([20,23,27] if kit.startswith('gk') else trim)*shade[am,None]
  editable_body=bm
  if kit=='away':
   number_mask, number_layer = render_fixed_number(
    base.size, '6', (20,23,27), (255,255,255), label='Away',
    label_colour=(20,23,27), label_stroke=(255,255,255),
   )
   logo_mask, logo_layer = render_adidas_front(base.size, (255,255,255), (15,20,35))
   editable_body=Image.fromarray(np.maximum.reduce((np.asarray(bm), np.asarray(number_mask), np.asarray(logo_mask))))
   target_layer=Image.fromarray(np.clip(target, 0, 255).astype('uint8'), 'RGB').convert('RGBA')
   target_layer=Image.alpha_composite(target_layer, number_layer)
   target_layer=Image.alpha_composite(target_layer, logo_layer)
   target=np.asarray(target_layer.convert('RGB'))
  report.append(save(kit+'_body',base,editable_body,target,source))
  source=args.stock_dir/f'tm_leet_v2_lower_body_variant{v}_color.png'; base=Image.open(source).convert('RGB'); assert base.size==(1024,1024)
  lm=Image.new('L',base.size); ld=ImageDraw.Draw(lm)
  for pts in cfg['legs'][v]: ld.polygon(pts,fill=255)
  if v=='b':
   for pts in [
    [(0,270),(220,270),(224,360),(0,360)],
    [(230,270),(450,270),(454,360),(230,360)],
    [(460,270),(695,270),(700,360),(460,360)],
    [(700,270),(945,270),(950,360),(700,360)],
   ]: ld.polygon(pts,fill=255)
  ll=np.asarray(base.convert('L')).astype(float); la=np.asarray(base).astype(float)
  ls=np.clip(.98+(ll-120)/340,.62,1.28)
  target=np.array(upper)*ls[:,:,None]
  target[700:]=np.array(sock)*ls[700:,:,None]
  band=trim if kit=='gkaway' else upper
  target[717:731]=np.array(band)*ls[717:731,:,None]
  target[746:754]=np.array(band)*ls[746:754,:,None]
  boot=Image.new('L',base.size); d=ImageDraw.Draw(boot)
  for pts in cfg['boots'][v]: d.polygon(pts,fill=255)
  b=np.asarray(boot)>0
  # GK black covers the full upper, including coloured lace stripes.
  if kit=='away':
   b &= ll>35; w=np.clip((ll[b]-35)/45,0,1)[:,None]
   col=np.array(bootcolor)*np.clip(.90+(ll[b]-90)/210,.58,1.22)[:,None]
   target[b]=col*w+la[b]*(1-w)
  else: target[b]=np.array(bootcolor)*np.clip(.85+(ll[b]-90)/220,.45,1.2)[:,None]
  combined=np.asarray(lm).copy(); combined[b]=255
  report.append(save(kit+'_legs',base,Image.fromarray(combined),target,source))
 (HERE/'remaining-kits-validation.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps(report,indent=2))
 if args.include_home:
  import subprocess,sys
  subprocess.run([sys.executable,str(HERE/'paint-home-kits.py')],check=True)

if __name__=='__main__': main()
