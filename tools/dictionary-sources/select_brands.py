#!/usr/bin/env python3
"""Отбор брендов из выгрузок fetch_sources.py: src/Vox.Core/Text/Data/brands.txt (имена, по одному в строке)
и selected-brands.tsv рядом (имя, источник: откуда взято).

Использование: python tools/dictionary-sources/select_brands.py

Правило одно для всех источников (подробности и пороги: README.md):
  1. Место в рейтинге: SO-теги и Hugging Face отбираются выгрузкой, Homebrew Casks топ-300, монеты
     CoinGecko топ-150, биржи топ-60, протоколы DefiLlama топ-150, организации и семейства Hugging Face
     от 2 упоминаний.
  2. Имя: одно слово из латинских букв, 5+ букв, без цифр и знаков, с заглавной буквой (строчное ещё
     не название: «printf», «dataframe»), но не из одних заглавных (тикеры и аббревиатуры произносятся по
     буквам) и не больше одного перехода «строчная → заглавная» внутри (UITableView, BetterTouchTool:
     идентификаторы кода и составные названия не произносят целиком) и не из двух английских слов подряд
     («FineTune», «readFile»: это описание, а не название); имя из тега Stack Overflow ещё и с
     заглавной внутри (одна заглавная в начале в описании тега бывает просто началом предложения); не форма слова из словаря
     («Webhooks» при «webhook»). Хвостовые общие слова («Exchange», «Labs»,
     «Wallet»: английские SCOWL уровня <= 50) отбрасываются: «Uniswap Labs» → «Uniswap»; если после этого
     осталось два слова, имя не берётся.
  3. Не обычное английское слово: остаток имени не целиком из слов SCOWL (любого уровня), иначе
     «Spring», «Polygon» стали бы заменять обычную речь.
  4. Нового в словаре нет: имя (без регистра, с пробелами и без) не совпадает с целью или написанием
     в terms.json.
Бренды идут в `brands.txt`, а не в terms.json: только цели поиска по звучанию (FuzzyTerms), без точной
подмены и без исправления регистра («config» не должен стать «Config»). Русские произношения не
генерируются: кириллицу и искажения к бренду приводит поиск по звучанию.
"""
import collections, glob, gzip, json, os, re, sys, unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
STAMP = "2026-10"
CUTOFF = {"homebrew-casks": 1500, "coingecko-coins": 300, "coingecko-exchanges": 100, "defillama-protocols": 300,
          "huggingface-orgs": None, "huggingface-families": None, "stackoverflow-names": None}  # None: как выгружено (HF: счётчик >= 2)
GENERIC_LEVEL = 50

english, level = {}, 0
with gzip.open(os.path.join(ROOT, "src/Vox.Core/Text/Data/english.txt.gz"), "rt", encoding="utf8") as f:
    for line in f:
        line = line.rstrip("\n")
        if line.startswith("#"):
            level = int(line[1:])
        elif line:
            english.setdefault(line, level)

terms = json.load(open(os.path.join(ROOT, "src/Vox.Core/Text/Data/terms.json"), encoding="utf8"))
known = set()
for group in terms.values():
    for target, sources in group.items():
        for s in [target, *sources]:
            known.add(s.lower())
            known.add(s.lower().replace(" ", ""))


def clean(name):
    name = unicodedata.normalize("NFKC", name)
    name = re.sub(r"\s*[\(\[].*?[\)\]]", "", name).strip()
    words = name.split()
    # хвостовые общие слова: «Uniswap Labs» → «Uniswap», «Coinbase Exchange» → «Coinbase»
    while len(words) > 1 and english.get(words[-1].lower(), 99) <= GENERIC_LEVEL:
        words.pop()
    if len(words) != 1 or not re.fullmatch(r"[A-Za-z]{5,}", words[0]) or words[0].isupper() or words[0].islower():
        return None
    name = words[0]
    if len(re.findall(r"[a-z][A-Z]", name)) > 1:
        return None
    lower = name.lower()
    if any(w in english or w in known for w in (lower, lower.removesuffix("s"), lower.removesuffix("es"))):
        return None
    # Два английских слова подряд («readFile», «FineTune», «MonoGame»): описание, а не название.
    parts = re.findall(r"[A-Z]?[a-z]+", name)
    if len(parts) > 1 and all(p.lower() in english for p in parts):
        return None
    return name


chosen = collections.OrderedDict()  # имя без регистра → (имя, источник)
for path in sorted(glob.glob(os.path.join(HERE, f"*-{STAMP}.tsv"))):
    source = os.path.basename(path)[: -len(f"-{STAMP}.tsv")]
    cutoff = CUTOFF[source]
    for line in open(path, encoding="utf8"):
        name, _, n = line.rstrip("\n").partition("\t")
        if source.startswith("huggingface"):
            ok = int(n) >= 2
        else:
            ok = cutoff is None or int(n) <= cutoff
        if source == "stackoverflow-names" and not re.search(r"[a-z][A-Z]", name):
            ok = False  # без заглавной внутри: начало предложения в описании тега («Config», «Prisma»), не название
        if ok and (c := clean(name)) and c.lower() not in chosen:
            chosen[c.lower()] = (c, source)

counts = collections.Counter(s for _, s in chosen.values())
with open(os.path.join(ROOT, "src/Vox.Core/Text/Data/brands.txt"), "w", encoding="utf8", newline="\n") as f:
    f.write("".join(f"{name}\n" for name, _ in chosen.values()))
with open(os.path.join(HERE, "selected-brands.tsv"), "w", encoding="utf8", newline="\n") as f:
    f.write("".join(f"{name}\t{source}\n" for name, source in chosen.values()))
print("selected", len(chosen), dict(counts))
