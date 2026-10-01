/* ---------------- State ---------------- */
const G={mode:"attract",cars:[],time:0,started:false,paused:false,over:false,camX:0,camY:0,camA:0,view:150,headUp:!reduceMotion,cam:"cockpit",
  laps:5,field:8,diff:"pro",inc:"real",startTyre:"M",trackIdx:2,skids:new Float32Array(2400*4),skN:0,skHead:0,rec:null,sessBest:[null,null,null],
  neutral:null,sc:null,yel:[],flagMsg:null,raceDist:1};
let TR=null;
const input={up:false,down:false,left:false,right:false,hb:false,boost:false};

function poseAt(i,lat){
  const n=TR.n;i=((i%n)+n)%n;
  return{i,x:TR.X[i]+TR.NX[i]*lat,y:TR.Y[i]+TR.NY[i]*lat,h:Math.atan2(TR.TY[i],TR.TX[i])};
}
/* smooth position at a fractional index and lateral offset */
function posF(p,lat){
  const n=TR.n,i0=Math.floor(p),f=p-i0,a=((i0%n)+n)%n,b=(a+1)%n;
  const x=lerp(TR.X[a]+TR.NX[a]*lat,TR.X[b]+TR.NX[b]*lat,f),y=lerp(TR.Y[a]+TR.NY[a]*lat,TR.Y[b]+TR.NY[b]*lat,f);
  return[x,y];
}
function gridPose(slot){
  const back=10+slot*8;
  return poseAt(-Math.round(back/STEP),(slot%2?-1:1)*TR.W*.22);
}
function makeCar(p,o){
  return Object.assign({x:p.x,y:p.y,h:p.h,vx:0,vy:0,vf:0,vl:0,steer:0,thr:0,brk:0,hb:false,idx:p.i,prog:p.i-TR.n,pf:p.i-TR.n,d:0,surf:0,
    lane:0,laneBase:0,maxLaps:-1,lapStart:0,secIdx:0,secStart:0,sec:[null,null,null],lastLap:null,bestLap:null,
    finished:false,finishTime:null,invalid:false,recover:0,stuck:0,skPrev:null,wrong:false,blocked:false,
    soc:ERS.CAP*.9,socCap:ERS.CAP,ersMode:"bal",boost:false,otArmed:false,otActive:false,aero:"corner",aeroZone:-1,dep:0,harv:0,
    tyre:"M",wear:0,warm:1,used:new Set(),dmg:0,pen:0,penServed:0,tl:0,tlOff:false,pit:null,boxReq:false,nextTyre:"H",stops:0,stints:[],
    retired:false,dnf:null,spin:null,mech:null,parked:false,hidden:false,blue:false,vscDelta:0,pass:null,planLap:2,stratLap:-9,garage:0,lastDet:-1},o);
}
function setTyre(c,t){c.tyre=t;c.used.add(t);c.stints.push(t);c.wear=0;}

function newSession(mode){
  const t=TRACKS[G.trackIdx];TR=buildTrack(t);
  if(typeof R3!=="undefined")reset3DCars();
  G.mode=mode;G.time=0;G.over=false;G.cars=[];G.skN=0;G.skHead=0;G.sessBest=[null,null,null];
  G.neutral=null;G.sc=null;G.yel=new Array(TR.MS).fill(null).map(()=>({lvl:0,until:0}));G.flagMsg=null;G.greenUntil=0;G.radioQ=[];
  G.started=mode!=="race";G.raceStart=0;G.lightsHold=4.6+Math.random()*1.2;
  const lapLen=TR.n*STEP;
  G.raceDist=(mode==="race"?Math.max(3,G.laps):6)*lapLen;
  if(mode==="tt"){
    const p=poseAt(-Math.round(160/STEP),0);
    const me=makeCar(p,{me:true,ai:false,code:"YOU",color:ME_COLOR,accent:ME_ACCENT,skill:1,garage:3});
    setTyre(me,"S");me.soc=ERS.CAP;
    G.cars.push(me);G.player=me;
    G.rec=store.get("loa3:"+t.id)||{};
    G.curGhost=[];G.ghostAcc=0;G.curSplits=new Array(Math.ceil(TR.n/10)).fill(null);
  }else{
    const N=mode==="race"?G.field:8;
    const meSlot=mode==="race"?Math.floor(N/2):-1;
    const base=mode==="race"?DIFF[G.diff]:.93;
    let r=0;
    for(let s=0;s<N;s++){
      const p=gridPose(s);
      if(s===meSlot){
        const me=makeCar(p,{me:true,ai:false,code:"YOU",color:ME_COLOR,accent:ME_ACCENT,skill:1,garage:s});
        setTyre(me,G.startTyre);me.nextTyre=G.startTyre==="H"?"M":"H";G.cars.push(me);G.player=me;continue;
      }
      const [code,color,accent]=RIVALS[r++%RIVALS.length];
      const c=makeCar(p,{ai:true,code,color,accent,garage:s,skill:clamp(base-s*.005+Math.random()*.012,.7,1),laneBase:(Math.random()-.5)*1.2});
      const roll=Math.random();setTyre(c,roll<.35?"S":roll<.85?"M":"H");
      const lifeA=TYRES[c.tyre].life,nxt=c.tyre==="H"?"M":"H",lifeB=TYRES[nxt].life;
      c.planLap=clamp(Math.round(G.laps*lifeA/(lifeA+lifeB)*rnd(.8,1.2)),1,Math.max(1,G.laps-1));
      c.nextTyre=nxt;c.lane=c.laneBase;G.cars.push(c);
    }
    if(mode!=="race")G.player=null;
  }
  const cam=G.player||G.cars[0];
  G.camCar=cam;G.camX=cam.x;G.camY=cam.y;G.camA=cam.h;G.view=140;
  fitMini();
}

