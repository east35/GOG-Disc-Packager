import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';

const editions = [
  {title:'Anachronix', outside:'Anachronix - Out.png', inside:'Anachronix - In.png', disc:'Anachronix - Disc.png'},
  {title:'Sleeping Dogs', outside:'Sleeping Dogs - Out.png', inside:'Case Template - In.png', disc:'Sleeping Dogs Disc.png'},
  {title:'Cyberpunk 2077 Ultimate', outside:'Cyberpunk 2077 Ultimate - Out.png', inside:'Cyberpunk 2077 Ultimate - In.png', disc:'Cyberpunk Disc 4.png', secondDisc:'Cyberpunk Disc 5.png'},
  {title:'The Witcher', outside:'Witcher 1 Case - Out.png', inside:'Witcher 1 Case - In.png', disc:'Witcher 1 Disc - Install + Play.png', secondDisc:'Witcher 1 Disc - Install + Extras.png'},
  {title:'The Witcher 2', outside:'The Witcher 2 - Out.jpg', inside:'The Witcher 2 - In.jpg', disc:'W2.png'},
  {title:'Mad Max', outside:'Mad Max - Out.png', inside:'Mad Max - In.png', disc:'Mad Max - Disc.png'},
  {title:'Look Outside', outside:'Look Outside - Out.png', inside:'Look Outside - In.png', disc:'Look Outside - Disc.png'},
  {title:'Septerra Core', outside:'Septerra Core - Out.png', inside:'Septerra Core - In.png', disc:'Septerra Core - Disc.png'},
  {title:'Final Fantasy IX', outside:'Final Fantasy IX - Out.jpg', inside:'Final Fantasy IX - In.jpg', disc:'FFIX.png'},
  {title:'Breath of Fire IV', outside:'Breath of Fire - Out.jpg', inside:'Breath of Fire - In.jpg', disc:'BoF.png'},
  {title:'The Blood of Dawnwalker', outside:'Dawnwalker - Out.png', inside:'Dawnwalker - In.png', disc:'Dawnwalker Disc 1.jpg', secondDisc:'Dawnwalker Disc 2.jpg'},
];
const stage = document.querySelector('#stage');
const prompt = document.querySelector('#cursor-prompt');
const reduced = matchMedia('(prefers-reduced-motion: reduce)');
let selected = 0, opened = false, renderer, hovering = false;
let cycleElapsed = 0, rotationY = 0, rotationX = 0;
function updateUI(){
  stage.setAttribute('aria-label',`${editions[selected].title}. ${opened?'Close':'Open'} case. Drag to rotate. Arrow keys browse; Shift plus arrow keys rotate.`);
  stage.setAttribute('aria-expanded',String(opened));
  prompt.textContent=opened?'Click to close · Drag to rotate':'Click to open · Drag to rotate';
}
function select(i){selected=(i+editions.length)%editions.length;opened=false;rotationY=0;rotationX=0;cycleElapsed=0;updateUI();}
function toggle(){opened=!opened;rotationY=0;rotationX=0;cycleElapsed=0;updateUI();}
stage.addEventListener('keydown',e=>{
  if(e.shiftKey&&['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key)){
    e.preventDefault();cycleElapsed=0;
    if(e.key==='ArrowLeft')rotationY-=Math.PI/4;
    if(e.key==='ArrowRight')rotationY+=Math.PI/4;
    if(e.key==='ArrowUp')rotationX=Math.max(-.65,rotationX-.15);
    if(e.key==='ArrowDown')rotationX=Math.min(.65,rotationX+.15);
    return;
  }
  if(e.key==='ArrowLeft'){e.preventDefault();select(selected-1);}
  if(e.key==='ArrowRight'){e.preventDefault();select(selected+1);}
  if(e.key==='Enter'||e.key===' '){e.preventDefault();toggle();}
  if(e.key==='Escape'){opened=false;rotationY=0;rotationX=0;cycleElapsed=0;updateUI();}
});
stage.addEventListener('blur',()=>{cycleElapsed=0;});
updateUI();

try { renderer=new THREE.WebGLRenderer({antialias:true,alpha:true}); }
catch { document.querySelector('#fallback').hidden=false;stage.setAttribute('aria-disabled','true'); }
if(renderer){
renderer.setPixelRatio(Math.min(devicePixelRatio,2));
renderer.setClearColor(0,0);
renderer.toneMapping=THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure=1;
stage.append(renderer.domElement);
renderer.domElement.setAttribute('aria-hidden','true');
const scene=new THREE.Scene();
const camera=new THREE.PerspectiveCamera(33,1,.1,100);
camera.position.set(0,.4,12);
camera.lookAt(0,0,0);
const pmrem=new THREE.PMREMGenerator(renderer);
const room=new RoomEnvironment();scene.environment=pmrem.fromScene(room,.04).texture;
room.dispose();pmrem.dispose();
scene.add(new THREE.HemisphereLight(0xe6d8ff,0x15101f,2));
const key=new THREE.DirectionalLight(0xf1e6ff,4);key.position.set(-3,5,6);scene.add(key);
const rim=new THREE.DirectionalLight(0x9a58ff,3);rim.position.set(4,1,-2);scene.add(rim);
const plastic=new THREE.MeshStandardMaterial({color:0x19171e,roughness:.32,metalness:.22});
const edge=new THREE.MeshStandardMaterial({color:0x343139,roughness:.25,metalness:.35});
function box(parent,w,h,d,x,y,z,material=plastic){
 const mesh=new THREE.Mesh(new RoundedBoxGeometry(w,h,d,2,.025),material);
 mesh.position.set(x,y,z);parent.add(mesh);return mesh;
}
const textureLoader = new THREE.TextureLoader();
const textures = new Map();
function artworkTexture(filename){
 if(textures.has(filename))return textures.get(filename);
 const texture=textureLoader.load(`${import.meta.env.BASE_URL}assets/${encodeURIComponent(filename).replaceAll('%2B','+')}`,undefined,()=>{
   const message=document.querySelector('#fallback');
   message.textContent='Some case artwork could not load. Please refresh to try again.';message.hidden=false;
 });
 texture.colorSpace=THREE.SRGBColorSpace;
 texture.anisotropy=renderer.capabilities.getMaxAnisotropy();
 textures.set(filename,texture);return texture;
}
// Print-sheet UV regions omit white bleed/crop marks: back, spine, front.
const printRegions={front:[.525,.047,.446,.906],back:[.029,.047,.446,.906],spine:[.476,.047,.048,.906]};
function artworkPlane(parent,w,h,x,y,z,filename,region,rotation=0){
 const geometry=new THREE.PlaneGeometry(w,h);
 const uv=geometry.attributes.uv;
 for(let i=0;i<uv.count;i++)uv.setXY(i,region[0]+uv.getX(i)*region[2],region[1]+uv.getY(i)*region[3]);
 // Printed artwork retains source colors; only the case plastic receives studio reflections.
 const mesh=new THREE.Mesh(geometry,new THREE.MeshBasicMaterial({map:artworkTexture(filename),toneMapped:false}));
 mesh.position.set(x,y,z);mesh.rotation.y=rotation;parent.add(mesh);return mesh;
}
function buildCase(entry,i){
 const group=new THREE.Group();scene.add(group);group.userData.index=i;
 box(group,1.92,2.72,.105,0,0,-.07);
 artworkPlane(group,1.81,2.59,0,0,-.125,entry.outside,printRegions.back,Math.PI);
 artworkPlane(group,1.81,2.59,0,0,-.014,entry.inside,[.525,0,.475,1]);
 // Molded outer tray rails and circular disc recess.
 box(group,.04,2.64,.13,-.91,0,.025,edge);box(group,.04,2.64,.13,.91,0,.025,edge);
 box(group,1.84,.04,.13,0,1.31,.025,edge);box(group,1.84,.04,.13,0,-1.31,.025,edge);
 const tray=new THREE.Mesh(new THREE.TorusGeometry(.815,.022,10,100),edge);tray.position.set(.03,-.05,.015);group.add(tray);
 const discMaterial=new THREE.MeshBasicMaterial({map:artworkTexture(entry.disc),toneMapped:false,side:THREE.DoubleSide});
 const disc=new THREE.Mesh(new THREE.RingGeometry(.158,.78,100),discMaterial);disc.position.set(.03,-.05,.062);group.add(disc);
 const innerRing=new THREE.Mesh(new THREE.RingGeometry(.1,.157,60),new THREE.MeshStandardMaterial({color:0xb6aec8,metalness:.7,roughness:.2}));innerRing.position.set(.03,-.05,.065);group.add(innerRing);
 const hub=new THREE.Mesh(new THREE.CylinderGeometry(.085,.105,.07,24),edge);hub.rotation.x=Math.PI/2;hub.position.set(.03,-.05,.075);group.add(hub);
 const hinge=new THREE.Group();hinge.position.set(-.96,0,.06);group.add(hinge);
 box(hinge,1.92,2.72,.065,.96,0,.065);
 box(hinge,.04,2.62,.075,.07,0,.014,edge);box(hinge,.04,2.62,.075,1.85,0,.014,edge);
 for(const y of [-.92,.92])box(hinge,.2,.07,.07,1.63,y,-.01,edge);
 artworkPlane(hinge,1.81,2.59,.97,0,.1,entry.outside,printRegions.front);
 artworkPlane(hinge,1.81,2.59,.97,0,.03,entry.inside,[0,0,.475,1],Math.PI);
 if(entry.secondDisc){
   const extraDisc=new THREE.Mesh(new THREE.RingGeometry(.158,.78,100),new THREE.MeshBasicMaterial({map:artworkTexture(entry.secondDisc),toneMapped:false}));
   extraDisc.position.set(.97,-.05,-.01);extraDisc.rotation.y=Math.PI;hinge.add(extraDisc);
   const extraHub=hub.clone();extraHub.position.set(.97,-.05,-.02);hinge.add(extraHub);
 }
 box(group,.085,2.65,.21,-.97,0,.005,edge);
 artworkPlane(group,.2,2.59,-1.015,0,.005,entry.outside,printRegions.spine,-Math.PI/2);
 group.rotation.set(-.045,-.2,0);return {group,hinge};
}
const cases=editions.map(buildCase);
const ray=new THREE.Raycaster(),pointer=new THREE.Vector2();
let down=null,tilt=0;
stage.addEventListener('pointerdown',e=>{
 if(down||e.button!==0)return;
 const index=hitIndex(e);if(index===undefined)return;
 down={id:e.pointerId,index,x:e.clientX,y:e.clientY,yaw:index===selected?rotationY:0,pitch:index===selected?rotationX:0,dragged:false};
 stage.setPointerCapture(e.pointerId);cycleElapsed=0;
});
function hitIndex(e){
 const rect=stage.getBoundingClientRect();
 pointer.set((e.clientX-rect.left)/rect.width*2-1,-(e.clientY-rect.top)/rect.height*2+1);
 ray.setFromCamera(pointer,camera);
 const hit=ray.intersectObjects(cases.filter(c=>c.group.visible).map(c=>c.group),true)[0];
 if(!hit)return undefined;
 let object=hit.object;while(object.parent&&object.userData.index===undefined)object=object.parent;
 return object.userData.index;
}
stage.addEventListener('pointermove',e=>{
 const rect=stage.getBoundingClientRect();
 if(down&&e.pointerId===down.id){
   const dx=e.clientX-down.x,dy=e.clientY-down.y;
   if(!down.dragged&&Math.hypot(dx,dy)>6){
     down.dragged=true;
     if(down.index!==selected)select(down.index);
   }
   if(down.dragged){
     rotationY=down.yaw+dx*.012;
     rotationX=Math.max(-.65,Math.min(.65,down.pitch+dy*.006));
     tilt=0;hovering=true;prompt.hidden=true;stage.style.cursor='grabbing';
     return;
   }
 }
 tilt=((e.clientX-rect.left)/rect.width-.5)*.16;
 hovering=hitIndex(e)!==undefined;
 prompt.hidden=!hovering||e.pointerType==='touch';
 prompt.style.left=`${Math.min(e.clientX-rect.left+18,rect.width-240)}px`;
 prompt.style.top=`${Math.min(e.clientY-rect.top+18,rect.height-45)}px`;
 stage.style.cursor=hovering?'grab':'default';
});
stage.addEventListener('pointerleave',()=>{if(down)return;tilt=0;hovering=false;prompt.hidden=true;cycleElapsed=0;});
function releasePointer(e){
 if(!down||e.pointerId!==down.id)return null;
 const gesture=down;down=null;cycleElapsed=0;
 if(stage.hasPointerCapture(e.pointerId))stage.releasePointerCapture(e.pointerId);
 stage.style.cursor='grab';return gesture;
}
stage.addEventListener('pointercancel',e=>{releasePointer(e);hovering=false;prompt.hidden=true;});
stage.addEventListener('lostpointercapture',e=>{if(down?.id===e.pointerId){down=null;cycleElapsed=0;hovering=false;prompt.hidden=true;}});
stage.addEventListener('pointerup',e=>{
 const gesture=releasePointer(e);if(!gesture)return;
 const rect=stage.getBoundingClientRect();
 hovering=e.pointerType!=='touch'&&e.clientX>=rect.left&&e.clientX<=rect.right&&e.clientY>=rect.top&&e.clientY<=rect.bottom;
 if(gesture.dragged){prompt.hidden=true;return;}
 const index=hitIndex(e);
 if(index===gesture.index){if(index===selected)toggle();else{select(index);opened=true;updateUI();}}
});
function resize(){const {width,height}=stage.getBoundingClientRect();renderer.setSize(width,height);camera.aspect=width/height;camera.position.z=width<500?11.8:10.4;camera.updateProjectionMatrix();}
new ResizeObserver(resize).observe(stage);resize();
let last=0;
renderer.setAnimationLoop(time=>{
 const dt=Math.min((time-last)/1000,.05);last=time;
 if(!document.hidden&&!down&&!opened&&!hovering&&!stage.matches(':focus-visible')&&!reduced.matches){
   cycleElapsed+=dt;if(cycleElapsed>=4.5)select(selected+1);
 }
 const lerp=reduced.matches?1:1-Math.exp(-dt*7);
 cases.forEach(({group,hinge},i)=>{
 let offset=(i-selected+editions.length)%editions.length;if(offset>editions.length/2)offset-=editions.length;
 const active=offset===0;
 // Recycle distant cases offstage instead of sweeping them through the center.
 const wasFar=group.userData.offset!==undefined&&Math.abs(group.userData.offset)>2;
 if(wasFar){group.position.x=offset*2.32;group.position.z=-.85-Math.abs(offset)*.55;}
 group.userData.offset=offset;
 group.visible=Math.abs(offset)<=2;
 const x=active?(opened?.83:0):offset*(opened?2.95:2.32);
 const z=active?.65:-.85-Math.abs(offset)*.55;
 group.position.x=THREE.MathUtils.lerp(group.position.x,x,lerp);
 group.position.z=THREE.MathUtils.lerp(group.position.z,z,lerp);
 group.position.y=THREE.MathUtils.lerp(group.position.y,active?.04:-.13,lerp);
 const scale=active?(opened?1.55:1.70):1.32;
 group.scale.lerp(new THREE.Vector3(scale,scale,scale),lerp);
 const rotationLerp=down?.dragged?1:lerp;
 group.rotation.y=THREE.MathUtils.lerp(group.rotation.y,active?(opened?-.08:-.19)+rotationY+(reduced.matches?0:tilt):offset*.13-.18,rotationLerp);
 group.rotation.x=THREE.MathUtils.lerp(group.rotation.x,-.045+(active?rotationX:0),rotationLerp);
 hinge.rotation.y=THREE.MathUtils.lerp(hinge.rotation.y,active&&opened?-Math.PI*.91:0,lerp);
 });
 renderer.render(scene,camera);
});
}
