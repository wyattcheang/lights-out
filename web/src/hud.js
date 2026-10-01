/* ---------------- HUD ---------------- */
const el={tower:$("#tower"),lapnum:$("#lapnum"),laptime:$("#laptime"),delta:$("#delta"),last:$("#lastline"),spd:$("#spd"),gear:$("#gear"),
  thr:$("#pthr"),brk:$("#pbrk"),lights:$("#lights"),toast:$("#toast"),ww:$("#ww"),flag:$("#flagbar"),flagT:$("#flagText"),flagS:$("#flagSub"),
  soc:$("#socFill"),socV:$("#socVal"),ersMode:$("#ersMode"),cBoost:$("#chipBoost"),cOT:$("#chipOT"),cAero:$("#chipAero"),
  tyreC:$("#tyreC"),tyreW:$("#tyreWear"),tyreBar:$("#tyreBar"),tyreNext:$("#tyreNext"),boxBtn:$("#boxState"),radio:$("#radio"),pitInfo:$("#pitInfo"),lapsLbl:$("#lapsLbl")};
let toastT=null;
function toast(msg,cls,ms){
  el.toast.textContent=msg;el.toast.className="show "+(cls||"");
  clearTimeout(toastT);toastT=setTimeout(()=>{el.toast.className="";},ms||1400);
}
function showRadio(){
  const now=G.time;
  el.radio.innerHTML=G.radioQ.filter(r=>now-r.t<7).map(r=>`<div class="msg ${r.cls||""}"><span class="who">Radio</span>${r.msg}</div>`).join("");
}
function tyreDot(t){return`<span class="tdot" style="--c:${TYRES[t].color}">${t}</span>`;}
let hudTick=-1;
function hud(){
  const p=G.player;
  flagHud();
  if(!p)return;
  el.spd.textContent=Math.round(Math.abs(p.vf)*3.6);
  let gear="N";
  if(p.pit)gear="P";
  else if(p.vf<-.5)gear="R";else if(p.vf>.5){for(let g=1;g<GEARS.length;g++)if(p.vf<GEARS[g]||g===GEARS.length-1){gear=String(g);break;}}
  el.gear.textContent=gear;
  el.thr.style.setProperty("--v",p.thr);el.brk.style.setProperty("--v",p.brk);
  el.ww.hidden=!p.wrong;
  // energy
  const f=p.soc/ERS.CAP;
  el.soc.style.setProperty("--v",Math.min(1.125,f));
  el.soc.dataset.state=p.otActive?"ot":p.boost&&p.dep>0?"boost":p.harv>0?"harv":f<.15?"low":"";
  el.socV.textContent=p.soc.toFixed(1)+" MJ";
  el.ersMode.textContent=p.ersMode==="rec"?"Recharge":"Balanced";
  el.cBoost.dataset.on=p.boost&&p.dep>0?"1":"0";
  el.cOT.dataset.on=p.otActive?"2":p.otArmed?"1":"0";
  el.cOT.textContent=p.otActive?"Overtake on":p.otArmed?"Overtake ready":"Overtake";
  el.cAero.dataset.on=p.aero==="straight"?"1":"0";
  el.cAero.textContent=p.aero==="straight"?"Straight mode":"Corner mode";
  // tyres
  el.tyreC.textContent=p.tyre;el.tyreC.style.setProperty("--c",TYRES[p.tyre].color);
  const life=Math.max(0,1-p.wear);el.tyreW.textContent=Math.round(life*100)+"%";
  el.tyreBar.style.setProperty("--v",life);el.tyreBar.dataset.state=life<.25?"low":life<.5?"mid":"";
  if(el.tyreNext.dataset.k!==p.nextTyre){el.tyreNext.dataset.k=p.nextTyre;el.tyreNext.innerHTML=["S","M","H"].map(t=>`<button class="tsel${p.nextTyre===t?" on":""}" data-t="${t}" style="--c:${TYRES[t].color}" aria-label="Next tyre ${TYRES[t].name}">${t}</button>`).join("");}
  let box="Box: P";
  if(p.pit)box=p.pit.ph==="stop"?"Stationary "+Math.max(0,p.pit.stopTime-p.pit.stop).toFixed(1)+" s":"Pit limiter 80";
  else if(p.boxReq)box="Box this lap";
  el.boxBtn.textContent=box;el.boxBtn.dataset.on=p.boxReq||p.pit?"1":"0";
  const need=G.mode==="race"&&p.used.size<2&&!p.pit;
  el.pitInfo.textContent=need?"Mandatory stop: use a 2nd compound":(p.dmg>.05?"Front wing damage "+Math.round(p.dmg*100)+"%":"Stops "+p.stops+" · "+p.stints.join(" › "));
  el.pitInfo.dataset.warn=need||p.dmg>.05?"1":"0";
  // start lights
  if(G.mode==="race"&&!G.started){
    el.lights.hidden=false;const lit=Math.min(5,Math.floor(G.time-.4));
    [...el.lights.children].forEach((pod,i)=>pod.classList.toggle("on",i<lit));
  }else if(G.mode==="race"&&G.time-G.raceStart<1.2){[...el.lights.children].forEach(pod=>pod.classList.remove("on"));}
  else el.lights.hidden=true;
  const n=TR.n,now=G.time;
  const cur=p.maxLaps>=0?now-p.lapStart:0;
  if(G.mode==="race"){
    el.lapsLbl.textContent="Lap";
    el.lapnum.textContent=Math.min(G.laps,Math.max(1,p.maxLaps+1))+"/"+G.laps;
    el.laptime.textContent=fmt(p.finished?p.finishTime:(G.started?now-G.raceStart:0));
    const N=G.neutral;
    if(N&&N.type==="VSC"&&!p.finished){const d=p.vscDelta;el.delta.textContent="VSC Δ "+(d>=0?"+":"−")+Math.abs(d).toFixed(1);el.delta.className="mono "+(d>=0?"dn":"up");}
    else{el.delta.textContent=p.pen?("+"+p.pen+" s penalty"):"";el.delta.className="mono up";}
    el.last.textContent="Last "+fmt(p.lastLap);
  }else{
    el.lapsLbl.textContent="Lap";
    el.lapnum.textContent=String(Math.max(1,p.maxLaps+1));
    el.laptime.textContent=p.maxLaps>=0?fmt(cur):"Out lap";
    const rec=G.rec;
    if(p.maxLaps>=0&&rec&&rec.splits){
      const pos=((p.prog%n)+n)%n,si=Math.floor(pos/10),ref=rec.splits[si];
      if(ref!=null&&!p.invalid){const d=cur-ref;el.delta.textContent=(d>=0?"+":"−")+Math.abs(d).toFixed(3);el.delta.className="mono "+(d>=0?"up":"dn");}
      else{el.delta.textContent=p.invalid?"Invalid":"";el.delta.className="mono up";}
    }else{el.delta.textContent=p.invalid?"Invalid":"";el.delta.className="mono up";}
    el.last.textContent="Best "+fmt(rec&&rec.t);
  }
  if(++hudTick%6)return;
  showRadio();
  if(G.mode==="race")tower();
  else{
    const sc=G.secCls||[null,null,null],lv=G.liveSec||[null,null,null];
    let html='<div class="hd"><span>Time trial</span><span>Sectors</span></div><div class="sec">';
    for(let i=0;i<3;i++){const done=p.maxLaps>=0&&(i<p.secIdx)&&lv[i]!=null;html+=`<div class="${done?sc[i]:""}">${done?lv[i].toFixed(2):"S"+(i+1)}</div>`;}
    html+="</div>";
    html+=`<div class="kv"><span>Last</span><span>${fmt(p.lastLap)}</span></div>`;
    html+=`<div class="kv"><span>Session best</span><span>${fmt(p.bestLap)}</span></div>`;
    const ideal=G.rec&&G.rec.bs&&G.rec.bs.every(x=>x!=null)?G.rec.bs[0]+G.rec.bs[1]+G.rec.bs[2]:null;
    html+=`<div class="kv"><span>Ideal lap</span><span>${fmt(ideal)}</span></div>`;
    el.tower.innerHTML=html;
  }
}
function tower(){
  const n=TR.n,o=order(),lead=o[0];
  const lapNow=Math.min(G.laps,Math.max(1,(lead?lead.maxLaps:0)+1));
  let html=`<div class="hd"><span>Lap ${lapNow}/${G.laps}</span><span>Interval</span></div>`;
  o.forEach((c,i)=>{
    let gap="Interval";
    if(i===0)gap="Leader";
    else{const a=o[i-1];
      if(c.retired)gap="DNF";
      else if(a.finished&&c.finished)gap="+"+((c.finishTime+c.pen)-(a.finishTime+a.pen)).toFixed(1);
      else{const dp=(a.finished?G.laps*n:a.pf)-c.pf;gap=dp>n?"+1 lap":"+"+(dp*STEP/GAP_SPEED).toFixed(1);}
    }
    if(!G.started)gap=i===0?"Pole":"";
    const tag=c.retired?"":c.pit?'<span class="tag pit">PIT</span>':c.recover>0&&c.stops&&c.prog%n<TR.PX1+10?'<span class="tag out">OUT</span>':c.pen?`<span class="tag pen">+${c.pen}</span>`:c.otActive?'<span class="tag ot">OT</span>':"";
    html+=`<div class="row${c.me?" me":""}${c.retired?" dnf":""}"><span class="pos">${i+1}</span><span class="sw" style="background:${c.color}"></span><span class="code">${c.code}</span>${tag}<span class="gap">${gap}</span>${tyreDot(c.tyre)}</div>`;
  });
  el.tower.innerHTML=html;
}
function flagHud(){
  const N=G.neutral,p=G.player;
  let cls="",t="",s="";
  if(N&&N.type==="SC"){cls="sc";t="Safety Car";s=N.phase==="in"?"Safety Car in this lap":N.phase==="restart"?"Leader controls the restart":"No overtaking";}
  else if(N&&N.type==="VSC"){cls="vsc";t=N.phase==="ending"?"VSC ending":"Virtual Safety Car";s="Keep the delta positive";}
  else if(p&&p.blue){cls="blue";t="Blue flag";s="Let the faster car through";}
  else if(p&&sectorYellow(p.idx)===2){cls="yellow";t="Double yellow";s="Sector "+(sectorOf(p.idx)+1)+" · slow down, be ready to stop";}
  else if(p&&sectorYellow(p.idx)===1){cls="yellow";t="Yellow flag";s="Sector "+(sectorOf(p.idx)+1)+" · no overtaking";}
  else if(G.time<(G.bwUntil||0)){cls="bw";t="Black and white flag";s="Track limits";}
  else if(G.time<G.greenUntil){cls="green";t="Green flag";s="Track clear";}
  if(!t||G.mode==="attract"){el.flag.hidden=true;return;}
  el.flag.hidden=false;el.flag.className=cls;el.flagT.textContent=t;el.flagS.textContent=s;
}
el.tyreNext.addEventListener("click",e=>{const b=e.target.closest("[data-t]");if(b&&G.player){G.player.nextTyre=b.dataset.t;}});
el.boxBtn.addEventListener("click",()=>toggleBox());
function toggleBox(){
  const p=G.player;if(!p||G.mode!=="race"||p.pit||p.finished)return;
  p.boxReq=!p.boxReq;
  radio(p.boxReq?"Box this lap, box this lap. "+TYRES[p.nextTyre].name+"s ready.":"Stay out, stay out.",p.boxReq?"":"yellow");
}

