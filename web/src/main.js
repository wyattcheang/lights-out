/* ---------------- Loop ---------------- */
let last=performance.now(),acc=0;
function frame(now,bg){
  let dt=Math.min(bg?1:.05,(now-last)/1000);last=now;
  if(!G.paused&&TR){acc+=dt;while(acc>=1/120){update(1/120);acc-=1/120;}}
  if(TR&&!bg){if(use3D()){sync3D(dt);drawMini();}else draw();hud();}
  netFrame(dt);
  audioUpdate();
  if(!bg)requestAnimationFrame(frame);
}
/* keep an online race running (and the clock honest) while this tab is in the background */
setInterval(()=>{if(document.hidden&&G.net)frame(performance.now(),true);},250);

/* ---------------- Menu ---------------- */
function radioVal(name){return document.querySelector(`input[name=${name}]:checked`).value;}
function bestFor(t){const r=store.get("loa3:"+t.id);return r&&r.t;}
function drawThumb(cvs,t,sel){
  const r=2,w=150*r,h=100*r;cvs.width=w;cvs.height=h;const g=cvs.getContext("2d");
  let minX=1e9,minY=1e9,maxX=-1e9,maxY=-1e9;
  for(let i=0;i<t.p.length;i+=2){minX=Math.min(minX,t.p[i]);maxX=Math.max(maxX,t.p[i]);minY=Math.min(minY,t.p[i+1]);maxY=Math.max(maxY,t.p[i+1]);}
  const pad=10*r,s=Math.min((w-2*pad)/(maxX-minX),(h-2*pad)/(maxY-minY)),ox=(w-(maxX-minX)*s)/2-minX*s,oy=(h-(maxY-minY)*s)/2-minY*s;
  g.lineJoin="round";g.lineCap="round";g.strokeStyle=sel?"#ffd21f":"#f1ede4";g.lineWidth=2.4*r;g.beginPath();
  for(let i=0;i<t.p.length;i+=2){const x=ox+t.p[i]*s,y=oy+t.p[i+1]*s;i?g.lineTo(x,y):g.moveTo(x,y);}
  g.closePath();g.stroke();
  g.fillStyle="#ee3a2f";g.beginPath();g.arc(ox+t.p[0]*s,oy+t.p[1]*s,3.2*r,0,Math.PI*2);g.fill();
}
const cards=[];
function buildMenu(){
  TRACKS.forEach((t,i)=>{
    const b=document.createElement("button");b.className="card";b.type="button";b.setAttribute("aria-pressed","false");
    const c=document.createElement("canvas");
    const best=bestFor(t),night=NIGHT.has(t.id),street=STREET.has(t.id);
    b.innerHTML=`<span class="cn">${t.city}</span><span class="cm"><span>${t.country}</span><span>${(t.len/1000).toFixed(3)} km</span></span><span class="cf">${night?'<i class="night">Night</i>':""}${street?'<i>Street</i>':""}<em class="best">${best?"PB "+fmt(best):""}</em></span>`;
    b.prepend(c);drawThumb(c,t,false);
    b.addEventListener("click",()=>selectTrack(i));
    b.addEventListener("dblclick",()=>{selectTrack(i);start();});
    (t.cur?$("#gridCur"):$("#gridPast")).appendChild(b);cards.push({b,c,t});
  });
}
function selectTrack(i){
  const prev=G.trackIdx;G.trackIdx=i;
  cards.forEach((c,k)=>{const on=k===i;c.b.setAttribute("aria-pressed",String(on));if(k===i||k===prev)drawThumb(c.c,c.t,on);});
  const t=TRACKS[i];
  $("#selCountry").textContent=t.country;$("#selCity").textContent=t.city;
  $("#selLen").textContent=(t.len/1000).toFixed(3)+" km";$("#selOpen").textContent=t.opened;
  $("#selType").textContent=(STREET.has(t.id)?"Street":"Permanent")+(NIGHT.has(t.id)?", night":"");
  $("#selBest").textContent=bestFor(t)?fmt(bestFor(t)):"No lap yet";
  store.set("loa:last",i);
  newSession("attract");
}
function syncOpts(){
  const tt=radioVal("mode")==="tt";
  ["#fsLaps","#fsField","#fsDiff","#fsInc"].forEach(s=>$(s).disabled=tt);
  $("#goLabel").textContent=tt?"Start time trial":"Start race";
}
document.querySelectorAll(".opts input").forEach(i=>i.addEventListener("change",syncOpts));
document.querySelectorAll("input[name=cam]").forEach(i=>i.addEventListener("change",()=>setCam(i.value,false)));
function start(){
  audioInit();
  if(G.net)leaveRoom();
  G.laps=+radioVal("laps");G.field=+radioVal("field");G.diff=radioVal("diff");G.inc=radioVal("inc");G.startTyre=G.startTyre||"M";
  newSession(radioVal("mode"));
  $("#menu").hidden=true;$("#hud").hidden=false;$("#results").hidden=true;$("#pause").hidden=true;G.paused=false;
  document.body.dataset.mode=G.mode;
  $("#touch").hidden=!matchMedia("(pointer: coarse)").matches;
  el.tyreNext.dataset.k="";
  resize();setViewCanvas();
  showPrerace();
}
function showPrerace(){
  const p=G.player;if(!p)return;
  G.paused=true;
  const t=TRACKS[G.trackIdx],race=G.mode==="race";
  const slot=G.cars.indexOf(p)+1;
  $("#preTitle").textContent=t.city+(race?" · Grid":" · Time trial");
  $("#preSub").textContent=race?("You start P"+slot+" of "+G.cars.length+" · "+G.laps+" laps · two compounds required"):"Pick your tyres for a flying lap";
  const laps=Math.max(3,race?G.laps:6);
  const desc={S:"Fastest over one lap, wears quickly",M:"Balanced pace and life",H:"Slowest warm-up, longest life"};
  const box=$("#preTyres");box.innerHTML="";
  for(const k of["S","M","H"]){
    const b=document.createElement("button");b.type="button";b.className="tcard"+(k===G.startTyre?" on":"");b.dataset.t=k;
    b.innerHTML=`<span class="tdot big" style="--c:${TYRES[k].color}">${k}</span><b>${TYRES[k].name}</b><span>${desc[k]}</span><span class="mono">≈ ${(TYRES[k].life*laps).toFixed(1)} laps of life · grip ${Math.round(TYRES[k].grip*100)}%</span>`;
    b.addEventListener("click",()=>{G.startTyre=k;box.querySelectorAll(".tcard").forEach(x=>x.classList.toggle("on",x===b));});
    box.appendChild(b);
  }
  $("#prerace").hidden=false;$("#bPreGo").focus();
}
function preraceGo(){
  const p=G.player;if(!p)return;
  p.used=new Set();p.stints=[];setTyre(p,G.startTyre);p.nextTyre=G.startTyre==="H"?"M":"H";el.tyreNext.dataset.k="";
  $("#prerace").hidden=true;G.paused=false;last=performance.now();
}
function toMenu(){
  $("#hud").hidden=true;$("#pause").hidden=true;$("#results").hidden=true;$("#menu").hidden=false;G.paused=false;
  const t=TRACKS[G.trackIdx],c=cards[G.trackIdx],best=bestFor(t);
  c.b.querySelector(".best").textContent=best?"PB "+fmt(best):"";
  selectTrack(G.trackIdx);setViewCanvas();
}
function pause(on){
  if(G.mode==="attract"||G.over)return;
  if(G.net){$("#pause").hidden=!on;$("#bRestart").hidden=true;$("#pauseSub").textContent="Online race keeps running while this is open.";return;}
  $("#bRestart").hidden=false;
  G.paused=on;$("#pause").hidden=!on;
  if(on){const t=TRACKS[G.trackIdx];$("#pauseSub").textContent=t.city+", "+t.country+" · "+(G.mode==="race"?G.laps+"-lap race":"Time trial");$("#bResume").focus();}
}
function showResults(){
  const n=TR.n,now=G.time-G.raceStart;
  const rows=G.cars.map(c=>{
    let status="",t=null;
    if(c.retired){status="DNF";t=1e6-c.pf;}
    else if(c.finished)t=c.finishTime+c.pen;
    else t=now+((G.laps*n-c.pf)*STEP)/GAP_SPEED+c.pen;
    if(!c.retired&&c.used.size<2&&G.laps>=3){status="DSQ";t=2e6-c.pf;}
    return{c,t,status};
  }).sort((a,b)=>a.t-b.t);
  let fl=null;for(const r of rows)if(r.c.bestLap!=null&&(fl==null||r.c.bestLap<fl))fl=r.c.bestLap;
  const win=rows[0].t;let pos=0;
  $("#resBody").innerHTML=rows.map((r,i)=>{
    const classified=!r.status;if(classified)pos++;
    const pts=classified&&pos<=10?POINTS[pos-1]:0;
    const time=r.status?(r.status==="DNF"?"DNF · "+r.c.dnf:"DSQ · 1 compound"):i===0?fmt(r.t):"+"+(r.t-win).toFixed(3);
    return`<tr class="${r.c.me?"me":""}"><td>${classified?pos:"–"}</td><td><span class="sw" style="background:${r.c.color}"></span>${r.c.code}</td><td class="r">${time}${r.c.pen&&!r.status?` <small>(+${r.c.pen}s)</small>`:""}</td><td class="tyres">${r.c.stints.map(tyreDot).join("")}</td><td class="r ${r.c.bestLap!=null&&r.c.bestLap===fl?"fl":""}">${fmt(r.c.bestLap)}</td><td class="r">${pts||""}</td></tr>`;
  }).join("");
  const meRow=rows.find(r=>r.c.me),t=TRACKS[G.trackIdx];
  let myPos=0;for(const r of rows){if(!r.status)myPos++;if(r.c.me)break;}
  $("#resTitle").textContent=meRow.status==="DSQ"?"Disqualified":myPos===1?"Race winner":"P"+myPos+" finish";
  $("#resSub").textContent=t.city+", "+t.country+" · "+G.laps+" laps · fastest lap in purple";
  $("#results").hidden=false;
  if(G.net){$("#bAgain").textContent=NET.host?"Back to room":"Waiting for host…";$("#bAgain").disabled=!NET.host;$("#bMenu2").textContent="Leave room";
    clearTimeout(G.resT);G.resT=setTimeout(()=>{if(!$("#results").hidden&&G.net)showResults();},1000);}
  else{$("#bAgain").textContent="Race again";$("#bAgain").disabled=false;$("#bMenu2").textContent="Change track";$("#bAgain").focus();}
}

