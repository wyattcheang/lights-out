"use strict";
const TRACKS=__TRACKS__;
const STREET=new Set(['mc-1929','sg-2008','az-2016','sa-2021','us-2023','us-2022','ca-1978','es-2026']);
const NIGHT=new Set(['sg-2008','us-2023','bh-2002','sa-2021','qa-2004','ae-2009']);
const STEP=3;
/* 2026-style car: ~400 kW combustion engine + up to 350 kW from the MGU-K (energy store ~4 MJ usable) */
const CAR={MASS:800,PICE:400e3,PERS:350e3,KCORNER:.95,KSTRAIGHT:.75,TRAC:12.5,BRK:40,LAT:44,GRIP:9,YAWMAX:2.6,MECH:24,AERO:.0042};
/* grip grows with downforce: ~1.7 g mechanical at walking pace, ~4.8 g at 300 km/h */
const latF=(v,straight)=>CAR.MECH+CAR.AERO*v*v*(straight?.55:1);
const brkF=v=>24+.0035*v*v;
const ERS={CAP:4,OT_BONUS:.5,HARV_BRAKE:.35,HARV_LIFT:.1,TAPER_FROM:80.6,TAPER_TO:96,OT_FULL:93.6,OT_TO:98.6};
const PIT_SPEED=80/3.6;
const LAT_AI=40,BRK_AI=26,GAP_SPEED=58;
const TYRES={
  S:{name:"Soft",grip:1.035,life:.5,color:"#e8322e"},
  M:{name:"Medium",grip:1.0,life:.8,color:"#ffd21f"},
  H:{name:"Hard",grip:.972,life:1.15,color:"#f2f0ea"}
};
const POINTS=[25,18,15,12,10,8,6,4,2,1];
const DIFF={rookie:.86,pro:.92,ace:.975};
const INCIDENT_RATE={off:0,real:.035,chaos:.11};
const RIVALS=[["VAL","#e8322e","#1b1d22"],["ORS","#2f6bff","#ffd21f"],["KIM","#ff8a1f","#1d2a44"],["DUV","#13c4a3","#0d1f2b"],["MAR","#c9ced6","#00a19b"],
  ["SEN","#7a3cff","#ffffff"],["HAK","#ff4f9a","#1a1a1a"],["LOR","#0b6b3a","#e9e3c8"],["BRE","#1a1f2b","#e83b3b"]];
const ME_COLOR="#ffd21f",ME_ACCENT="#16181c";
const GEARS=[0,22,32,42,52,62,72,82,100];

const $=s=>document.querySelector(s);
const clamp=(v,a,b)=>v<a?a:v>b?b:v;
const lerp=(a,b,t)=>a+(b-a)*t;
const smooth=t=>{t=clamp(t,0,1);return t*t*(3-2*t);};
const rnd=(a,b)=>a+Math.random()*(b-a);
const wrapA=a=>{while(a>Math.PI)a-=2*Math.PI;while(a<-Math.PI)a+=2*Math.PI;return a};
const fmt=t=>{if(t==null||!isFinite(t))return"–:––.–––";const m=Math.floor(t/60),s=t-m*60;return m+":"+(s<10?"0":"")+s.toFixed(3)};
const store={get(k){try{const v=localStorage.getItem(k);return v?JSON.parse(v):null}catch(e){return null}},set(k,v){try{localStorage.setItem(k,JSON.stringify(v))}catch(e){}}};
const reduceMotion=matchMedia("(prefers-reduced-motion: reduce)").matches;

