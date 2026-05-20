#!/usr/bin/env python3
"""
Vox — Universal Voice Input

Push-to-talk voice recognition daemon.
Hold Right Ctrl → speak → release → text is pasted at cursor.

Uses Vosk for offline Russian speech recognition.
English tech terms are auto-replaced (паст → paste, копи → copy, etc.)
"""

import json
import os
import queue
import re
import shutil
import sys
import tempfile
import threading
import time

import keyboard
import numpy as np
import pyperclip
import sounddevice as sd
import winsound

from vosk import KaldiRecognizer, Model, SetLogLevel


def get_safe_model_path(original_path: str) -> str:
    """
    Vosk (C library) doesn't handle non-ASCII paths on Windows.
    If path contains non-ASCII, copy model to temp directory.
    """
    try:
        original_path.encode('ascii')
        return original_path
    except UnicodeEncodeError:
        pass
    
    temp_base = os.path.join(tempfile.gettempdir(), 'vosk_models')
    model_name = os.path.basename(original_path)
    safe_path = os.path.join(temp_base, model_name)
    
    if os.path.isdir(safe_path):
        if os.path.isdir(os.path.join(safe_path, 'am')):
            print(f'[INIT] Using cached model at {safe_path}')
            return safe_path
    
    print(f'[INIT] Path contains non-ASCII characters')
    print(f'[INIT] Copying model to {safe_path}...')
    
    if os.path.exists(safe_path):
        shutil.rmtree(safe_path)
    
    shutil.copytree(original_path, safe_path)
    print(f'[INIT] Model copied successfully')
    
    return safe_path


# ============================================================================
# Configuration — edit these to customize
# ============================================================================

HOTKEY = 'right ctrl'           # Push-to-talk key
SAMPLE_RATE = 16000             # Audio sample rate (16kHz for Vosk)
MIN_RECORD_MS = 250             # Ignore recordings shorter than this
ENABLE_BEEPS = True             # Play beep on start/stop
AUTO_PASTE = True               # Auto-paste after transcription (Ctrl+V)

# Model path (Russian model with English term replacements)
def get_model_path():
    """Get model path - works for both script and frozen exe."""
    model_name = 'vosk-model-small-ru-0.22'
    
    # For frozen exe: use %APPDATA%\vox\models\
    if getattr(sys, 'frozen', False):
        appdata = os.environ.get('APPDATA', os.path.expanduser('~'))
        return os.path.join(appdata, 'vox', 'models', model_name)
    
    # For script: use ./models/
    return os.path.join(
        os.path.dirname(os.path.abspath(__file__)),
        'models',
        model_name
    )

MODEL_PATH = get_model_path()