/* ---------------- Input ---------------- */
const KEYMAP={ArrowUp:"up",KeyW:"up",ArrowDown:"down",KeyS:"down",ArrowLeft:"left",KeyA:"left",ArrowRight:"right",KeyD:"right",Space:"hb",KeyB:"boost"};
window.addEventListener("keydown",e=>{
  const inGame=$("#menu").hidden;
  if(KEYMAP[e.code]&&inGame){input[KEYMAP[e.code]]=true;e.preventDefault();return;}
  if(!inGame){if(e.code==="Enter"&&document.activeElement===document.body&&$("#lobby").hidden)start();return;}
  if(!$("#prerace").hidden){if(e.code==="Enter")preraceGo();return;}
  const p=G.player;
  if(e.code==="Escape"){if(!$("#results").hidden)return;pause(!G.paused);}
  else if(G.paused)return;
  else if(e.code==="KeyR"&&p&&!p.pit){recover(p);if(G.mode==="tt"&&p.maxLaps>=0)p.invalid=true;}
  else if(e.code==="KeyC"){const o=R3.ok?["cockpit","chase","top"]:["top"];setCam(o[(o.indexOf(G.cam)+1)%o.length],true);}
  else if(e.code==="KeyM"){AU.on=!AU.on;toast(AU.on?"Engine sound on":"Engine sound off","yellow",900);}
  else if(e.code==="KeyP")toggleBox();
  else if(e.code==="KeyO"&&p){if(!activateOT(p))toast(G.neutral?"Not under caution":"Overtake not available","yellow",900);}
  else if(e.code==="KeyE"&&p){p.ersMode=p.ersMode==="rec"?"bal":"rec";toast(p.ersMode==="rec"?"Recharge mode":"Balanced mode","yellow",900);}
  else if(p&&(e.code==="Digit1"||e.code==="Digit2"||e.code==="Digit3")){p.nextTyre=["S","M","H"][+e.code.slice(-1)-1];toast("Next tyres: "+TYRES[p.nextTyre].name,"yellow",900);}
});
window.addEventListener("keyup",e=>{if(KEYMAP[e.code])input[KEYMAP[e.code]]=false;});
window.addEventListener("blur",()=>{for(const k in input)input[k]=false;});
document.addEventListener("visibilitychange",()=>{if(document.hidden&&$("#menu").hidden&&!G.paused)pause(true);});
document.querySelectorAll("#touch button").forEach(b=>{
  const k=b.dataset.k,act=b.dataset.act;
  if(act){b.addEventListener("click",()=>{const p=G.player;if(!p)return;if(act==="box")toggleBox();else if(act==="ot")activateOT(p);else if(act==="ers")p.ersMode=p.ersMode==="rec"?"bal":"rec";});return;}
  const on=e=>{e.preventDefault();input[k]=true;b.classList.add("on");},off=()=>{input[k]=false;b.classList.remove("on");};
  b.addEventListener("pointerdown",on);b.addEventListener("pointerup",off);b.addEventListener("pointercancel",off);b.addEventListener("pointerleave",off);
});
$("#go").addEventListener("click",start);
$("#pausebtn").addEventListener("click",()=>pause(true));
$("#bResume").addEventListener("click",()=>pause(false));
$("#bRestart").addEventListener("click",()=>{start();});
$("#bMenu").addEventListener("click",()=>{if(G.net)leaveRoom();toMenu();});
$("#bAgain").addEventListener("click",()=>{if(G.net){if(NET.host)backToLobby();}else start();});
$("#bMenu2").addEventListener("click",()=>{if(G.net)leaveRoom();toMenu();});
$("#bPreGo").addEventListener("click",preraceGo);

/* ---------------- Boot ---------------- */
resize();init3D();resize();
setCam(store.get("loa:cam")||"cockpit",false);
if(!R3.ok){document.querySelectorAll("#cCock,#cChase").forEach(i=>{i.disabled=true;});}
buildMenu();wireLobby();netInit();
const lastI=store.get("loa:last");
selectTrack(Number.isInteger(lastI)&&lastI>=0&&lastI<TRACKS.length?lastI:2);
syncOpts();
requestAnimationFrame(t=>{last=t;frame(t);});
