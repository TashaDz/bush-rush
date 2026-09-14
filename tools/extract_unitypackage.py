#!/usr/bin/env python3
"""Распаковать .unitypackage (tar.gz: <guid>/asset, asset.meta, pathname) в каталог проекта.
Нужно для headless-импорта TMP Essential Resources: AssetDatabase.ImportPackage в batchmode не успевает до выхода."""
import sys, tarfile, os, io
pkg, dest = sys.argv[1], sys.argv[2]
entries = {}
with tarfile.open(pkg, 'r:gz') as tf:
    for m in tf.getmembers():
        parts = [x for x in m.name.split('/') if x not in ('', '.')]
        if len(parts) < 2: continue
        guid, kind = parts[0], parts[1]
        e = entries.setdefault(guid, {})
        if kind == 'pathname': e['path'] = tf.extractfile(m).read().decode().split('\n')[0].strip()
        elif kind == 'asset' and m.isfile(): e['asset'] = tf.extractfile(m).read()
        elif kind == 'asset.meta': e['meta'] = tf.extractfile(m).read()
n = 0
for guid, e in entries.items():
    if 'path' not in e: continue
    p = os.path.join(dest, e['path'])
    if 'asset' in e:
        os.makedirs(os.path.dirname(p), exist_ok=True); open(p, 'wb').write(e['asset']); n += 1
    else:
        os.makedirs(p, exist_ok=True)
    if 'meta' in e: open(p + '.meta', 'wb').write(e['meta'])
print(f"extracted {n} files from {os.path.basename(pkg)} → {dest}")
