# Builds english.txt.gz from SCOWL final/: words of levels 10-80, lower case a-z only.
# Proper names and capitalised words (london, jamaica) are kept from level 70 on: they only protect themselves.
import sys, re, gzip, os
F = sys.argv[1]; best = {}
L = [10, 20, 35, 40, 50, 55, 60, 70, 80]
for lvl in L:
    for pre in ["english", "american", "british"]:
        for kind, floor in [("words", 0), ("upper", 70), ("proper-names", 70)]:
            p = f"{F}/{pre}-{kind}.{lvl}"
            if not os.path.exists(p): continue
            for w in open(p, encoding="latin-1").read().split():
                w2 = w.lower() if kind != "words" else w
                if re.fullmatch(r"[a-z]+", w2):
                    lv = max(lvl, floor)
                    if lv < best.get(w2, 999): best[w2] = lv
out = []
for lvl in L:
    out.append(f"#{lvl}"); out += sorted(w for w, l in best.items() if l == lvl)
data = ("\n".join(out) + "\n").encode()
with gzip.GzipFile("english.txt.gz", "wb", mtime=0, compresslevel=9) as g: g.write(data)
print(len(best), sum(1 for l in best.values() if l <= 60), os.path.getsize("english.txt.gz"))