# Russian phonetic → English tech term replacements
# Big dictionary of programming terms
TERM_REPLACEMENTS = {
    # === React / Hooks ===
    'юзстейт': 'useState',
    'юз стейт': 'useState',
    'юзэффект': 'useEffect',
    'юз эффект': 'useEffect',
    'юзконтекст': 'useContext',
    'юз контекст': 'useContext',
    'юзреф': 'useRef',
    'юз реф': 'useRef',
    'юзмемо': 'useMemo',
    'юз мемо': 'useMemo',
    'юзколбэк': 'useCallback',
    'юз колбэк': 'useCallback',
    'юзредьюсер': 'useReducer',
    'юз редьюсер': 'useReducer',
    'юзлэйаут эффект': 'useLayoutEffect',
    'юзимперативхендл': 'useImperativeHandle',
    'пропс': 'props',
    'пропсы': 'props',
    'стейт': 'state',
    'сетстейт': 'setState',
    'компонент': 'component',
    'компоненты': 'components',
    'хук': 'hook',
    'хуки': 'hooks',
    'контекст': 'context',
    'провайдер': 'provider',
    'консьюмер': 'consumer',
    'редьюсер': 'reducer',
    'экшен': 'action',
    'экшн': 'action',
    'диспатч': 'dispatch',
    'редакс': 'Redux',
    'мобэкс': 'MobX',
    'зустанд': 'Zustand',
    'джотай': 'Jotai',
    'рекойл': 'Recoil',
    
    # === Languages / Frameworks ===
    'джаваскрипт': 'JavaScript',
    'джава скрипт': 'JavaScript',
    'джс': 'JS',
    'тайпскрипт': 'TypeScript',
    'тайп скрипт': 'TypeScript',
    'тс': 'TS',
    'питон': 'Python',
    'пайтон': 'Python',
    'пай': 'py',
    'джава': 'Java',
    'котлин': 'Kotlin',
    'свифт': 'Swift',
    'обджектив си': 'Objective-C',
    'сишарп': 'C#',
    'си шарп': 'C#',
    'сиплюсплюс': 'C++',
    'си плюс плюс': 'C++',
    'гоу': 'Go',
    'голанг': 'Golang',
    'раст': 'Rust',
    'руби': 'Ruby',
    'пхп': 'PHP',
    'пиэйчпи': 'PHP',
    'перл': 'Perl',
    'скала': 'Scala',
    'эликсир': 'Elixir',
    'кложур': 'Clojure',
    'хаскелл': 'Haskell',
    'эрланг': 'Erlang',
    'люа': 'Lua',
    'дарт': 'Dart',
    'зиг': 'Zig',
    
    # === Frontend Frameworks ===
    'реакт': 'React',
    'риэкт': 'React',
    'вью': 'Vue',
    'вьюджс': 'Vue.js',
    'ангуляр': 'Angular',
    'свелт': 'Svelte',
    'свелткит': 'SvelteKit',
    'некст': 'Next.js',
    'некстджс': 'Next.js',
    'нукст': 'Nuxt',
    'нукстджс': 'Nuxt.js',
    'гэтсби': 'Gatsby',
    'ремикс': 'Remix',
    'астро': 'Astro',
    'солид': 'Solid',
    'солиджс': 'SolidJS',
    'квик': 'Qwik',
    'преакт': 'Preact',
    'алпайн': 'Alpine.js',
    'эмбер': 'Ember',
    'бэкбон': 'Backbone',
    'джейквери': 'jQuery',
    'джи квери': 'jQuery',
    
    # === Backend Frameworks ===
    'ноджс': 'Node.js',
    'нод': 'Node',
    'экспресс': 'Express',
    'экспрессджс': 'Express.js',
    'фастифай': 'Fastify',
    'коа': 'Koa',
    'нест': 'Nest',
    'нестджс': 'NestJS',
    'джанго': 'Django',
    'фласк': 'Flask',
    'фастапи': 'FastAPI',
    'фаст апи': 'FastAPI',
    'эйоник': 'aiohttp',
    'торнадо': 'Tornado',
    'рейлс': 'Rails',
    'руби он рейлс': 'Ruby on Rails',
    'ларавел': 'Laravel',
    'симфони': 'Symfony',
    'спринг': 'Spring',
    'спринг бут': 'Spring Boot',
    'джин': 'Gin',
    'эхо': 'Echo',
    'файбер': 'Fiber',
    'актикс': 'Actix',
    'рокет': 'Rocket',
    'флаттер': 'Flutter',
    'реакт нейтив': 'React Native',
    'электрон': 'Electron',
    'таури': 'Tauri',
    
    # === CSS / Styling ===
    'цсс': 'CSS',
    'сиэсэс': 'CSS',
    'сасс': 'Sass',
    'скссс': 'SCSS',
    'лесс': 'Less',
    'стайлус': 'Stylus',
    'тейлвинд': 'Tailwind',
    'тэйлвинд': 'Tailwind',
    'бутстрап': 'Bootstrap',
    'материал юай': 'Material UI',
    'чакра': 'Chakra',
    'чакра юай': 'Chakra UI',
    'энт дизайн': 'Ant Design',
    'стайлд компонентс': 'styled-components',
    'эмоушн': 'Emotion',
    'флексбокс': 'Flexbox',
    'флекс': 'flex',
    'грид': 'grid',
    'медиа квери': 'media query',
    
    # === Build Tools / Bundlers ===
    'вебпак': 'Webpack',
    'вайт': 'Vite',
    'роллап': 'Rollup',
    'парсел': 'Parcel',
    'есбилд': 'esbuild',
    'свц': 'SWC',
    'бабель': 'Babel',
    'бейбл': 'Babel',
    'тёрбо': 'Turbo',
    'турбопак': 'Turbopack',
    'турборепо': 'Turborepo',
    'лёрна': 'Lerna',
    'нх': 'Nx',
    
    # === Package Managers ===
    'нпм': 'npm',
    'энпиэм': 'npm',
    'ярн': 'yarn',
    'пнпм': 'pnpm',
    'бан': 'bun',
    'пип': 'pip',
    'пипенв': 'pipenv',
    'поэтри': 'poetry',
    'карго': 'cargo',
    'композер': 'composer',
    'мавен': 'Maven',
    'грэдл': 'Gradle',
    'кокоаподс': 'CocoaPods',
    'брю': 'brew',
    'хоумбрю': 'Homebrew',
    'чоко': 'choco',
    'чоколати': 'Chocolatey',
    
    # === Testing ===
    'джест': 'Jest',
    'витест': 'Vitest',
    'мока': 'Mocha',
    'чай': 'Chai',
    'жасмин': 'Jasmine',
    'карма': 'Karma',
    'сайпресс': 'Cypress',
    'плейврайт': 'Playwright',
    'паппетир': 'Puppeteer',
    'селениум': 'Selenium',
    'юнит тест': 'unit test',
    'юнит тесты': 'unit tests',
    'интеграционный тест': 'integration test',
    'е ту е': 'e2e',
    'иту|и': 'e2e',
    'энд ту энд': 'end-to-end',
    'мок': 'mock',
    'моки': 'mocks',
    'стаб': 'stub',
    'спай': 'spy',
    'фикстура': 'fixture',
    'ассерт': 'assert',
    'эксепт': 'expect',
    'тест кейс': 'test case',
    'тест сьют': 'test suite',
    'ковередж': 'coverage',
    'покрытие': 'coverage',
    
    # === Git / Version Control ===
    'гит': 'git',
    'гитхаб': 'GitHub',
    'гитлаб': 'GitLab',
    'битбакет': 'Bitbucket',
    'коммит': 'commit',
    'коммиты': 'commits',
    'пуш': 'push',
    'пушить': 'push',
    'пул': 'pull',
    'пулить': 'pull',
    'фетч': 'fetch',
    'клон': 'clone',
    'клонировать': 'clone',
    'пулл реквест': 'pull request',
    'пул реквест': 'pull request',
    'пи ар': 'PR',
    'мёрдж': 'merge',
    'мердж': 'merge',
    'мёрджить': 'merge',
    'бранч': 'branch',
    'бранчи': 'branches',
    'ветка': 'branch',
    'ветки': 'branches',
    'ребейз': 'rebase',
    'черри пик': 'cherry-pick',
    'черрипик': 'cherry-pick',
    'сташ': 'stash',
    'стэш': 'stash',
    'чекаут': 'checkout',
    'чек аут': 'checkout',
    'ресет': 'reset',
    'риверт': 'revert',
    'дифф': 'diff',
    'блейм': 'blame',
    'тег': 'tag',
    'теги': 'tags',
    'ориджин': 'origin',
    'апстрим': 'upstream',
    'ремоут': 'remote',
    'хед': 'HEAD',
    'мастер': 'master',
    'мейн': 'main',
    'девелоп': 'develop',
    'фича': 'feature',
    'хотфикс': 'hotfix',
    'релиз': 'release',
    'гитигнор': '.gitignore',
    
    # === DevOps / Infrastructure ===
    'докер': 'Docker',
    'докерфайл': 'Dockerfile',
    'докер компоуз': 'docker-compose',
    'контейнер': 'container',
    'контейнеры': 'containers',
    'имидж': 'image',
    'имейдж': 'image',
    'кубернетес': 'Kubernetes',
    'кубер': 'Kubernetes',
    'кейт эйт эс': 'K8s',
    'кубернетис': 'Kubernetes',
    'хелм': 'Helm',
    'истио': 'Istio',
    'терраформ': 'Terraform',
    'ансибл': 'Ansible',
    'паппет': 'Puppet',
    'шеф': 'Chef',
    'вагрант': 'Vagrant',
    'дженкинс': 'Jenkins',
    'гитхаб экшенс': 'GitHub Actions',
    'гитлаб сиай': 'GitLab CI',
    'сиай сиди': 'CI/CD',
    'си ай': 'CI',
    'си ди': 'CD',
    'пайплайн': 'pipeline',
    'деплой': 'deploy',
    'деплоймент': 'deployment',
    'продакшен': 'production',
    'продакшн': 'production',
    'прод': 'prod',
    'стейджинг': 'staging',
    'стейдж': 'stage',
    'дев': 'dev',
    'локал': 'local',
    'энвайронмент': 'environment',
    'энв': 'env',
    'конфиг': 'config',
    'конфигурация': 'configuration',
    
    # === Cloud Providers ===
    'авс': 'AWS',
    'эйдаблюэс': 'AWS',
    'амазон': 'Amazon',
    'эй даблю эс': 'AWS',
    'гугл клауд': 'Google Cloud',
    'джисипи': 'GCP',
    'азур': 'Azure',
    'эйжур': 'Azure',
    'майкрософт азур': 'Microsoft Azure',
    'дигитал оушен': 'DigitalOcean',
    'херроку': 'Heroku',
    'хероку': 'Heroku',
    'верцель': 'Vercel',
    'версель': 'Vercel',
    'нетлифай': 'Netlify',
    'клаудфлер': 'Cloudflare',
    'файрбейз': 'Firebase',
    'фаербейс': 'Firebase',
    'сюпабейз': 'Supabase',
    'супабейс': 'Supabase',
    'планетскейл': 'PlanetScale',
    'рейлвей': 'Railway',
    'флай ио': 'Fly.io',
    
    # === Databases ===
    'постгрес': 'PostgreSQL',
    'постгрескюэл': 'PostgreSQL',
    'постгря': 'Postgres',
    'майскюэл': 'MySQL',
    'мускул': 'MySQL',
    'маскюэл': 'MySQL',
    'скюлайт': 'SQLite',
    'скюэлайт': 'SQLite',
    'монго': 'MongoDB',
    'монгодиби': 'MongoDB',
    'редис': 'Redis',
    'мемкэшд': 'Memcached',
    'эластиксёрч': 'Elasticsearch',
    'эластик': 'Elastic',
    'кассандра': 'Cassandra',
    'динамодиби': 'DynamoDB',
    'коучдиби': 'CouchDB',
    'нео фор джей': 'Neo4j',
    'скюэл': 'SQL',
    'эскуэль': 'SQL',
    'ноускюэл': 'NoSQL',
    'орм': 'ORM',
    'призма': 'Prisma',
    'секвелайз': 'Sequelize',
    'тайпорм': 'TypeORM',
    'дризл': 'Drizzle',
    'алембик': 'Alembic',
    'миграция': 'migration',
    'миграции': 'migrations',
    'сидинг': 'seeding',
    'сид': 'seed',
    'скима': 'schema',
    'схема': 'schema',
    'таблица': 'table',
    'колонка': 'column',
    'индекс': 'index',
    'квери': 'query',
    'кверя': 'query',
    'джойн': 'join',
    'лефт джойн': 'LEFT JOIN',
    'иннер джойн': 'INNER JOIN',
    'веар': 'WHERE',
    'ордер бай': 'ORDER BY',
    'груп бай': 'GROUP BY',
    'хэвинг': 'HAVING',
    'лимит': 'LIMIT',
    'оффсет': 'OFFSET',
    'транзакция': 'transaction',
    
    # === APIs / Protocols ===
    'эй пи ай': 'API',
    'апи': 'API',
    'рест': 'REST',
    'рестфул': 'RESTful',
    'графкюэл': 'GraphQL',
    'граф кюэл': 'GraphQL',
    'джейсон': 'JSON',
    'джсон': 'JSON',
    'иксэмэл': 'XML',
    'хмл': 'XML',
    'эйчтиэмэл': 'HTML',
    'хтмл': 'HTML',
    'ямл': 'YAML',
    'йамл': 'YAML',
    'томл': 'TOML',
    'эйчтитипи': 'HTTP',
    'хттп': 'HTTP',
    'эйчтитипиэс': 'HTTPS',
    'хттпс': 'HTTPS',
    'вебсокет': 'WebSocket',
    'веб сокет': 'WebSocket',
    'сокет': 'socket',
    'сокеты': 'sockets',
    'сокет ио': 'Socket.io',
    'грпц': 'gRPC',
    'джиарписи': 'gRPC',
    'тиарписи': 'tRPC',
    'оаус': 'OAuth',
    'о аус': 'OAuth',
    'джейдаблютэ': 'JWT',
    'джот': 'JWT',
    'токен': 'token',
    'токены': 'tokens',
    'эндпоинт': 'endpoint',
    'эндпоинты': 'endpoints',
    'роут': 'route',
    'роуты': 'routes',
    'роутер': 'router',
    'роутинг': 'routing',
    'мидлвер': 'middleware',
    'мидлвэр': 'middleware',
    'хедер': 'header',
    'хедеры': 'headers',
    'хэдер': 'header',
    'бади': 'body',
    'боди': 'body',
    'пэйлоад': 'payload',
    'пейлоад': 'payload',
    'респонс': 'response',
    'респонсы': 'responses',
    'реквест': 'request',
    'реквесты': 'requests',
    'статус код': 'status code',
    'корс': 'CORS',
    'вебхук': 'webhook',
    'вебхуки': 'webhooks',
    
    # === AI / ML ===
    'девин': 'Devin',
    'клод': 'Claude',
    'джипити': 'GPT',
    'чатджипити': 'ChatGPT',
    'чат джипити': 'ChatGPT',
    'опенэйай': 'OpenAI',
    'опен эй ай': 'OpenAI',
    'антропик': 'Anthropic',
    'копайлот': 'Copilot',
    'коупайлот': 'Copilot',
    'джемини': 'Gemini',
    'лама': 'LLaMA',
    'мистраль': 'Mistral',
    'промт': 'prompt',
    'промпт': 'prompt',
    'промты': 'prompts',
    'промпты': 'prompts',
    'эмбеддинг': 'embedding',
    'эмбеддинги': 'embeddings',
    'файнтюнинг': 'fine-tuning',
    'файн тюнинг': 'fine-tuning',
    'инференс': 'inference',
    'модел': 'model',
    'модель': 'model',
    'нейросеть': 'neural network',
    'трансформер': 'transformer',
    'аттеншен': 'attention',
    'лангчейн': 'LangChain',
    'ланг чейн': 'LangChain',
    'лламаиндекс': 'LlamaIndex',
    'пайторч': 'PyTorch',
    'тензорфлоу': 'TensorFlow',
    'керас': 'Keras',
    'сайкитлёрн': 'scikit-learn',
    'хаггингфейс': 'Hugging Face',
    
    # === JavaScript/TypeScript Keywords ===
    'функция': 'function',
    'функшн': 'function',
    'фанкшн': 'function',
    'класс': 'class',
    'классы': 'classes',
    'метод': 'method',
    'методы': 'methods',
    'модуль': 'module',
    'модули': 'modules',
    'пакет': 'package',
    'пакеты': 'packages',
    'импорт': 'import',
    'импортировать': 'import',
    'экспорт': 'export',
    'экспортировать': 'export',
    'дефолт': 'default',
    'дифолт': 'default',
    'конст': 'const',
    'константа': 'const',
    'лет': 'let',
    'вар': 'var',
    'иф': 'if',
    'элс': 'else',
    'элс иф': 'else if',
    'фор': 'for',
    'форич': 'forEach',
    'фор ич': 'forEach',
    'вайл': 'while',
    'ду вайл': 'do while',
    'свитч': 'switch',
    'кейс': 'case',
    'брейк': 'break',
    'континью': 'continue',
    'ретурн': 'return',
    'риторн': 'return',
    'ретёрн': 'return',
    'трай': 'try',
    'кэтч': 'catch',
    'файналли': 'finally',
    'троу': 'throw',
    'сроу': 'throw',
    'нью': 'new',
    'дис': 'this',
    'зис': 'this',
    'супер': 'super',
    'экстендс': 'extends',
    'имплементс': 'implements',
    'статик': 'static',
    'паблик': 'public',
    'прайват': 'private',
    'протектед': 'protected',
    'эсинк': 'async',
    'эсюнк': 'async',
    'эвейт': 'await',
    'авэйт': 'await',
    'йилд': 'yield',
    'йелд': 'yield',
    'промис': 'Promise',
    'промисы': 'promises',
    'зен': 'then',
    'эррор': 'error',
    'эксепшн': 'exception',
    'эксепшен': 'exception',
    
    # === Types ===
    'тайп': 'type',
    'тайпы': 'types',
    'интерфейс': 'interface',
    'интерфейсы': 'interfaces',
    'стринг': 'string',
    'стринги': 'strings',
    'намбер': 'number',
    'намберы': 'numbers',
    'буллеан': 'boolean',
    'булеан': 'boolean',
    'бул': 'bool',
    'булл': 'boolean',
    'эррей': 'array',
    'эррэй': 'array',
    'массив': 'array',
    'обджект': 'object',
    'объект': 'object',
    'нулл': 'null',
    'налл': 'null',
    'андефайнд': 'undefined',
    'войд': 'void',
    'эни': 'any',
    'анноун': 'unknown',
    'невер': 'never',
    'тупл': 'tuple',
    'тюпл': 'tuple',
    'кортеж': 'tuple',
    'энам': 'enum',
    'инам': 'enum',
    'юнион': 'union',
    'дженерик': 'generic',
    'дженерики': 'generics',
    'тайпоф': 'typeof',
    'тайп оф': 'typeof',
    'кейоф': 'keyof',
    'кей оф': 'keyof',
    'ридонли': 'readonly',
    'рид онли': 'readonly',
    'партиал': 'Partial',
    'рекорд': 'Record',
    'пик': 'Pick',
    'омит': 'Omit',
    'эксклуд': 'Exclude',
    'экстракт': 'Extract',
    'нон наллабл': 'NonNullable',
    'ретурн тайп': 'ReturnType',
    
    # === Common Programming Concepts ===
    'вариабл': 'variable',
    'переменная': 'variable',
    'переменные': 'variables',
    'аргумент': 'argument',
    'аргументы': 'arguments',
    'параметр': 'parameter',
    'параметры': 'parameters',
    'колбэк': 'callback',
    'колбек': 'callback',
    'хендлер': 'handler',
    'хэндлер': 'handler',
    'листенер': 'listener',
    'ивент': 'event',
    'евент': 'event',
    'ивенты': 'events',
    'эмит': 'emit',
    'эмитить': 'emit',
    'сабскрайб': 'subscribe',
    'ансабскрайб': 'unsubscribe',
    'обсёрвер': 'observer',
    'обсервер': 'observer',
    'паттерн': 'pattern',
    'паттерны': 'patterns',
    'алгоритм': 'algorithm',
    'рекурсия': 'recursion',
    'рекурсивный': 'recursive',
    'итерация': 'iteration',
    'итератор': 'iterator',
    'итерировать': 'iterate',
    'луп': 'loop',
    'цикл': 'loop',
    'циклы': 'loops',
    'условие': 'condition',
    'условия': 'conditions',
    'кондишн': 'condition',
    'тернарный': 'ternary',
    'оператор': 'operator',
    'операторы': 'operators',
    'инкремент': 'increment',
    'декремент': 'decrement',
    'конкатенация': 'concatenation',
    'интерполяция': 'interpolation',
    'деструктуризация': 'destructuring',
    'спред': 'spread',
    'дефолт вэлью': 'default value',
    
    # === Data Structures ===
    'структура': 'structure',
    'структуры': 'structures',
    'стек': 'stack',
    'очередь': 'queue',
    'кью': 'queue',
    'дек': 'deque',
    'лист': 'list',
    'листы': 'lists',
    'линкд лист': 'linked list',
    'связный список': 'linked list',
    'три': 'tree',
    'дерево': 'tree',
    'бинарное дерево': 'binary tree',
    'бинари три': 'binary tree',
    'граф': 'graph',
    'хэш': 'hash',
    'хеш': 'hash',
    'хэшмэп': 'hashmap',
    'хешмап': 'hashmap',
    'сет': 'set',
    'мап': 'map',
    'мапа': 'map',
    'мэп': 'map',
    'дикт': 'dict',
    'словарь': 'dictionary',
    'нода': 'node',
    'ноды': 'nodes',
    'эдж': 'edge',
    'вертекс': 'vertex',
    'рут': 'root',
    'корень': 'root',
    'лиф': 'leaf',
    'парент': 'parent',
    'чайлд': 'child',
    'чилдрен': 'children',
    'сиблинг': 'sibling',
    
    # === OOP ===
    'ооп': 'OOP',
    'о о п': 'OOP',
    'объектно ориентированный': 'object-oriented',
    'наследование': 'inheritance',
    'инхеританс': 'inheritance',
    'полиморфизм': 'polymorphism',
    'инкапсуляция': 'encapsulation',
    'абстракция': 'abstraction',
    'абстрактный': 'abstract',
    'конструктор': 'constructor',
    'деструктор': 'destructor',
    'инстанс': 'instance',
    'инстансы': 'instances',
    'экземпляр': 'instance',
    'объекты': 'objects',
    'проперти': 'property',
    'пропертис': 'properties',
    'геттер': 'getter',
    'сеттер': 'setter',
    'аксессор': 'accessor',
    'мутатор': 'mutator',
    'оверрайд': 'override',
    'оверлоад': 'overload',
    'перегрузка': 'overload',
    'переопределение': 'override',
    
    # === Functional Programming ===
    'эфпи': 'FP',
    'функциональный': 'functional',
    'чистая функция': 'pure function',
    'пьюр фанкшн': 'pure function',
    'иммутабл': 'immutable',
    'иммьютабл': 'immutable',
    'мутабл': 'mutable',
    'мьютабл': 'mutable',
    'сайд эффект': 'side effect',
    'хайер ордер': 'higher order',
    'хоф': 'HOF',
    'карринг': 'currying',
    'композиция': 'composition',
    'пайп': 'pipe',
    'фильтер': 'filter',
    'редьюс': 'reduce',
    'фолд': 'fold',
    'флэтмэп': 'flatMap',
    'флэт': 'flat',
    'фаинд': 'find',
    'файнд': 'find',
    'сам': 'some',
    'эври': 'every',
    'инклудс': 'includes',
    'индексоф': 'indexOf',
    'сорт': 'sort',
    'сортировка': 'sort',
    'реверс': 'reverse',
    'слайс': 'slice',
    'сплайс': 'splice',
    'конкат': 'concat',
    'сплит': 'split',
    'трим': 'trim',
    'сабстринг': 'substring',
    'репсейс': 'replace',
    'реплейс': 'replace',
    'матч': 'match',
    'тест': 'test',
    'серч': 'search',
    
    # === DOM / Browser ===
    'дом': 'DOM',
    'ди оу эм': 'DOM',
    'документ': 'document',
    'виндоу': 'window',
    'элемент': 'element',
    'элементы': 'elements',
    'квери селектор': 'querySelector',
    'гет элемент бай айди': 'getElementById',
    'гет элементс бай класс': 'getElementsByClassName',
    'криейт элемент': 'createElement',
    'аппенд': 'append',
    'аппенд чайлд': 'appendChild',
    'ремув': 'remove',
    'инсерт': 'insert',
    'иннер эйчтиэмэл': 'innerHTML',
    'аутер эйчтиэмэл': 'outerHTML',
    'текст контент': 'textContent',
    'атрибут': 'attribute',
    'атрибуты': 'attributes',
    'сет атрибут': 'setAttribute',
    'гет атрибут': 'getAttribute',
    'класслист': 'classList',
    'класс лист': 'classList',
    'стайл': 'style',
    'стайлы': 'styles',
    'эвент листенер': 'event listener',
    'эдд эвент листенер': 'addEventListener',
    'клик': 'click',
    'сабмит': 'submit',
    'чейндж': 'change',
    'инпут': 'input',
    'фокус': 'focus',
    'блюр': 'blur',
    'скролл': 'scroll',
    'ресайз': 'resize',
    'лоад': 'load',
    'анлоад': 'unload',
    'маунт': 'mount',
    'анмаунт': 'unmount',
    'рендер': 'render',
    'рендеринг': 'rendering',
    'ререндер': 'rerender',
    'гидрейшн': 'hydration',
    'гидратация': 'hydration',
    
    # === Debug / Console ===
    'консоль': 'console',
    'консоль лог': 'console.log',
    'лог': 'log',
    'логи': 'logs',
    'логгер': 'logger',
    'логгинг': 'logging',
    'дебаг': 'debug',
    'дебаггер': 'debugger',
    'дебаггинг': 'debugging',
    'отладка': 'debugging',
    'брейкпоинт': 'breakpoint',
    'брейк поинт': 'breakpoint',
    'точка останова': 'breakpoint',
    'стектрейс': 'stacktrace',
    'стэк трейс': 'stack trace',
    'эрроры': 'errors',
    'ворнинг': 'warning',
    'ворнинги': 'warnings',
    'варнинг': 'warning',
    'инфо': 'info',
    'трейс': 'trace',
    'ассершн': 'assertion',
    
    # === Files / Paths ===
    'файл': 'file',
    'файлы': 'files',
    'директория': 'directory',
    'директории': 'directories',
    'фолдер': 'folder',
    'папка': 'folder',
    'пас': 'path',
    'паф': 'path',
    'путь': 'path',
    'руут': 'root',
    'базнейм': 'basename',
    'дирнейм': 'dirname',
    'экстеншен': 'extension',
    'расширение': 'extension',
    'ридфайл': 'readFile',
    'райтфайл': 'writeFile',
    'эппендфайл': 'appendFile',
    'анлинк': 'unlink',
    'мкдир': 'mkdir',
    'рмдир': 'rmdir',
    'ридир': 'readdir',
    'стат': 'stat',
    'эксистс': 'exists',
    'рид': 'read',
    'райт': 'write',
    'читать': 'read',
    'писать': 'write',
    'создать': 'create',
    'удалить': 'delete',
    'копировать': 'copy',
    'переместить': 'move',
    'переименовать': 'rename',
    'стрим': 'stream',
    'стримы': 'streams',
    'буфер': 'buffer',
    'буферы': 'buffers',
    
    # === Security ===
    'секьюрити': 'security',
    'безопасность': 'security',
    'аутентификация': 'authentication',
    'аусентикейшн': 'authentication',
    'авторизация': 'authorization',
    'осоризейшн': 'authorization',
    'логин': 'login',
    'логаут': 'logout',
    'войти': 'login',
    'выйти': 'logout',
    'сайн ин': 'sign in',
    'сайн ап': 'sign up',
    'сайн аут': 'sign out',
    'пассворд': 'password',
    'пароль': 'password',
    'хэшировать': 'hash',
    'солт': 'salt',
    'соль': 'salt',
    'энкрипт': 'encrypt',
    'дикрипт': 'decrypt',
    'шифровать': 'encrypt',
    'расшифровать': 'decrypt',
    'сертификат': 'certificate',
    'серт': 'cert',
    'ключ': 'key',
    'приватный ключ': 'private key',
    'публичный ключ': 'public key',
    'сикрет': 'secret',
    'секрет': 'secret',
    'креденшалс': 'credentials',
    'кредс': 'creds',
    'ксс': 'XSS',
    'иксэсэс': 'XSS',
    'ксрф': 'CSRF',
    'инджекшн': 'injection',
    'эскейп': 'escape',
    'санитайз': 'sanitize',
    'валидейт': 'validate',
    'валидация': 'validation',
    
    # === Performance ===
    'перформанс': 'performance',
    'производительность': 'performance',
    'оптимизация': 'optimization',
    'оптимайз': 'optimize',
    'кэш': 'cache',
    'кеш': 'cache',
    'кэширование': 'caching',
    'мемоизация': 'memoization',
    'мемоайз': 'memoize',
    'лейзи': 'lazy',
    'лэйзи': 'lazy',
    'лейзи лоадинг': 'lazy loading',
    'код сплиттинг': 'code splitting',
    'трии шейкинг': 'tree shaking',
    'бандл': 'bundle',
    'бандлинг': 'bundling',
    'минификация': 'minification',
    'минифай': 'minify',
    'компрессия': 'compression',
    'гзип': 'gzip',
    'профайлинг': 'profiling',
    'профайлер': 'profiler',
    'бенчмарк': 'benchmark',
    'метрика': 'metric',
    'метрики': 'metrics',
    'латенси': 'latency',
    'латентность': 'latency',
    'сруупут': 'throughput',
    'пропускная способность': 'throughput',
    
    # === Misc Tools ===
    'вокс': 'vox',
    'войс': 'voice',
    'терминал': 'terminal',
    'шелл': 'shell',
    'баш': 'bash',
    'зш': 'zsh',
    'павершелл': 'PowerShell',
    'пауэршелл': 'PowerShell',
    'cmd': 'cmd',
    'коммандлайн': 'command line',
    'сиэлай': 'CLI',
    'сли': 'CLI',
    'юай': 'UI',
    'ю ай': 'UI',
    'юэкс': 'UX',
    'юзер интерфейс': 'user interface',
    'гуи': 'GUI',
    'гюи': 'GUI',
    'ай ди и': 'IDE',
    'вскод': 'VS Code',
    'ви эс код': 'VS Code',
    'вижуал студио': 'Visual Studio',
    'интеллиджей': 'IntelliJ',
    'вебшторм': 'WebStorm',
    'пайчарм': 'PyCharm',
    'атом': 'Atom',
    'саблайм': 'Sublime',
    'вим': 'Vim',
    'неовим': 'Neovim',
    'имакс': 'Emacs',
    'нано': 'nano',
    'линтер': 'linter',
    'линт': 'lint',
    'линтинг': 'linting',
    'еслинт': 'ESLint',
    'преттиер': 'Prettier',
    'форматтер': 'formatter',
    'форматирование': 'formatting',
    'сниппет': 'snippet',
    'сниппеты': 'snippets',
    'автокомплит': 'autocomplete',
    'интеллисенс': 'IntelliSense',
    'рефакторинг': 'refactoring',
    'рефактор': 'refactor',
    
    # === Common English words (phonetic) ===
    # Clipboard operations
    'паст': 'paste',
    'пэйст': 'paste',
    'пейст': 'paste',
    'копи': 'copy',
    'копия': 'copy',
    'кат': 'cut',
    'андо': 'undo',
    'анду': 'undo',
    'редо': 'redo',
    'риду': 'redo',
    
    # File operations
    'сейв': 'save',
    'сэйв': 'save',
    'лоуд': 'load',
    'опен': 'open',
    'оупен': 'open',
    'клоуз': 'close',
    'клоз': 'close',
    'ньюс': 'news',
    'криейт': 'create',
    'делит': 'delete',
    'мув': 'move',
    
    # Actions
    'ран': 'run',
    'раннинг': 'running',
    'стоп': 'stop',
    'старт': 'start',
    'рестарт': 'restart',
    'клир': 'clear',
    'клиа': 'clear',
    'гет': 'get',
    'пут': 'put',
    'пост': 'post',
    'патч': 'patch',
    'сенд': 'send',
    'ресив': 'receive',
    'апдейт': 'update',
    'апгрейд': 'upgrade',
    'даунгрейд': 'downgrade',
    'инсталл': 'install',
    'анинсталл': 'uninstall',
    'энейбл': 'enable',
    'дизейбл': 'disable',
    'актив': 'active',
    'инактив': 'inactive',
    'экспанд': 'expand',
    'коллапс': 'collapse',
    'шоу': 'show',
    'хайд': 'hide',
    'хидден': 'hidden',
    'визибл': 'visible',
    
    # Common adjectives
    'олд': 'old',
    'бэд': 'bad',
    'гуд': 'good',
    'биг': 'big',
    'смолл': 'small',
    'фаст': 'fast',
    'слоу': 'slow',
    'хард': 'hard',
    'софт': 'soft',
    'лонг': 'long',
    'шорт': 'short',
    'хай': 'high',
    'лоу': 'low',
    'фулл': 'full',
    'эмпти': 'empty',
    'файнал': 'final',
    'ферст': 'first',
    'ласт': 'last',
    'прев': 'prev',
    'превиус': 'previous',
    'каррент': 'current',
    'кастом': 'custom',
    'симпл': 'simple',
    'комплекс': 'complex',
    'бэйсик': 'basic',
    'эдванс': 'advanced',
    'эдвансд': 'advanced',
    
    # Common nouns
    'юзер': 'user',
    'юзеры': 'users',
    'админ': 'admin',
    'гест': 'guest',
    'хост': 'host',
    'хоум': 'home',
    'пейдж': 'page',
    'пэйдж': 'page',
    'сайт': 'site',
    'линк': 'link',
    'линки': 'links',
    'урл': 'URL',
    'ю ар эл': 'URL',
    'имейл': 'email',
    'мейл': 'mail',
    'месседж': 'message',
    'мессадж': 'message',
    'мессейдж': 'message',
    'чат': 'chat',
    'коммент': 'comment',
    'комментс': 'comments',
    'лайк': 'like',
    'шер': 'share',
    'шэр': 'share',
    'вьюс': 'views',
    'дэшборд': 'dashboard',
    'дашборд': 'dashboard',
    'виджет': 'widget',
    'модал': 'modal',
    'попап': 'popup',
    'поп ап': 'popup',
    'тултип': 'tooltip',
    'тул тип': 'tooltip',
    'дропдаун': 'dropdown',
    'дроп даун': 'dropdown',
    'сайдбар': 'sidebar',
    'сайд бар': 'sidebar',
    'футер': 'footer',
    'контент': 'content',
    'враппер': 'wrapper',
    'врэппер': 'wrapper',
    'айтем': 'item',
    'айтемс': 'items',
    'тейбл': 'table',
    'ров': 'row',
    'роу': 'row',
    'колум': 'column',
    'колумн': 'column',
    'селл': 'cell',
    'форм': 'form',
    'формы': 'forms',
    'филд': 'field',
    'филды': 'fields',
    'лейбл': 'label',
    'лэйбл': 'label',
    'плейсхолдер': 'placeholder',
    'баттон': 'button',
    'батн': 'button',
    'кнопка': 'button',
    'чекбокс': 'checkbox',
    'радио': 'radio',
    'селект': 'select',
    'оптион': 'option',
    'опшн': 'option',
    'вэлью': 'value',
    'велью': 'value',
    'валуе': 'value',
    'кей': 'key',
    'айди': 'id',
    'ай ди': 'id',
    'нейм': 'name',
    'тайтл': 'title',
    'дескрипшн': 'description',
    'дескрипшен': 'description',
    'текст': 'text',
    'пикча': 'picture',
    'иконка': 'icon',
    'айкон': 'icon',
    'аватар': 'avatar',
    'бэкграунд': 'background',
    'бордер': 'border',
    'марджин': 'margin',
    'маргин': 'margin',
    'паддинг': 'padding',
    'пэддинг': 'padding',
    'сайз': 'size',
    'сизе': 'size',
    'видс': 'width',
    'вайдс': 'width',
    'хайт': 'height',
    'хэйт': 'height',
    'колор': 'color',
    'колер': 'color',
    
    # Time-related
    'дэйт': 'date',
    'дейт': 'date',
    'тайм': 'time',
    'таймстэмп': 'timestamp',
    'дэдлайн': 'deadline',
    'дедлайн': 'deadline',
    'таймаут': 'timeout',
    'тайм аут': 'timeout',
    'интервал': 'interval',
    'делэй': 'delay',
    'дилэй': 'delay',
    
    # Status
    'саксес': 'success',
    'саксесс': 'success',
    'фейл': 'fail',
    'фэйл': 'fail',
    'фейлд': 'failed',
    'пендинг': 'pending',
    'прогресс': 'progress',
    'комплит': 'complete',
    'комплитед': 'completed',
    'реди': 'ready',
    'рэди': 'ready',
    'лоадинг': 'loading',
    'лоудинг': 'loading',
    'процессинг': 'processing',
    'вэйтинг': 'waiting',
    'вейтинг': 'waiting',
    
    # Misc
    'хелп': 'help',
    'хэлп': 'help',
    'ок': 'ok',
    'окей': 'okay',
    'йес': 'yes',
    'ноу': 'no',
    'кэнсел': 'cancel',
    'кансел': 'cancel',
    'рижект': 'reject',
    'эпрув': 'approve',
    'эппрув': 'approve',
    'дикляйн': 'decline',
    'деклайн': 'decline',
    'конфёрм': 'confirm',
    'конферм': 'confirm',
    'ченж': 'change',
    'эдит': 'edit',
    'модифай': 'modify',
    'трансформ': 'transform',
    'конверт': 'convert',
    'парс': 'parse',
    'парсить': 'parse',
    'верифай': 'verify',
    'чек': 'check',
    'фикс': 'fix',
    'солв': 'solve',
    'ризолв': 'resolve',
    'хендл': 'handle',
    'хэндл': 'handle',
    'процесс': 'process',
    'анализ': 'analyze',
    'аналайз': 'analyze',
    'сёрч': 'search',
    'фильтр': 'filter',
    'сортировать': 'sort',
    'груп': 'group',
    'групп': 'group',
    'комбайн': 'combine',
    'препенд': 'prepend',
    'инсёрт': 'insert',
    'дупликейт': 'duplicate',
    'дубликат': 'duplicate',
    
    # === File extensions ===
    'ехе': '.exe',
    'ексе': '.exe',
    'экзе': '.exe',
    'джейэс': '.js',
    'джиэс': '.js',
    'тиэс': '.ts',
    'эмди': '.md',
    'маркдаун': '.md',
    'ямлфайл': '.yaml',
    'тхт': '.txt',
    'бат': '.bat',
    'зип': '.zip',
    'тар': '.tar',
    'гз': '.gz',
    'таргз': '.tar.gz',
    'рар': '.rar',
    'севен зип': '.7z',
    'пнг': '.png',
    'джипег': '.jpg',
    'джпг': '.jpg',
    'джипиджи': '.jpg',
    'гиф': '.gif',
    'свг': '.svg',
    'вебп': '.webp',
    'айко': '.ico',
    'пдф': '.pdf',
    'док': '.doc',
    'докс': '.docx',
    'иксэль': '.xlsx',
    'эксель': '.xlsx',
    'цсв': '.csv',
    'сиэсви': '.csv',
    'эсКюЭль': '.sql',
    'сиквел': '.sql',
    'дотенв': '.env',
    'енв': '.env',
    'мейкфайл': 'Makefile',
}