/* ---------------- Audio ---------------- */
const AU={ctx:null,on:true};
function audioInit(){
  if(AU.ctx){AU.ctx.resume&&AU.ctx.resume();return;}
  try{
    const A=new(window.AudioContext||window.webkitAudioContext)();
    const o1=A.createOscillator(),o2=A.createOscillator(),o3=A.createOscillator(),f=A.createBiquadFilter(),g=A.createGain(),g3=A.createGain();
    o1.type="sawtooth";o2.type="square";o3.type="sine";f.type="lowpass";f.frequency.value=1600;f.Q.value=4;g.gain.value=0;g3.gain.value=0;
    o1.connect(f);o2.connect(f);f.connect(g);g.connect(A.destination);o3.connect(g3);g3.connect(A.destination);o1.start();o2.start();o3.start();
    Object.assign(AU,{ctx:A,o1,o2,o3,f,g,g3});
  }catch(e){}
}
function audioUpdate(){
  if(!AU.ctx)return;
  const p=G.player,t=AU.ctx.currentTime;
  const active=AU.on&&p&&!G.paused&&G.mode!=="attract"&&document.visibilityState==="visible";
  let rpm=4000;
  if(p){const v=Math.abs(p.vf);let lo=0,hi=GEARS[1];for(let g=1;g<GEARS.length;g++){if(v<GEARS[g]||g===GEARS.length-1){lo=GEARS[g-1];hi=GEARS[g];break;}}
    rpm=p.pit?7000:v<1?4000+p.thr*3000:6500+5000*clamp((v-lo)/(hi-lo),0,1);}
  const fq=rpm/60*1.6;
  AU.o1.frequency.setTargetAtTime(fq,t,.03);AU.o2.frequency.setTargetAtTime(fq*.5,t,.03);
  AU.f.frequency.setTargetAtTime(900+(p?p.thr:0)*1800,t,.05);
  AU.g.gain.setTargetAtTime(active?.022+(p?p.thr:0)*.03:0,t,.05);
  // MGU-K whine when deploying or harvesting
  AU.o3.frequency.setTargetAtTime(600+Math.abs(p?p.vf:0)*22,t,.05);
  AU.g3.gain.setTargetAtTime(active&&p&&(p.dep>0||p.harv>.2)?.008:0,t,.08);
}
