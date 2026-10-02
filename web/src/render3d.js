/* ---------------- 3D renderer ---------------- */
const LIN=hex=>new THREE.Color(hex).convertSRGBToLinear();
function setC(mat,hex){mat.color.set(hex).convertSRGBToLinear();}
function linTree(root){root.traverse(o=>{const ms=o.material?(Array.isArray(o.material)?o.material:[o.material]):[];for(const m of ms){if(m.userData.lin||m.isShaderMaterial)continue;m.userData.lin=1;if(m.color)m.color.convertSRGBToLinear();if(m.emissive)m.emissive.convertSRGBToLinear();m.needsUpdate=true;}});}
function elevAt(p){const n=TR.n,i0=Math.floor(p),f=p-i0,a=((i0%n)+n)%n,b=(a+1)%n;return TR.E[a]+(TR.E[b]-TR.E[a])*f;}
function gradeAt(p){const n=TR.n,i=((Math.floor(p)%n)+n)%n;return TR.GR[i];}
const R3={ok:false,cars:new Map(),trackId:null,world:null,roll:0,chase:null,frame:0};
function use3D(){return R3.ok&&G.cam!=="top";}
function canvasTex(w,h,draw,clampEdge){
  const c=document.createElement("canvas");c.width=w;c.height=h;draw(c.getContext("2d"),w,h);
  const t=new THREE.CanvasTexture(c);t.encoding=THREE.sRGBEncoding;
  if(!clampEdge)t.wrapS=t.wrapT=THREE.RepeatWrapping;
  if(R3.r)t.anisotropy=Math.min(8,R3.r.capabilities.getMaxAnisotropy());
  return t;
}
function speckle(g,w,h,base,cols,count,size){
  g.fillStyle=base;g.fillRect(0,0,w,h);
  for(let i=0;i<count;i++){g.fillStyle=cols[i%cols.length];const s=size*(.5+Math.random());g.fillRect(Math.random()*w,Math.random()*h,s,s);}
}
const BRANDS=[["NORTHLINE","#0d2a6b","#ffffff"],["APEX OIL","#d8322a","#ffffff"],["KAIROS","#111111","#ffd21f"],["VELOCE","#ffffff","#d8322a"],["MERIDIAN AIR","#0a6e5c","#ffffff"],["HALCYON","#f2f0ea","#1a1f2b"],["TORQUE","#ff8a1f","#111111"],["SOLARIS","#2b2f8f","#f5c400"]];
function makeTextures3D(){
  const T={};
  T.asphalt=canvasTex(256,256,(g,w,h)=>speckle(g,w,h,"#56595f",["#62656b","#4a4d52","#6d7076","#3f4146"],8000,2));
  T.runoff=canvasTex(256,256,(g,w,h)=>speckle(g,w,h,"#4b4e54",["#55585e","#43464b","#5d6066"],6000,2));
  T.grass=canvasTex(256,256,(g,w,h)=>{speckle(g,w,h,"#3c7136",["#447c3d","#33622e","#4a8542","#2d5829"],7000,3);g.fillStyle="rgba(255,255,255,.055)";g.fillRect(0,0,w/2,h);});
  T.city=canvasTex(256,256,(g,w,h)=>{speckle(g,w,h,"#6c6f74",["#75787d","#606368","#7f8287"],5000,3);g.strokeStyle="rgba(0,0,0,.18)";g.lineWidth=2;g.strokeRect(0,0,w,h);});
  T.gravel=canvasTex(256,256,(g,w,h)=>speckle(g,w,h,"#b6a785",["#c4b593","#a39573","#d0c3a2","#8f8263","#e0d6bd"],12000,2.2));
  T.kerb=canvasTex(16,64,(g,w,h)=>{g.fillStyle="#d8322a";g.fillRect(0,0,w,h);g.fillStyle="#f3f0ea";g.fillRect(0,0,w,h/2);g.fillStyle="rgba(0,0,0,.12)";g.fillRect(0,0,2,h);});
  T.wall=canvasTex(512,64,(g,w,h)=>{
    g.fillStyle="#d4d6da";g.fillRect(0,0,w,h);
    let x=0,k=0;while(x<w){const b=BRANDS[k++%BRANDS.length],bw=128;g.fillStyle=b[1];g.fillRect(x,h*.12,bw-4,h*.62);g.fillStyle=b[2];g.font="bold 22px Arial, sans-serif";g.textAlign="center";g.textBaseline="middle";g.fillText(b[0],x+bw/2-2,h*.44,bw-14);x+=bw;}
    g.fillStyle="#9fa3a9";g.fillRect(0,h*.82,w,h*.18);});
  T.barrier=canvasTex(512,64,(g,w,h)=>{
    g.fillStyle="#24272c";g.fillRect(0,0,w,h);
    let x=0,k=3;while(x<w){const b=BRANDS[k++%BRANDS.length],bw=128;g.fillStyle=b[1];g.fillRect(x+2,4,bw-4,h*.6);g.fillStyle=b[2];g.font="bold 21px Arial, sans-serif";g.textAlign="center";g.textBaseline="middle";g.fillText(b[0],x+bw/2,h*.34,bw-14);x+=bw;}
    for(let xx=0;xx<w;xx+=16){g.fillStyle=(xx/16)%2?"#d8322a":"#eeeae2";g.fillRect(xx,h*.72,16,h*.28);}});
  T.fence=canvasTex(64,64,(g,w,h)=>{g.clearRect(0,0,w,h);g.strokeStyle="rgba(205,210,215,.75)";g.lineWidth=1;for(let i=-w;i<w*2;i+=8){g.beginPath();g.moveTo(i,0);g.lineTo(i+h,h);g.stroke();g.beginPath();g.moveTo(i,h);g.lineTo(i+h,0);g.stroke();}g.fillStyle="#6b7077";g.fillRect(0,0,4,h);g.fillRect(0,0,w,3);});
  T.crowd=canvasTex(256,128,(g,w,h)=>{speckle(g,w,h,"#3a3f48",["#d33","#fd2","#28f","#eee","#f80","#2c6","#a4f","#555","#f6c"],6000,3);for(let y=0;y<h;y+=16){g.fillStyle="rgba(0,0,0,.35)";g.fillRect(0,y,w,3);}});
  T.garage=canvasTex(512,128,(g,w,h)=>{
    g.fillStyle="#e9e7e1";g.fillRect(0,0,w,h);g.fillStyle="#1a1f27";g.fillRect(0,0,w,h*.16);
    const cols=["#e8322e","#2f6bff","#ff8a1f","#13c4a3","#c9ced6","#7a3cff","#ff4f9a","#0b6b3a"];
    for(let i=0;i<8;i++){const x=i*64+6;g.fillStyle="#2a2f37";g.fillRect(x,h*.3,52,h*.66);g.fillStyle=cols[i];g.fillRect(x,h*.22,52,h*.07);g.fillStyle="rgba(255,255,255,.08)";for(let y=h*.34;y<h*.94;y+=6)g.fillRect(x,y,52,1);}});
  T.check=canvasTex(64,8,(g,w,h)=>{for(let x=0;x<w;x+=4)for(let y=0;y<h;y+=4){g.fillStyle=((x+y)/4)%2?"#111":"#f2f0ea";g.fillRect(x,y,4,4);}});
  T.boards=["300","200","100"].map(t=>canvasTex(128,160,(g,w,h)=>{g.fillStyle="#f4f2ec";g.fillRect(0,0,w,h);g.strokeStyle="#111";g.lineWidth=8;g.strokeRect(4,4,w-8,h-8);g.fillStyle="#111";g.font="bold 64px Arial, sans-serif";g.textAlign="center";g.textBaseline="middle";g.fillText(t,w/2,h/2);},true));
  T.panel={};
  for(const [k,txt,bg,fg] of [["SC","SC","#ffd21f","#111"],["VSC","VSC","#ffd21f","#111"]])
    T.panel[k]=canvasTex(128,96,(g,w,h)=>{g.fillStyle="#111";g.fillRect(0,0,w,h);g.fillStyle=bg;g.fillRect(6,6,w-12,h-12);g.fillStyle=fg;g.font="bold 54px Arial, sans-serif";g.textAlign="center";g.textBaseline="middle";g.fillText(txt,w/2,h/2+2);},true);
  T.cloud=canvasTex(256,128,(g,w,h)=>{g.clearRect(0,0,w,h);for(let i=0;i<26;i++){const x=w*.15+Math.random()*w*.7,y=h*.35+Math.random()*h*.35,r=18+Math.random()*34;const gr=g.createRadialGradient(x,y,0,x,y,r);gr.addColorStop(0,"rgba(255,255,255,.9)");gr.addColorStop(1,"rgba(255,255,255,0)");g.fillStyle=gr;g.beginPath();g.arc(x,y,r,0,7);g.fill();}},true);
  T.smoke=canvasTex(64,64,(g,w,h)=>{const gr=g.createRadialGradient(32,32,0,32,32,32);gr.addColorStop(0,"rgba(255,255,255,1)");gr.addColorStop(.5,"rgba(255,255,255,.45)");gr.addColorStop(1,"rgba(255,255,255,0)");g.fillStyle=gr;g.fillRect(0,0,w,h);},true);
  T.rim=canvasTex(128,128,(g,w,h)=>{g.fillStyle="#2b2e33";g.fillRect(0,0,w,h);g.translate(w/2,h/2);g.fillStyle="#8c9198";for(let i=0;i<5;i++){g.rotate(Math.PI*2/5);g.fillRect(-5,6,10,52);}g.fillStyle="#c9ccd1";g.beginPath();g.arc(0,0,10,0,7);g.fill();},true);
  T.windows=canvasTex(128,256,(g,w,h)=>{g.fillStyle="#8a8f96";g.fillRect(0,0,w,h);for(let y=8;y<h;y+=14)for(let x=6;x<w;x+=14){g.fillStyle=Math.random()<.5?"#2f3a48":"#44536a";g.fillRect(x,y,9,9);}});
  T.windowsNight=canvasTex(128,256,(g,w,h)=>{g.fillStyle="#141820";g.fillRect(0,0,w,h);for(let y=8;y<h;y+=14)for(let x=6;x<w;x+=14){g.fillStyle=Math.random()<.45?"#f3d27a":"#1d2430";g.fillRect(x,y,9,9);}});
  return T;
}
function init3D(){
  if(!window.THREE)return;
  try{
    const r=new THREE.WebGLRenderer({canvas:$("#three"),antialias:true,powerPreference:"high-performance"});
    R3.r=r;
    r.setPixelRatio(Math.min(1.5,window.devicePixelRatio||1));
    r.shadowMap.enabled=true;r.shadowMap.type=THREE.PCFSoftShadowMap;
    r.outputEncoding=THREE.sRGBEncoding;r.toneMapping=THREE.ACESFilmicToneMapping;r.toneMappingExposure=1.05;
    const scene=new THREE.Scene();
    const cam=new THREE.PerspectiveCamera(72,1,.05,5000);cam.rotation.order="YXZ";
    const hemi=new THREE.HemisphereLight(0xdde8f5,0x3a4a30,.8);scene.add(hemi);
    const sun=new THREE.DirectionalLight(0xfff0d8,.85);
    sun.castShadow=true;const coarse=matchMedia("(pointer: coarse)").matches;
    sun.shadow.mapSize.set(coarse?1024:2048,coarse?1024:2048);
    Object.assign(sun.shadow.camera,{left:-42,right:42,top:42,bottom:-42,near:10,far:700});
    sun.shadow.bias=-.0006;scene.add(sun);scene.add(sun.target);
    Object.assign(R3,{ok:true,scene,cam,hemi,sun,tex:makeTextures3D()});
    resize3D();
  }catch(e){R3.ok=false;}
}
function makeEnv(sky){
  try{
    const s=new THREE.Scene(),geo=new THREE.SphereGeometry(50,24,12),cols=[];
    const top=LIN(sky[0]),hor=LIN(sky[2]),gnd=LIN(R3.night?0x0b0d10:0x3b4636);
    const p=geo.attributes.position;
    for(let i=0;i<p.count;i++){const y=p.getY(i)/50;const c=y>0?hor.clone().lerp(top,Math.pow(y,.6)):hor.clone().lerp(gnd,Math.min(1,-y*4));cols.push(c.r,c.g,c.b);}
    geo.setAttribute("color",new THREE.Float32BufferAttribute(cols,3));
    s.add(new THREE.Mesh(geo,new THREE.MeshBasicMaterial({vertexColors:true,side:THREE.BackSide})));
    if(!R3.night){const sunM=new THREE.Mesh(new THREE.SphereGeometry(4,8,8),new THREE.MeshBasicMaterial({color:LIN(0xfff6e0).multiplyScalar(4)}));sunM.position.set(-25,35,15);s.add(sunM);}
    const pm=new THREE.PMREMGenerator(R3.r);const rt=pm.fromScene(s,.02);pm.dispose();
    return rt.texture;
  }catch(e){return null;}
}
function resize3D(){
  if(!R3.ok)return;
  R3.r.setSize(cw,ch,false);R3.cam.aspect=cw/Math.max(1,ch);R3.cam.updateProjectionMatrix();
}
/* camera FOVs are tuned for a 16:9 screen; on narrower screens (phones in portrait) keep that horizontal view
   by widening the vertical FOV, up to 100 degrees, so the track doesn't look narrow next to the car */
