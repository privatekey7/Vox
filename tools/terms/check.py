"""Check a terms source file ("## group" / "Target = v1, v2" lines) against the Russian frequency list
and the existing dictionary. Usage: VOX_FREQ=ru_full.txt python3 tools/terms/check.py FILE
The frequency list is hermitdave/FrequencyWords content/2018/ru/ru_full.txt (CC BY-SA, not in the repo)."""
import json,os,sys,re
FREQ=os.environ.get('VOX_FREQ','ru_full.txt')
existing=json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'../../src/Vox.Core/Text/Data/terms.json'),encoding='utf-8'))
ex_src={s.lower():t for g in existing.values() for t,ss in g.items() for s in ss}
ex_tgt={t.lower() for g in existing.values() for t in g}
rank={}
for i,l in enumerate(open(FREQ,encoding='utf-8')):
    w,c=l.split()
    if int(c)<5: break
    rank.setdefault(w,i+1)
Hard=["а","у","ом","е","ы","ов","ам","ами","ах"];AYa=["ы","и","е","у","ой","ою","ам","ами","ах"];Soft=["я","ю","ем","и","ей","е"];Yot=["я","ю","ем","е","ев","и"]
cons="бвгджзклмнпрстфхцчшщ"
def forms(w):
    if len(w)<4 or not all('а'<=c<='я' or c=='ё' for c in w): return []
    l=w[-1]
    st,e=(w[:-1],AYa) if l in 'ая' else (w[:-1],Soft) if l=='ь' else (w[:-1],Yot) if l=='й' else (w,Hard) if l in cons else (None,None)
    return [st+x for x in e] if st else []
seen={}
problems=0
for n,ln in enumerate(open(sys.argv[1],encoding='utf-8'),1):
    ln=ln.rstrip('\n')
    if not ln.strip() or ln.startswith('#'): continue
    if ' = ' not in ln: print(f'{n}: BAD LINE {ln}'); problems+=1; continue
    t,_,v=ln.partition(' = ')
    t=t.strip()
    if t.lower() in ex_tgt: print(f'{n}: target already in terms.json: {t}'); problems+=1
    for x in [x.strip() for x in v.split(',') if x.strip()]:
        k=x.lower()
        if not re.fullmatch(r"[\w]+(?:-[\w]+)*(?: [\w]+(?:-[\w]+)*)*",k): print(f'{n}: bad source shape "{x}"'); problems+=1
        if k in ex_src: print(f'{n}: source "{x}" already maps to {ex_src[k]}'); problems+=1
        if k in seen and seen[k]!=t: print(f'{n}: source "{x}" also used for {seen[k]}'); problems+=1
        seen[k]=t
        if ' ' not in k:
            if k in rank and rank[k]<=400000: print(f'{n}: COMMON WORD "{x}" (rank {rank[k]}) → {t}'); problems+=1
            for f in forms(k):
                if len(f)>=5 and f in rank and rank[f]<=400000: print(f'{n}: common case form "{f}" of "{x}" (rank {rank[f]}) → {t}'); problems+=1
print(f'{problems} problems')
