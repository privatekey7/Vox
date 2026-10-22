# Встроенный IT/ИИ-словарь

Файл — `src/Vox.Core/Text/Data/terms.json`: `{ группа: { написание: [варианты произношения] } }`.
Код — `Terms` в `src/Vox.Core/Text/TextRules.cs`, версии моделей — `ModelVersions.cs`, тесты —
`tests/Vox.Core.Tests/TermsDictionaryTests.cs` и `ModelVersionsTests.cs`.

## Политика (решение владельца, 22.10.2026)

- **Латиницей** — только названия, бренды, продукты, модели, аббревиатуры и код: Claude, Cursor,
  DeepSeek, LLM, MCP, RAG, useState, GitHub.
- **Прижившиеся заимствования — кириллицей**, исправляется только написание (группа `spelling`):
  промпт, токен, агент, нейросеть, датасет, эмбеддинг, файнтюнинг, трансформер, токенизатор, бэкенд,
  фронтенд, деплой, коммит, юзер… Если модель уже пишет слово правильно — записи нет вовсе.
- **Группа `exact`** — точные записи без поиска по звучанию, кириллическая цель разрешена: связки
  («сделай комет» → «сделай коммит», «The Deploy» → «задеплой», «pin d server» → «пинг до сервера») и
  слова, чьё звучание тянет чужие («диплей» → «деплой», иначе и «дисплей»; «эндпоинт», иначе «int end»).
- **Обычные слова** (курсор, агент, опус, сонет, лама, кодекс, кафка, хром, андроид, юпитер, Кими,
  Сора, Джулс, крон…) меняются только в связке: «клод опус» → Claude Opus, «апач кафка» → Apache
  Kafka. Одиночные источники сверены с частотным словарём (`docs/declension.md`); совпавшие с
  обычными словами или именами убраны или переведены в связку.
- Вариантов произношения 2–5 на термин: слитно/раздельно/через дефис, е/э, и/ы, двойные согласные,
  плюс латинские написания модели в нижнем регистре (`deepseek`, `chat gpt`) — регистр выправляется.

## Как применяется

- Один проход по словам, самая длинная фраза первой. Дефис — часть слова («пиф-паф» не трогается).
- Падежи: склоняется последнее слово источника («в докер компоузе», «на гитхабе», «в промте» →
  «в промпте»). Латинская цель теряет окончание, кириллическая — получает его же.
- Версии (`ModelVersions`, после словаря и до ITN): бренд + число словами или цифрами, через «и»,
  «точка», «.» или «,»: «клод опус четыре и пять» → Claude Opus 4.5, «джипити четыре о» → GPT-4o,
  «джемини два и пять про.» → Gemini 2.5 Pro (но «Gemini 2.5 про погоду» — «про» тут предлог),
  «дипсик эр один» → DeepSeek R1, «кими кей два» → Kimi K2, «квен три» → Qwen3. «Опус», «сонет»,
  «хайку» без «клод» — только с дробной версией («сонет 66» — стихотворение); «лама», «джемма»,
  «сора», «кими», «кандинский» — только с версией.

## Смесь алфавитов

`MixedScript` (до словаря): слово из кириллицы и латиницы («индoint») переписывается целиком в один
алфавит (латиница транслитом в кириллицу или наоборот) и меняется, только если результат знает словарь
или поиск по звучанию («индоинт» → «эндпоинт», «Dockerе» → Docker). Иначе слово остаётся как было.

## Cloud → Claude (только GigaAM)

`ClaudeNames.cs`, до словаря: одиночные «Cloud»/«клауд»/«клоуд»/«клот» (+падежи) → Claude, связки
с Code/Desktop/Chrome/AI. Не трогаются названия облаков: перед словом Google, Яндекс, Amazon,
Microsoft, Oracle, Mail, Hetzner, Selectel, Sound, Next, i…; после — flare, Run, Storage, Functions,
Console, Platform, Shell, Drive, Native…; слитные iCloud/Cloudflare. Для Parakeet не применяется.

## Не сделано (сознательно)