# ============================================================================
# Global state
# ============================================================================

SetLogLevel(-1)  # Silence Vosk internal logs

model = None
audio_queue = queue.Queue()
recording = False
record_start_time = 0.0


def beep_start():
    """Short high beep when recording starts."""
    if ENABLE_BEEPS:
        winsound.Beep(800, 80)


def beep_stop():
    """Short low beep when recording stops."""
    if ENABLE_BEEPS:
        winsound.Beep(500, 80)


def audio_callback(indata, frames, time_info, status):
    """Called by sounddevice for each audio chunk."""
    if status:
        print(f'[AUDIO] {status}', file=sys.stderr)
    if recording:
        # Make a copy of the data (indata buffer gets reused)
        audio_queue.put(indata.copy())


def replace_terms(text: str) -> str:
    """Replace Russian phonetic transcriptions with English tech terms."""
    result = text
    for ru, en in TERM_REPLACEMENTS.items():
        # Use word boundaries to avoid replacing inside words
        # \b works with Cyrillic in Python 3
        pattern = re.compile(r'\b' + re.escape(ru) + r'\b', re.IGNORECASE)
        result = pattern.sub(en, result)
    return result


# Question words for auto-punctuation
QUESTION_WORDS = {
    'почему', 'зачем', 'как', 'что', 'где', 'когда', 'кто', 'кого', 'кому',
    'какой', 'какая', 'какое', 'какие', 'каким', 'какую',
    'сколько', 'который', 'которая', 'которое', 'которые',
    'куда', 'откуда', 'отчего', 'чей', 'чья', 'чьё', 'чьи',
    'неужели', 'разве',
    # English question words (phonetic)
    'вай', 'хау', 'вот', 'вер', 'вен', 'ху', 'хуз', 'вич', 'виз',
}

