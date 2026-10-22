#!/usr/bin/env python3
"""Выгрузка внешних рейтингов названий для select_brands.py. Результат кладётся рядом, по файлу на источник.

Использование: python tools/dictionary-sources/fetch_sources.py [источник ...]   (без аргументов: все)
Источники (все открытые, без ключей API; каждый файл: «название<TAB>место в рейтинге или число упоминаний»):
  stackoverflow-names  написание тегов Stack Overflow (из википедии тега: stackoverflow-tags-2026-10.txt)
  huggingface-names    названия организаций Hugging Face (fullname) и семейства моделей (CamelCase-слова
                       в идентификаторах huggingface-models-2026-10.txt)
  homebrew-casks       Homebrew Casks, топ приложений по установкам за 365 дней (показываемое имя)
  coingecko            CoinGecko: монеты по капитализации и биржи по рейтингу доверия
  defillama            DefiLlama: протоколы по TVL
"""
import collections, concurrent.futures, json, os, re, sys, time, urllib.parse, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
STAMP = "2026-10"


def get(url, retries=3):
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "vox-dictionary-sources", "Accept-Encoding": "identity"})
            with urllib.request.urlopen(req, timeout=20) as r:
                return json.load(r)
        except Exception as e:  # сеть/лимиты: подождать и повторить
            print("retry", url[:80], e, file=sys.stderr)
            time.sleep(5 * (attempt + 1))
    raise RuntimeError(url)


def save(name, rows):
    with open(os.path.join(HERE, f"{name}-{STAMP}.tsv"), "w", encoding="utf8") as f:
        for n, rank in rows:
            f.write(f"{n}\t{rank}\n")
    print(name, len(rows))


def stackoverflow_names():
    tags = [t.strip() for t in open(os.path.join(HERE, f"stackoverflow-tags-{STAMP}.txt"), encoding="utf8") if t.strip()]
    tags = [(i + 1, t) for i, t in enumerate(tags) if re.fullmatch(r"[a-z]{5,}", t)]
    rows = []
    for k in range(0, len(tags), 20):
        chunk = tags[k:k + 20]
        data = get("https://api.stackexchange.com/2.3/tags/" + ";".join(t for _, t in chunk) + "/wikis?site=stackoverflow", retries=1)
        excerpts = {i["tag_name"]: re.sub(r"<[^>]+>", " ", i.get("excerpt", "")) for i in data["items"]}
        for rank, tag in chunk:
            m = re.search(rf"(?<![A-Za-z]){tag}(?![A-Za-z])", excerpts.get(tag, ""), re.IGNORECASE)
            if m:
                rows.append((m.group(0), rank))  # написание как в первом упоминании в описании тега
        time.sleep(1)
    save("stackoverflow-names", rows)


def huggingface_names():
    ids = [l.strip() for l in open(os.path.join(HERE, f"huggingface-models-{STAMP}.txt"), encoding="utf8") if "/" in l]
    orgs = collections.Counter(i.split("/")[0] for i in ids)
    def fullname(org):
        try:
            return get(f"https://huggingface.co/api/organizations/{org}/overview", retries=1).get("fullname", "").strip()
        except Exception:
            return ""  # не организация (пользователь) или нет ответа: пропуск

    with concurrent.futures.ThreadPoolExecutor(8) as pool:
        names = list(pool.map(fullname, orgs))
    rows = [(full, n) for full, n in zip(names, orgs.values()) if full]
    save("huggingface-orgs", rows)
    fam = collections.defaultdict(set)
    for i in ids:
        for tok in re.split(r"[-_. /]", i.split("/", 1)[1]):
            if re.fullmatch(r"[A-Za-z]{5,}", tok) and re.search(r"[a-z][A-Z]", tok):  # внутри заглавная: MiniLM, DeBERTa
                fam[tok].add(i)
    save("huggingface-families", [(t, len(s)) for t, s in sorted(fam.items(), key=lambda p: -len(p[1])) if len(s) >= 2])


def homebrew_casks():
    top = [i for i in get("https://formulae.brew.sh/api/analytics/cask-install/365d.json")["items"] if not i["cask"].startswith("font-")][:1500]  # шрифты не приложения
    names = {c["token"]: (c["name"] or [""])[0] for c in get("https://formulae.brew.sh/api/cask.json")}
    save("homebrew-casks", [(names[i["cask"]], k + 1) for k, i in enumerate(top) if names.get(i["cask"])])


def coingecko():
    rows = []
    for page in (1, 2):
        for k, c in enumerate(get(f"https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&order=market_cap_desc&per_page=250&page={page}")):
            rows.append((c["name"], 250 * (page - 1) + k + 1))
        time.sleep(15)
    save("coingecko-coins", rows)
    ex = get("https://api.coingecko.com/api/v3/exchanges?per_page=150&page=1")
    save("coingecko-exchanges", [(e["name"], k + 1) for k, e in enumerate(ex)])


def defillama():
    protocols = sorted(get("https://api.llama.fi/protocols"), key=lambda p: -(p.get("tvl") or 0))[:500]
    save("defillama-protocols", [(p["name"], k + 1) for k, p in enumerate(protocols)])


ALL = {f.__name__.replace("_", "-"): f for f in (stackoverflow_names, huggingface_names, homebrew_casks, coingecko, defillama)}
for name in sys.argv[1:] or ALL:
    ALL[name]()
