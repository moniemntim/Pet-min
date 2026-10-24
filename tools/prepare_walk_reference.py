from pathlib import Path
import cv2, json, math
from PIL import Image, ImageDraw, ImageFont
VIDEO=Path(r'C:\Users\787\Downloads\LINE_ALBUM_薯餅_261023_1.mp4')
OUT=Path('pet-art/walk-bokchoy-20261023/references'); OUT.mkdir(parents=True,exist_ok=True)
def read_at(cap,i):
 cap.set(cv2.CAP_PROP_POS_FRAMES,i); ok,b=cap.read(); return cv2.cvtColor(b,cv2.COLOR_BGR2RGB) if ok else None
def board(items,path,cols=4):
 if not items:return
 thumbs=[]
 for im,t in items:
  h,w=im.shape[:2]; tw=400; th=round(h*tw/w); pic=Image.fromarray(im).resize((tw,th)); canvas=Image.new('RGB',(tw,th+28),'white');canvas.paste(pic,(0,28)); ImageDraw.Draw(canvas).text((6,6),t,fill='black');thumbs.append(canvas)
 rows=math.ceil(len(thumbs)/cols); out=Image.new('RGB',(cols*400,rows*thumbs[0].height),(230,230,230))
 for n,p in enumerate(thumbs):out.paste(p,((n%cols)*400,(n//cols)*p.height))
 out.save(path,quality=92)
def main():
 cap=cv2.VideoCapture(str(VIDEO)); fps=cap.get(cv2.CAP_PROP_FPS); n=int(cap.get(cv2.CAP_PROP_FRAME_COUNT)); w=int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)); h=int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)); dur=n/fps if fps else 0
 meta={'source':str(VIDEO),'frames':n,'fps':fps,'width':w,'height':h,'duration_seconds':dur,'orientation':'decoded landscape; no metadata rotation applied'}
 (OUT/'metadata.json').write_text(json.dumps(meta,indent=2),encoding='utf8')
 even=[]
 for j,i in enumerate([round(x) for x in [k*(n-1)/11 for k in range(12)]]):
  im=read_at(cap,i)
  if im is not None:
   p=OUT/f'even-{j:02d}-{i:05d}.jpg'; cv2.imwrite(str(p),cv2.cvtColor(im,cv2.COLOR_RGB2BGR),[cv2.IMWRITE_JPEG_QUALITY,94]); even.append((im,f'{j+1:02d}  frame {i}  {i/fps:.2f}s'))
 board(even,OUT/'even-12-contact.jpg',4)
 # Dense sampling at 5 fps; this preserves a reviewable walking sequence without duplicating every source frame.
 dense=[]
 for j,i in enumerate(range(0,n,max(1,round(fps/5)))):
  im=read_at(cap,i)
  if im is not None:
   p=OUT/f'dense-{j:03d}-{i:05d}.jpg';cv2.imwrite(str(p),cv2.cvtColor(im,cv2.COLOR_RGB2BGR),[cv2.IMWRITE_JPEG_QUALITY,90]);dense.append((im,f'{i/fps:.2f}s'))
 board(dense,OUT/'dense-walk-contact.jpg',5)
 # Small readable windows: feeding and short repositioning references, not a complete gait cycle.
 for tag,start,end,count in [('early-0-3s',0,3,10),('middle-6-10s',6,10,12),('late-46-55s',46,55,12)]:
  c=cv2.VideoCapture(str(VIDEO)); ids=[round(start*fps+k*(end-start)*fps/(count-1)) for k in range(count)]; picks=[]
  for j,idx2 in enumerate(ids):
   c.set(cv2.CAP_PROP_POS_FRAMES,idx2); ok,b=c.read()
   if ok:
    im=cv2.cvtColor(b,cv2.COLOR_BGR2RGB); q=OUT/f'{tag}-{j:02d}-{idx2:05d}.jpg'; cv2.imwrite(str(q),cv2.cvtColor(im,cv2.COLOR_RGB2BGR),[cv2.IMWRITE_JPEG_QUALITY,94]); picks.append((im,f'{idx2/fps:.2f}s'))
  c.release(); board(picks,OUT/f'{tag}-contact.jpg',4)
 cap.release(); print(json.dumps({'metadata':meta,'even':len(even),'dense':len(dense),'output':str(OUT)},ensure_ascii=False))
if __name__=='__main__':main()
