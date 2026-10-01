import json,subprocess,os,re,sys
T=json.load(open('data/tracks.json'))
L={t['id']:t['len'] for t in T}
M={'au-1953':('ti25','Australian Grand Prix'),'cn-2004':('ti25','Chinese Grand Prix'),'jp-1962':('ti25','Japanese Grand Prix'),
'bh-2002':('ti25','Bahrain Grand Prix'),'sa-2021':('ti25','Saudi Arabian Grand Prix'),'us-2022':('ti25','Miami Grand Prix'),
'ca-1978':('ti25','Canadian Grand Prix'),'mc-1929':('ti25','Monaco Grand Prix'),'es-1991':('ti25','Spanish Grand Prix'),
'at-1969':('ti25','Austrian Grand Prix'),'gb-1948':('ti25','British Grand Prix'),'be-1925':('ti25','Belgian Grand Prix'),
'hu-1986':('ti25','Hungarian Grand Prix'),'nl-1948':('ti25','Dutch Grand Prix'),'it-1922':('ti25','Italian Grand Prix'),
'az-2016':('ti25','Azerbaijan Grand Prix'),'sg-2008':('ti25','Singapore Grand Prix'),'us-2012':('ti25','United States Grand Prix'),
'mx-1962':('ti25','Mexico City Grand Prix'),'br-1940':('ti25','São Paulo Grand Prix'),'us-2023':('ti25','Las Vegas Grand Prix'),
'qa-2004':('ti25','Qatar Grand Prix'),'ae-2009':('ti25','Abu Dhabi Grand Prix'),'it-1953':('ti25','Emilia Romagna Grand Prix'),
'es-2026':('ti26','Spanish Grand Prix'),'fr-1969':('ti2022','French Grand Prix'),'de-1932':('ti2019','German Grand Prix'),
'ru-2014':('ti2021','Russian Grand Prix'),'de-1927':('ti2020','Eifel Grand Prix'),'pt-2008':('ti2021','Portuguese Grand Prix'),
'it-1914':('ti2020','Tuscan Grand Prix'),'tr-2005':('ti2021','Turkish Grand Prix')}
os.makedirs('tel',exist_ok=True)
listing={}
def files(repo):
    if repo not in listing:
        out=subprocess.run(['git','-C',repo,'-c','core.quotepath=off','ls-tree','-r','HEAD','--name-only'],capture_output=True,text=True).stdout.splitlines()
        listing[repo]=out
    return listing[repo]
for tid,(repo,ev) in M.items():
    if os.path.exists(f'tel/{tid}.json'):continue
    fs=[f for f in files(repo) if f.startswith(ev+'/') and f.endswith('_tel.json')]
    sess=None
    for s in ['Qualifying','Race','Practice 3','Practice 2','Practice 1','Sprint']:
        if any(f.startswith(f'{ev}/{s}/') for f in fs): sess=s;break
    cands=[f for f in fs if f.startswith(f'{ev}/{sess}/')]
    # prefer mid-session laps
    def key(f):
        m=re.search(r'/(\d+)_tel\.json$',f);return abs(int(m.group(1))-6) if m else 99
    cands.sort(key=key)
    ok=False
    for f in cands[:25]:
        r=subprocess.run(['git','-C',repo,'checkout','HEAD','--',f],capture_output=True,text=True)
        try:d=json.load(open(os.path.join(repo,f)))['tel']
        except Exception as e:continue
        dist=d['distance'][-1];sp=min(d['speed']) if d['speed'] else 0
        if abs(dist-L[tid])/L[tid]<0.06 and sp>35 and len(d['x'])>200:
            json.dump({'x':d['x'],'y':d['y'],'z':d['z'],'dist':d['distance'],'src':f,'repo':repo},open(f'tel/{tid}.json','w'))
            print(tid,'OK',f,round(dist),L[tid],len(d['x']),'zrange',round((max(d['z'])-min(d['z']))/10,1),flush=True);ok=True;break
    if not ok:print(tid,'FAIL',sess,len(cands),flush=True)