/* ---------------- Track building ---------------- */
const built=new Map();
function curvature(HX,HY,n){
  const th=new Float32Array(n),k=new Float32Array(n),k2=new Float32Array(n);
  for(let i=0;i<n;i++){const a=(i-1+n)%n,b=(i+1)%n;th[i]=Math.atan2(HY[b]-HY[a],HX[b]-HX[a]);}
  for(let i=0;i<n;i++)k[i]=wrapA(th[(i+2)%n]-th[(i-2+n)%n])/(4*STEP);
  for(let i=0;i<n;i++){let s=0;for(let j=-3;j<=3;j++)s+=k[(i+j+n)%n];k2[i]=s/7;}
  return k2;
}
function buildTrack(t){
  if(built.has(t.id))return built.get(t.id);
  const street=STREET.has(t.id),W=street?13:15;
  const raw=[];for(let i=0;i<t.p.length;i+=2)raw.push([t.p[i],t.p[i+1],t.e?t.e[i/2]:0]);
  const m=raw.length,dense=[];
  for(let i=0;i<m;i++){
    const p0=raw[(i-1+m)%m],p1=raw[i],p2=raw[(i+1)%m],p3=raw[(i+2)%m];
    const k=Math.max(1,Math.ceil(Math.hypot(p2[0]-p1[0],p2[1]-p1[1])));
    for(let j=0;j<k;j++){
      const s=j/k,s2=s*s,s3=s2*s,pt=[0,0,0];
      for(let d=0;d<3;d++)pt[d]=.5*(2*p1[d]+(-p0[d]+p2[d])*s+(2*p0[d]-5*p1[d]+4*p2[d]-p3[d])*s2+(-p0[d]+3*p1[d]-3*p2[d]+p3[d])*s3);
      dense.push(pt);
    }
  }
  const resample=(PX,PY,PZ)=>{
    const D=PX.length,cum=new Float64Array(D+1);
    for(let i=1;i<=D;i++){const a=i-1,b=i%D;cum[i]=cum[i-1]+Math.hypot(PX[b]-PX[a],PY[b]-PY[a]);}
    const total=cum[D],mm=Math.round(total/STEP),st=total/mm,RX=new Float32Array(mm),RY=new Float32Array(mm),RZ=new Float32Array(mm);
    for(let i=0,j=0;i<mm;i++){
      const s=i*st;while(j<D-1&&cum[j+1]<s)j++;
      const b=(j+1)%D,f=(s-cum[j])/((cum[j+1]-cum[j])||1);
      RX[i]=PX[j]+(PX[b]-PX[j])*f;RY[i]=PY[j]+(PY[b]-PY[j])*f;RZ[i]=PZ[j]+(PZ[b]-PZ[j])*f;
    }
    return[RX,RY,RZ];
  };
  let [X,Y,E]=resample(dense.map(q=>q[0]),dense.map(q=>q[1]),dense.map(q=>q[2]));
  let n=X.length;
  const sm=(A)=>{const B=new Float32Array(n);for(let i=0;i<n;i++){const a=(i-1+n)%n,b=(i+1)%n;B[i]=.25*A[a]+.5*A[i]+.25*A[b];}return B;};
  for(let p=0;p<3;p++){X=sm(X);Y=sm(Y);E=sm(E);}
  for(let it=0;it<30;it++){
    const k=curvature(X,Y,n),flag=new Uint8Array(n);let hit=0;
    for(let i=0;i<n;i++)if(Math.abs(k[i])>1/16){hit++;for(let j=-4;j<=4;j++)flag[(i+j+n)%n]=1;}
    if(!hit)break;
    const X2=X.slice(),Y2=Y.slice();
    for(let i=0;i<n;i++)if(flag[i]){const a=(i-1+n)%n,b=(i+1)%n;X2[i]=.25*X[a]+.5*X[i]+.25*X[b];Y2[i]=.25*Y[a]+.5*Y[i]+.25*Y[b];}
    [X,Y,E]=resample(Array.from(X2),Array.from(Y2),Array.from(E));n=X.length;
  }
  for(let p=0;p<6;p++)E=sm(E);
  // gradient and vertical curvature (crests lighten the car, compressions load it)
  const GR=new Float32Array(n),KV=new Float32Array(n);
  for(let i=0;i<n;i++){const a=(i-2+n)%n,b=(i+2)%n;GR[i]=(E[b]-E[a])/(4*STEP);}
  for(let i=0;i<n;i++){const a=(i-4+n)%n,b=(i+4)%n;KV[i]=(GR[b]-GR[a])/(8*STEP);}
  const TX=new Float32Array(n),TY=new Float32Array(n),NX=new Float32Array(n),NY=new Float32Array(n);
  for(let i=0;i<n;i++){
    const a=(i-1+n)%n,b=(i+1)%n;let dx=X[b]-X[a],dy=Y[b]-Y[a];const l=Math.hypot(dx,dy)||1;dx/=l;dy/=l;
    TX[i]=dx;TY[i]=dy;NX[i]=-dy;NY[i]=dx;
  }
  const KC=curvature(X,Y,n);
  const lim=W/2-1.7;let RL=new Float32Array(n);
  for(let it=0;it<160;it++){
    const R2=new Float32Array(n);
    for(let i=0;i<n;i++){
      const a=(i-1+n)%n,b=(i+1)%n;
      const mx=(X[a]+NX[a]*RL[a]+X[b]+NX[b]*RL[b])/2,my=(Y[a]+NY[a]*RL[a]+Y[b]+NY[b]*RL[b])/2;
      R2[i]=clamp((mx-X[i])*NX[i]+(my-Y[i])*NY[i],-lim,lim);
    }
    RL=R2;
  }
  const LX=new Float32Array(n),LY=new Float32Array(n);
  for(let i=0;i<n;i++){LX[i]=X[i]+NX[i]*RL[i];LY[i]=Y[i]+NY[i]*RL[i];}
  const KR=curvature(LX,LY,n);
  const VP=new Float32Array(n);
  const vCorner=k=>{k=Math.abs(k);const m=.92*CAR.MECH,a=.92*CAR.AERO;return k<=a+1e-4?95:Math.min(95,Math.sqrt(m/(k-a)));};
  for(let i=0;i<n;i++){const load=clamp(1+KV[i]*600,.75,1.3);VP[i]=Math.min(vCorner(KR[i]/load),1.25*vCorner(KC[i]/load));}
  for(let r=0;r<2;r++)for(let i=n-1;i>=0;i--){const b=(i+1)%n,vb=VP[b];VP[i]=Math.min(VP[i],Math.sqrt(vb*vb+2*.78*brkF(vb)*STEP));}
  let minX=1e9,minY=1e9,maxX=-1e9,maxY=-1e9;
  for(let i=0;i<n;i++){minX=Math.min(minX,X[i]);maxX=Math.max(maxX,X[i]);minY=Math.min(minY,Y[i]);maxY=Math.max(maxY,Y[i]);}
  const nearest=(px,py)=>{let b=1e18;for(let j=0;j<n;j+=2){const dx=X[j]-px,dy=Y[j]-py,d=dx*dx+dy*dy;if(d<b)b=d;}return Math.sqrt(b);};
  // barriers (gaps wherever a barrier would come too close to another part of the track)
  const wallD=street?W/2+1.6:W/2+11,wallP=new Uint8Array(n),wallN=new Uint8Array(n);
  for(let i=0;i<n;i++){
    wallP[i]=nearest(X[i]+NX[i]*wallD,Y[i]+NY[i]*wallD)>wallD-1.2?1:0;
    wallN[i]=nearest(X[i]-NX[i]*wallD,Y[i]-NY[i]*wallD)>wallD-1.2?1:0;
  }
  // gravel traps on the outside of corners
  const gravP=new Uint8Array(n),gravN=new Uint8Array(n),kerb=new Uint8Array(n);
  for(let i=0;i<n;i++){
    if(Math.abs(KC[i])>1/120)kerb[i]=1;
    if(street)continue;
    if(KC[i]>1/160)for(let q=-8;q<24;q++)gravN[(i+q+n)%n]=1;
    if(KC[i]<-1/160)for(let q=-8;q<24;q++)gravP[(i+q+n)%n]=1;
  }
  // corners, braking boards, overtake detection point
  const boards=[],apexes=[];let lastApex=-1e9;
  for(let i=0;i<n;i++){
    const v=VP[i];if(v>55)continue;
    if(!(v<=VP[(i-1+n)%n]&&v<VP[(i+1)%n]))continue;
    let mx=0;for(let k=1;k<=100;k++)mx=Math.max(mx,VP[(i-k+n)%n]);
    if(mx-v<22||i-lastApex<60)continue;
    let e=i;for(let k=0;k<60;k++){const q=(e-1+n)%n;if(VP[q]>v+4)break;e=q;}
    const side=KC[i]>0?-1:1;
    for(let k=0;k<3;k++)boards.push({i:(e-Math.round((3-k)*100/STEP)+2*n)%n,k,side});
    apexes.push(i);lastApex=i;
  }
  let det=n-110;
  for(const a of apexes)if(a>n*.55&&a<n-25)det=a;
  // active aero straight-mode zones: long low-curvature runs
  const aero=new Uint8Array(n),zones=[];
  const low=i=>Math.abs(KC[i])<1/450;
  let s0=0;while(s0<n&&low(s0))s0++;
  for(let k=0;k<n;){
    const i=(s0+k)%n;if(!low(i)){k++;continue;}
    let len=0;while(len<n&&low((s0+k+len)%n))len++;
    if(len*STEP>=300){const st=(s0+k+6)%n;zones.push({start:st,len:len-10});for(let q=6;q<len-4;q++)aero[(s0+k+q)%n]=1;}
    k+=len;
  }
  // pit lane alongside the start/finish straight, on the side with more room
  const PE0=-72,PE1=-52,PX0=24,PX1=44;
  let freeP=0,freeN=0;
  for(let k=PE0;k<=PX1;k+=4){const i=(k+n)%n;
    if(nearest(X[i]+NX[i]*(wallD+14),Y[i]+NY[i]*(wallD+14))>wallD+10)freeP++;
    if(nearest(X[i]-NX[i]*(wallD+14),Y[i]-NY[i]*(wallD+14))>wallD+10)freeN++;}
  const PS=freeP>freeN?1:-1;
  const pitLat=PS*(wallD+4.5);
  const pitMask=PS>0?wallP:wallN;
  for(let k=PE0;k<=PE1+2;k++)pitMask[(k+n)%n]=0;
  for(let k=PX0-2;k<=PX1;k++)pitMask[(k+n)%n]=0;
  for(let k=PE1+3;k<PX0-2;k++)pitMask[(k+n)%n]=1;
  const MS=clamp(Math.round(n*STEP/320),10,22);
  let cornerCount=0;for(let i=0;i<n;i++)if(Math.abs(KC[i])>1/150)cornerCount++;
  // 2D paths
  const loop=(off)=>{const p=new Path2D();for(let i=0;i<n;i++){const x=X[i]+NX[i]*off,y=Y[i]+NY[i]*off;i?p.lineTo(x,y):p.moveTo(x,y);}p.closePath();return p;};
  const path=loop(0),edgeL=loop(W/2-.35),edgeR=loop(-(W/2-.35));
  const kerbs=new Path2D();
  for(const s of[1,-1]){let on=false;
    for(let k=0;k<=n;k++){const i=k%n;
      if(kerb[i]){const x=X[i]+NX[i]*s*(W/2+.45),y=Y[i]+NY[i]*s*(W/2+.45);if(on)kerbs.lineTo(x,y);else{kerbs.moveTo(x,y);on=true;}}else on=false;}}
  const T={t,id:t.id,street,night:NIGHT.has(t.id),W,n,X,Y,E,GR,KV,hasElev:!!t.e,TX,TY,NX,NY,RL,VP,KC,path,edgeL,edgeR,kerbs,kerb,wallD,wallP,wallN,gravP,gravN,
    boards,apexes,det,aero,zones,PE0,PE1,PX0,PX1,PS,pitLat,MS,cornerFrac:Math.max(.05,cornerCount/n),nearest,
    bounds:{minX,minY,maxX,maxY}};
  built.set(t.id,T);
  return T;
}