/* ---------------- Physics ---------------- */
function locate(c){
  const n=TR.n;let best=1e18,bi=c.idx;
  for(let k=-12;k<=40;k++){const i=(c.idx+k+n)%n,dx=c.x-TR.X[i],dy=c.y-TR.Y[i],d=dx*dx+dy*dy;if(d<best){best=d;bi=i;}}
  let delta=bi-c.idx;if(delta>n/2)delta-=n;if(delta<-n/2)delta+=n;
  c.idx=bi;c.prog+=delta;
  const dx=c.x-TR.X[bi],dy=c.y-TR.Y[bi];
  c.d=dx*TR.NX[bi]+dy*TR.NY[bi];
  c.pf=c.prog+(dx*TR.TX[bi]+dy*TR.TY[bi])/STEP;
}
const SURF_G=[1,.85,.55,.42],SURF_DRAG=[0,.04,.55,1.15];
function surfaceAt(c){
  const ad=Math.abs(c.d),hw=TR.W/2;
  if(ad<=hw+1.2||TR.street)return 0;
  if((c.d>0?TR.gravP:TR.gravN)[c.idx]&&ad<hw+9)return 3;
  return ad<hw+5?1:2;
}
function tyreGrip(c){
  const w=c.wear;
  const f=w<.6?1-.05*w/.6:w<1?.95-.2*(w-.6)/.4:.75-.08*Math.min(1,(w-1)/.3);
  return TYRES[c.tyre].grip*f*(.955+.045*c.warm);
}
function ersStep(c,v){
  let dep=0,h=0,clip=0;
  const neutral=!!G.neutral;
  if(c.thr>.9&&G.started){
    if(c.soc>.02){
      let frac;
      if(c.otActive||c.boost)frac=1;
      else if(c.ersMode==="rec")frac=.25;
      else frac=.55*clamp((c.soc-.3)/1.2,.35,1);
      if(neutral)frac=Math.min(frac,.3);
      const taper=c.otActive?clamp((ERS.OT_TO-v)/(ERS.OT_TO-ERS.OT_FULL),0,1):clamp((ERS.TAPER_TO-v)/(ERS.TAPER_TO-ERS.TAPER_FROM),0,1);
      dep=CAR.PERS*frac*taper;
    }
    if(!c.boost&&!c.otActive){
      if(c.ersMode==="rec"&&v>60){h=.25;clip=h*1e6;}
      else if(v>78){h=.15;clip=h*1e6;}
    }
  }else if(c.brk>.05&&v>8)h=ERS.HARV_BRAKE*Math.min(1,c.brk*1.3)*Math.min(1,v/30);
  else if(c.thr<.15&&v>12)h=ERS.HARV_LIFT;
  return[dep,h,clip];
}
function physics(c,dt){
  const surf=surfaceAt(c),grip=tyreGrip(c),g=SURF_G[surf]*grip;
  const straight=c.aero==="straight";
  let ch_=Math.cos(c.h),sh_=Math.sin(c.h);
  let vf=c.vx*ch_+c.vy*sh_;
  const sp=Math.abs(vf);
  const load=clamp(1+TR.KV[c.idx]*sp*sp/9.81,.55,1.45);c.load=load;
  const lat=latF(sp,straight)*g*load*(1-.12*c.dmg);
  let yaw=c.steer*Math.min(CAR.YAWMAX,lat/Math.max(sp,6))*Math.min(1,sp/3)*(vf>=0?1:-1);
  if(c.hb)yaw*=1.35;
  c.h=wrapA(c.h+yaw*dt);
  ch_=Math.cos(c.h);sh_=Math.sin(c.h);
  vf=c.vx*ch_+c.vy*sh_;let vl=-c.vx*sh_+c.vy*ch_;
  const v=Math.max(0,vf);
  const[dep,h,clip]=ersStep(c,v);
  let a=0;
  if(c.thr>0&&vf>-.5){
    const pw=Math.max(0,CAR.PICE*c.thr-clip)+dep;
    a+=Math.min(pw/(CAR.MASS*Math.max(v,4)),CAR.TRAC*g)*(surf>=2?.75:1);
  }
  if(c.brk>0){
    if(vf>.5)a-=c.brk*brkF(v)*g*Math.min(1.2,load);
    else if(vf>-12&&!c.thr)a-=c.brk*6;
  }
  if(c.hb&&vf>0)a-=8;
  a-=(straight?CAR.KSTRAIGHT:CAR.KCORNER)*vf*Math.abs(vf)/CAR.MASS+.15*Math.sign(vf);
  a-=vf*SURF_DRAG[surf];
  a-=9.81*TR.GR[c.idx]*Math.cos(c.h-Math.atan2(TR.TY[c.idx],TR.TX[c.idx]));
  const pv=vf;vf+=a*dt;
  if(c.brk>0&&pv>0&&vf<0)vf=0;
  if(Math.abs(vf)<.05&&!c.thr)vf=0;
  vl*=Math.exp(-CAR.GRIP*g*(c.hb?.28:1)*dt);
  c.vx=vf*ch_-vl*sh_;c.vy=vf*sh_+vl*ch_;
  c.x+=c.vx*dt;c.y+=c.vy*dt;
  c.vf=vf;c.vl=vl;c.surf=surf;c.dep=dep;c.harv=h;
  c.soc=clamp(c.soc+(h-dep/1e6)*dt,0,c.socCap);
  if(c.socCap>ERS.CAP&&c.soc<=ERS.CAP)c.socCap=ERS.CAP;
  // tyre wear & warm-up
  const ds=Math.abs(vf)*dt,latUse=Math.min(1.5,Math.abs(vf*yaw)/Math.max(lat,1));
  c.wear+=ds/(TYRES[c.tyre].life*G.raceDist)*(.7+.9*latUse*latUse+(Math.abs(vl)>2?1.4:0)+(c.brk>.9&&v>30?.4:0));
  if(c.warm<1)c.warm=Math.min(1,c.warm+ds/600);
}
function walls(c){
  const lim=TR.wallD-1.0,ad=Math.abs(c.d);
  if(ad<=lim||ad>lim+5)return 0;
  if(!(c.d>0?TR.wallP:TR.wallN)[c.idx])return 0;
  const s=Math.sign(c.d),i=c.idx,nx=TR.NX[i]*s,ny=TR.NY[i]*s,ex=ad-lim;
  c.x-=nx*ex;c.y-=ny*ex;c.d=s*lim;
  const vn=c.vx*nx+c.vy*ny;
  if(vn>0){
    c.vx-=nx*vn*1.25;c.vy-=ny*vn*1.25;
    const loss=1-Math.min(.45,vn/35);c.vx*=loss;c.vy*=loss;
    const th=Math.atan2(TR.TY[i],TR.TX[i]),fw=Math.cos(c.h-th)>=0?th:th+Math.PI;
    c.h+=wrapA(fw-c.h)*.15;
    return vn;
  }
  return 0;
}
function recover(c){
  const p=poseAt(c.idx,TR.RL[c.idx]);
  c.x=p.x;c.y=p.y;c.h=p.h;c.vx=c.vy=c.vf=c.vl=0;c.steer=0;c.recover=1.2;c.stuck=0;
  locate(c);
}
function aeroStep(c){
  if(c.pit||c.retired){c.aero="corner";return;}
  const z=TR.aero[c.idx];
  if(c.aero==="straight"){
    if(!z||c.brk>.1||Math.abs(c.steer)>.5||G.neutral||sectorYellow(c.idx)>1)c.aero="corner";
  }else if(z&&c.thr>.9&&Math.abs(c.steer)<.3&&!G.neutral&&G.started){
    const zid=zoneId(c.idx);
    if(zid!==c.aeroZone){c.aero="straight";c.aeroZone=zid;}
  }
  if(!z)c.aeroZone=-1;
}
function zoneId(i){for(let k=0;k<TR.zones.length;k++){const z=TR.zones[k];const r=((i-z.start)%TR.n+TR.n)%TR.n;if(r<z.len)return k;}return -1;}