function fovFor(v){
  const a=R3.cam.aspect,ref=16/9;
  if(a>=ref)return v;
  const h=Math.atan(Math.tan(v*Math.PI/360)*ref);
  return Math.min(100,Math.atan(Math.tan(h)/a)*360/Math.PI);
}
/* ribbon between two lateral offsets (numbers or functions of the index) */
function strip(o1,y1,o2,y2,mat,mask,vLen,uMax,opts){
  const n=TR.n,X=TR.X,Y=TR.Y,NX=TR.NX,NY=TR.NY,pos=[],uv=[];
  const f1=typeof o1==="function"?o1:()=>o1,f2=typeof o2==="function"?o2:()=>o2;
  for(let i=0;i<n;i++){
    if(mask&&!mask[i])continue;
    const j=(i+1)%n,a1=f1(i),a2=f2(i),b1=f1(j),b2=f2(j);
    const ax1=X[i]+NX[i]*a1,az1=Y[i]+NY[i]*a1,ax2=X[i]+NX[i]*a2,az2=Y[i]+NY[i]*a2;
    const bx1=X[j]+NX[j]*b1,bz1=Y[j]+NY[j]*b1,bx2=X[j]+NX[j]*b2,bz2=Y[j]+NY[j]*b2;
    const v0=i*STEP/vLen,v1=(i+1)*STEP/vLen;
    const ei=TR.E[i],ej=TR.E[j];
    pos.push(ax1,y1+ei,az1,ax2,y2+ei,az2,bx2,y2+ej,bz2, ax1,y1+ei,az1,bx2,y2+ej,bz2,bx1,y1+ej,bz1);
    uv.push(0,v0,uMax,v0,uMax,v1, 0,v0,uMax,v1,0,v1);
  }
  return addGeo(pos,uv,mat,opts);
}
/* ribbon along a parametric path p in [p0,p1] (index units) with lateral function lat(p) */
function ribbonP(p0,p1,lat,w1,w2,y1,y2,mat,vLen,opts){
  const pos=[],uv=[],dp=.5;
  for(let p=p0;p<p1;p+=dp){
    const q=Math.min(p1,p+dp),la=lat(p),lb=lat(q);
    const[ax1,az1]=posF(p,la+w1),[ax2,az2]=posF(p,la+w2),[bx1,bz1]=posF(q,lb+w1),[bx2,bz2]=posF(q,lb+w2);
    const v0=p*STEP/vLen,v1=q*STEP/vLen;
    const ea=elevAt(p),eb=elevAt(q);
    pos.push(ax1,y1+ea,az1,ax2,y2+ea,az2,bx2,y2+eb,bz2, ax1,y1+ea,az1,bx2,y2+eb,bz2,bx1,y1+eb,bz1);
    uv.push(0,v0,1,v0,1,v1, 0,v0,1,v1,0,v1);
  }
  return addGeo(pos,uv,mat,opts);
}
function addGeo(pos,uv,mat,opts){
  const geo=new THREE.BufferGeometry();
  geo.setAttribute("position",new THREE.Float32BufferAttribute(pos,3));
  geo.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));
  geo.computeVertexNormals();
  const m=new THREE.Mesh(geo,mat);
  if(!opts||opts.receive!==false)m.receiveShadow=true;
  R3.world.add(m);return m;
}
function lam(o){return new THREE.MeshLambertMaterial(Object.assign({side:THREE.DoubleSide},o));}
function onTrackPoint(x,z,margin){return TR.nearest(x,z)<margin;}
function build3D(){
  if(!R3.ok||R3.trackId===TR.id)return;
  R3.trackId=TR.id;
  const S=R3.scene,X3=R3.tex;
  if(R3.world){S.remove(R3.world);R3.world.traverse(o=>{if(o.geometry)o.geometry.dispose();if(o.material&&o.material.dispose&&!o.material.keep)o.material.dispose();});}
  const world=new THREE.Group();R3.world=world;S.add(world);
  const W=TR.W,n=TR.n,night=TR.night,street=TR.street,wd=TR.wallD;R3.night=night;
  const sky=night?["#03050b","#0b1220","#1f2a40"]:["#3a74bd","#8fb7d9","#e6e1d3"];
  if(R3.skyTex)R3.skyTex.dispose();
  R3.skyTex=canvasTex(4,256,(g,w,h)=>{const gr=g.createLinearGradient(0,0,0,h);gr.addColorStop(0,sky[0]);gr.addColorStop(.62,sky[1]);gr.addColorStop(1,sky[2]);g.fillStyle=gr;g.fillRect(0,0,w,h);},true);
  S.background=R3.skyTex;
  S.fog=new THREE.Fog(LIN(sky[2]),night?220:420,night?1500:2800);
  R3.hemi.color.copy(LIN(night?0x9fb4dc:0xdfe9f7));R3.hemi.groundColor.copy(LIN(night?0x1c2430:0x4a5a3c));R3.hemi.intensity=night?1.0:.95;
  R3.sun.intensity=night?.45:1.7;R3.sun.color.copy(LIN(night?0xc8d4ff:0xfff1dc));
  if(R3.env)R3.env.dispose();R3.env=makeEnv(sky);
  const b=TR.bounds,cx=(b.minX+b.maxX)/2,cz=(b.minY+b.maxY)/2,size=Math.max(b.maxX-b.minX,b.maxY-b.minY)+3600;
  // terrain: hugs the real track elevation near the circuit and rolls away into the landscape
  const TER=buildTerrain(size,cx,cz,street,wd);R3.ter=TER;
  const ground=new THREE.Mesh(TER.geo,lam({map:street?X3.city:X3.grass,side:THREE.FrontSide}));ground.receiveShadow=true;world.add(ground);
  const tH=(x,z)=>terrainH(TER,x,z);
  // distant hills
  {const R=size*.47,seg=120,pos=[],cols=[],c1=LIN(night?0x0a0f18:0x6f8396),c2=LIN(night?0x05070b:0x4f6b55);
    for(let k=0;k<seg;k++){const a0=k/seg*Math.PI*2,a1=(k+1)/seg*Math.PI*2,h=i=>60+50*Math.sin(i*.21)+35*Math.sin(i*.53+1)+25*Math.sin(i*1.3);
      const p=(a,y)=>[cx+Math.cos(a)*R,y+TER.base,cz+Math.sin(a)*R];const A=p(a0,-5),B=p(a1,-5),C=p(a1,h(k+1)),D=p(a0,h(k));
      pos.push(...A,...B,...C,...A,...C,...D);for(let q=0;q<6;q++){const cc=q===2||q===4||q===5?c1:c2;cols.push(cc.r,cc.g,cc.b);}}
    const hg=new THREE.BufferGeometry();hg.setAttribute("position",new THREE.Float32BufferAttribute(pos,3));hg.setAttribute("color",new THREE.Float32BufferAttribute(cols,3));
    world.add(new THREE.Mesh(hg,new THREE.MeshBasicMaterial({vertexColors:true,side:THREE.DoubleSide,fog:false})));}
  // surfaces
  const off=(f)=>({polygonOffset:true,polygonOffsetFactor:f,polygonOffsetUnits:f});
  const asph=lam(Object.assign({map:X3.asphalt},off(-2)));
  if(street){strip(W/2-.1,.03,wd+.2,.03,asph,null,8,1);strip(-W/2+.1,.03,-wd-.2,.03,asph,null,8,1);}
  else{
    const ro=lam(Object.assign({map:X3.runoff},off(-1)));
    strip(W/2-.1,.02,W/2+5,.02,ro,null,8,1);strip(-W/2+.1,.02,-W/2-5,.02,ro,null,8,1);
    const gv=lam(Object.assign({map:X3.gravel},off(-1.5)));
    strip(W/2+1.1,.03,W/2+9,.03,gv,TR.gravP,6,2);strip(-W/2-1.1,.03,-W/2-9,.03,gv,TR.gravN,6,2);
  }
  strip(-W/2,.04,W/2,.04,asph,null,8,W/8);
  strip(i=>TR.RL[i]-1.3,.045,i=>TR.RL[i]+1.3,.045,new THREE.MeshBasicMaterial(Object.assign({color:0x000000,transparent:true,opacity:.16,depthWrite:false},off(-3))),null,8,1);
  const kerbM=lam(Object.assign({map:X3.kerb},off(-4)));
  strip(W/2-.2,.07,W/2+1.1,.05,kerbM,TR.kerb,2,1);strip(-W/2+.2,.07,-W/2-1.1,.05,kerbM,TR.kerb,2,1);
  const line=new THREE.MeshBasicMaterial(Object.assign({color:night?0xffffff:0xf2efe8,side:THREE.DoubleSide},off(-5)));
  strip(W/2-.55,.06,W/2-.25,.06,line,null,8,1);strip(-W/2+.55,.06,-W/2+.25,.06,line,null,8,1);
  // barriers, advertising and catch fences
  const bm=lam({map:street?X3.wall:X3.barrier}),bh=street?1.15:1.05;
  strip(wd,0,wd,bh,bm,TR.wallP,street?24:20,1);strip(-wd,0,-wd,bh,bm,TR.wallN,street?24:20,1);
  const fm=lam({map:X3.fence,transparent:true,alphaTest:.3,depthWrite:true});
  strip(wd+.05,bh,wd+.05,bh+3.6,fm,TR.wallP,4,1,{receive:false});strip(-wd-.05,bh,-wd-.05,bh+3.6,fm,TR.wallN,4,1,{receive:false});
  // pit lane, pit wall gaps, garages
  const lat0=TR.RL[((TR.PE0%n)+n)%n];
  const pl=p=>pitLatAt({p,lat0});
  ribbonP(TR.PE0,TR.PX1,pl,-3.6,3.6,.035,.035,asph,8);
  ribbonP(TR.PE0+4,TR.PX1-4,pl,TR.PS*3.4,TR.PS*3.6,.05,.05,line,8);
  ribbonP(TR.PE1,TR.PX0,pl,-TR.PS*3.4,-TR.PS*3.6,.05,.05,line,8);
  const boxM=new THREE.MeshBasicMaterial(Object.assign({color:0xffd21f},off(-5)));
  for(let gidx=0;gidx<10;gidx++){const bi=-44+gidx*6;ribbonP(bi-1,bi+1,p=>TR.pitLat+TR.PS*1.6,-.12,.12,.06,.06,boxM,8);ribbonP(bi-1,bi-.92,p=>TR.pitLat+TR.PS*1.6,-1.1,1.1,.06,.06,boxM,8);}
  const garM=lam({map:X3.garage}),roofM=lam({color:0xdfe1e5});
  ribbonP(TR.PE1+2,TR.PX0-2,p=>TR.pitLat+TR.PS*7.5,0,0,0,7.5,garM,48).geometry;
  {const m=ribbonP(TR.PE1+2,TR.PX0-2,p=>TR.pitLat,TR.PS*7.5,TR.PS*24,7.5,7.5,roofM,10);m.castShadow=false;}
  // start/finish line, grid slots, start-light gantry
  const th0=Math.atan2(TR.TY[0],TR.TX[0]);
  const sg=new THREE.PlaneGeometry(1.8,W);sg.rotateX(-Math.PI/2);
  const sl=new THREE.Mesh(sg,new THREE.MeshBasicMaterial(Object.assign({map:X3.check},off(-6))));
  sl.position.set(TR.X[0],.08+TR.E[0],TR.Y[0]);sl.rotation.y=-th0;world.add(sl);
  const gm=new THREE.MeshBasicMaterial(Object.assign({color:0xf2efe8},off(-6)));
  const gfront=new THREE.PlaneGeometry(.25,2.4);gfront.rotateX(-Math.PI/2);const gside=new THREE.PlaneGeometry(1.8,.2);gside.rotateX(-Math.PI/2);
  for(let s=0;s<10;s++){
    const p=gridPose(s),grp=new THREE.Group();grp.position.set(p.x,.08+TR.E[p.i],p.y);grp.rotation.y=-p.h;
    const f=new THREE.Mesh(gfront,gm);f.position.x=3.2;grp.add(f);
    for(const z of[-1.1,1.1]){const sd=new THREE.Mesh(gside,gm);sd.position.set(2.3,0,z);grp.add(sd);}
    world.add(grp);
  }
  {const gan=new THREE.Group();gan.position.set(TR.X[2],TR.E[2],TR.Y[2]);gan.rotation.y=-th0;
    const steel=lam({color:0x2b2f36});const span=W+5;
    for(const s of[-1,1]){const post=new THREE.Mesh(new THREE.BoxGeometry(.5,7.6,.5),steel);post.position.set(0,3.8,s*span/2);post.castShadow=true;gan.add(post);}
    const beam=new THREE.Mesh(new THREE.BoxGeometry(.7,.9,span+.6),steel);beam.position.set(0,7.3,0);beam.castShadow=true;gan.add(beam);
    R3.lamps=[];
    for(let k=0;k<5;k++){
      const pod=new THREE.Mesh(new THREE.BoxGeometry(.45,1.5,.6),lam({color:0x111317}));pod.position.set(-.1,6.2,(k-2)*.9);gan.add(pod);
      const mat=new THREE.MeshBasicMaterial({color:0x2a0b0b});R3.lamps.push(mat);
      for(const y of[6.6,6.05]){const l=new THREE.Mesh(new THREE.CircleGeometry(.19,16),mat);l.position.set(-.34,y,(k-2)*.9);l.rotation.y=-Math.PI/2;gan.add(l);}
    }
    world.add(gan);}
  // braking boards
  for(const bd of TR.boards){
    const i=bd.i,s=bd.side,o=s*(street?wd+.6:W/2+5.5);
    const x=TR.X[i]+TR.NX[i]*o,z=TR.Y[i]+TR.NY[i]*o;
    const bmesh=new THREE.Mesh(new THREE.PlaneGeometry(1.2,1.5),new THREE.MeshBasicMaterial({map:X3.boards[bd.k],side:THREE.DoubleSide}));
    bmesh.position.set(x,(street?2.0:1.6)+TR.E[i],z);bmesh.rotation.y=Math.atan2(-TR.TX[i],-TR.TY[i]);world.add(bmesh);
    if(!street){const post=new THREE.Mesh(new THREE.BoxGeometry(.1,1,.1),lam({color:0x888c92}));post.position.set(x,.5+TR.E[i],z);world.add(post);}
  }
  // marshal posts with digital light panels
  R3.posts=[];
  const postM=lam({color:0xf07a1a});
  for(let s=0;s<TR.MS;s++){
    const i=(Math.floor(s*n/TR.MS)+4)%n;
    let side=TR.wallN[i]?-1:TR.wallP[i]?1:-1;
    const o=side*(wd+2.4),x=TR.X[i]+TR.NX[i]*o,z=TR.Y[i]+TR.NY[i]*o;
    const hut=new THREE.Mesh(new THREE.BoxGeometry(1.4,2.3,1.4),postM);hut.position.set(x,1.15+TR.E[i],z);hut.castShadow=true;world.add(hut);
    const mat=new THREE.MeshBasicMaterial({color:0x111111});
    const pan=new THREE.Mesh(new THREE.PlaneGeometry(1.5,1.1),mat);
    pan.position.set(x-TR.TX[i]*.75,3.4+TR.E[i],z-TR.TY[i]*.75);pan.rotation.y=Math.atan2(-TR.TX[i],-TR.TY[i]);world.add(pan);
    const pole=new THREE.Mesh(new THREE.BoxGeometry(.12,1.2,.12),lam({color:0x555a60}));pole.position.set(x,2.6+TR.E[i],z);world.add(pole);
    R3.posts.push({s,mat,state:""});
  }
  // grandstands opposite the pits and at the main braking zones
  const crowd=lam({map:X3.crowd}),rail=lam({color:0x2a2e35}),roof=new THREE.MeshBasicMaterial({color:night?0x3a3f48:0xc4c8ce,side:THREE.DoubleSide});
  const stand=(mask,sgn)=>{strip(sgn*(wd+3),.8,sgn*(wd+16),9,crowd,mask,10,1);strip(sgn*(wd+3),0,sgn*(wd+3),.8,rail,mask,10,1);strip(sgn*(wd+3),12.5,sgn*(wd+17),13.5,roof,mask,10,1);};
  const mkMask=(from,to,sgn)=>{const m=new Uint8Array(n);let cnt=0;for(let k=from;k<=to;k++){const i=((k%n)+n)%n;const px=TR.X[i]+TR.NX[i]*sgn*(wd+18),pz=TR.Y[i]+TR.NY[i]*sgn*(wd+18);if(TR.nearest(px,pz)>wd+12&&(sgn>0?TR.wallP:TR.wallN)[i]){m[i]=1;cnt++;}}return cnt>8?m:null;};
  {const m=mkMask(-40,30,-TR.PS);if(m)stand(m,-TR.PS);}
  let stands=0;
  for(const a of TR.apexes){if(stands>=3)break;const sg2=TR.KC[a]>0?-1:1;const m=mkMask(a-30,a+5,sg2);if(m){stand(m,sg2);stands++;}}
  // scenery
  const dummy=new THREE.Object3D(),col=new THREE.Color();
  const bx0=b.minX-450,bx1=b.maxX+450,by0=b.minY-450,by1=b.maxY+450;
  const pitZone=(x,z)=>{for(let p=TR.PE0;p<=TR.PX1;p+=6){const[px,pz]=posF(p,TR.pitLat+TR.PS*12);if((px-x)**2+(pz-z)**2<35*35)return true;}return false;};
  if(street){
    const bg=new THREE.BoxGeometry(1,1,1);bg.translate(0,.5,0);
    const inst=new THREE.InstancedMesh(bg,night?new THREE.MeshBasicMaterial({map:X3.windowsNight}):lam({map:X3.windows,side:THREE.FrontSide}),560);let k=0;
    for(let a=0;a<9000&&k<560;a++){
      const w=rnd(12,40),d=rnd(12,40),h=rnd(10,80),x=rnd(bx0,bx1),z=rnd(by0,by1);
      const dist=TR.nearest(x,z);if(dist<wd+8+Math.max(w,d)*.72||dist>240||pitZone(x,z))continue;
      dummy.position.set(x,tH(x,z)-1,z);dummy.rotation.set(0,rnd(0,Math.PI),0);dummy.scale.set(w,h,d);dummy.updateMatrix();inst.setMatrixAt(k,dummy.matrix);
      col.setHSL(rnd(.55,.62),rnd(.03,.12),night?rnd(.7,1):rnd(.75,1));inst.setColorAt(k,col.convertSRGBToLinear());k++;
    }
    inst.count=k;world.add(inst);
  }else{
    const cone=new THREE.ConeGeometry(2.4,7,7);cone.translate(0,5.5,0);
    const ball=new THREE.IcosahedronGeometry(3.2,1);ball.translate(0,5.2,0);
    const trunk=new THREE.CylinderGeometry(.3,.4,2.4,5);trunk.translate(0,1.2,0);
    const N=1700,ti=new THREE.InstancedMesh(cone,lam({side:THREE.FrontSide}),N),bi=new THREE.InstancedMesh(ball,lam({side:THREE.FrontSide}),N),tt=new THREE.InstancedMesh(trunk,lam({color:0x4a3826,side:THREE.FrontSide}),N*2);
    let k=0,kb=0,kt=0;
    for(let a=0;a<14000&&k+kb<N;a++){
      const x=rnd(bx0,bx1),z=rnd(by0,by1),dist=TR.nearest(x,z);
      if(dist<wd+6||dist>420||pitZone(x,z))continue;
      const s=rnd(.7,1.5);dummy.position.set(x,tH(x,z)-.2,z);dummy.rotation.set(0,rnd(0,6),0);dummy.scale.set(s,s*rnd(.8,1.3),s);dummy.updateMatrix();
      tt.setMatrixAt(kt++,dummy.matrix);
      if(Math.random()<.55){ti.setMatrixAt(k,dummy.matrix);col.setHSL(rnd(.26,.33),rnd(.35,.55),night?rnd(.06,.12):rnd(.16,.27));ti.setColorAt(k,col.convertSRGBToLinear());k++;}
      else{bi.setMatrixAt(kb,dummy.matrix);col.setHSL(rnd(.2,.3),rnd(.35,.6),night?rnd(.07,.13):rnd(.22,.34));bi.setColorAt(kb,col.convertSRGBToLinear());kb++;}
    }
    ti.count=k;bi.count=kb;tt.count=kt;world.add(ti);world.add(bi);world.add(tt);
  }
  if(night){
    const pg=new THREE.BoxGeometry(.25,10,.25);pg.translate(0,5,0);const hg=new THREE.BoxGeometry(1.8,.3,.7);hg.translate(0,10,0);
    const N=Math.ceil(n/12),pi=new THREE.InstancedMesh(pg,lam({color:0x55595f,side:THREE.FrontSide}),N),hi=new THREE.InstancedMesh(hg,new THREE.MeshBasicMaterial({color:0xfff6dc}),N);let k=0;
    for(let i=0;i<n;i+=12){
      const s=(i/12)%2?1:-1,mask=s>0?TR.wallP:TR.wallN;if(!mask[i])continue;
      const o=s*(wd+1.2);dummy.position.set(TR.X[i]+TR.NX[i]*o,TR.E[i],TR.Y[i]+TR.NY[i]*o);dummy.rotation.set(0,-Math.atan2(TR.TY[i],TR.TX[i]),0);dummy.scale.set(1,1,1);dummy.updateMatrix();
      pi.setMatrixAt(k,dummy.matrix);hi.setMatrixAt(k,dummy.matrix);k++;
    }
    pi.count=hi.count=k;world.add(pi);world.add(hi);
  }
  if(!night){
    for(let k=0;k<18;k++){
      const sp=new THREE.Sprite(new THREE.SpriteMaterial({map:X3.cloud,fog:false,transparent:true,opacity:rnd(.5,.85),depthWrite:false}));
      const a=rnd(0,Math.PI*2),r=rnd(.22,.44)*size;sp.position.set(cx+Math.cos(a)*r,TER.base+rnd(280,560),cz+Math.sin(a)*r);
      const s=rnd(320,760);sp.scale.set(s,s*.42,1);world.add(sp);
    }
  }
  linTree(world);
}
function buildTerrain(size,cx,cz,street,wd){
  const N=street?150:190,x0=cx-size/2,z0=cz-size/2,cell=size/(N-1);
  const n=TR.n,X=TR.X,Y=TR.Y,E=TR.E,HC=40,hash=new Map();
  for(let i=0;i<n;i++){const k=Math.floor(X[i]/HC)+","+Math.floor(Y[i]/HC);let a=hash.get(k);if(!a){a=[];hash.set(k,a);}a.push(i);}
  const samp=[];for(let i=0;i<n;i+=10)samp.push(i);
  let mean=0;for(let i=0;i<n;i++)mean+=E[i];mean/=n;
  const H=new Float32Array(N*N),amp=street?3:(TR.hasElev?20:9),near2=(TR.W/2+18)**2;
  for(let iz=0;iz<N;iz++)for(let ix=0;ix<N;ix++){
    const x=x0+ix*cell,z=z0+iz*cell,hx=Math.floor(x/HC),hz=Math.floor(z/HC);
    let best=1e18,bi=-1,minNear=1e9;
    for(let dx=-4;dx<=4;dx++)for(let dz=-4;dz<=4;dz++){const a=hash.get((hx+dx)+","+(hz+dz));if(!a)continue;
      for(const i of a){const ddx=X[i]-x,ddz=Y[i]-z,d=ddx*ddx+ddz*ddz;if(d<best){best=d;bi=i;}if(d<near2&&E[i]<minNear)minNear=E[i];}}
    const d=bi<0?1e4:Math.sqrt(best);
    let sw=0,sh=0;for(const i of samp){const ddx=X[i]-x,ddz=Y[i]-z,w=1/(ddx*ddx+ddz*ddz+260*260);sw+=w;sh+=w*E[i];}
    const far=sh/sw,nx=x*.0021,nz=z*.0021;
    const noise=Math.sin(nx*1.7+Math.sin(nz*1.3))*.6+Math.sin(nz*2.3+nx*.7)*.4+Math.sin((nx+nz)*4.1)*.15;
    const hn=(bi<0?far:(minNear<1e8?minNear:E[bi]))-.3;
    H[iz*N+ix]=lerp(hn,far+noise*amp*smooth((d-120)/500)-.3,smooth((d-(wd+6))/150));
  }
  const geo=new THREE.PlaneGeometry(size,size,N-1,N-1);geo.rotateX(-Math.PI/2);
  const pos=geo.attributes.position,uv=geo.attributes.uv,rep=size/(street?20:26);
  for(let i=0;i<pos.count;i++){pos.setY(i,H[i]);uv.setXY(i,uv.getX(i)*rep,uv.getY(i)*rep);}
  geo.translate(cx,0,cz);geo.computeVertexNormals();
  return{geo,H,N,x0,z0,cell,base:mean};
}
function terrainH(T,x,z){
  const fx=clamp((x-T.x0)/T.cell,0,T.N-1.001),fz=clamp((z-T.z0)/T.cell,0,T.N-1.001),ix=Math.floor(fx),iz=Math.floor(fz),u=fx-ix,v=fz-iz,N=T.N,H=T.H;
  return lerp(lerp(H[iz*N+ix],H[iz*N+ix+1],u),lerp(H[(iz+1)*N+ix],H[(iz+1)*N+ix+1],u),v);
}
/* tyre smoke, lock-up puffs and gravel dust */
const PART={N:600,i:0};
function initParticles(){
  const N=PART.N,geo=new THREE.BufferGeometry();
  PART.pos=new Float32Array(N*3);PART.size=new Float32Array(N);PART.alpha=new Float32Array(N);PART.col=new Float32Array(N*3);
  PART.vel=new Float32Array(N*3);PART.age=new Float32Array(N).fill(9);PART.life=new Float32Array(N).fill(1);PART.grow=new Float32Array(N);
  geo.setAttribute("position",new THREE.BufferAttribute(PART.pos,3));geo.setAttribute("size",new THREE.BufferAttribute(PART.size,1));
  geo.setAttribute("alpha",new THREE.BufferAttribute(PART.alpha,1));geo.setAttribute("col",new THREE.BufferAttribute(PART.col,3));
  const mat=new THREE.ShaderMaterial({uniforms:{map:{value:R3.tex.smoke},scale:{value:600}},transparent:true,depthWrite:false,
    vertexShader:"uniform float scale;attribute float size;attribute float alpha;attribute vec3 col;varying float vA;varying vec3 vC;void main(){vA=alpha;vC=col;vec4 mv=modelViewMatrix*vec4(position,1.0);gl_PointSize=size*scale/max(.1,-mv.z);gl_Position=projectionMatrix*mv;}",
    fragmentShader:"uniform sampler2D map;varying float vA;varying vec3 vC;void main(){vec4 t=texture2D(map,gl_PointCoord);gl_FragColor=vec4(vC,t.a*vA);}"});
  PART.pts=new THREE.Points(geo,mat);PART.pts.frustumCulled=false;R3.scene.add(PART.pts);PART.geo=geo;
}
function emitP(x,y,z,vx,vy,vz,size,r,g,b,life,grow){
  const i=PART.i=(PART.i+1)%PART.N;
  PART.pos.set([x,y,z],i*3);PART.vel.set([vx,vy,vz],i*3);PART.col.set([r,g,b],i*3);
  PART.size[i]=size;PART.age[i]=0;PART.life[i]=life;PART.grow[i]=grow;
}
function stepParticles(dt){
  const N=PART.N;
  for(let i=0;i<N;i++){
    if(PART.age[i]>=PART.life[i]){PART.alpha[i]=0;continue;}
    PART.age[i]+=dt;const k=i*3,f=PART.age[i]/PART.life[i];
    PART.pos[k]+=PART.vel[k]*dt;PART.pos[k+1]+=PART.vel[k+1]*dt;PART.pos[k+2]+=PART.vel[k+2]*dt;
    PART.vel[k]*=.96;PART.vel[k+2]*=.96;PART.size[i]+=PART.grow[i]*dt;
    PART.alpha[i]=(1-f)*Math.min(1,PART.age[i]*8)*.55;
  }
  PART.uniformScale=ch;PART.pts.material.uniforms.scale.value=ch*.9;
  PART.geo.attributes.position.needsUpdate=true;PART.geo.attributes.size.needsUpdate=true;PART.geo.attributes.alpha.needsUpdate=true;PART.geo.attributes.col.needsUpdate=true;
}
function carEffects(c,y){
  if(c.hidden||c.retired&&!c.spin)return;
  const v=Math.abs(c.vf||0),ch_=Math.cos(c.h),sh_=Math.sin(c.h);
  const at=(fx,fz)=>[c.x+ch_*fx-sh_*fz,c.y+sh_*fx+ch_*fz];
  const lock=c.brk>.9&&v>26&&!c.pit,slide=Math.abs(c.vl||0)>3.2||c.spin,launch=G.started&&c.thr>.9&&v<14&&v>1&&G.time-G.raceStart<2.5;
  if((lock||slide||launch)&&Math.random()<.55){
    const front=lock&&!slide;const xs=front?1.55:-1.8;
    for(const s of[-.85,.85]){const[x,z]=at(xs,s);emitP(x,y+.25,z,c.vx*.25+rnd(-.5,.5),rnd(.3,.9),c.vy*.25+rnd(-.5,.5),slide?1.4:1,.86,.86,.88,slide?2.2:1.3,slide?2.6:1.6);}
  }
  if(c.surf>=2&&v>7&&Math.random()<.7){
    const grav=c.surf===3;for(const s of[-.85,.85]){const[x,z]=at(-1.9,s);emitP(x,y+.2,z,c.vx*.3+rnd(-1,1),rnd(.8,2),c.vy*.3+rnd(-1,1),grav?1.3:.9,grav?.72:.42,grav?.64:.5,grav?.5:.3,grav?1.8:1,grav?2.2:1.2);}
  }
}