# Words/patterns that indicate a yes/no question (without question word)
QUESTION_ENDINGS = {'да', 'нет', 'правда', 'так', 'ведь', 'же'}
QUESTION_PARTICLES = {'ли', 'разве', 'неужели'}


def add_punctuation(text: str) -> str:
    """Add basic punctuation and capitalize first letter."""
    if not text:
        return text
    
    # Capitalize first letter
    text = text[0].upper() + text[1:] if len(text) > 1 else text.upper()
    
    # Already has punctuation at the end
    if text[-1] in '.?!':
        return text
    
    words = text.lower().split()
    if not words:
        return text + '.'
    
    first_word = words[0]
    last_word = words[-1]
    
    # Check if starts with a question word
    if first_word in QUESTION_WORDS:
        return text + '?'
    
    # Check for question word in first 3 words
    for word in words[:3]:
        if word in QUESTION_WORDS:
            return text + '?'
    
    # Check for question particles anywhere ("ли" etc.)
    for word in words:
        if word in QUESTION_PARTICLES:
            return text + '?'
    
    # Check for tag question endings ("..., да?", "..., правда?")
    if last_word in QUESTION_ENDINGS:
        return text + '?'
    
    # Check for common yes/no question starters + short sentence with verb
    # "Это работает?" "Теперь работает?" "Уже готово?" "А это что?"
    yes_no_starters = {'это', 'а', 'ну', 'и', 'теперь', 'уже', 'ещё', 'еще', 'всё', 'все', 'он', 'она', 'оно', 'они', 'ты', 'вы', 'мы'}
    if first_word in yes_no_starters and len(words) <= 6:
        # Could be a question, but not certain
        # Check if contains verb-like ending patterns
        verb_endings = ('ает', 'яет', 'ит', 'ет', 'ут', 'ют', 'ат', 'ят',
                        'ишь', 'ешь', 'ёшь', 'ал', 'ял', 'ил', 'ел',
                        'али', 'яли', 'или', 'ели', 'ло', 'ла', 'ли',
                        'ать', 'ять', 'ить', 'еть', 'оть', 'уть', 'ыть')
        for word in words:
            if word.endswith(verb_endings):
                # Likely a yes/no question like "Это работает?"
                return text + '?'
    
    # Default: add period
    return text + '.'