/* ---------------- Race control helpers ---------------- */
function sectorOf(i){return Math.min(TR.MS-1,Math.floor(i/TR.n*TR.MS));}
function sectorYellow(i){const y=G.yel[sectorOf(i)];return y&&y.until>G.time?y.lvl:0;}
function setYellow(i,lvl,dur){
  const s=sectorOf(i),y=G.yel[s];
  if(y.until<G.time||y.lvl<lvl)y.lvl=lvl;y.until=Math.max(y.until,G.time+dur);
  const p=G.yel[(s-1+TR.MS)%TR.MS];if(p.until<G.time+dur){p.lvl=Math.max(1,p.until>G.time?p.lvl:1);p.until=G.time+dur;}
}
function radio(msg,cls){
  G.radioQ.push({msg,cls,t:G.time});if(G.radioQ.length>3)G.radioQ.shift();
  if(typeof showRadio==="function")showRadio();
}
function running(){return G.cars.filter(c=>!c.retired);}
function order(){
  return[...G.cars].sort((a,b)=>{
    if(a.retired!==b.retired)return a.retired?1:-1;
    if(a.finished&&b.finished)return(a.finishTime+a.pen)-(b.finishTime+b.pen);
    if(a.finished)return -1;if(b.finished)return 1;return b.pf-a.pf;
  });
}
function carAhead(c){
  // nearest running car physically ahead on track (any lap)
  const n=TR.n;let best=null,bd=1e9;
  for(const o of G.cars){if(o===c||o.retired||o.hidden||o.pit)continue;
    let dp=o.pf-c.pf;dp=((dp%n)+n)%n;if(dp<bd){bd=dp;best=o;}}
  return best?{car:best,dist:bd*STEP}:null;
}
function deployVSC(){
  if(G.neutral||G.over)return;
  G.neutral={type:"VSC",phase:"on",t:0,clear:rnd(14,22)};
  for(const c of G.cars){c.vscDelta=.4;c.otActive=false;c.aero="corner";}
  radio("Virtual Safety Car. Slow down and keep the delta positive.","yellow");
}
function deploySC(){
  if(G.over)return;
  if(G.neutral&&G.neutral.type==="SC")return;
  G.neutral={type:"SC",phase:"on",t:0,clear:rnd(28,40)};
  const lead=order().find(c=>!c.retired&&!c.finished);
  if(!lead)return;
  G.sc={pf:lead.pf+60,v:Math.max(30,Math.hypot(lead.vx,lead.vy)*.8),lights:true,x:0,y:0,h:0,inPit:false,gone:false,lat:0};
  for(const c of G.cars){c.otActive=false;c.aero="corner";}
  radio("Safety Car, Safety Car. No overtaking. Box if you need tyres.","yellow");
}
function endNeutral(){
  G.neutral=null;G.sc=null;G.greenUntil=G.time+4;
  for(const c of G.cars)if(c.retired&&c.parked)c.hidden=true;
  radio("Track is clear. Green flag.","green");
}
function neutralStep(dt){
  const N=G.neutral;if(!N)return;
  N.t+=dt;
  if(N.type==="VSC"){
    if(N.phase==="on"&&N.t>N.clear){N.phase="ending";N.endAt=N.t+rnd(10,15);radio("VSC ending. Green in 10 to 15 seconds.","yellow");}
    else if(N.phase==="ending"&&N.t>N.endAt)endNeutral();
    return;
  }
  // Safety Car
  const S=G.sc;if(!S)return;
  const n=TR.n,lead=order().find(c=>!c.retired&&!c.finished&&!c.pit);
  if(N.phase==="on"&&N.t>N.clear){N.phase="in";S.lights=false;radio("Safety Car in this lap.","yellow");}
  if(!S.inPit){
    const i=((Math.floor(S.pf)%n)+n)%n;
    let v=clamp(TR.VP[i]*.72,22,62);
    if(lead){const gap=(S.pf-lead.pf)*STEP;if(gap>140)v*=.6;else if(gap<25)v=Math.min(v*1.15,55);}
    S.v+=clamp(v-S.v,-10*dt,6*dt);
    const rel=((S.pf-(n+TR.PE0))%n+n)%n;
    if(N.phase==="in"&&rel<6&&N.t>N.clear+3){S.inPit=true;S.pp=TR.PE0+rel;}
  }
  if(S.inPit){
    S.v=Math.max(PIT_SPEED,S.v-12*dt);
    S.pp+=S.v*dt/STEP;S.pf+=S.v*dt/STEP;
    const lat=S.pp<TR.PE1?lerp(TR.RL[((Math.floor(S.pp)%n)+n)%n],TR.pitLat,smooth((S.pp-TR.PE0)/(TR.PE1-TR.PE0))):TR.pitLat;
    S.lat=lat;
    if(S.pp>TR.PE1+12&&!S.gone){S.gone=true;N.phase="restart";radio("Safety Car is in. Leader controls the restart.","yellow");}
  }else{S.pf+=S.v*dt/STEP;S.lat=TR.RL[((Math.floor(S.pf)%n)+n)%n];}
  const[x,y]=posF(S.pf,S.lat),[x2,y2]=posF(S.pf+.5,S.lat);
  S.x=x;S.y=y;S.h=Math.atan2(y2-y,x2-x);
  if(N.phase==="restart"&&lead){
    if(lead.maxLaps>(N.restartLap??(N.restartLap=lead.maxLaps)))endNeutral();
  }
}

