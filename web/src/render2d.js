/* ---------------- Canvas & top-down renderer ---------------- */
const cv=$("#game"),ctx=cv.getContext("2d");
const mini=$("#mini"),mctx=mini.getContext("2d");
let cw=0,ch=0,dpr=1;
function resize(){
  dpr=Math.min(2,window.devicePixelRatio||1);
  cw=window.innerWidth;ch=window.innerHeight;
  cv.width=Math.round(cw*dpr);cv.height=Math.round(ch*dpr);
  const r=mini.getBoundingClientRect();
  mini.width=Math.round((r.width||170)*dpr);mini.height=Math.round((r.height||170)*dpr);
  if(typeof R3!=="undefined")resize3D();
  fitMini();
}
window.addEventListener("resize",resize);
function makePattern(base,speck){
  const c=document.createElement("canvas");c.width=c.height=64;const g=c.getContext("2d");
  g.fillStyle=base;g.fillRect(0,0,64,64);
  let seed=7;const r=()=>(seed=(seed*16807)%2147483647)/2147483647;
  for(let i=0;i<220;i++){g.fillStyle=speck[i%speck.length];g.fillRect(r()*64,r()*64,1+r()*1.5,1+r()*1.5);}
  const p=ctx.createPattern(c,"repeat");
  try{p.setTransform(new DOMMatrix([.14,0,0,.14,0,0]));}catch(e){}
  return p;
}
const GRASS=makePattern("#1c3221",["#23402a","#182b1c","#26442c"]);
const CITY=makePattern("#1d1f23",["#24272c","#191b1e","#2a2d32"]);
function track2D(){
  if(TR.p2d)return TR.p2d;
  const n=TR.n,W=TR.W,P={};
  const seg=(mask,o1)=>{const p=new Path2D();let on=false;for(let k=0;k<=n;k++){const i=k%n;if(mask[i]){const x=TR.X[i]+TR.NX[i]*o1,y=TR.Y[i]+TR.NY[i]*o1;if(on)p.lineTo(x,y);else{p.moveTo(x,y);on=true;}}else on=false;}return p;};
  P.gravP=seg(TR.gravP,W/2+5);P.gravN=seg(TR.gravN,-(W/2+5));
  const pl=new Path2D();
  for(let p=TR.PE0;p<=TR.PX1;p+=.5){const[x,y]=posF(p,pitLatAt({p,lat0:TR.RL[((Math.floor(TR.PE0)%n)+n)%n]}));p===TR.PE0?pl.moveTo(x,y):pl.lineTo(x,y);}
  P.pit=pl;
  TR.p2d=P;return P;
}
function drawCar2D(c,alpha){
  ctx.save();ctx.translate(c.x,c.y);ctx.rotate(c.h);ctx.globalAlpha=alpha;
  ctx.fillStyle="rgba(0,0,0,.35)";ctx.fillRect(-2.5,-.75,5.4,1.9);
  ctx.fillStyle=c.tyreColor||"#0c0c0d";
  ctx.fillRect(-2.25,-1.0,.95,.42);ctx.fillRect(-2.25,.58,.95,.42);
  for(const s of[-1,1]){ctx.save();ctx.translate(1.55,s*.86);ctx.rotate((c.steer||0)*.35);ctx.fillRect(-.37,-.19,.74,.38);ctx.restore();}
  ctx.fillStyle=c.color;
  ctx.beginPath();
  ctx.moveTo(2.75,-.13);ctx.lineTo(2.75,.13);ctx.lineTo(1.1,.32);ctx.lineTo(.4,.42);ctx.lineTo(-.2,.72);ctx.lineTo(-1.5,.66);ctx.lineTo(-2.05,.34);ctx.lineTo(-2.3,.3);
  ctx.lineTo(-2.3,-.3);ctx.lineTo(-2.05,-.34);ctx.lineTo(-1.5,-.66);ctx.lineTo(-.2,-.72);ctx.lineTo(.4,-.42);ctx.lineTo(1.1,-.32);ctx.closePath();ctx.fill();
  ctx.fillRect(2.35,-.95,.36,1.9);ctx.fillRect(-2.75,-.72,.42,1.44);
  ctx.fillStyle="#0c0c0d";ctx.beginPath();ctx.ellipse(.1,0,.55,.27,0,0,Math.PI*2);ctx.fill();
  ctx.fillStyle=c.me?"#fff":"#1b1b1d";ctx.beginPath();ctx.arc(-.05,0,.17,0,Math.PI*2);ctx.fill();
  if(c.brk>.3&&c.vf>1){ctx.fillStyle="#ff2a1a";ctx.fillRect(-2.85,-.12,.12,.24);}
  ctx.restore();
}
function draw(){
  ctx.setTransform(dpr,0,0,dpr,0,0);
  ctx.fillStyle=TR.street?"#1d1f23":"#1c3221";ctx.fillRect(0,0,cw,ch);
  const ppm=Math.max(cw,ch)/G.view,P=track2D();
  ctx.save();
  ctx.translate(cw/2,ch/2);
  if(G.headUp)ctx.rotate(-G.camA-Math.PI/2);
  ctx.scale(ppm,ppm);ctx.translate(-G.camX,-G.camY);
  const R=Math.hypot(cw,ch)/ppm/2+4;
  ctx.fillStyle=TR.street?CITY:GRASS;ctx.fillRect(G.camX-R,G.camY-R,2*R,2*R);
  ctx.lineJoin="round";ctx.lineCap="round";
  const W=TR.W;
  ctx.strokeStyle="#34373c";ctx.lineWidth=7;ctx.stroke(P.pit);
  if(TR.street){
    ctx.strokeStyle="#a3a8af";ctx.lineWidth=W+4;ctx.stroke(TR.path);
    ctx.strokeStyle="#2a2d32";ctx.lineWidth=W+3.1;ctx.stroke(TR.path);
  }else{
    ctx.strokeStyle="#3a3c40";ctx.lineWidth=W+10;ctx.stroke(TR.path);
    ctx.lineCap="butt";ctx.strokeStyle="#9a8c70";ctx.lineWidth=8;ctx.stroke(P.gravP);ctx.stroke(P.gravN);ctx.lineCap="round";
  }
  ctx.lineCap="butt";
  ctx.strokeStyle="#ebe7df";ctx.lineWidth=1.1;ctx.stroke(TR.kerbs);
  ctx.setLineDash([2.4,2.4]);ctx.strokeStyle="#d23a2c";ctx.stroke(TR.kerbs);ctx.setLineDash([]);
  ctx.lineCap="round";
  ctx.strokeStyle="#2f3237";ctx.lineWidth=W;ctx.stroke(TR.path);
  ctx.strokeStyle="rgba(240,236,228,.8)";ctx.lineWidth=.3;ctx.stroke(TR.edgeL);ctx.stroke(TR.edgeR);
  ctx.strokeStyle="rgba(240,236,228,.7)";ctx.lineWidth=.25;
  for(let s=0;s<10;s++){
    const p=gridPose(s);ctx.save();ctx.translate(p.x,p.y);ctx.rotate(p.h);
    ctx.beginPath();ctx.moveTo(1.4,-1.2);ctx.lineTo(3.2,-1.2);ctx.lineTo(3.2,1.2);ctx.lineTo(1.4,1.2);ctx.stroke();ctx.restore();
  }
  ctx.save();ctx.translate(TR.X[0],TR.Y[0]);ctx.rotate(Math.atan2(TR.TY[0],TR.TX[0]));
  const sq=.9,rows=Math.ceil(W/sq);
  for(let r=0;r<2;r++)for(let k=0;k<rows;k++){ctx.fillStyle=(r+k)%2?"#141414":"#f2f0ea";ctx.fillRect(-sq+r*sq,-W/2+k*sq,sq,Math.min(sq,W-k*sq));}
  ctx.restore();
  if(G.skN){
    ctx.strokeStyle="rgba(10,10,12,.38)";ctx.lineWidth=.32;ctx.beginPath();
    for(let i=0;i<G.skN;i++){const o=i*4;ctx.moveTo(G.skids[o],G.skids[o+1]);ctx.lineTo(G.skids[o+2],G.skids[o+3]);}
    ctx.stroke();
  }
  if(G.mode==="tt"&&G.player&&G.player.maxLaps>=0&&G.rec&&G.rec.ghost){
    const gh=G.rec.ghost,f=(G.time-G.player.lapStart)*20,i=Math.floor(f),fr=f-i;
    if((i+1)*3+2<gh.length){const a=i*3,b=a+3;
      drawCar2D({x:gh[a]+(gh[b]-gh[a])*fr,y:gh[a+1]+(gh[b+1]-gh[a+1])*fr,h:gh[a+2]+wrapA(gh[b+2]-gh[a+2])*fr,color:"#b65cff",steer:0,brk:0,vf:0},.38);}
  }
  for(const c of G.cars)if(!c.me&&!c.hidden)drawCar2D(Object.assign(c,{tyreColor:TYRES[c.tyre].color}),c.retired?.5:c.recover>0?.45:1);
  if(G.player)drawCar2D(Object.assign(G.player,{tyreColor:TYRES[G.player.tyre].color}),G.player.recover>0?.5:1);
  if(G.sc&&!G.sc.gone){
    const S=G.sc;ctx.save();ctx.translate(S.x,S.y);ctx.rotate(S.h);
    ctx.fillStyle="#cfd3d8";ctx.fillRect(-2.3,-.95,4.6,1.9);ctx.fillStyle="#1a1d22";ctx.fillRect(-.6,-.8,1.6,1.6);
    ctx.fillStyle=S.lights&&Math.floor(G.time*4)%2?"#ffb000":"#3a2a00";ctx.fillRect(-.2,-.7,.35,1.4);ctx.restore();
  }
  ctx.restore();
  drawMini();
}
let MINI={s:1,ox:0,oy:0,w:170,h:170};
function fitMini(){
  if(!TR)return;
  const r=mini.getBoundingClientRect(),w=r.width||170,h=r.height||170,b=TR.bounds,pad=12;
  const s=Math.min((w-2*pad)/(b.maxX-b.minX),(h-2*pad)/(b.maxY-b.minY));
  MINI={s,ox:pad+((w-2*pad)-(b.maxX-b.minX)*s)/2-b.minX*s,oy:pad+((h-2*pad)-(b.maxY-b.minY)*s)/2-b.minY*s,w,h};
}
function drawMini(){
  if($("#hud").hidden)return;
  const M=MINI;
  mctx.setTransform(dpr,0,0,dpr,0,0);mctx.clearRect(0,0,M.w,M.h);
  mctx.save();mctx.translate(M.ox,M.oy);mctx.scale(M.s,M.s);
  mctx.lineJoin="round";mctx.strokeStyle="rgba(241,237,228,.85)";mctx.lineWidth=2.4/M.s;mctx.stroke(TR.path);
  // flagged marshal sectors
  const n=TR.n;
  for(let s=0;s<TR.MS;s++){
    const y=G.yel[s],lvl=y&&y.until>G.time?y.lvl:0,neutral=G.neutral;
    if(!lvl&&!neutral)continue;
    mctx.strokeStyle=neutral?"#ffb000":"#ffd21f";mctx.lineWidth=(lvl===2?5:4)/M.s;mctx.beginPath();
    const a=Math.floor(s*n/TR.MS),b=Math.floor((s+1)*n/TR.MS);
    for(let i=a;i<=b;i++){const j=i%n;i===a?mctx.moveTo(TR.X[j],TR.Y[j]):mctx.lineTo(TR.X[j],TR.Y[j]);}
    mctx.stroke();
  }
  mctx.strokeStyle="#ffd21f";mctx.lineWidth=2/M.s;mctx.beginPath();
  mctx.moveTo(TR.X[0]+TR.NX[0]*14,TR.Y[0]+TR.NY[0]*14);mctx.lineTo(TR.X[0]-TR.NX[0]*14,TR.Y[0]-TR.NY[0]*14);mctx.stroke();
  for(const c of G.cars){
    if(c.me||c.hidden)continue;mctx.fillStyle=c.retired?"#555":c.color;mctx.beginPath();mctx.arc(c.x,c.y,3.2/M.s,0,Math.PI*2);mctx.fill();
  }
  if(G.sc&&!G.sc.gone){mctx.fillStyle="#ffb000";mctx.fillRect(G.sc.x-4/M.s,G.sc.y-4/M.s,8/M.s,8/M.s);}
  if(G.player){const c=G.player;mctx.fillStyle=ME_COLOR;mctx.strokeStyle="#0f1114";mctx.lineWidth=1.5/M.s;mctx.beginPath();mctx.arc(c.x,c.y,4.6/M.s,0,Math.PI*2);mctx.fill();mctx.stroke();}
  mctx.restore();
}