def transcribe() -> str:
    """Process queued audio through Vosk and return transcription."""
    recognizer = KaldiRecognizer(model, SAMPLE_RATE)
    
    # Collect all audio chunks
    chunks = []
    while not audio_queue.empty():
        try:
            chunks.append(audio_queue.get_nowait())
        except queue.Empty:
            break
    
    if not chunks:
        return ''
    
    # Combine chunks into single array and flatten (remove channel dimension)
    audio_data = np.concatenate(chunks).flatten()
    
    # Check audio level
    if np.max(np.abs(audio_data)) < 100:
        return ''
    
    # Feed to Vosk
    recognizer.AcceptWaveform(audio_data.tobytes())
    
    final = json.loads(recognizer.FinalResult())
    return final.get('text', '').strip()


def on_key_press(event):
    """Handle push-to-talk key press."""
    global recording, record_start_time
    
    if recording:
        return  # Already recording
    
    # Clear any stale audio
    while not audio_queue.empty():
        try:
            audio_queue.get_nowait()
        except queue.Empty:
            break
    
    record_start_time = time.time()
    recording = True
    beep_start()
    print('[REC] Listening...')


def on_key_release(event):
    """Handle push-to-talk key release."""
    global recording
    
    if not recording:
        return
    
    recording = False
    elapsed_ms = (time.time() - record_start_time) * 1000
    beep_stop()
    
    if elapsed_ms < MIN_RECORD_MS:
        print(f'[REC] Too short ({elapsed_ms:.0f}ms), ignored')
        return
    
    print(f'[REC] Stopped ({elapsed_ms:.0f}ms), transcribing...')
    
    text = transcribe()
    if not text:
        print('[STT] No speech recognized')
        return
    
    # Apply term replacements and punctuation
    text = replace_terms(text)
    text = add_punctuation(text)
    print(f'[STT] "{text}"')
    
    # Copy to clipboard
    pyperclip.copy(text)
    print('[CLIP] Copied to clipboard')
    
    if AUTO_PASTE:
        # Small delay to ensure key release is processed
        time.sleep(0.05)
        keyboard.send('ctrl+v')
        print('[PASTE] Pasted')