/* ---------------- Incidents ---------------- */
function startIncident(c){
  const r=Math.random(),corner=TR.KC[c.idx];
  if(r<.58){c.spin={t:0,stop:0,rate:rnd(3.5,6)*(Math.random()<.5?-1:1),crash:false};radio(c.code+" has spun.","yellow");}
  else if(r<.84){
    c.spin={t:0,stop:0,rate:rnd(2,4)*(corner>0?1:-1),crash:true};
    const s=corner>0?-1:1;c.vx+=TR.NX[c.idx]*s*c.vf*.45;c.vy+=TR.NY[c.idx]*s*c.vf*.45;
    radio(c.code+" is in the barrier.","red");
  }else{c.mech={t:0};radio(c.code+" reports a power unit problem.","yellow");}
  setYellow(c.idx,1,10);
}
function spinStep(c,dt){
  const S=c.spin;S.t+=dt;
  const v=Math.hypot(c.vx,c.vy);
  if(v>.3){const k=Math.max(0,v-16*dt)/v;c.vx*=k;c.vy*=k;}else{c.vx=c.vy=0;}
  c.h=wrapA(c.h+S.rate*dt*clamp(v/20,.15,1));
  c.x+=c.vx*dt;c.y+=c.vy*dt;c.vf=v;c.vl=0;c.thr=0;c.brk=1;
  locate(c);walls(c);
  const onTrack=Math.abs(c.d)<TR.W/2+.5;
  setYellow(c.idx,onTrack?2:1,6);
  if(v<.4){
    S.stop+=dt;
    if(S.crash&&S.stop>1){
      c.retired=true;c.retAt=G.time;c.dnf="Accident";c.parked=true;c.spin=null;c.vx=c.vy=0;
      if(onTrack||TR.street||Math.random()<.55)deploySC();else deployVSC();
      if(!G.neutral)setYellow(c.idx,2,25);
    }else if(!S.crash&&S.stop>rnd(1.8,3.5)){c.spin=null;recover(c);c.recover=1.5;}
  }
}
function mechStep(c,dt){
  const M=c.mech;M.t+=dt;
  c.thr=Math.min(c.thr,.25);
  const side=c.d>=0?1:-1;c.lane=side*(TR.W/2+3)-TR.RL[c.idx];
  setYellow(c.idx,1,5);
  if(M.t>5&&c.vf<6){c.brk=1;c.thr=0;}
  if(M.t>5&&c.vf<.5){
    c.retired=true;c.retAt=G.time;c.dnf="Power unit";c.parked=true;c.mech=null;c.vx=c.vy=0;
    if(Math.random()<.6)deployVSC();else setYellow(c.idx,2,20);
  }
}