- `o3`/`o4-mini` («о три»), Groq (звучит как Grok), LoRA в одиночку («лора» — имя), CUDA («куда»),
  Phi, Codex/Cursor без связки — слишком похожи на обычную речь.
- Соцсети латиницей (владелец, 22.10): YouTube, Telegram… «Телега», «рилс», «шорты» — кириллицей;
  «телеграмм» не источник («телеграмма» — обычное слово), Zoom — только «созвон/звонок в зуме».
- «VSL» → WSL (владелец: в его диктовках это WSL).
- Латинские искажения Parakeet для новых терминов (как «Pithon») неизвестны без корпуса — добавлять
  по результатам `tools/Vox.Replay`.

## Пересмотр прежнего словаря: изменено 13, удалено 532

### Изменена цель

| Источник | Было | Стало |
|---|---|---|
| иксэль | .xlsx | Excel |
| композер | composer | Composer |
| маркдаун | .md | Markdown |
| о о п | OOP | ООП |
| ооп | OOP | ООП |
| поэтри | poetry | Poetry |
| промпт | prompt | промпт |
| промпт инжиниринг | prompt engineering | промпт-инжиниринг |
| промт | prompt | промпт |
| солид | Solid | SOLID |
| файн тюнинг | fine-tuning | файнтюнинг |
| эксель | .xlsx | Excel |
| эсКюЭль | .sql | SQL |

### Удалено (обычные слова и прижившиеся заимствования остаются как сказаны)