def download_model_if_needed():
    """Download Vosk model if not present."""
    if os.path.isdir(MODEL_PATH):
        return True
    
    model_url = 'https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip'
    model_name = 'vosk-model-small-ru-0.22'
    
    print(f'[INIT] Model not found at: {MODEL_PATH}')
    print(f'[INIT] Downloading model (~45 MB)...')
    
    try:
        import urllib.request
        import zipfile
        
        # Create models directory
        models_dir = os.path.dirname(MODEL_PATH)
        os.makedirs(models_dir, exist_ok=True)
        
        # Download to temp file
        zip_path = os.path.join(models_dir, f'{model_name}.zip')
        
        def show_progress(block_num, block_size, total_size):
            downloaded = block_num * block_size
            if total_size > 0:
                percent = min(100, downloaded * 100 // total_size)
                mb_downloaded = downloaded / (1024 * 1024)
                mb_total = total_size / (1024 * 1024)
                print(f'\r[INIT] Downloading: {mb_downloaded:.1f}/{mb_total:.1f} MB ({percent}%)', end='', flush=True)
        
        urllib.request.urlretrieve(model_url, zip_path, show_progress)
        print()  # newline after progress
        
        print(f'[INIT] Extracting model...')
        with zipfile.ZipFile(zip_path, 'r') as zf:
            zf.extractall(models_dir)
        
        # Remove zip file
        os.remove(zip_path)
        print(f'[INIT] Model installed to: {MODEL_PATH}')
        return True
        
    except Exception as e:
        print(f'[ERROR] Failed to download model: {e}')
        return False


def get_log_dir():
    """Get log directory path."""
    if getattr(sys, 'frozen', False):
        base = os.environ.get('APPDATA', os.path.expanduser('~'))
        return os.path.join(base, 'vox', 'logs')
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs')


def setup_logging():
    """Redirect stdout/stderr to log file (needed for --windowed exe)."""
    log_dir = get_log_dir()
    os.makedirs(log_dir, exist_ok=True)
    log_file = os.path.join(log_dir, 'vox.log')
    
    # Truncate old log if too big (>5MB)
    if os.path.exists(log_file) and os.path.getsize(log_file) > 5 * 1024 * 1024:
        os.remove(log_file)
    
    log_fp = open(log_file, 'a', encoding='utf-8', buffering=1)
    sys.stdout = log_fp
    sys.stderr = log_fp
    return log_dir, log_file


def get_icon_image():
    """Load tray icon — works for script and frozen exe."""
    from PIL import Image
    
    if getattr(sys, 'frozen', False):
        icon_path = os.path.join(sys._MEIPASS, 'vox.ico')
    else:
        icon_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'vox.ico')
    
    if os.path.exists(icon_path):
        return Image.open(icon_path)
    
    # Fallback: solid blue square
    img = Image.new('RGB', (64, 64), color=(59, 130, 246))
    return img


