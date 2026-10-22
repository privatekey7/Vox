"""Счёт терминов набора anglicisms-30.tsv по Final, полученному через Vox.Replay.

Использование:
  python3 tools/eval/score-set.py [--misses] gigaam.tsv parakeet.tsv
где gigaam.tsv / parakeet.tsv — вывод `Vox.Replay --model gigaam|parakeet файл`, в котором
каждая строка `Raw<TAB>Raw` колонки набора (порядок строк как в наборе). См. run-set.sh.
"""
import sys

K = [["фронтенд","React","бэкенд","Python"],["Задеплой","Vercel"],["коммит","запушь","GitHub"],["terms.json"],["Playwright","Puppeteer"],["Docker","PostgreSQL","Redis"],["эндпоинт","JSON","XML"],["npm","pip"],["JavaScript","TypeScript"],["пул-реквест","код-ревью"],["Claude Opus","Gemini"],["LM Studio","Ollama"],["Hugging Face","файнтюнинг"],["DeepSeek","ChatGPT"],["промпт","Midjourney"],["MCP","Claude Code"],["Notepad++"],["DeepL"],["Telegram","Discord"],["Google Chrome","Firefox"],["Google Drive","Obsidian"],["OBS","DaVinci Resolve"],["MetaMask","Arbitrum"],["Ethereum","Solana","CoinMarketCap"],["Rabby Wallet","Phantom"],["DeBank","свап","Uniswap"],["NVIDIA","AMD Ryzen"],["BIOS","XMP"],["VPN","пинг"],["Sony","JBL"]]


def hit(t, s):
    return t in s or (t[0].islower() and t.lower() in s.lower())


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    show = "--misses" in sys.argv
    rows = [l.rstrip("\n").split("\t") for l in open("tools/eval/anglicisms-30.tsv", encoding="utf-8")][1:]
    total = sum(len(k) for k in K)
    for col, path in zip((1, 2), args):
        final = [l.rstrip("\n").split("\t")[-1] for l in open(path, encoding="utf-8")]
        ok = 0
        for i, k in enumerate(K):
            for t in k:
                if hit(t, final[i]):
                    ok += 1
                elif show:
                    print(f"[{path.rsplit('/', 1)[-1]}] {t!r}\n   exp: {rows[i][0]}\n   raw: {rows[i][col]}\n   fin: {final[i]}")
        print(f"{path}: {ok}/{total}")


main()
