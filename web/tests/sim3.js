const { chromium } = require('playwright');
(async()=>{
  const b=await chromium.launch({args:['--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const p=await b.newPage({viewport:{width:900,height:600}});
  const errs=[];p.on('pageerror',e=>errs.push(e.message+' '+e.stack));
  await p.goto('file://'+__dirname+'/test.html');await p.waitForTimeout(1500);
  const only=process.argv[2];
  const res=await p.evaluate((only)=>{
    const T=window.__T,out=[];
    T.G.paused=true; // stop rAF sim
    for(let i=0;i<T.TRACKS.length;i++){
      if(only&&T.TRACKS[i].city!==only)continue;
      T.G.trackIdx=i;T.G.field=8;T.G.diff='pro';T.G.laps=5;T.G.inc='real';T.G.startTyre='M';
      T.newSession('race');T.G.paused=true;
      const pl=T.G.player;pl.ai=true;pl.skill=.95;pl.planLap=2;pl.nextTyre='H';
      let sc=0,vsc=0,prevN=null,steps=0,maxSoc=0,minSoc=9,aeroT=0,otAct=0;
      while(steps<120*900){
        T.update(1/120);steps++;
        const N=T.G.neutral;if(N&&N!==prevN){if(N.type==='SC')sc++;else vsc++;}prevN=N;
        for(const c of T.G.cars){if(c.aero==='straight')aeroT++;if(c.otActive)otAct++;}
        if(pl.soc>maxSoc)maxSoc=pl.soc;if(T.G.started&&pl.soc<minSoc)minSoc=pl.soc;
        if(T.G.cars.every(c=>c.finished||c.retired))break;
      }
      const cs=T.G.cars;
      out.push({city:T.TRACKS[i].city,t:(steps/120).toFixed(0),fin:cs.filter(c=>c.finished).length,dnf:cs.filter(c=>c.retired).length,
        stops:cs.map(c=>c.stops).join(''),one:cs.filter(c=>!c.retired&&c.used.size<2).length,sc,vsc,pen:cs.reduce((a,c)=>a+c.pen,0),
        best:Math.min(...cs.map(c=>c.bestLap||1e9)).toFixed(1),soc:minSoc.toFixed(1)+'-'+maxSoc.toFixed(1),aero:(aeroT/120/8).toFixed(0),ot:(otAct/120).toFixed(0),
        wear:cs.map(c=>c.wear.toFixed(2)).slice(0,3).join(',')});
    }
    return out;
  },only);
  console.table(res);console.log('errors',errs.slice(0,5));
  await b.close();
})();
