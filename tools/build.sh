#!/bin/bash
# Headless-цикл Bush Rush: compile → forge → tests → smoke → autoplay → webgl.
# Использование: tools/build.sh [compile|forge|tests|smoke|autoplay|webgl|all]
#   AUTOPLAY=1 tools/build.sh forge   — сцена, где игрок тоже бот (автостарт боя)
#   SEEDS=50 BOT=strong tools/build.sh autoplay — матрица 6×6 пресетов
#   DEV=1 tools/build.sh webgl        — development-билд без сжатия
set -e
U=/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity
P=/Users/natala/cloude/bush-rush/unity
mkdir -p "$P/CI"
if [ -f "$P/Temp/UnityLockfile" ]; then
  if lsof "$P/Temp/UnityLockfile" >/dev/null 2>&1; then echo "Unity Editor держит проект (Temp/UnityLockfile) — закрой редактор"; exit 1; fi
  rm -f "$P/Temp/UnityLockfile"   # остался от упавшего headless-прогона
fi
step() {
  echo "== $1"
  "$U" -batchmode -nographics -projectPath "$P" -quit -logFile "$P/CI/$1.log" -executeMethod "Warbands.EditorTools.CI.$2" >/dev/null 2>&1 \
    || { echo "FAIL $1 (см. CI/$1.log)"; grep -n "error CS\|\[SW\]\|Exception" "$P/CI/$1.log" | head -40; exit 1; }
  grep -n "\[SW\]" "$P/CI/$1.log" | grep -v "UnityEngine\|StackTrace" | sed 's/^[0-9]*:\[SW\] //' | head -80
}
run_tests() {   # $1 — EditMode|PlayMode, $2 — имя шага (файлы CI/$2.xml/.log), $3 — фильтр тестов (опционально)
  echo "== $2"
  rm -f "$P/CI/$2.xml"
  "$U" -batchmode -nographics -projectPath "$P" -runTests -testPlatform "$1" ${3:+-testFilter "$3"} \
    -testResults "$P/CI/$2.xml" -logFile "$P/CI/$2.log" >/dev/null 2>&1 || true
  [ -f "$P/CI/$2.xml" ] || { echo "нет CI/$2.xml"; grep -n "error CS\|Exception" "$P/CI/$2.log" | head; exit 1; }
  python3 - "$P/CI/$2.xml" "$2" <<'PY'
import sys,xml.etree.ElementTree as ET
r=ET.parse(sys.argv[1]).getroot()
print("%s: total=%s passed=%s failed=%s"%(sys.argv[2],r.get('total'),r.get('passed'),r.get('failed')))
for tc in r.iter('test-case'):
    if tc.get('result')!='Passed':
        m=tc.find('failure/message'); print(" FAIL",tc.get('name'),(m.text or '').strip()[:600] if m is not None else '')
sys.exit(0 if r.get('failed')=='0' else 1)
PY
}
tests() { run_tests EditMode tests; }
# смоук (В12, 11.09): PlayMode-тест поднимает сцену боя и крутит автоплей 6 с — ловит падения UI, которых не видят compile/tests/autoplay
smoke() { run_tests PlayMode smoke "Warbands.PlayTests"; }
# TMP Essential Resources (шрифт LiberationSans SDF, TMP Settings) — распаковать из пакета ugui, если ещё нет
if [ ! -f "$P/Assets/TextMesh Pro/Resources/TMP Settings.asset" ]; then
  PKG=$(ls "$P"/Library/PackageCache/com.unity.ugui@*/Package\ Resources/TMP\ Essential\ Resources.unitypackage 2>/dev/null | head -1)
  [ -n "$PKG" ] && python3 "$(dirname "$0")/extract_unitypackage.py" "$PKG" "$P"
fi
# Epic Toon FX → URP-материалы из пакета в Upgrade/ (один раз; признак — URP-шейдер в Materials/Basics)
ETFX="$P/Assets/Epic Toon FX"
if [ -d "$ETFX" ] && ! grep -q "m_Shader: {fileID: 4800000" "$ETFX/Materials/Basics/"*.mat 2>/dev/null; then
  PKG=$(ls "$ETFX"/Upgrade/ETFX\ URP\ Upgrade\ \(6000*.unitypackage 2>/dev/null | head -1)
  [ -n "$PKG" ] && python3 "$(dirname "$0")/extract_unitypackage.py" "$PKG" "$P"
fi
case "${1:-all}" in
  compile)  step compile Compile ;;
  forge)    step forge Forge ;;
  tests)    tests ;;
  smoke)    smoke ;;
  autoplay) step autoplay Autoplay ;;
  webgl)    step webgl BuildWebGL ;;
  all)      step compile Compile; step forge Forge; tests; smoke; step autoplay Autoplay; step webgl BuildWebGL ;;
esac
