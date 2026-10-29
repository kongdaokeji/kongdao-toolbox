import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
const cache=new Map();
const loader=new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
function material(color,metalness=.15,roughness=.5){return new THREE.MeshStandardMaterial({color,metalness,roughness});}
function block(g,w,h,d,x,y,z,m,r=.035){const o=new THREE.Mesh(new RoundedBoxGeometry(w,h,d,2,Math.min(r,w/3,h/3,d/3)),m);o.position.set(x,y,z);g.add(o);return o;}
function cylinder(g,r,h,x,y,z,m){const o=new THREE.Mesh(new THREE.CylinderGeometry(r,r,h,32),m);o.position.set(x,y,z);g.add(o);return o;}
function label(g,text,w,h,x,y,z,color='#d8e8ed'){const c=document.createElement('canvas');c.width=512;c.height=192;const ctx=c.getContext('2d');ctx.fillStyle=color;ctx.font='600 54px sans-serif';ctx.textAlign='center';ctx.fillText(text,256,112);const t=new THREE.CanvasTexture(c);t.colorSpace=THREE.SRGBColorSpace;const p=new THREE.Mesh(new THREE.PlaneGeometry(w,h),new THREE.MeshBasicMaterial({map:t,transparent:true,depthWrite:false}));p.rotation.x=-Math.PI/2;p.position.set(x,y,z);g.add(p);}
function original(kind,brand,screenTexture){
 const g=new THREE.Group(),pcb=material(0x174b48),metal=material(0xa6b8c9,.65,.28),black=material(0x17202a),gold=material(0xc4a45c,.65,.3),blue=material(0x478fbc);
 if(kind==='显示器'||kind==='系统'){
  block(g,3.9,2.4,.13,0,1.85,0,black,.06);
  block(g,3.77,2.27,.02,0,1.85,.077,material(0x060b13,.15,.3),.035);
  const screen=new THREE.Mesh(new THREE.PlaneGeometry(3.66,2.16),new THREE.MeshBasicMaterial({map:screenTexture||null,color:screenTexture?0xffffff:0x244d77,toneMapped:false}));
  screen.position.set(0,1.85,.09);g.add(screen);
  block(g,.16,.78,.17,0,.56,-.07,metal,.03);block(g,1.48,.065,.8,0,.14,.1,metal,.05);
  const light=new THREE.Mesh(new THREE.SphereGeometry(.013,12,8),new THREE.MeshBasicMaterial({color:0x60cee6}));light.position.set(1.65,.695,.085);g.add(light);
 }else if(kind==='BIOS'){
  block(g,2.5,.15,1.7,0,0,0,pcb);block(g,1.3,.32,.85,0,.23,0,black,.1);
  for(const side of [-1,1])for(let i=0;i<4;i++){
   block(g,.16,.06,.24,-.48+i*.32,.1,side*.58,metal,.02);
   block(g,.16,.19,.06,-.48+i*.32,.17,side*.46,metal,.02);
  }
  label(g,'UEFI',.95,.3,0,.4,0);
 }else if(kind==='NPU'){
  block(g,2.3,.14,2.3,0,0,0,pcb);block(g,1.95,.2,1.95,0,.15,0,black,.07);
  block(g,1.05,.07,1.05,0,.29,0,metal,.025);block(g,.83,.02,.83,0,.34,0,blue,.01);
  for(const side of [-1,1])for(let i=0;i<12;i++)block(g,.06,.03,.11,-.94+i*.17,.1,side*1.09,gold,.006);
  label(g,'NPU',.65,.25,0,.36,0);
 }else if(kind==='处理器'){
  block(g,2,.12,2,0,0,0,pcb);block(g,1.7,.1,1.7,0,.1,0,black);
  if(brand==='amd'){
   const s=new THREE.Shape(),edge=.81,cut=.48,notch=.64;
   s.moveTo(-edge,-edge);s.lineTo(-cut,-edge);s.lineTo(-cut,-notch);s.lineTo(cut,-notch);s.lineTo(cut,-edge);s.lineTo(edge,-edge);
   s.lineTo(edge,-cut);s.lineTo(notch,-cut);s.lineTo(notch,cut);s.lineTo(edge,cut);s.lineTo(edge,edge);
   s.lineTo(cut,edge);s.lineTo(cut,notch);s.lineTo(-cut,notch);s.lineTo(-cut,edge);s.lineTo(-edge,edge);
   s.lineTo(-edge,cut);s.lineTo(-notch,cut);s.lineTo(-notch,-cut);s.lineTo(-edge,-cut);s.closePath();
   const lid=new THREE.Mesh(new THREE.ExtrudeGeometry(s,{depth:.12,bevelEnabled:true,bevelThickness:.025,bevelSize:.025,bevelSegments:3,steps:1}),metal);
   lid.rotation.x=-Math.PI/2;lid.position.y=.16;g.add(lid);
  }else block(g,1.5,.16,1.5,0,.2,0,metal,.08);
  const pins=new THREE.InstancedMesh(new THREE.CylinderGeometry(.025,.025,brand==='amd'?.012:.08,8),gold,144),matrix=new THREE.Matrix4();
  for(let x=0;x<12;x++)for(let z=0;z<12;z++){matrix.makeTranslation(-.82+x*.15,brand==='amd'?-.067:-.11,-.82+z*.15);pins.setMatrixAt(x*12+z,matrix);}g.add(pins);
  label(g,brand==='amd'?'AMD RYZEN':'PROCESSOR',1.2,.35,0,brand==='amd'?.315:.29,0,'#304457');
 }else if(kind==='内存'){
  block(g,4,.1,1.25,0,0,0,pcb);
  for(let i=0;i<8;i++)block(g,.39,.11,.62,-1.67+i*.48,.12,-.08,black,.02);
  for(let i=0;i<48;i++)block(g,.05,.02,.19,-1.84+i*.078,.066,.5,gold,.002);
  block(g,2.4,.08,.12,0,.2,-.5,blue);label(g,'MEMORY',2,.22,0,.22,.2);
 }else if(kind==='硬盘'){
  block(g,3.9,.08,1.25,0,0,0,pcb);
  for(let i=0;i<3;i++)block(g,.75,.12,.9,-.9+i*.93,.12,0,black,.025);
  block(g,.35,.025,1.16,-1.8,.065,0,gold,.006);
  label(g,'NVMe SSD',2.4,.42,.35,.19,0);
  for(const x of [-1.77,1.78])cylinder(g,.07,.03,x,.08,0,metal);
 }else if(kind==='电池'){
  for(let i=0;i<3;i++)block(g,1.05,.22,2,-1.1+i*1.1,0,0,black,.08);
  block(g,3.2,.08,.18,0,.16,-.9,pcb);block(g,.7,.12,.28,.7,.12,-1.12,gold);
  label(g,'Li-ion',1.8,.55,0,.13,0);
 }else if(kind==='显卡'){
  block(g,4,.16,1.65,0,0,0,pcb);block(g,3.85,.35,1.6,0,.22,0,black,.1);
  block(g,.1,.7,1.85,-2.1,.15,0,metal);block(g,2.2,.05,.2,-.3,-.08,.89,gold);
  for(const x of [-.96,.96]){
   cylinder(g,.63,.04,x,.43,0,metal);cylinder(g,.55,.05,x,.47,0,black);cylinder(g,.15,.07,x,.52,0,metal);
   for(let b=0;b<9;b++){const a=b*Math.PI*2/9;const o=block(g,.42,.045,.14,x+Math.cos(a)*.34,.53,Math.sin(a)*.34,blue);o.rotation.y=-a+.6;}
  }
 }else if(kind==='网卡'){
  block(g,2.2,.09,2.8,0,0,0,pcb,.07);block(g,1.92,.22,1.7,0,.16,-.27,metal,.07);
  block(g,1.8,.012,1.56,0,.282,-.27,material(0xaab7bf,.6,.4),.04);
  for(const x of [-.86,.86]){cylinder(g,.1,.06,x,.11,-1.21,gold);cylinder(g,.045,.07,x,.12,-1.21,black);}
  for(let i=0;i<22;i++)block(g,.065,.018,.22,-.94+i*.09,.057,1.25,gold,.004);
  for(let i=0;i<6;i++){block(g,.11,.06,.13,-.84+i*.31,.095,.79,black,.012);block(g,.07,.025,.12,-.74+i*.31,.065,1.01,metal,.005);}
  label(g,'Wi-Fi',1.2,.34,0,.295,-.45,'#24303e');label(g,'WIRELESS MODULE',1.45,.18,0,.295,.05,'#4f5b65');
 }else if(kind==='声卡'){
  block(g,3.7,.09,2,0,0,0,pcb,.045);block(g,1.1,.16,1.1,-.25,.13,-.2,black,.035);
  block(g,.12,.55,2.25,-1.94,.16,0,metal,.025);
  for(let i=0;i<5;i++){const jack=cylinder(g,.13,.28,-2.08,.19,-.76+i*.38,gold);jack.rotation.z=Math.PI/2;}
  for(let i=0;i<7;i++){cylinder(g,.1,.23,.62+(i%2)*.35,.18,-.7+Math.floor(i/2)*.43,black);cylinder(g,.09,.015,.62+(i%2)*.35,.304,-.7+Math.floor(i/2)*.43,metal);}
  for(let i=0;i<24;i++)block(g,.07,.018,.18,-1.2+i*.105,.057,.94,gold,.004);
  label(g,'AUDIO',.9,.25,-.25,.22,-.2);
 }else{
  const isBoard=kind==='主板',w=isBoard?3.5:3,h=isBoard?3.5:1.9;
  block(g,w,.09,h,0,0,0,pcb);block(g,.9,.17,.9,-.4,.13,-.35,black);
  block(g,.72,.08,.72,-.4,.27,-.35,metal);label(g,kind==='网卡'?'NETWORK':isBoard?'MAINBOARD':'AUDIO',1.5,.28,.4,.13,.65);
  for(let i=0;i<3;i++)block(g,.12,.18,isBoard?2.1:.8,w/2-.35-i*.27,.13,0,i%2?black:blue);
  for(let i=0;i<10;i++){const trace=new THREE.Mesh(new THREE.BoxGeometry(.025,.006,.8),gold);trace.position.set(-w/2+.2+i*.16,.055,.8);g.add(trace);}
  if(kind==='网卡'){for(const x of [-1.1,1.1]){const a=cylinder(g,.055,1.4,x,.68,-.85,black);a.rotation.z=x<0?-.24:.24;}}
  if(isBoard){for(let i=0;i<3;i++)block(g,2.3,.2,.15,-.15,.18,.65+i*.37,black);for(const z of [-1.2,-.7])block(g,.45,.4,.4,-1.62,.2,z,metal);}
 }
 return g;
}
function normalize(root){const bounds=new THREE.Box3().setFromObject(root);const center=bounds.getCenter(new THREE.Vector3());const size=bounds.getSize(new THREE.Vector3());root.position.sub(center);const wrapper=new THREE.Group();wrapper.add(root);wrapper.scale.setScalar(4.1/Math.max(size.x,size.y,size.z));const result=new THREE.Group();result.add(wrapper);return result;}
export async function hardwareComponent(kind,brand,screenTexture){
 const key=`${kind}:${brand||''}:${['显示器','系统'].includes(kind)?screenTexture?.uuid||'default':''}`;
 if(!cache.has(key))cache.set(key,(async()=>{
  const file=kind==='处理器'?(brand==='intel'?'cpu':null):({'内存':'ram','显卡':'gpu','硬盘':'storage','主板':'motherboard'})[kind];
  let root,source='procedural';
  if(file){try{
   root=(await loader.loadAsync(`models/${file}.glb`)).scene;
   if(kind==='显卡')root.rotateZ(Math.PI/2);
   if(kind==='内存'){root.rotateX(Math.PI/2);root.rotateY(Math.PI/2);}
   let hasTexture=false;
   root.traverse(node=>{if(node.isMesh){const materials=Array.isArray(node.material)?node.material:[node.material];for(const m of materials){hasTexture ||=!!m.map;m.roughness=Math.max(.34,m.roughness);m.envMapIntensity=.75;if(m.emissive&&!m.emissiveMap)m.emissive.setHex(0);}}});
   if(['cpu','gpu','ram','storage'].includes(file)&&!hasTexture)throw new Error(`No component textures loaded: ${file}`);
   source=file;
  }catch(error){console.warn('Hardware component fallback',file,error);root=original(kind,brand,screenTexture);}}
  else root=original(kind,brand,screenTexture);
  const result=normalize(root);
  if(kind==='处理器'&&brand==='intel'){
   const bounds=new THREE.Box3().setFromObject(result);
   label(result,'intel',1.55,.53,0,bounds.max.y+.012,-.15,'#314253');
  }
  result.userData={source,brand:brand||'generic'};return result;
 })());
 return (await cache.get(key)).clone(true);
}