/* ---------------- Pit lane ---------------- */
function pitLatAt(P){
  const n=TR.n;
  if(P.p<TR.PE1)return lerp(P.lat0,TR.pitLat,smooth((P.p-TR.PE0)/(TR.PE1-TR.PE0)));
  if(P.p<TR.PX0)return TR.pitLat;
  return lerp(TR.pitLat,TR.RL[((Math.floor(TR.PX1)%n)+n)%n],smooth((P.p-TR.PX0)/(TR.PX1-TR.PX0)));
}
function boxIdx(c){return -44+c.garage*6;}
function enterPit(c,rel){
  c.pit={p:TR.PE0+rel,ph:"in",lat0:c.d,v:Math.max(PIT_SPEED,c.vf),stop:0,stopTime:0};
  c.otActive=false;c.aero="corner";c.boost=false;
  if(c.me)radio("Pit limiter on. Box, box.","");
}
function pitStep(c,dt){
  const P=c.pit,bx=boxIdx(c);
  if(P.ph==="in"||P.ph==="lane"){
    const toBox=(bx-P.p)*STEP;
    let vt=PIT_SPEED;
    if(toBox<22)vt=Math.min(vt,Math.sqrt(Math.max(0,2*7*toBox)));
    P.v+=clamp(vt-P.v,-35*dt,8*dt);
    if(P.p>TR.PE1)P.ph="lane";
    if(toBox<.3&&P.v<1.2){
      P.ph="stop";P.v=0;P.p=bx;
      P.stopTime=rnd(2.1,2.9)+(c.dmg>.05?4.5:0);
      const owe=c.pen-c.penServed;if(owe>0){P.stopTime+=owe;c.penServed=c.pen;P.served=owe;}
      if(c.me)radio(P.served?"Serving "+P.served+" s penalty first.":"Stationary.","");
    }
  }else if(P.ph==="stop"){
    P.stop+=dt;P.v=0;
    if(P.stop>=P.stopTime){
      setTyre(c,c.nextTyre);c.warm=0;c.dmg=0;c.stops++;c.boxReq=false;P.ph="out";
      if(c.me)radio("Go, go, go! "+TYRES[c.tyre].name+"s on.","green");
    }
  }else if(P.ph==="out"){
    const vt=P.p<TR.PX0?PIT_SPEED:42;
    P.v+=clamp(vt-P.v,-10*dt,(P.p<TR.PX0?8:12)*dt);
    if(P.p>=TR.PX1){
      const[x2,y2]=posF(P.p+.5,pitLatAt(P));
      c.h=Math.atan2(y2-c.y,x2-c.x);c.vx=Math.cos(c.h)*P.v;c.vy=Math.sin(c.h)*P.v;c.vf=P.v;
      c.pit=null;c.recover=.8;c.lane=0;locate(c);
      if(c.me)radio("Pit exit clear. Watch the white line.","");
      return;
    }
  }
  P.p+=P.v*dt/STEP;
  const lat=pitLatAt(P);
  const[x,y]=posF(P.p,lat),[x2,y2]=posF(P.p+.6,pitLatAt({...P,p:P.p+.6}));
  c.x=x;c.y=y;if(P.v>.2)c.h=Math.atan2(y2-y,x2-x);
  c.vx=Math.cos(c.h)*P.v;c.vy=Math.sin(c.h)*P.v;c.vf=P.v;c.vl=0;c.thr=P.v<PIT_SPEED-.5?.4:.2;c.brk=0;c.steer=0;
  locate(c);
}
function maybeEnterPit(c){
  if(!c.boxReq||c.pit||c.retired||c.finished||!G.started)return;
  const n=TR.n,rel=((c.idx-(n+TR.PE0))%n+n)%n;
  if(rel<4&&c.maxLaps>=0){
    if(G.mode==="race"&&c.maxLaps>=G.laps-0)return;
    enterPit(c,rel);
  }
}
function chooseTyre(c){
  const left=1-clamp(c.pf/(G.laps*TR.n),0,1);
  let t=left>.55?"H":left>.28?"M":"S";
  if(c.used.size<2&&t===c.tyre)t=c.tyre==="H"?"M":"H";
  return t;
}
function aiStrategy(c){
  if(c.retired||c.finished||c.pit||c.boxReq||G.mode!=="race")return;
  const n=TR.n,lapNow=c.maxLaps+1;
  if(lapNow<1||lapNow>=G.laps)return;
  const rel=((c.idx-(n-230))%n+n)%n;if(rel>6)return;
  if(c.stratLap===lapNow)return;c.stratLap=lapNow;
  const need=c.used.size<2,left=G.laps-lapNow;
  let box=false;
  if(need&&lapNow>=c.planLap)box=true;
  if(need&&left<=1)box=true;
  if(c.wear>.82&&left>=1)box=true;
  if(G.neutral&&G.neutral.type==="SC"&&need&&left>=1)box=true;
  if(G.neutral&&G.neutral.type==="VSC"&&need&&left>=1&&Math.random()<.6)box=true;
  if(c.dmg>.3&&left>=1)box=true;
  if(box){c.boxReq=true;c.nextTyre=chooseTyre(c);}
}