/* ---------------- Car models ---------------- */
function loftGeo(secs,seg,pw){
  seg=seg||18;pw=pw||3;
  const ring=[];for(let k=0;k<seg;k++){const a=k/seg*Math.PI*2,c=Math.cos(a),s=Math.sin(a);ring.push([Math.sign(c)*Math.pow(Math.abs(c),2/pw),Math.sign(s)*Math.pow(Math.abs(s),2/pw)]);}
  const pos=[],idx=[];
  for(const[x,w,h,yc,zc]of secs)for(const[u,v]of ring)pos.push(x,yc+v*h/2,(zc||0)+u*w/2);
  for(let i=0;i<secs.length-1;i++)for(let k=0;k<seg;k++){const a=i*seg+k,b=i*seg+(k+1)%seg,c=(i+1)*seg+k,d=(i+1)*seg+(k+1)%seg;idx.push(a,c,b,b,c,d);}
  const L=secs.length-1,cs=pos.length/3;pos.push(secs[0][0],secs[0][3],secs[0][4]||0,secs[L][0],secs[L][3],secs[L][4]||0);
  for(let k=0;k<seg;k++){idx.push(cs,k,(k+1)%seg);idx.push(cs+1,L*seg+(k+1)%seg,L*seg+k);}
  const g=new THREE.BufferGeometry();g.setAttribute("position",new THREE.Float32BufferAttribute(pos,3));g.setIndex(idx);g.computeVertexNormals();
  return g;
}
function mergeGeos(list){
  const gs=list.map(g=>g.index?g.toNonIndexed():g);let total=0;for(const g of gs)total+=g.attributes.position.count;
  const pos=new Float32Array(total*3),nor=new Float32Array(total*3);let o=0;
  for(const g of gs){pos.set(g.attributes.position.array,o*3);nor.set(g.attributes.normal.array,o*3);o+=g.attributes.position.count;}
  const r=new THREE.BufferGeometry();r.setAttribute("position",new THREE.BufferAttribute(pos,3));r.setAttribute("normal",new THREE.BufferAttribute(nor,3));
  return r;
}
function place(g,x,y,z,rx,ry,rz){
  const m=new THREE.Matrix4().compose(new THREE.Vector3(x,y,z),new THREE.Quaternion().setFromEuler(new THREE.Euler(rx||0,ry||0,rz||0)),new THREE.Vector3(1,1,1));
  g.applyMatrix4(m);return g;
}
function rodGeo(a,b,r){
  const A=new THREE.Vector3(...a),B=new THREE.Vector3(...b),len=A.distanceTo(B);
  const g=new THREE.CylinderGeometry(r,r,len,5);
  const q=new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0,1,0),B.clone().sub(A).normalize());
  g.applyMatrix4(new THREE.Matrix4().compose(A.clone().add(B).multiplyScalar(.5),q,new THREE.Vector3(1,1,1)));
  return g;
}
const SHARED={};
function sharedGeo(){
  if(SHARED.tyreF)return SHARED;
  const prof=(R,w)=>{const hw=w/2,pts=[[.235,-hw],[.30,-hw],[R-.02,-hw+.02],[R,-hw+.06],[R,hw-.06],[R-.02,hw-.02],[.30,hw],[.235,hw]].map(([x,y])=>new THREE.Vector2(x,y));const g=new THREE.LatheGeometry(pts,26);g.rotateX(Math.PI/2);return g;};
  SHARED.tyreF=prof(.36,.37);SHARED.tyreR=prof(.38,.45);
  SHARED.rimF=new THREE.CylinderGeometry(.235,.235,.33,18);SHARED.rimF.rotateX(Math.PI/2);
  SHARED.rimR=new THREE.CylinderGeometry(.235,.235,.41,18);SHARED.rimR.rotateX(Math.PI/2);
  SHARED.band=new THREE.TorusGeometry(.305,.014,5,36);
  return SHARED;
}
function stdMat(color,rough,metal){return new THREE.MeshStandardMaterial({color,roughness:rough,metalness:metal,envMap:R3.env||null,envMapIntensity:.9});}
function carMesh(color,accent,me,ghost){
  const g=new THREE.Group(),SG=sharedGeo();
  const mk=(c,r,m)=>ghost?new THREE.MeshBasicMaterial({color:0xb65cff,transparent:true,opacity:.3,depthWrite:false}):stdMat(c,r,m);
  const paint=mk(color,.3,.45),acc=mk(accent,.35,.35),carbon=mk(0x17191c,.55,.25),tyreM=mk(0x151515,.92,0);
  const P=[],A=[],C=[];
  // monocoque, nose and engine cover
  P.push(loftGeo([[3.02,.09,.07,.26],[2.78,.2,.13,.28],[2.2,.3,.21,.32],[1.5,.42,.3,.39],[.95,.64,.42,.45],[.4,.8,.46,.48],[-.35,.84,.48,.48],[-.85,.8,.66,.56],[-1.5,.62,.56,.55],[-2.1,.4,.4,.48],[-2.48,.26,.28,.44]],20,3));
  // sidepods
  for(const s of[-1,1])P.push(loftGeo([[.42,.08,.28,.36,.62*s],[.22,.42,.44,.38,.6*s],[-.5,.46,.46,.38,.58*s],[-1.2,.34,.38,.35,.5*s],[-1.9,.14,.24,.32,.36*s]],16,3.4));
  // airbox / roll hoop and engine fin
  C.push(loftGeo([[-.22,.18,.12,.88],[-.42,.34,.3,.9],[-.9,.3,.32,.84],[-1.6,.1,.22,.7],[-2.25,.04,.14,.6]],14,2.6));
  A.push(place(new THREE.BoxGeometry(.18,.06,.07),-.45,1.07,0));
  // floor and plank
  C.push(place(new THREE.BoxGeometry(3.9,.045,1.5),-.45,.1,0));C.push(place(new THREE.BoxGeometry(3.7,.05,.3),-.45,.07,0));
  // front wing main plane and endplates
  C.push(place(new THREE.BoxGeometry(.34,.035,1.98),2.88,.11,0));
  for(const s of[-1,1]){A.push(place(new THREE.BoxGeometry(.6,.27,.03),2.78,.2,s*.99));}
  // rear wing endplates, beam wing, pylon
  for(const s of[-1,1])A.push(place(new THREE.BoxGeometry(.72,.6,.03),-2.5,.82,s*.52));
  C.push(place(new THREE.BoxGeometry(.28,.04,1.02),-2.56,.88,0));
  C.push(place(new THREE.BoxGeometry(.2,.03,.82),-2.42,.44,0));C.push(place(new THREE.BoxGeometry(.08,.42,.05),-2.5,.66,0));
  // halo
  {const hoop=new THREE.TorusGeometry(.5,.022,6,22,Math.PI);hoop.rotateZ(-Math.PI/2);hoop.rotateX(Math.PI/2);C.push(place(hoop,.05,1.17,0));
   C.push(place(new THREE.BoxGeometry(.03,.52,.045),.6,.92,0,0,0,.28));
   for(const s of[-1,1])C.push(rodGeo([-.42,1.13,s*.48],[-.55,.72,s*.4],.022));}
  // mirrors
  for(const s of[-1,1]){C.push(rodGeo([.48,.6,s*.36],[.5,.7,s*.54],.01));P.push(place(new THREE.BoxGeometry(.05,.055,.13),.52,.71,s*.58));}
  // suspension
  for(const s of[-1,1]){
    C.push(rodGeo([1.25,.47,s*.18],[1.55,.46,s*.7],.016),rodGeo([1.85,.45,s*.18],[1.55,.46,s*.7],.016),rodGeo([1.3,.3,s*.2],[1.55,.28,s*.72],.016),rodGeo([1.8,.29,s*.18],[1.55,.28,s*.72],.016));
    C.push(rodGeo([-1.45,.5,s*.3],[-1.8,.45,s*.66],.016),rodGeo([-2.05,.48,s*.2],[-1.8,.45,s*.66],.016),rodGeo([-1.5,.3,s*.3],[-1.8,.3,s*.66],.016));
  }
  if(!ghost)for(const s of[-1,1]){const gl=new THREE.Mesh(new THREE.PlaneGeometry(.115,.045),new THREE.MeshBasicMaterial({color:0x9fb3c8}));gl.position.set(.494,.71,s*.58);gl.rotation.y=-Math.PI/2;g.add(gl);}
  const add=(geos,mat)=>{const m=new THREE.Mesh(mergeGeos(geos),mat);m.castShadow=!ghost;g.add(m);return m;};
  add(P,paint);add(A,acc);add(C,carbon);
  // active-aero flaps
  const fFlap=new THREE.Group();fFlap.position.set(2.72,.19,0);const ff=new THREE.Mesh(new THREE.BoxGeometry(.22,.03,1.78),paint);ff.position.x=-.1;ff.castShadow=!ghost;fFlap.add(ff);g.add(fFlap);
  const rFlap=new THREE.Group();rFlap.position.set(-2.42,1.0,0);const rf=new THREE.Mesh(new THREE.BoxGeometry(.24,.04,1.02),acc);rf.position.x=-.12;rf.castShadow=!ghost;rFlap.add(rf);g.add(rFlap);
  // driver
  const helmet=new THREE.Group();helmet.position.set(-.05,.8,0);
  helmet.add(new THREE.Mesh(new THREE.SphereGeometry(.165,14,12),mk(me?0xffffff:accent,.25,.3)));
  const visor=new THREE.Mesh(new THREE.SphereGeometry(.168,14,8,-.9,1.8,1.2,.45),mk(0x0b0d10,.1,.6));visor.rotation.y=Math.PI/2;helmet.add(visor);
  g.add(helmet);
  // wheels
  const bandM=ghost?tyreM:new THREE.MeshBasicMaterial({color:0xffd21f});
  const rimMat=ghost?tyreM:[stdMat(0x2b2e33,.4,.6),new THREE.MeshStandardMaterial({map:R3.tex.rim,roughness:.4,metalness:.5}),new THREE.MeshStandardMaterial({map:R3.tex.rim,roughness:.4,metalness:.5})];
  const fronts=[],spins=[];
  const wheel=(x,y,z,front)=>{
    const piv=new THREE.Group();piv.position.set(x,y,z);
    const spin=new THREE.Group();piv.add(spin);
    const ty=new THREE.Mesh(front?SG.tyreF:SG.tyreR,tyreM);ty.castShadow=!ghost;spin.add(ty);
    spin.add(new THREE.Mesh(front?SG.rimF:SG.rimR,rimMat));
    const hw=(front?.37:.45)/2+.003;
    for(const s of[-1,1]){const bd=new THREE.Mesh(SG.band,bandM);bd.position.z=s*hw;piv.add(bd);}
    g.add(piv);spins.push(spin);if(front)fronts.push(piv);
  };
  for(const s of[-1,1]){wheel(1.55,.36,.86*s,true);wheel(-1.8,.38,.84*s,false);}
  // rain/ERS light
  const lightM=new THREE.MeshBasicMaterial({color:0x330000});
  const rl=new THREE.Mesh(new THREE.BoxGeometry(.04,.08,.14),lightM);rl.position.set(-2.63,.5,0);g.add(rl);
  let sw=null,screen=null;
  if(me&&!ghost){
    sw=new THREE.Group();sw.position.set(.4,.72,0);
    sw.add(new THREE.Mesh(new THREE.BoxGeometry(.04,.13,.27),carbon));
    const sc=document.createElement("canvas");sc.width=160;sc.height=80;screen={c:sc,g:sc.getContext("2d"),t:new THREE.CanvasTexture(sc)};
    const scr=new THREE.Mesh(new THREE.PlaneGeometry(.11,.055),new THREE.MeshBasicMaterial({map:screen.t}));scr.position.set(-.022,.005,0);scr.rotation.y=-Math.PI/2;sw.add(scr);
    for(const s of[-1,1]){const grip=new THREE.Mesh(new THREE.BoxGeometry(.06,.14,.05),mk(0x2a2c30,.8,0));grip.position.set(0,-.02,.135*s);sw.add(grip);}
    const leds=new THREE.Mesh(new THREE.BoxGeometry(.01,.012,.16),new THREE.MeshBasicMaterial({color:0x22ff66}));leds.position.set(-.025,.05,0);sw.add(leds);sw.userData.leds=leds;
    g.add(sw);
  }
  linTree(g);
  return{g,fronts,spins,helmet,sw,screen,fFlap,rFlap,bandM,lightM,paint};
}
function scMesh(){
  const g=new THREE.Group(),silver=stdMat(0xc8ccd2,.25,.7),glass=stdMat(0x0d1014,.08,.6),dark=stdMat(0x18191c,.6,.2),tyre=stdMat(0x141414,.9,0);
  const body=new THREE.Mesh(loftGeo([[2.35,1.5,.3,.5],[2.0,1.85,.5,.55],[1.0,1.95,.62,.6],[.3,1.95,.75,.66],[-.2,1.85,1.1,.78],[-1.3,1.82,1.08,.78],[-1.95,1.9,.72,.66],[-2.35,1.8,.5,.6]],22,4),silver);body.castShadow=true;g.add(body);
  const cabin=new THREE.Mesh(loftGeo([[.25,1.6,.02,1.05],[0,1.7,.42,1.13],[-1.2,1.66,.42,1.13],[-1.75,1.55,.02,1.0]],16,3),glass);g.add(cabin);
  const stripe=new THREE.Mesh(new THREE.BoxGeometry(4.6,.08,1.96),new THREE.MeshBasicMaterial({color:0x00a19b}));stripe.position.set(0,.62,0);g.add(stripe);
  const bar=new THREE.Mesh(new THREE.BoxGeometry(.3,.1,1.25),dark);bar.position.set(-.55,1.4,0);g.add(bar);
  const lampA=new THREE.MeshBasicMaterial({color:0xffb000}),lampB=new THREE.MeshBasicMaterial({color:0x22cc66});
  for(let k=0;k<4;k++){const l=new THREE.Mesh(new THREE.BoxGeometry(.31,.08,.22),k%2?lampB:lampA);l.position.set(-.55,1.46,-.45+k*.3);g.add(l);}
  const wg=new THREE.CylinderGeometry(.34,.34,.28,18);wg.rotateX(Math.PI/2);
  for(const x of[1.45,-1.45])for(const s of[-1,1]){const w=new THREE.Mesh(wg,tyre);w.position.set(x,.34,s*.86);g.add(w);}
  linTree(g);
  return{g,lampA,lampB};
}
function disposeGroup(grp){grp.traverse(o=>{if(o.geometry&&!Object.values(SHARED).includes(o.geometry))o.geometry.dispose();if(o.material){const ms=Array.isArray(o.material)?o.material:[o.material];ms.forEach(m=>m.dispose());}});}
function reset3DCars(){
  if(!R3.ok)return;
  for(const m of R3.cars.values()){R3.scene.remove(m.g);disposeGroup(m.g);}
  R3.cars.clear();
  if(R3.ghost){R3.scene.remove(R3.ghost.g);disposeGroup(R3.ghost.g);R3.ghost=null;}
  if(R3.scm){R3.scm.g.visible=false;}
  R3.chase=null;
}
function drawWheelScreen(m,c){
  const S=m.screen,g=S.g,w=160,h=80;
  g.fillStyle="#05070a";g.fillRect(0,0,w,h);
  g.fillStyle="#ffffff";g.font="bold 40px Arial, sans-serif";g.textAlign="center";g.textBaseline="middle";
  g.fillText($("#gear").textContent,w/2,h/2-4);
  g.font="bold 15px Arial, sans-serif";g.textAlign="left";g.fillText(Math.round(Math.abs(c.vf)*3.6),6,14);
  g.textAlign="right";g.fillStyle=TYRES[c.tyre].color;g.fillText(c.tyre+" "+Math.round((1-Math.min(1,c.wear))*100)+"%",w-6,14);
  const f=c.soc/ERS.CAP;g.fillStyle="#1a2230";g.fillRect(6,h-14,w-12,8);g.fillStyle=c.otActive?"#2fd673":c.boost?"#ffd21f":"#3aa0ff";g.fillRect(6,h-14,(w-12)*Math.min(1,f),8);
  g.fillStyle=c.aero==="straight"?"#2fd673":"#555";g.fillRect(6,h-26,10,8);
  S.t.needsUpdate=true;
}
function sync3D(dt){
  build3D();R3.frame++;
  const n=TR.n;
  for(const c of G.cars){
    let m=R3.cars.get(c);
    if(!m){m=carMesh(c.color,c.accent||"#16181c",!!c.me,false);R3.scene.add(m.g);R3.cars.set(c,m);m.tyre=null;}
    m.g.visible=!c.hidden;if(c.hidden)continue;
    const cy=elevAt(c.pf);c._y=cy;
    m.g.position.set(c.x,cy,c.y);m.g.rotation.order="YZX";
    m.g.rotation.set(0,-c.h,Math.atan(gradeAt(c.pf)*Math.cos(c.h-Math.atan2(TR.TY[c.idx],TR.TX[c.idx])))-(c.brk||0)*.006);
    carEffects(c,cy);
    for(const f of m.fronts)f.rotation.y=-c.steer*.35;
    const spin=(c.vf||0)*dt/.37;for(const s of m.spins)s.rotation.z-=spin;
    if(m.tyre!==c.tyre){m.tyre=c.tyre;setC(m.bandM,TYRES[c.tyre].color);}
    const open=c.aero==="straight";
    m.fFlap.rotation.z+=((open?-.28:0)-m.fFlap.rotation.z)*Math.min(1,dt*10);
    m.rFlap.rotation.z+=((open?.55:0)-m.rFlap.rotation.z)*Math.min(1,dt*10);
    const flash=(c.harv>0&&c.thr>.9)||c.pit;setC(m.lightM,flash&&Math.floor(G.time*6)%2?0xff2a1a:0x330000);
    if(m.sw){m.sw.rotation.x=-c.steer*1.4;if(R3.frame%6===0)drawWheelScreen(m,c);
      const rpmF=clamp((Math.abs(c.vf)%10)/10,0,1);setC(m.sw.userData.leds.material,rpmF>.85?0xff3344:rpmF>.6?0xffd21f:0x22ff66);}
    m.helmet.visible=!(c===G.camCar&&G.cam==="cockpit"&&G.mode!=="attract");
  }
  // ghost
  let gv=false;
  if(G.mode==="tt"&&G.player&&G.player.maxLaps>=0&&G.rec&&G.rec.ghost){
    const gh=G.rec.ghost,f=(G.time-G.player.lapStart)*20,i=Math.floor(f),fr=f-i;
    if((i+1)*3+2<gh.length){
      if(!R3.ghost){R3.ghost=carMesh(0,0,false,true);R3.scene.add(R3.ghost.g);}
      const a=i*3,b=a+3;
      const gx=gh[a]+(gh[b]-gh[a])*fr,gz=gh[a+1]+(gh[b+1]-gh[a+1])*fr;R3.gIdx=nearIdx(gx,gz,R3.gIdx||0);
      R3.ghost.g.position.set(gx,TR.E[R3.gIdx],gz);
      R3.ghost.g.rotation.y=-(gh[a+2]+wrapA(gh[b+2]-gh[a+2])*fr);gv=true;
    }
  }
  if(R3.ghost)R3.ghost.g.visible=gv;
  // safety car
  if(G.sc&&!G.sc.gone){
    if(!R3.scm){R3.scm=scMesh();R3.scene.add(R3.scm.g);}
    const S=G.sc;R3.scm.g.visible=true;R3.scm.g.position.set(S.x,elevAt(S.pf||0),S.y);R3.scm.g.rotation.y=-S.h;
    const on=S.lights&&Math.floor(G.time*5)%2;setC(R3.scm.lampA,S.lights?(on?0xffb000:0x442c00):0x222222);setC(R3.scm.lampB,S.lights?0x222222:0x22cc66);
  }else if(R3.scm)R3.scm.g.visible=false;
  // start lights
  if(R3.lamps){
    const lit=G.mode==="race"&&!G.started?Math.min(5,Math.floor(G.time-.4)):0;
    R3.lamps.forEach((m,k)=>setC(m,k<lit?0xff1a0a:0x2a0b0b));
  }
  // marshal light panels
  if(R3.posts){
    const N=G.neutral,blink=Math.floor(G.time*3)%2;
    for(const P of R3.posts){
      const y=G.yel[P.s],lvl=y&&y.until>G.time?y.lvl:0;
      let st="off";
      if(N)st=N.type==="SC"?"SC":"VSC";
      else if(lvl===2)st=blink?"y":"off";
      else if(lvl===1)st="y";
      else if(G.time<G.greenUntil)st="g";
      if(st!==P.state){P.state=st;
        if(st==="SC"||st==="VSC"){P.mat.map=R3.tex.panel[st];setC(P.mat,0xffffff);}
        else{P.mat.map=null;setC(P.mat,st==="y"?0xffd21f:st==="g"?0x22dd55:0x111111);}
        P.mat.needsUpdate=true;}
    }
  }
  // camera
  const c=G.camCar,cam=R3.cam,v=Math.hypot(c.vx,c.vy),fx=Math.cos(c.h),fz=Math.sin(c.h),cy=c._y??elevAt(c.pf);
  if(!PART.pts)initParticles();stepParticles(dt);
  const mode=G.mode==="attract"?"chase":G.cam;
  if(mode==="cockpit"){
    const yawRate=c.steer*Math.min(CAR.YAWMAX,CAR.LAT/Math.max(Math.abs(c.vf),6))*Math.min(1,Math.abs(c.vf)/3);
    const lat=clamp(c.vf*yawRate/45,-1,1);
    R3.roll+=(lat*.035-R3.roll)*Math.min(1,dt*6);
    const shake=reduceMotion?0:(Math.random()-.5)*.006*Math.min(1,v/80)*(c.surf?3:1);
    const bt=-clamp(TR.KV[c.idx]*v*v*.004,-.07,.07);R3.bounce=(R3.bounce||0)+(bt-(R3.bounce||0))*Math.min(1,dt*8);
    R3.pitch=(R3.pitch||0)+(((c.brk||0)*.018-(c.thr||0)*.006)-(R3.pitch||0))*Math.min(1,dt*5);
    cam.position.set(c.x-fx*.1,cy+1.06+shake+R3.bounce,c.y-fz*.1);
    cam.rotation.set(-.075-R3.pitch+Math.atan(gradeAt(c.pf)*Math.cos(c.h-Math.atan2(TR.TY[c.idx],TR.TX[c.idx])))*.9,-c.h-Math.PI/2,R3.roll);
    cam.fov=fovFor(74+Math.min(12,v*.13));
  }else{
    const tx=c.x-fx*8,tz=c.y-fz*8,ty=cy+2.6;
    if(!R3.chase||Math.hypot(R3.chase.x-tx,R3.chase.z-tz)>40)R3.chase={x:tx,y:ty,z:tz};
    const k=Math.min(1,dt*7);R3.chase.x+=(tx-R3.chase.x)*k;R3.chase.y+=(ty-R3.chase.y)*k;R3.chase.z+=(tz-R3.chase.z)*k;
    cam.position.set(R3.chase.x,R3.chase.y,R3.chase.z);
    cam.lookAt(c.x+fx*6,cy+1.0,c.y+fz*6);
    cam.fov=fovFor(62+Math.min(10,v*.1));
  }
  cam.updateProjectionMatrix();
  // shadow camera follows the viewed car
  R3.sun.position.set(c.x-140,cy+260,c.y+90);R3.sun.target.position.set(c.x+fx*15,cy,c.y+fz*15);R3.sun.target.updateMatrixWorld();
  R3.r.render(R3.scene,cam);
}
function nearIdx(x,y,start){const n=TR.n;let best=1e18,bi=start;for(let k=-20;k<=60;k++){const i=(start+k+n)%n,dx=x-TR.X[i],dy=y-TR.Y[i],d=dx*dx+dy*dy;if(d<best){best=d;bi=i;}}if(best>2500){for(let i=0;i<n;i+=3){const dx=x-TR.X[i],dy=y-TR.Y[i],d=dx*dx+dy*dy;if(d<best){best=d;bi=i;}}}return bi;}
function setViewCanvas(){
  const three=use3D();
  $("#three").hidden=!three;$("#game").hidden=three;
}
const CAM_LABEL={cockpit:"Cockpit view",chase:"Chase view",top:"Top-down view"};
function setCam(m,announce){
  if(!R3.ok&&m!=="top")m="top";
  G.cam=m;store.set("loa:cam",m);
  const r=document.querySelector(`input[name=cam][value=${m}]`);if(r)r.checked=true;
  setViewCanvas();
  if(announce)toast(CAM_LABEL[m],"yellow",900);
}
