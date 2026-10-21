# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec for Vox.
# Run via: pyinstaller vox.spec  (from any ASCII-only build directory)

from PyInstaller.utils.hooks import collect_data_files, collect_dynamic_libs


# sherpa-onnx ships native .dll files alongside its Python bindings.
_sherpa_binaries = collect_dynamic_libs('sherpa_onnx')
_sherpa_datas = collect_data_files('sherpa_onnx')


a = Analysis(
    ['vox.py'],
    pathex=[],
    binaries=_sherpa_binaries,
    datas=[('vox.ico', '.')] + _sherpa_datas,
    hiddenimports=[
        'sherpa_onnx',
        'voxapp',
        'voxapp.capitalization',
        'voxapp.config',
        'voxapp.correction_monitor',
        'voxapp.currency',
        'voxapp.downloader',
        'voxapp.engines',
        'voxapp.fillers',
        'voxapp.gui',
        'voxapp.itn',
        'voxapp.post_processor',
        'voxapp.punctuation',
        'voxapp.registry',
        'voxapp.segmenter',
        'voxapp.storage',
        'voxapp.terms',
        'voxapp.tray',
        'voxapp.worker',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        'torch', 'pandas', 'scipy', 'matplotlib', 'cv2', 'tensorflow',
        'sklearn', 'pytest', 'IPython', 'jupyter', 'pygments', 'boto3',
        'pyarrow', 'cryptography', 'sqlalchemy', 'django', 'flask',
        'fastapi', 'pydantic', 'yt_dlp', 'mutagen', 'curl_cffi',
        'websockets', 'psutil', 'playwright',
    ],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='vox',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=['vox.ico'],
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name='vox',
)