def vox_worker():
    """Main PTT loop — runs in background thread."""
    global model
    
    if not download_model_if_needed():
        print('[FATAL] Model download failed, exiting worker')
        return
    
    safe_model_path = get_safe_model_path(MODEL_PATH)
    
    print(f'[INIT] Loading Vosk model...')
    print(f'       {safe_model_path}')
    model = Model(safe_model_path)
    print('[INIT] Model loaded')
    
    keyboard.on_press_key(HOTKEY, on_key_press, suppress=False)
    keyboard.on_release_key(HOTKEY, on_key_release, suppress=False)
    
    print(f'[INIT] Opening microphone ({SAMPLE_RATE} Hz)')
    
    try:
        with sd.InputStream(
            samplerate=SAMPLE_RATE,
            blocksize=4000,
            dtype='int16',
            channels=1,
            callback=audio_callback
        ):
            print('=' * 50)
            print(' Vox — Voice Input')
            print('=' * 50)
            print(f' Hotkey:     [{HOTKEY.upper()}] (hold to record)')
            print(f' Auto-paste: {AUTO_PASTE}')
            print(f' Beeps:      {ENABLE_BEEPS}')
            print(f' Terms:      {len(TERM_REPLACEMENTS)} replacements')
            print('=' * 50)
            print('Ready. Hold the hotkey and speak.')
            
            keyboard.wait()  # blocks forever
            
    except Exception as e:
        print(f'[ERROR] {e}')
        import traceback
        traceback.print_exc()


