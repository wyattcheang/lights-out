(()=>{
const me=Math.random().toString(36).slice(2,10);
const bc=new BroadcastChannel("mockroom");
function mkRoom(name){
  const peers=new Map();let mine={};const lis=[];
  const self=()=>({peer:me,by:"u_"+me,isMe:true,sameTab:true,kind:"viewer",guest:false,presence:mine,updatedAt:Date.now()});
  let snap=[];
  const fire=(joined,left,updated)=>{snap=Object.freeze([self(),...peers.values()]);for(const f of lis)setTimeout(()=>f({peers:snap,joined,left,updated}),0);};
  bc.addEventListener("message",e=>{const m=e.data;if(m.room!==name||m.peer===me)return;
    if(m.t==="pres"){const had=peers.has(m.peer);const p={peer:m.peer,by:"u_"+m.peer,isMe:false,sameTab:false,kind:"viewer",guest:false,presence:Object.freeze(m.p),updatedAt:Date.now()};peers.set(m.peer,p);fire(had?[]:[p],[],had?[p]:[]);}
    if(m.t==="hello")bc.postMessage({t:"pres",room:name,peer:me,p:mine});
    if(m.t==="bye"){const p=peers.get(m.peer);if(p){peers.delete(m.peer);fire([],[p],[]);}}});
  bc.postMessage({t:"hello",room:name,peer:me});
  return{name,
    presence:async patch=>{mine=Object.freeze(Object.fromEntries(Object.entries({...mine,...patch}).filter(([k,v])=>v!==null)));bc.postMessage({t:"pres",room:name,peer:me,p:mine});fire([],[],[self()]);},
    peers:()=>snap.length?snap:[self()],
    onPeers:f=>{lis.push(f);setTimeout(()=>f({peers:[self(),...peers.values()],joined:[self(),...peers.values()],left:[],updated:[]}),0);return()=>{};},
    onConnection:f=>{setTimeout(()=>f(true),0);return()=>{};},connected:()=>true,
    emit:async()=>{},on:()=>()=>{},
    leave:async()=>{bc.postMessage({t:"bye",room:name,peer:me});}};
}
const lobby=mkRoom("");
lobby.join=async n=>mkRoom(n);
const user={me:async()=>({id:"u_"+me,name:"Tester "+me.slice(0,3),avatarUrl:"",color:"#888",email:null,isOwner:false,canEdit:false}),
  profiles:async ids=>Object.fromEntries((Array.isArray(ids)?ids:[ids]).map(id=>[id,{id,name:"Driver "+id.slice(2,5),avatarUrl:"",color:"#888",email:null,isMe:false,guest:false}]))};
window.claude={use:async n=>n==="room"?lobby:n==="user"?user:null};
})();