/* ---------------- AI ---------------- */
function aiDrive(c,dt){
  const n=TR.n,v=Math.max(0,c.vf),halfW=TR.W/2,N=G.neutral;
  const noPass=!!N||sectorYellow(c.idx)>0;
  let want=c.mech?c.lane:c.laneBase;c.blocked=false;let capV=1e9,wantBoost=false;
  if(c.blue)want=(c.d>=0?1:-1)*(halfW-2)-TR.RL[c.idx];
  for(const o of G.cars){
    if(o===c||o.recover>0||o.pit||o.hidden)continue;
    let dp=o.pf-c.pf;dp=((dp%n)+n+n/2)%n-n/2;const dm=dp*STEP;
    if(dm>0&&dm<34){
      const dl=o.d-c.d;
      if(Math.abs(dl)<2.7){
        if(o.retired||o.spin){const side=(c.d>=o.d?1:-1);want=o.d+side*3.6-TR.RL[c.idx];continue;}
        if(noPass){capV=Math.min(capV,(o.vf||0)+(dm-12)*.5);continue;}
        const side=(c.d>=o.d?1:-1);
        const target=o.d+side*3.4-TR.RL[c.idx];
        if(Math.abs(o.d+side*3.4)>halfW-1.3)want=o.d-side*3.4-TR.RL[c.idx];else want=target;
        if(dm<10&&v>(o.vf||0)){c.blocked=true;capV=Math.min(capV,(o.vf||0)-1);}
        if(TR.aero[c.idx]&&c.soc>1.2)wantBoost=true;
      }
    }
  }
  if(c.mech)want=c.lane;
  c.lane+=clamp(want-c.lane,-2.2*dt,2.2*dt);
  const la=Math.max(3,Math.round((9+v*.32)/STEP)),j=(c.idx+la)%n;
  const off=clamp(TR.RL[j]+c.lane,-(halfW-1.2)-(c.mech?4:0),halfW-1.2+(c.mech?4:0));
  const tx=TR.X[j]+TR.NX[j]*off,ty=TR.Y[j]+TR.NY[j]*off;
  const dx=tx-c.x,dy=ty-c.y,L=Math.hypot(dx,dy)||1;
  const ang=wrapA(Math.atan2(dy,dx)-c.h);
  const k=2*Math.sin(ang)/L,vs=Math.max(v,6),maxYaw=Math.min(CAR.YAWMAX,latF(vs,c.aero==="straight")*(c.load||1)/vs);
  c.steer=clamp(vs*k/maxYaw,-1,1);
  let target=1e9;for(let q=0,qe=Math.round(v*.12/STEP)+3;q<=qe;q++)target=Math.min(target,TR.VP[(c.idx+q)%n]);
  target*=c.skill*Math.sqrt(tyreGrip(c)/1.0)*(1-.06*c.dmg);
  if(Math.abs(c.d)>halfW)target=Math.min(target,30);
  const yl=sectorYellow(c.idx);if(yl===1)target*=.9;else if(yl===2)target*=.72;
  if(c.blue)target*=.93;
  if(N){
    if(N.type==="VSC")target=Math.min(target,vscRef(c.idx)*.97);
    else{
      const ahead=carAhead(c);let tgtGap=14,lv=null,gd=1e9;
      if(ahead){lv=ahead.car.vf;gd=ahead.dist;}
      if(G.sc&&!G.sc.inPit){const sgap=((G.sc.pf-c.pf)%n+n)%n*STEP;if(sgap<gd){gd=sgap;lv=G.sc.v;tgtGap=22;}}
      if(N.phase==="restart"&&isLeader(c)){
        const toLine=(n-((c.idx%n)+n)%n)*STEP;target=toLine<160?target:Math.min(target,TR.VP[c.idx]*.55);
      }else if(lv!=null&&gd<220)target=Math.min(target,Math.max(0,lv+(gd-tgtGap)*.6));
      else target=Math.min(target,TR.VP[c.idx]*.62);
    }
  }
  target=Math.min(target,capV>0?capV:target);
  if(c.mech&&c.mech.t>5)target=0;
  if(c.boxReq){const toPit=((((n+TR.PE0)-c.idx)%n+n)%n)*STEP;if(toPit<160)target=Math.min(target,PIT_SPEED+toPit*.25);}
  if(v<target-.5){c.thr=1;c.brk=0;}
  else if(v>target+1.5){c.thr=0;c.brk=clamp((v-target)/8,0,1);}
  else{c.thr=.35;c.brk=0;}
  c.hb=false;
  c.boost=wantBoost&&!!TR.aero[c.idx]&&c.soc>.8&&!N;
  c.ersMode=c.soc<.8?"rec":"bal";
  if(c.otArmed&&TR.aero[c.idx]&&!c.otActive&&!N)activateOT(c);
}
function isLeader(c){const o=order();return o.find(x=>!x.retired&&!x.finished)===c;}
function vscRef(i){return Math.max(16,TR.VP[i]*.6);}
function activateOT(c){
  if(!c.otArmed||c.otActive)return false;
  c.otArmed=false;c.otActive=true;c.socCap=ERS.CAP+ERS.OT_BONUS;c.soc=Math.min(c.socCap,c.soc+ERS.OT_BONUS);
  if(c.me)radio("Overtake mode on. +0.5 MJ, full power to 337 km/h.","green");
  return true;
}
function playerControl(c,dt){
  const tgt=(input.right?1:0)-(input.left?1:0);
  const rate=tgt===0?7:(c.steer!==0&&Math.sign(tgt)!==Math.sign(c.steer)?10:4.5);
  c.steer+=clamp(tgt-c.steer,-rate*dt,rate*dt);
  c.thr=input.up?1:0;c.brk=input.down?1:0;c.hb=input.hb;c.boost=input.boost&&!G.neutral;
}
function collide(a,b){
  if(a.recover>0||b.recover>0||a.pit||b.pit||a.hidden||b.hidden)return;
  const dx0=b.x-a.x,dy0=b.y-a.y;if(dx0*dx0+dy0*dy0>49)return;
  const pa=[1.5,-1.5],R=2.1;
  if(a.kin&&b.kin)return;
  for(const fa of pa)for(const fb of pa){
    const ax=a.x+Math.cos(a.h)*fa,ay=a.y+Math.sin(a.h)*fa,bx=b.x+Math.cos(b.h)*fb,by=b.y+Math.sin(b.h)*fb;
    let dx=bx-ax,dy=by-ay;const d=Math.hypot(dx,dy);
    if(d<R&&d>1e-4){
      dx/=d;dy/=d;const ov=(R-d)/2;
      const ma=a.parked||a.kin?0:1,mb=b.parked||b.kin?0:1,sum=ma+mb||1;
      a.x-=dx*ov*2*ma/sum;a.y-=dy*ov*2*ma/sum;b.x+=dx*ov*2*mb/sum;b.y+=dy*ov*2*mb/sum;
      const rel=(b.vx-a.vx)*dx+(b.vy-a.vy)*dy;
      if(rel<0){const j=-(1.3)*rel/sum;if(ma){a.vx-=dx*j;a.vy-=dy*j;}if(mb){b.vx+=dx*j;b.vy+=dy*j;}}
    }
  }
}
function addSkid(c){
  const sliding=(Math.abs(c.vl)>2.6||(c.brk>.85&&c.vf>28)||(c.hb&&c.vf>8)||c.spin)&&c.surf===0&&c.recover<=0&&!c.pit;
  if(!sliding){c.skPrev=null;return;}
  const ch_=Math.cos(c.h),sh_=Math.sin(c.h),rx=c.x-ch_*1.8,ry=c.y-sh_*1.8;
  const pts=[[rx-sh_*.8,ry+ch_*.8],[rx+sh_*.8,ry-ch_*.8]];
  if(c.skPrev){
    for(let w=0;w<2;w++){
      const o=G.skHead*4;G.skids[o]=c.skPrev[w][0];G.skids[o+1]=c.skPrev[w][1];G.skids[o+2]=pts[w][0];G.skids[o+3]=pts[w][1];
      G.skHead=(G.skHead+1)%2400;G.skN=Math.min(2400,G.skN+1);
    }
  }
  c.skPrev=pts;
}

