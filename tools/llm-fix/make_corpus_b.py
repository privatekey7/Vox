"""Corpus B: 300 plain Russian sentences (no Latin, no digits, 6-25 words) from the UD Russian-SynTagRus
test set, seed 7. The text is CC BY-NC-SA: downloaded for a local check, not kept in the repository.

    python3 make_corpus_b.py corpusB.txt
"""
import random
import re
import sys
import urllib.request

URL = "https://raw.githubusercontent.com/UniversalDependencies/UD_Russian-SynTagRus/master/ru_syntagrus-ud-test.conllu"

text = urllib.request.urlopen(URL).read().decode("utf-8")
sentences = [l[9:].strip() for l in text.splitlines() if l.startswith("# text = ")]
plain = [s for s in sentences
         if 6 <= len(s.split()) <= 25 and not re.search(r"[A-Za-z0-9]", s) and s[-1] in ".!?" and s[0].isupper()]
random.seed(7)
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(random.sample(plain, 300)) + "\n")
