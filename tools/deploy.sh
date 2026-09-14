#!/bin/bash
# Деплой WebGL-билда на GitHub Pages (TashaDz/bush-rush-web) без ожидания кэша:
#   • каталог Build → Build-<epoch>, а index.html НЕ меняется — он сам читает имя каталога из build.txt?t=<now>
#     (шаблон Assets/WebGLTemplates/Warbands/index.html), так что 10-минутный кэш Pages/браузера на index.html не мешает;
#   • история deploy-репозитория не копится: каждый деплой — один orphan-коммит, push --force (репо ≈ размер одного билда).
#   tools/deploy.sh → https://tashadz.github.io/bush-rush-web/
set -e
SRC=/Users/natala/cloude/bush-rush/unity/Builds/WebGL
ROOT=/Users/natala/cloude/bush-rush/deploy
STAMP=$(date +%s)
DEST="${DEST:-}"   # DEST=route — выложить в подпапку (ветка), корень не трогать
[ -f "$SRC/index.html" ] || { echo "нет билда — сначала tools/build.sh webgl"; exit 1; }
[ -d "$ROOT/.git" ] || { echo "нет deploy/.git — сначала создать репозиторий TashaDz/bush-rush-web и клонировать в deploy/"; exit 1; }
grep -q 'build.txt' "$SRC/index.html" || { echo "index.html билда без загрузчика build.txt — пересобери webgl с актуальным шаблоном"; exit 1; }
if [ -n "$DEST" ]; then
  mkdir -p "$ROOT/$DEST"
  rsync -a --delete "$SRC/" "$ROOT/$DEST/"
  mv "$ROOT/$DEST/Build" "$ROOT/$DEST/Build-$STAMP"
  printf 'Build-%s' "$STAMP" > "$ROOT/$DEST/build.txt"
else
  rsync -a --delete --exclude .git --exclude README.md --exclude .nojekyll "$SRC/" "$ROOT/"
  mv "$ROOT/Build" "$ROOT/Build-$STAMP"
  printf 'Build-%s' "$STAMP" > "$ROOT/build.txt"
fi
touch "$ROOT/.nojekyll"
[ -f "$ROOT/README.md" ] || echo "# Bush Rush — web build" > "$ROOT/README.md"
cd "$ROOT"
git checkout -q --orphan deploy-tmp
git add -A
git -c user.name="TashaDz" -c user.email="4b9tyddkpy@privaterelay.appleid.com" commit -q -m "deploy $(date '+%Y-%m-%d %H:%M') (Build-$STAMP)"
git branch -q -D main 2>/dev/null || true
git branch -q -m main
git -c http.postBuffer=524288000 push -q --force origin main
git reflog expire --expire=now --all && git gc -q --prune=now
echo "deployed Build-$STAMP → https://tashadz.github.io/bush-rush-web/${DEST:+$DEST/} (build.txt=$(cat ${DEST:+$DEST/}build.txt))"
