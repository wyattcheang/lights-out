import json,numpy as np,os
from scipy.spatial import cKDTree
T=json.load(open('data/tracks.json'))
out={}
for t in T:
    f=f"tel/{t['id']}.json"
    if not os.path.exists(f):continue
    d=json.load(open(f))
    Q=np.c_[np.array(d['x'])/10,np.array(d['y'])/10];Z=np.array(d['z'])/10
    P=np.array(t['p']).reshape(-1,2)
    # densify P
    seg=[];
    for i in range(len(P)):
        a,b=P[i],P[(i+1)%len(P)];k=max(1,int(np.linalg.norm(b-a)/5))
        for s in range(k):seg.append(a+(b-a)*s/k)
    Pd=np.array(seg);tree=cKDTree(Pd)
    pc=Pd.mean(0);best=None
    for refl in (1,-1):
        Qr=Q*np.array([1,refl]);qc=Qr.mean(0)
        for ang in np.radians(np.arange(0,360,2)):
            R=np.array([[np.cos(ang),-np.sin(ang)],[np.sin(ang),np.cos(ang)]])
            A=(Qr-qc)@R.T+pc
            e=tree.query(A)[0].mean()
            if best is None or e<best[0]:best=(e,refl,ang)
    e,refl,ang=best
    Qr=Q*np.array([1,refl]);qc=Qr.mean(0)
    R=np.array([[np.cos(ang),-np.sin(ang)],[np.sin(ang),np.cos(ang)]]);A=(Qr-qc)@R.T+pc
    # ICP refine (rigid)
    for it in range(30):
        dd,ii=tree.query(A);M=Pd[ii]
        ma,mm=A.mean(0),M.mean(0);H=(A-ma).T@(M-mm);U,S,Vt=np.linalg.svd(H);Rr=Vt.T@U.T
        if np.linalg.det(Rr)<0:Vt[-1]*=-1;Rr=Vt.T@U.T
        A=(A-ma)@Rr.T+mm
    err=cKDTree(Pd).query(A)[0]
    # progress-consistent elevation for each raw vertex
    N=len(A);tA=cKDTree(A)
    cum=np.r_[0,np.cumsum(np.linalg.norm(np.diff(np.r_[P,P[:1]],axis=0),axis=1))];Ltot=cum[-1];fr=cum[:-1]/Ltot
    _,j=tA.query(P);jf=j/N
    # direction: compare progress increments
    dj=np.diff(np.unwrap(jf*2*np.pi))/(2*np.pi);direction=1 if np.median(dj)>0 else -1
    off=np.angle(np.mean(np.exp(1j*2*np.pi*(jf-direction*fr))))/(2*np.pi)
    z=[]
    for k in range(len(P)):
        exp=(off+direction*fr[k])%1*N;w=int(0.06*N)
        idx=np.arange(int(exp)-w,int(exp)+w)%N
        dk=np.linalg.norm(A[idx]-P[k],axis=1);z.append(Z[idx[np.argmin(dk)]])
    z=np.array(z)
    # smooth along track by arclength (~40 m window) on raw vertices, circular
    zs=z.copy()
    for k in range(len(P)):
        dists=np.abs(cum[:-1]-cum[k]);dists=np.minimum(dists,Ltot-dists);w=np.exp(-(dists/25)**2);zs[k]=(w*z).sum()/w.sum()
    zs-=zs.min()
    # start index: raw vertex nearest the telemetry lap start (timing line)
    k0=int(np.argmin(np.linalg.norm(P-A[0],axis=1)))
    out[t['id']]={'z':[round(float(v),2) for v in zs],'dir':int(direction),'k0':k0,'err':round(float(err.mean()),1)}
    print(f"{t['city']:14} align mean {err.mean():5.1f}m p90 {np.percentile(err,90):5.1f} dir {direction} refl {refl} elev {zs.max():5.1f}m")
json.dump(out,open('data/elev.json','w'))
