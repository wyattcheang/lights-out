"""Assemble web/src into a single-file page: web/dist/lights-out.html (plus test pages for the headless tests).
Run from anywhere: python web/build.py"""
import os
ROOT = os.path.dirname(os.path.abspath(__file__))
SRC, DIST = os.path.join(ROOT, 'src'), os.path.join(ROOT, 'dist')
THREE = 'https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js'
read = lambda p: open(p, encoding='utf-8').read()
js = ''.join(read(os.path.join(SRC, f)) + '\n' for f in ['core.js', 'sim.js', 'render2d.js', 'carmodel.js', 'render3d.js', 'hud.js', 'net.js', 'main.js'])
js = js.replace('__TRACKS__', read(os.path.join(ROOT, '..', 'data', 'tracks3.json')))
page = read(os.path.join(SRC, 'head.html')) + read(os.path.join(SRC, 'body.html')) + f'\n<script src="{THREE}"></script>\n<script>\n(()=>{{\n' + js + '})();\n</script>\n'
os.makedirs(DIST, exist_ok=True)
open(os.path.join(DIST, 'lights-out.html'), 'w', encoding='utf-8').write(page)
# test pages: expose internals as window.__T; test_mp.html adds a BroadcastChannel mock of the room capability
hook = 'window.__T={G,update,newSession,TRACKS,recover,R3,order,deploySC,deployVSC,startIncident,activateOT,toggleBox,frame,NET,get TR(){return TR}};\n/* ---------------- Boot ---------------- */'
test = page.replace(THREE, 'three.min.js').replace('/* ---------------- Boot ---------------- */', hook)
pre = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body>'
tests = os.path.join(ROOT, 'tests')
open(os.path.join(tests, 'test.html'), 'w', encoding='utf-8').write(pre + test + '</body></html>')
open(os.path.join(tests, 'test_mp.html'), 'w', encoding='utf-8').write(pre + '<script src="mockroom.js"></script>' + test + '</body></html>')
print('built', len(page), 'bytes')
