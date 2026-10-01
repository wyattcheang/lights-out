/* ---------------- Online: public / private rooms over the artifact room ---------------- */
const NET={room:null,user:null,R:null,code:null,pub:false,host:false,myPeer:null,names:{},buf:new Map(),lastHostT:0,hostPeer:null,inRace:false,ready:false,tyre:"M",unsubs:[]};
const MAX_PLAYERS=10;
function randCode(){const a="abcdefghjkmnpqrstuvwxyz23456789";let s="";for(let i=0;i<6;i++)s+=a[Math.floor(Math.random()*a.length)];return s;}
async function netInit(){
  try{
    if(!window.claude||!window.claude.use)return;
    const [room,user]=await Promise.all([window.claude.use("room"),window.claude.use("user")]);
    NET.room=room;NET.user=user;
    if(!room){$("#onlineNote").textContent="Online racing works when this page is opened on claude.ai while signed in.";return;}
    $("#onlineBox").hidden=false;$("#onlineNote").textContent="";
    try{const me=await user.me();NET.myName=me.name||"";}catch(e){}
    room.onPeers(()=>renderPublicRooms());
    renderPublicRooms();
  }catch(e){}
}
async function nameOf(p){
  if(p.sameTab&&NET.myName)return NET.myName;
  if(p.by&&NET.user){try{const ps=await NET.user.profiles([p.by]);const nm=ps[p.by]&&ps[p.by].name;if(nm)return nm;}catch(e){}}
  return(p.presence&&p.presence.nick)||"Driver "+p.peer.slice(0,3).toUpperCase();
}
function codeFromName(nm){const s=(nm||"").replace(/[^A-Za-z]/g,"").toUpperCase();return(s+"XXX").slice(0,3);}
function renderPublicRooms(){
  const box=$("#roomList");if(!box||!NET.room)return;
  const ads=NET.room.peers().filter(p=>p.kind==="viewer"&&!p.sameTab&&p.presence&&p.presence.ad&&p.presence.ad.pub);
  const sig=JSON.stringify(ads.map(p=>[p.peer,p.presence.ad]));if(sig===NET.listSig)return;NET.listSig=sig;
  if(!ads.length){box.innerHTML='<p class="muted">No public rooms open right now. Host one and it appears here for everyone with this page open.</p>';return;}
  box.innerHTML="";
  for(const p of ads){
    const a=p.presence.ad,t=TRACKS[a.tr]||TRACKS[0];
    const b=document.createElement("button");b.type="button";b.className="roomcard";
    b.innerHTML=`<span class="rc-t"></span><span class="rc-m"></span><span class="rc-s"></span>`;
    b.querySelector(".rc-t").textContent=t.city;
    b.querySelector(".rc-m").textContent=(a.lp||5)+" laps · "+(a.n||1)+"/"+MAX_PLAYERS+" drivers";
    b.querySelector(".rc-s").textContent=a.st==="race"?"Racing":"Join";
    b.disabled=a.st==="race"||(a.n||1)>=MAX_PLAYERS;
    b.addEventListener("click",()=>joinRoom(String(a.c||"")));
    box.appendChild(b);
    nameOf(p).then(nm=>{b.querySelector(".rc-t").textContent=t.city+" · "+nm;});
  }
}
async function hostRoom(pub){
  if(!NET.room)return;
  await enterRoom(randCode(),true,pub);
}
async function joinRoom(code){
  code=(code||"").trim().toLowerCase().replace(/[^a-z0-9]/g,"");
  if(code.length<4){toast("Enter the 6-character room code","yellow",1400);return;}
  await enterRoom(code,false,false);
}
async function enterRoom(code,host,pub){
  try{
    const R=await NET.room.join("lo-"+code);
    Object.assign(NET,{R,code,host,pub,ready:false,inRace:false,buf:new Map(),hostPeer:null});
    NET.cfg=NET.cfg||{tr:G.trackIdx,laps:5,inc:"real",ai:3,diff:"pro"};
    NET.unsubs.forEach(u=>u());NET.unsubs=[];
    NET.unsubs.push(R.onPeers(ch=>onRoomPeers(ch)));
    NET.unsubs.push(R.onConnection(ok=>{$("#lobbyConn").textContent=ok?"":"Reconnecting…";}));
    G.net={host,code};
    pushPresence(true);
    if(host&&pub)advertise();
    showLobby();
  }catch(e){toast(e&&e.code==="limit_reached"?"Too many rooms open in this tab":"Could not open that room","red",1800);}
}
function advertise(){
  if(!NET.room)return;
  if(!NET.R||!NET.host||!NET.pub){if(NET.adSig!==null){NET.adSig=null;NET.room.presence({ad:null}).catch(()=>{});}return;}
  const humans=NET.R?NET.R.peers().filter(p=>p.kind==="viewer").length:1;
  const ad={c:NET.code,pub:1,tr:NET.cfg.tr,lp:NET.cfg.laps,n:humans,st:NET.inRace?"race":"lobby"},s=JSON.stringify(ad);
  if(s===NET.adSig)return;NET.adSig=s;
  NET.room.presence({ad}).catch(()=>{});
}
async function leaveRoom(){
  if(NET.R){try{await NET.R.leave();}catch(e){}}
  NET.unsubs.forEach(u=>u());NET.unsubs=[];
  NET.R=null;NET.host=false;NET.inRace=false;G.net=null;
  if(NET.room){NET.adSig=null;NET.room.presence({ad:null}).catch(()=>{});}
  NET.presSig=null;$("#lobby").hidden=true;
}
function hostOf(peers){return peers.find(p=>p.kind==="viewer"&&p.presence&&p.presence.h);}
function onRoomPeers(ch){
  const peers=NET.R?NET.R.peers():[];
  if(!NET.host){
    const h=hostOf(peers);
    if(h){NET.hostPeer=h.peer;
      const pr=h.presence;
      if(pr.cfg)NET.cfg=pr.cfg;
      if(pr.ph==="race"&&!NET.inRace&&pr.grid&&pr.grid.includes("p:"+myPeerId(peers)))startNetRace(pr);
      if(pr.ph==="lobby"&&NET.inRace)backToLobby();
      if(pr!==NET.lastHost){NET.lastHost=pr;NET.lastHostAt=performance.now();}
    }else if(NET.hostSeen&&!h){toast("The host left the room","red",2200);leaveRoom();toMenu();return;}
    if(h)NET.hostSeen=true;
  }
  const now=performance.now();
  for(const p of ch.updated.concat(ch.joined)){
    if(p.sameTab||!p.presence)continue;
    const st=p.presence.car;
    if(st){let b=NET.buf.get(p.peer);if(!b){b=[];NET.buf.set(p.peer,b);}b.push({t:now,s:st});if(b.length>12)b.shift();}
    if(!NET.host&&p.presence.h&&p.presence.ai){let b=NET.buf.get("__ai");if(!b){b=[];NET.buf.set("__ai",b);}b.push({t:now,s:p.presence.ai});if(b.length>12)b.shift();}
  }
  for(const p of ch.left){
    const c=G.cars.find(x=>x.peer===p.peer);
    if(c&&NET.inRace&&!c.finished){c.retired=true;c.dnf="Disconnected";c.hidden=true;}
  }
  if(!$("#lobby").hidden)renderLobby();
  if(NET.host)advertise();
}
function myPeerId(peers){const me=peers.find(p=>p.sameTab);return me?me.peer:null;}
/* ---- presence payloads ---- */
const r2=v=>Math.round(v*100)/100;
function carState(c){
  const flags=(c.brk>.3?1:0)|(c.aero==="straight"?2:0)|(c.pit?4:0)|(c.retired?8:0)|(c.finished?16:0)|((c.harv>0&&c.thr>.9)?32:0)|(c.otActive?64:0)|(c.boost&&c.dep>0?128:0);
  return[r2(c.x),r2(c.y),Math.round(c.h*1000)/1000,r2(c.vf),Math.round(c.steer*100)/100,flags,"SMH".indexOf(c.tyre),r2(c.pf),c.maxLaps,c.finishTime==null?-1:Math.round(c.finishTime*1000)/1000,c.pen,c.bestLap==null?-1:Math.round(c.bestLap*1000)/1000,c.stops,c.stints.join(""),c.dnf||""];
}
let presAcc=0;
function pushPresence(force,dt){
  if(!NET.R)return;
  presAcc+=dt||0;if(!force&&presAcc<1/30)return;presAcc=0;
  const P={nick:NET.myName||"",rdy:NET.ready?1:0,ty:NET.tyre};
  const me=G.player;
  if(NET.inRace&&me&&G.net)P.car=carState(me);else P.car=null;
  if(NET.host){
    P.h=1;P.cfg=NET.cfg;P.ph=NET.inRace?"race":"lobby";P.rt=Math.round(G.time*1000)/1000;P.grid=NET.grid||null;
    if(NET.inRace){
      P.ai=G.cars.filter(c=>c.ai&&!c.me).map(c=>carState(c));
      const N=G.neutral;P.N=N?[N.type,N.phase]:0;
      P.sc=G.sc&&!G.sc.gone?[r2(G.sc.x),r2(G.sc.y),Math.round(G.sc.h*1000)/1000,G.sc.lights?1:0,r2(G.sc.pf)]:0;
      P.y=G.yel.map(y=>y.until>G.time?y.lvl:0);
    }else{P.ai=null;P.N=0;P.sc=0;P.y=null;}
  }
  if(!NET.inRace){const s=JSON.stringify(P);if(s===NET.presSig&&!force)return;NET.presSig=s;}
  NET.R.presence(P).catch(()=>{});
}
/* ---- kinematic cars from the network ---- */
function sampleBuf(b,delay){
  if(!b||!b.length)return null;
  const rt=performance.now()-delay;
  let i=b.length-1;while(i>0&&b[i-1].t>rt)i--;
  if(i===0)return{a:b[0].s,b:b[0].s,f:0};
  const A=b[i-1],B=b[i];
  if(B.t<=rt)return{a:B.s,b:B.s,f:0,extra:Math.min(.15,(rt-B.t)/1000)};
  return{a:A.s,b:B.s,f:(rt-A.t)/Math.max(1,B.t-A.t)};
}
function netKin(c,dt){
  const key=c.peer||"__ai",b=NET.buf.get(key);
  const S=sampleBuf(b,110);if(!S)return;
  let a=S.a,bb=S.b;
  if(!c.peer){a=a&&a[c.aiIdx];bb=bb&&bb[c.aiIdx];if(!a||!bb)return;}
  const f=S.f,ex=S.extra||0;
  c.x=lerp(a[0],bb[0],f)+Math.cos(bb[2])*bb[3]*ex;c.y=lerp(a[1],bb[1],f)+Math.sin(bb[2])*bb[3]*ex;
  c.h=a[2]+wrapA(bb[2]-a[2])*f;c.vf=lerp(a[3],bb[3],f);c.steer=bb[4];
  c.vx=Math.cos(c.h)*c.vf;c.vy=Math.sin(c.h)*c.vf;
  const fl=bb[5];c.brk=fl&1?1:0;c.thr=fl&1?0:1;c.aero=fl&2?"straight":"corner";c.otActive=!!(fl&64);c.harv=fl&32?.2:0;c.dep=fl&128?1:0;c.boost=!!(fl&128);
  c.pit=fl&4?(c.pit||{ph:"lane",remote:1}):null;
  c.tyre="SMH"[bb[6]]||"M";
  const n=TR.n;c.pf=bb[7];c.prog=Math.floor(bb[7]);c.idx=((c.prog%n)+n)%n;
  const dx=c.x-TR.X[c.idx],dy=c.y-TR.Y[c.idx];c.d=dx*TR.NX[c.idx]+dy*TR.NY[c.idx];
  c.maxLaps=bb[8];c.finished=!!(fl&16);c.finishTime=bb[9]<0?null:bb[9];c.pen=bb[10];c.bestLap=bb[11]<0?null:bb[11];c.stops=bb[12];
  c.stints=String(bb[13]||c.tyre).split("");c.used=new Set(c.stints);
  if(fl&8&&!c.retired){c.retired=true;c.dnf=bb[14]||"Retired";c.parked=true;c.retAt=G.time;}
}
/* ---- race lifecycle ---- */
function hostStartRace(){
  if(!NET.host)return;
  const peers=NET.R.peers().filter(p=>p.kind==="viewer");
  const humans=peers.map(p=>"p:"+p.peer);
  for(let i=humans.length-1;i>0;i--){const j=Math.floor(Math.random()*(i+1));[humans[i],humans[j]]=[humans[j],humans[i]];}
  const grid=[];const nAi=Math.min(NET.cfg.ai,MAX_PLAYERS-humans.length);
  for(let i=0;i<nAi;i++)grid.push("ai:"+i);
  grid.push(...humans);
  NET.grid=grid;NET.cfg.lh=+(4.6+Math.random()*1.2).toFixed(2);
  startNetRace({cfg:NET.cfg,grid});
}
async function startNetRace(pr){
  NET.inRace=true;NET.cfg=pr.cfg;NET.grid=pr.grid;
  const peers=NET.R.peers();const mine="p:"+myPeerId(peers);
  G.trackIdx=pr.cfg.tr;G.laps=pr.cfg.laps;G.inc=pr.cfg.inc;G.diff=pr.cfg.diff;
  const names={};
  for(const p of peers)names["p:"+p.peer]=await nameOf(p);
  const t=TRACKS[G.trackIdx];TR=buildTrack(t);
  if(typeof R3!=="undefined")reset3DCars();
  Object.assign(G,{mode:"race",time:0,over:false,cars:[],skN:0,skHead:0,neutral:null,sc:null,flagMsg:null,greenUntil:0,radioQ:[],
    started:false,raceStart:0,lightsHold:pr.cfg.lh||5,net:{host:NET.host,code:NET.code},raceDist:Math.max(3,G.laps)*TR.n*STEP});
  G.yel=new Array(TR.MS).fill(null).map(()=>({lvl:0,until:0}));
  let ai=0;
  pr.grid.forEach((id,s)=>{
    const p=gridPose(s);
    if(id===mine){
      const me=makeCar(p,{me:true,ai:false,code:"YOU",color:ME_COLOR,accent:ME_ACCENT,skill:1,garage:s});
      setTyre(me,NET.tyre);me.nextTyre=NET.tyre==="H"?"M":"H";G.cars.push(me);G.player=me;
    }else if(id.startsWith("p:")){
      const pal=RIVALS[(s+3)%RIVALS.length];
      const c=makeCar(p,{kin:true,remote:true,peer:id.slice(2),code:codeFromName(names[id]),name:names[id],color:pal[1],accent:pal[2],garage:s});
      setTyre(c,"M");G.cars.push(c);
    }else{
      const k=+id.slice(3),[code,color,accent]=RIVALS[k%RIVALS.length];
      const c=makeCar(p,{ai:true,aiIdx:ai++,code,color,accent,garage:s,skill:clamp(DIFF[pr.cfg.diff]-s*.004,.7,1),laneBase:((k*37)%10-5)/8});
      setTyre(c,["M","S","M","H"][k%4]);c.nextTyre=c.tyre==="H"?"M":"H";
      const la=TYRES[c.tyre].life,lb=TYRES[c.nextTyre].life;c.planLap=clamp(Math.round(G.laps*la/(la+lb)),1,Math.max(1,G.laps-1));
      if(!NET.host)c.kin=true;
      c.lane=c.laneBase;G.cars.push(c);
    }
  });
  G.camCar=G.player||G.cars[0];G.camX=G.camCar.x;G.camY=G.camCar.y;G.camA=G.camCar.h;G.view=140;
  fitMini();
  $("#lobby").hidden=true;$("#menu").hidden=true;$("#hud").hidden=false;$("#results").hidden=true;$("#pause").hidden=true;$("#prerace").hidden=true;
  G.paused=false;document.body.dataset.mode="race";
  $("#touch").hidden=!matchMedia("(pointer: coarse)").matches;
  el.tyreNext.dataset.k="";resize();setViewCanvas();audioInit();
  radio("Online race: "+pr.grid.filter(x=>x.startsWith("p:")).length+" drivers"+(ai?" and "+ai+" AI cars":"")+". Lights in a few seconds.","");
  pushPresence(true);advertise();
}
function netFrame(dt){
  if(!NET.R)return;
  if(NET.inRace&&!NET.host&&NET.lastHost){
    const H=NET.lastHost,est=H.rt+(performance.now()-NET.lastHostAt)/1000;
    const drift=est-G.time;G.time+=Math.abs(drift)>2?drift:clamp(drift,-.03,.03);
    if(H.N){if(!G.neutral||G.neutral.type!==H.N[0]){if(H.N[0]==="VSC"){for(const c of G.cars)c.vscDelta=.4;}radio(H.N[0]==="SC"?"Safety Car deployed. No overtaking.":"Virtual Safety Car. Keep the delta positive.","yellow");}
      G.neutral={type:H.N[0],phase:H.N[1],t:0};}
    else if(G.neutral){G.neutral=null;G.greenUntil=G.time+4;radio("Track is clear. Green flag.","green");}
    if(H.sc){G.sc=G.sc||{};Object.assign(G.sc,{x:H.sc[0],y:H.sc[1],h:H.sc[2],lights:!!H.sc[3],pf:H.sc[4],gone:false,inPit:false,v:30});}else if(G.sc){G.sc.gone=true;}
    if(H.y)H.y.forEach((l,i)=>{if(G.yel[i]){if(l){G.yel[i].lvl=l;G.yel[i].until=G.time+1;}}});
  }
  pushPresence(false,dt);
}
function backToLobby(){
  NET.inRace=false;NET.ready=false;G.net={host:NET.host,code:NET.code};
  $("#results").hidden=true;$("#hud").hidden=true;
  newSession("attract");setViewCanvas();
  showLobby();pushPresence(true);advertise();
}
/* ---- lobby UI ---- */
function showLobby(){$("#lobby").hidden=false;renderLobby();}
async function renderLobby(){
  if(!NET.R)return;
  $("#lobbyCode").textContent=NET.code.toUpperCase();
  $("#lobbyKind").textContent=NET.pub?"Public room":"Private room";
  $("#lobbyHostCtl").hidden=!NET.host;$("#bStartOnline").hidden=!NET.host;$("#bReady").hidden=NET.host;
  $("#bReady").textContent=NET.ready?"Not ready":"Ready";
  const cfg=NET.cfg;
  if(NET.host){$("#lTrack").value=String(cfg.tr);$("#lLaps").value=String(cfg.laps);$("#lAi").value=String(cfg.ai);$("#lInc").value=cfg.inc;$("#lDiff").value=cfg.diff;}
  const t=TRACKS[cfg.tr]||TRACKS[0];
  $("#lobbySet").textContent=t.city+", "+t.country+" · "+cfg.laps+" laps · "+cfg.ai+" AI cars · incidents "+({off:"off",real:"realistic",chaos:"chaotic"})[cfg.inc];
  const peers=NET.R.peers().filter(p=>p.kind==="viewer");
  const list=$("#lobbyPlayers");list.innerHTML="";
  for(const p of peers){
    const li=document.createElement("li");const pr=p.presence||{};
    li.innerHTML=`<span class="tdot"></span><span class="pn"></span><span class="pb"></span>`;
    li.querySelector(".tdot").textContent=pr.ty||"M";li.querySelector(".tdot").style.setProperty("--c",TYRES[pr.ty||"M"].color);
    li.querySelector(".pb").textContent=pr.h?"Host":pr.rdy?"Ready":"Not ready";li.querySelector(".pb").dataset.on=pr.h||pr.rdy?"1":"0";
    nameOf(p).then(nm=>{li.querySelector(".pn").textContent=nm+(p.sameTab?" (you)":"")+(p.guest?" · guest":"");});
    list.appendChild(li);
  }
  $("#lobbyCount").textContent=peers.length+"/"+MAX_PLAYERS;
  document.querySelectorAll("#lobbyTyre button").forEach(b=>b.classList.toggle("on",b.dataset.t===NET.tyre));
}
function readHostCfg(){
  NET.cfg={tr:+$("#lTrack").value,laps:+$("#lLaps").value,ai:+$("#lAi").value,inc:$("#lInc").value,diff:$("#lDiff").value};
  pushPresence(true);advertise();renderLobby();
  if(G.mode==="attract"&&G.trackIdx!==NET.cfg.tr){G.trackIdx=NET.cfg.tr;newSession("attract");}
}
function wireLobby(){
  const sel=$("#lTrack");TRACKS.forEach((t,i)=>{const o=document.createElement("option");o.value=String(i);o.textContent=t.city+" ("+t.country+")";sel.appendChild(o);});
  ["#lTrack","#lLaps","#lAi","#lInc","#lDiff"].forEach(s=>$(s).addEventListener("change",readHostCfg));
  $("#bHostPub").addEventListener("click",()=>hostRoom(true));
  $("#bHostPriv").addEventListener("click",()=>hostRoom(false));
  $("#bJoin").addEventListener("click",()=>joinRoom($("#joinCode").value));
  $("#joinCode").addEventListener("keydown",e=>{if(e.key==="Enter")joinRoom($("#joinCode").value);});
  $("#bLeave").addEventListener("click",()=>{leaveRoom();});
  $("#bReady").addEventListener("click",()=>{NET.ready=!NET.ready;pushPresence(true);renderLobby();});
  $("#bStartOnline").addEventListener("click",()=>hostStartRace());
  $("#bCopyCode").addEventListener("click",()=>{const c=NET.code.toUpperCase();try{navigator.clipboard.writeText(c).then(()=>toast("Code copied","green",900),()=>{});}catch(e){}});
  document.querySelectorAll("#lobbyTyre button").forEach(b=>b.addEventListener("click",()=>{NET.tyre=b.dataset.t;pushPresence(true);renderLobby();}));
}