/* ---------------- Laps, detection, rules ---------------- */
function laps(c){
  const n=TR.n,now=G.time,done=Math.floor(c.prog/n);
  if(done>c.maxLaps){
    const firstCross=c.maxLaps<0;c.maxLaps=done;
    if(!firstCross){
      c.sec[2]=now-c.secStart;
      const lt=now-c.lapStart;c.lastLap=lt;
      const valid=!c.invalid;
      if(valid&&(c.bestLap==null||lt<c.bestLap))c.bestLap=lt;
      if(c.me)onPlayerLap(c,lt,valid);
      if(G.mode==="race"&&done>=G.laps&&!c.finished){
        c.finished=true;c.finishTime=now-G.raceStart;
        if(c.me)finishRace();
      }
    }
    const start=(G.mode==="race"&&done===0)?G.raceStart:now;
    c.lapStart=start;c.secStart=start;c.secIdx=0;c.invalid=false;
    if(c.me){
      if(G.mode==="tt"){G.curGhost=[];G.ghostAcc=0;G.curSplits=new Array(Math.ceil(n/10)).fill(null);G.secCls=[null,null,null];G.liveSec=[null,null,null];}
      if(G.mode==="race"&&done===G.laps-1&&G.laps>1){
        radio(c.used.size<2?"Final lap. You have not used two compounds: you will be disqualified!":"Final lap. Bring it home.",c.used.size<2?"red":"yellow");
      }
    }
  }
  if(c.maxLaps>=0){
    const pos=((c.prog%n)+n)%n,s=pos<n/3?0:pos<2*n/3?1:2;
    if(s>c.secIdx){
      const st=now-c.secStart;c.sec[c.secIdx]=st;
      if(c.me&&G.mode==="tt")onPlayerSector(c.secIdx,st,!c.invalid);
      c.secStart=now;c.secIdx=s;
    }
    if(c.me&&G.mode==="tt"){
      const si=Math.floor(pos/10);if(G.curSplits[si]==null)G.curSplits[si]=now-c.lapStart;
    }
  }
}
function detection(c){
  // Overtake-mode detection point: within 1.0 s of the car ahead
  const n=TR.n,pos=((c.idx%n)+n)%n,rel=((pos-TR.det)%n+n)%n;
  const lapKey=c.maxLaps;
  if(rel>4||c.lastDet===lapKey)return;
  c.lastDet=lapKey;
  if(c.otActive){c.otActive=false;}
  if(G.mode!=="race"||G.neutral||c.maxLaps<1||c.pit){c.otArmed=false;return;}
  const a=carAhead(c);
  const gap=a?a.dist/Math.max(15,Math.abs(c.vf)):99;
  const was=c.otArmed;
  c.otArmed=gap<=1.0&&!a.car.retired;
  if(c.me&&c.otArmed&&!was)radio("Within one second. Overtake available: press O on the straight.","green");
}
function onPlayerSector(i,st,valid){
  const bs=(G.rec.bs||[]);
  let cls="y";
  if(valid&&(bs[i]==null||st<bs[i]))cls="p";
  else if(valid&&(G.sessBest[i]==null||st<G.sessBest[i]))cls="g";
  if(valid){if(G.sessBest[i]==null||st<G.sessBest[i])G.sessBest[i]=st;}
  G.secCls[i]=cls;G.liveSec[i]=st;
}
function onPlayerLap(c,lt,valid){
  if(G.mode!=="tt")return;
  onPlayerSector(2,c.sec[2],valid);
  const rec=G.rec,t=TRACKS[G.trackIdx];
  if(valid){
    rec.bs=rec.bs||[null,null,null];
    for(let i=0;i<3;i++)if(c.sec[i]!=null&&(rec.bs[i]==null||c.sec[i]<rec.bs[i]))rec.bs[i]=c.sec[i];
    if(rec.t==null||lt<rec.t){rec.t=lt;rec.ghost=G.curGhost;rec.splits=G.curSplits;toast("Best lap "+fmt(lt),"purple",2400);}
    else toast("Lap "+fmt(lt),"yellow",1800);
    store.set("loa3:"+t.id,rec);
  }else toast("Lap deleted","red",1800);
}
function addPenalty(c,s,why){
  c.pen+=s;
  if(c.me){radio(s+" second time penalty: "+why+".","red");toast("+"+s+" s penalty","red",2200);}
}
function playerRules(c,dt){
  if(G.mode!=="race"||c.pit||c.finished||!G.started)return;
  const n=TR.n,N=G.neutral;
  // track limits: warning, warning, black-and-white flag, then 5 s
  if(!TR.street){
    const off=Math.abs(c.d)>TR.W/2+1.0;
    if(off&&!c.tlOff){
      c.tlOff=true;c.tl++;
      if(c.tl<=2)radio("Track limits. Warning "+c.tl+".","yellow");
      else if(c.tl===3){radio("Black and white flag. Next one is a penalty.","yellow");G.bwUntil=G.time+4;}
      else addPenalty(c,5,"track limits");
    }else if(!off&&Math.abs(c.d)<TR.W/2)c.tlOff=false;
  }
  // overtaking under yellow / VSC / SC
  const caution=!!N||sectorYellow(c.idx)>0;
  for(const o of G.cars){
    if(o===c)continue;
    let rel=c.pf-o.pf;rel=((rel%n)+n+n/2)%n-n/2;
    const prev=o._relMe;o._relMe=rel;
    if(prev==null)continue;
    if(prev<0&&rel>=0&&Math.abs(rel)<20){
      const exempt=o.pit||o.retired||o.spin||o.mech||o.recover>0;
      if(caution&&!exempt&&!c.pass){
        c.pass={car:o,until:G.time+8};
        radio("You passed "+o.code+" under "+(N?N.type==="SC"?"the Safety Car":"VSC":"yellow flags")+". Give the position back.","red");
      }
    }
    if(c.pass&&c.pass.car===o&&prev>=0&&rel<0){c.pass=null;radio("Position returned. Thanks.","green");}
  }
  if(c.pass&&G.time>c.pass.until){addPenalty(c,5,"overtaking under caution");c.pass=null;}
  // VSC delta
  if(N&&N.type==="VSC"){
    const ref=vscRef(c.idx);c.vscDelta+=dt*(1-Math.max(0,c.vf)/ref);
    c.vscDelta=Math.min(c.vscDelta,3);
    if(c.vscDelta<-1&&!N.pen){N.pen=true;addPenalty(c,5,"VSC delta");}
  }
}
function blueFlags(){
  const n=TR.n;
  for(const o of G.cars){o.blue=false;}
  if(G.mode!=="race")return;
  for(const c of G.cars){if(c.retired||c.pit)continue;
    for(const o of G.cars){if(o===c||o.retired||o.pit)continue;
      const ahead=c.pf-o.pf;
      if(ahead>n*.6){const phys=(o.pf+n-c.pf)*STEP;if(phys>0&&phys<60)o.blue=true;}
    }}
  const p=G.player;if(p&&p.blue&&!p._blueSaid){p._blueSaid=true;radio("Blue flag. Let the leader through.","blue");}
  if(p&&!p.blue)p._blueSaid=false;
}
function finishRace(){
  G.over=true;
  G.player.ai=true;G.player.skill=.8;G.player.laneBase=0;
  setTimeout(showResults,1800);
}

