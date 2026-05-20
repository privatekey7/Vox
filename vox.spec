# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec for Vox.
# Run via: pyinstaller vox.spec  (from any ASCII-only build directory)

import os
import importlib.util


def _vosk_dlls():
    """Locate Vosk's bundled DLLs in the installed package."""
    spec = importlib.util.find_spec('vosk')
    if not spec or not spec.origin:
        return []
    vosk_dir = os.path.dirname(spec.origin)
    dlls = ['libvosk.dll', 'libgcc_s_seh-1.dll', 'libstdc++-6.dll', 'libwinpthread-1.dll']
    return [(os.path.join(vosk_dir, d), 'vosk') for d in dlls if os.path.exists(os.path.join(vosk_dir, d))]


a = Analysis(
    ['vox.py'],
    pathex=[],
    binaries=_vosk_dlls(),
    datas=[('vox.ico', '.')],
    hiddenimports=[],
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