def setup_tray(log_dir):
    """Setup system tray icon and menu (blocks)."""
    import pystray
    
    def on_open_logs(icon, item):
        try:
            os.startfile(log_dir)
        except Exception as e:
            print(f'[TRAY] Failed to open logs: {e}')
    
    def on_open_models(icon, item):
        try:
            models_dir = os.path.dirname(MODEL_PATH)
            if os.path.exists(models_dir):
                os.startfile(models_dir)
        except Exception as e:
            print(f'[TRAY] Failed to open models: {e}')
    
    def on_quit(icon, item):
        print('[TRAY] Quit requested')
        icon.stop()
        os._exit(0)
    
    menu = pystray.Menu(
        pystray.MenuItem('Vox — Voice Input', None, default=True, enabled=False),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem(f'Hotkey: {HOTKEY.upper()}', None, enabled=False),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem('Open logs', on_open_logs),
        pystray.MenuItem('Open models folder', on_open_models),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem('Quit', on_quit)
    )
    
    icon = pystray.Icon('Vox', get_icon_image(), 'Vox — Voice Input', menu)
    icon.run()


def main():
    log_dir, log_file = setup_logging()
    print(f'\n=== Vox started at {time.strftime("%Y-%m-%d %H:%M:%S")} ===')
    
    # Start worker in background daemon thread
    worker = threading.Thread(target=vox_worker, daemon=True, name='VoxWorker')
    worker.start()
    
    # Tray icon (blocks main thread)
    try:
        setup_tray(log_dir)
    except Exception as e:
        print(f'[FATAL] Tray failed: {e}')
        import traceback
        traceback.print_exc()


if __name__ == '__main__':
    main()