cmd → cmd; автокомплит → autocomplete; админ → admin; ай ди → id; айди → id; айкон → icon; айтем → item; айтемс → items; аксессор → accessor; аналайз → analyze; андерфиттинг → underfitting; андо → undo; анду → undo; анинсталл → uninstall; анлоад → unload; анмаунт → unmount; ансабскрайб → unsubscribe; апгрейд → upgrade; апдейт → update; аппенд → append; апстрим → upstream; ассерт → assert; ассершн → assertion; аттеншен → attention; аттеншн → attention; аусентикейшн → authentication; аутпут → output; бади → body; бандл → bundle; бандлинг → bundling; батн → button; баттон → button; батч → batch; батчи → batches; бенчмарк → benchmark; биг → big; бинари три → binary tree; бинарное дерево → binary tree; блейм → blame; блюр → blur; боди → body; бордер → border; бранч → branch; бранчи → branches; брейк поинт → breakpoint; брейкпоинт → breakpoint; булл → boolean; бэд → bad; бэйсик → basic; бэкграунд → background; бэкпроп → backprop; вайдс → width; валидейт → validate; валуе → value; вар → var; вариабл → variable; варнинг → warning; вебхук → webhook; вебхуки → webhooks; вейтинг → waiting; велью → value; верифай → verify; вертекс → vertex; виджет → widget; видс → width; визибл → visible; виндоу → window; войс → voice; ворнинг → warning; ворнинги → warnings; враппер → wrapper; врэппер → wrapper; вью → Vue; вьюс → views; вэйтинг → waiting; вэлью → value; гест → guest; гет → get; геттер → getter; гидрейшн → hydration; гоу → Go; грид → grid; гуд → good; датасет → dataset; датасеты → datasets; даунгрейд → downgrade; дашборд → dashboard; дебаг → debug; дебаггер → debugger; дебаггинг → debugging; дев → dev; девелоп → develop; дейт → date; дек → deque; деклайн → decline; декремент → decrement; делэй → delay; деплой → deploy; деплоймент → deployment; дескрипшен → description; дескрипшн → description; деструктор → destructor; деструктуризация → destructuring; дефолт вэлью → default value; дженерик → generic; дженерики → generics; джиэс → .js; джойн → join; дизейбл → disable; дикляйн → decline; дикрипт → decrypt; дикт → dict; дилэй → delay; диспатч → dispatch; дифолт → default; дифф → diff; дроп даун → dropdown; дропдаун → dropdown; дупликейт → duplicate; дэйт → date; дэшборд → dashboard; евент → event; енв → .env; зеро шот → zero-shot; зирошот → zero-shot; ивент → event; ивенты → events; имейдж → image; иммутабл → immutable; иммьютабл → immutable; инактив → inactive; инджекшн → injection; инкремент → increment; инпут → input; инсерт → insert; инсталл → install; инстанс → instance; инстансы → instances; инстракт → instruct; инсёрт → insert; интеграционный тест → integration test; инференс → inference; инфо → info; инхеританс → inheritance; итерировать → iterate; иту|и → e2e; йес → yes; кансел → cancel; каррент → current; карринг → currying; кассандра → Cassandra; кастом → custom; квантизация → quantization; квери → query; кверя → query; кей → key; кеш → cache; клиа → clear; клир → clear; клоз → close; клоуз → close; ковередж → coverage; код сплиттинг → code splitting; колбек → callback; колбэк → callback; колор → color; колум → column; колумн → column; коммандлайн → command line; комментс → comments; коммит → commit; коммиты → commits; комплит → complete; комплитед → completed; компрессия → compression; кондишн → condition; консьюмер → consumer; континью → continue; конферм → confirm; конфиг → config; конфёрм → confirm; копи → copy; креденшалс → credentials; кредс → creds; криейт → create; кью → queue; кэнсел → cancel; кэтч → catch; кэширование → caching; лайк → like; ласт → last; латенси → latency; лейер → layer; лейзи → lazy; лейзи лоадинг → lazy loading; линкд лист → linked list; линки → links; линт → lint; линтер → linter; линтинг → linting; листенер → listener; лоад → load; лоадинг → loading; логгинг → logging; логиты → logits; локал → local; лонг → long; лосс → loss; лоу → low; лоуд → load; лоудинг → loading; луп → loop; лэйбл → label; лэйзи → lazy; мап → map; мапа → map; маргин → margin; марджин → margin; медиа квери → media query; мейн → main; мемоайз → memoize; мемоизация → memoization; мердж → merge; мессадж → message; месседж → message; мессейдж → message; мидлвер → middleware; мидлвэр → middleware; минифай → minify; минификация → minification; модал → modal; модифай → modify; мока → Mocha; моки → mocks; мув → move; мутабл → mutable; мутатор → mutator; мьютабл → mutable; мэп → map; мёрдж → merge; мёрджить → merge; намберы → numbers; нано → nano; нейм → name; нода → node; ноды → nodes; ньюс → news; обсервер → observer; обсёрвер → observer; объектно ориентированный → object-oriented; оверлоад → overload; оверрайд → override; оверфиттинг → overfitting; олд → old; опен → open; оптимайз → optimize; оптион → option; опшн → option; ориджин → origin; осоризейшн → authorization; оупен → open; оффсет → OFFSET; паблик → public; паддинг → padding; пайп → pipe; пайплайн → pipeline; парент → parent; парс → parse; парсить → parse; пассворд → password; паст → paste; патч → patch; паф → path; пейдж → page; пейлоад → payload; пейст → paste; пендинг → pending; пикча → picture; плейсхолдер → placeholder; поп ап → popup; попап → popup; прев → prev; превиус → previous; препенд → prepend; приватный ключ → private key; призма → Prisma; прод → prod; продакшен → production; промис → Promise; промисы → promises; промптинг → prompting; промпты → prompts; промты → prompts; проперти → property; пропертис → properties; пропс → props; пропсы → props; пропускная способность → throughput; профайлер → profiler; профайлинг → profiling; процессинг → processing; публичный ключ → public key; пул реквест → pull request; пулить → pull; пулл реквест → pull request; пут → put; пуш → push; пушить → push; пьюр фанкшн → pure function; пэддинг → padding; пэйдж → page; пэйлоад → payload; пэйст → paste; раннинг → running; ребейз → rebase; реди → ready; редо → redo; редьюсер → reducer; реквест → request; реквесты → requests; ремоут → remote; ремув → remove; рендер → render; рендеринг → rendering; ререндер → rerender; ресайз → resize; ресет → reset; ресив → receive; респонс → response; респонсы → responses; рестарт → restart; рефактор → refactor; риверт → revert; рид → read; риду → redo; рижект → reject; ризолв → resolve; ризонинг → reasoning; роу → row; роут → route; роутер → router; роутинг → routing; роуты → routes; рут → root; руут → root; рэди → ready; сабмит → submit; сабскрайб → subscribe; сайд бар → sidebar; сайд эффект → side effect; сайдбар → sidebar; сайз → size; сайн ап → sign up; сайн аут → sign out; сайн ин → sign in; саксес → success; саксесс → success; санитайз → sanitize; свитч → switch; связный список → linked list; сейв → save; секьюрити → security; селект → select; селл → cell; семплинг → sampling; сенд → send; серт → cert; серч → search; сеттер → setter; сиблинг → sibling; сид → seed; сидинг → seeding; сизе → size; сикрет → secret; симпл → simple; скима → schema; скролл → scroll; слайс → slice; сли → CLI; слоу → slow; смолл → small; сниппет → snippet; сниппеты → snippets; сокет → socket; сокеты → sockets; солв → solve; солт → salt; софтмакс → softmax; спай → spy; спред → spread; сруупут → throughput; стаб → stub; стайл → style; стайлы → styles; стат → stat; статик → static; статус код → status code; сташ → stash; стейдж → stage; стейджинг → staging; стейт → state; стек → stack; стектрейс → stacktrace; стринги → strings; стэк трейс → stack trace; стэш → stash; сэйв → save; сэмплинг → sampling; сёрч → search; тайм аут → timeout; таймаут → timeout; таймстэмп → timestamp; тайп → type; тайпы → types; тайтл → title; тейбл → table; тернарный → ternary; тест кейс → test case; тест сьют → test suite; токенайзер → tokenizer; токенизация → tokenization; точка останова → breakpoint; трай → try; трансформ → transform; трансформер → transformer; трейс → trace; трии шейкинг → tree shaking; трим → trim; тул тип → tooltip; тултип → tooltip; файн тюн → fine-tune; файнал → final; файнтюн → fine-tune; файнтюнинг → fine-tuning; фейл → fail; фейлд → failed; ферст → first; фетч → fetch; фикс → fix; фикстура → fixture; филд → field; филды → fields; фильтер → filter; фича → feature; флекс → flex; флексбокс → Flexbox; флэт → flat; фолд → fold; фолдер → folder; форматтер → formatter; фулл → full; футер → footer; фью шот → few-shot; фьюшот → few-shot; фэйл → fail; хайер ордер → higher order; хайперпараметр → hyperparameter; хайперпараметры → hyperparameters; хайт → height; халлюцинация → hallucination; хард → hard; хед → HEAD; хедер → header; хедеры → headers; хелп → help; хендл → handle; хендлер → handler; хешмап → hashmap; хидден → hidden; хотфикс → hotfix; хоум → home; хуки → hooks; хэдер → header; хэйт → height; хэлп → help; хэндл → handle; хэндлер → handler; хэшировать → hash; хэшмэп → hashmap; чайлд → child; чакра → Chakra; чейндж → change; чек аут → checkout; чекаут → checkout; чекбокс → checkbox; чекпоинт → checkpoint; ченж → change; черри пик → cherry-pick; черрипик → cherry-pick; чилдрен → children; чистая функция → pure function; чоко → choco; шелл → shell; шер → share; шэр → share; эвент листенер → event listener; эври → every; эдванс → advanced; эдвансд → advanced; эдж → edge; эдит → edit; эксепшен → exception; эксепшн → exception; эксистс → exists; экспанд → expand; экстеншен → extension; экшен → action; эмбеддинг → embedding; эмбеддинги → embeddings; эмит → emit; эмитить → emit; эмпти → empty; энв → env; энвайронмент → environment; энд ту энд → end-to-end; эндпоинт → endpoint; эндпоинты → endpoints; энейбл → enable; эни → any; энкрипт → encrypt; эппрув → approve; эпрув → approve; эррор → error; эрроры → errors; эскейп → escape; эттеншн → attention; юзер → user; юзер интерфейс → user interface; юзеры → users; юнит тест → unit test; юнит тесты → unit tests; ямлфайл → .yaml