/* ---------------- Main update ---------------- */
function update(dt){
  G.time+=dt;
  if(G.mode==="race"&&!G.started&&!G.prerace){
    if(G.time>=G.lightsHold){G.started=true;G.raceStart=G.net?G.lightsHold:G.time;radio("Lights out and away we go!","green");}
  }
  if(!G.net||G.net.host)neutralStep(dt);
  const cars=G.cars,rate=G.mode==="tt"||(G.net&&!G.net.host)?0:(G.mode==="attract"?INCIDENT_RATE.real:INCIDENT_RATE[G.inc]);
  if(Math.floor(G.time*4)!==Math.floor((G.time-dt)*4))blueFlags();
  for(const c of cars){
    if(c.hidden)continue;
    if(c.kin){if(typeof netKin==="function")netKin(c,dt);continue;}
    if(c.retired){c.vx=c.vy=0;c.thr=0;c.brk=0;if(c.parked&&!G.neutral&&G.time-c.retAt>25)c.hidden=true;continue;}
    if(!G.started){if(c.ai)aiDrive(c,dt);else playerControl(c,dt);c.vx=c.vy=0;continue;}
    if(c.pit){pitStep(c,dt);laps(c);continue;}
    if(c.spin){spinStep(c,dt);continue;}
    if(c.ai){
      aiDrive(c,dt);
      if(c.mech)mechStep(c,dt);
      else if(rate>0&&!G.over&&!c.finished&&c.maxLaps>=0&&Math.abs(TR.KC[c.idx])>1/150&&c.vf>20){
        const pPerM=rate/(TR.n*STEP*TR.cornerFrac);
        if(Math.random()<pPerM*c.vf*dt)startIncident(c);
      }
    }else playerControl(c,dt);
    aeroStep(c);
    physics(c,dt);locate(c);
    const hit=walls(c);
    if(hit>16&&c.me){
      c.dmg=Math.min(1,c.dmg+hit/40);setYellow(c.idx,1,8);
      radio(hit>30?"Big impact! Front wing damage, box for a new nose.":"Contact with the wall. Front wing damaged.","red");
    }
    if(c.recover>0)c.recover-=dt;
    if(!TR.street&&Math.abs(c.d)>TR.W/2+45&&!c.pit)recover(c);
    if(c.ai){const rate2=(c.pf-(c.prevPf??c.pf))*STEP/dt;if(rate2<2.5&&!G.neutral)c.stuck+=dt;else c.stuck=Math.max(0,c.stuck-dt);if(c.stuck>3)recover(c);}
    c.prevPf=c.pf;
    if(c.me&&G.mode==="tt"&&!c.invalid&&c.maxLaps>=0&&!TR.street&&Math.abs(c.d)>TR.W/2+1.2){c.invalid=true;toast("Track limits · lap deleted","red",1800);}
    if(c.me){const th=Math.atan2(TR.TY[c.idx],TR.TX[c.idx]);c.wrong=Math.cos(c.h-th)<-.3&&Math.hypot(c.vx,c.vy)>4&&c.vf>0;playerRules(c,dt);}
    laps(c);detection(c);aiStrategy(c);maybeEnterPit(c);addSkid(c);
  }
  if(G.started)for(let i=0;i<cars.length;i++)for(let j=i+1;j<cars.length;j++)collide(cars[i],cars[j]);
  if(G.mode==="tt"&&G.player&&G.player.maxLaps>=0){
    G.ghostAcc+=dt;
    while(G.ghostAcc>=.05){G.ghostAcc-=.05;const p=G.player;if(G.curGhost.length<60000)G.curGhost.push(Math.round(p.x*10)/10,Math.round(p.y*10)/10,Math.round(p.h*100)/100);}
  }
  if(G.mode==="attract"){
    let lead=null;for(const c of G.cars)if(!c.retired&&(!lead||c.pf>lead.pf))lead=c;
    if(lead&&(lead!==G.camCar&&Math.random()<.002||G.camCar.retired))G.camCar=lead;
    if(G.time>240)newSession("attract");
  }
  const c=G.camCar,v=Math.hypot(c.vx,c.vy),look=Math.min(42,v*.38);
  const k=Math.min(1,dt*6);
  G.camX+=(c.x+Math.cos(c.h)*look-G.camX)*k;G.camY+=(c.y+Math.sin(c.h)*look-G.camY)*k;
  G.camA+=wrapA(c.h-G.camA)*Math.min(1,dt*4);
  G.view+=((115+v*1.05)-G.view)*Math.min(1,dt*1.5);
}
