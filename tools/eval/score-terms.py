import sys,csv
K=[["фронтенд","React","бэкенд","Python"],["Задеплой","Vercel"],["коммит","запушь","GitHub"],["terms.json"],["Playwright","Puppeteer"],["Docker","PostgreSQL","Redis"],["эндпоинт","JSON","XML"],["npm","pip"],["JavaScript","TypeScript"],["пул-реквест","код-ревью"],["Claude Opus","Gemini"],["LM Studio","Ollama"],["Hugging Face","файнтюнинг"],["DeepSeek","ChatGPT"],["промпт","Midjourney"],["MCP","Claude Code"],["Notepad++"],["DeepL"],["Telegram","Discord"],["Google Chrome","Firefox"],["Google Drive","Obsidian"],["OBS","DaVinci Resolve"],["MetaMask","Arbitrum"],["Ethereum","Solana","CoinMarketCap"],["Rabby Wallet","Phantom"],["DeBank","свап","Uniswap"],["NVIDIA","AMD Ryzen"],["BIOS","XMP"],["VPN","пинг"],["Sony","JBL"]]
def load(p,col):
    r=[l.rstrip('\n').split('\t') for l in open(p)][1:]
    r.sort(key=lambda x:x[0]); return [x[col] for x in r]
cols={}
for name,p,col in [("запись(сборка)",sys.argv[1],4)]+[(a.split('=')[0],a.split('=')[1],3) for a in sys.argv[2:]]:
    cols[name]=load(p,col)
tot=sum(len(k) for k in K)
for n,v in cols.items():
    ok=sum(1 for i,k in enumerate(K) for t in k if t in v[i] or (t[0].islower() and t.lower() in v[i].lower()))
    print(f"{n}: {ok}/{tot}")
