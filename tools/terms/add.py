"""Merge terms written as "## group" / "Target = вариант1, вариант2" lines into terms.json.
Usage: python3 tools/terms/add.py FILE [FILE...]   (run tools/terms/check.py on each file first)"""
import json
import os
import sys

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '../../src/Vox.Core/Text/Data/terms.json')
groups = json.load(open(path, encoding='utf-8'))
seen = {s.lower(): t for g in groups.values() for t, ss in g.items() for s in ss}
added = 0
for file in sys.argv[1:]:
    group = None
    for line in open(file, encoding='utf-8'):
        line = line.rstrip('\n')
        if line.startswith('## '):
            group = groups.setdefault(line[3:].strip(), {})
            continue
        if not line.strip() or line.startswith('#'):
            continue
        target, _, sources = line.partition(' = ')
        target = target.strip()
        bucket = group.setdefault(target, [])
        for source in (s.strip() for s in sources.split(',')):
            if not source:
                continue
            if source.lower() in seen:
                if seen[source.lower()] != target:
                    print(f'skipped "{source}": already → {seen[source.lower()]}')
                continue
            seen[source.lower()] = target
            bucket.append(source)
            added += 1
        if not bucket:
            del group[target]
json.dump(groups, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(f'{added} sources added; {sum(len(g) for g in groups.values())} terms, {len(seen)} sources in total')
