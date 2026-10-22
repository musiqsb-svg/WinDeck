#!/usr/bin/env bash
# Codex 이미지 생성으로 WinDeck 아이콘 원본을 만든다. (Git Bash 에서 실행, Codex CLI 로그인 필요)
#
#   bash tools/icons/codex_generate.sh <목록.tsv> <원본 출력 폴더> [동시 실행 수, 기본 4]
#   ICON_STYLE=flat bash tools/icons/codex_generate.sh ...   ← 2D 플랫 세트 (기본은 3d)
#
# 목록.tsv : 한 줄에 "이름<TAB>영문 설명"   예) coffee<TAB>a coffee cup with saucer and steam
# 결과     : <출력 폴더>/<이름>.png (약 1254px, 투명 배경). 이미 있는 이름은 건너뛴다.
# 다음 단계: assets/icons/manifest.tsv 에 "이름<TAB>한글 이름<TAB>분류" 를 추가하고
#            python tools/icons/process_icons.py <출력 폴더> assets/icons --sheet 미리보기.png
set -euo pipefail

list="$1"
out="$(mkdir -p "$2" && cd "$2" && pwd)"
jobs="${3:-4}"
case "${ICON_STYLE:-3d}" in
  flat) STYLE_TEXT="flat 2D vector icon style - clean geometric shapes, solid flat colors with at most a subtle two-tone shade, no gradients, no 3D, no glossy highlights, no shadows, bold simple silhouette with rounded corners, vivid but not neon colors, like a modern flat app icon set" ;;
  *)    STYLE_TEXT="Windows 11 Fluent 3D emoji style - soft gradients, gentle top-left lighting, subtle soft shadow, glossy but simple, friendly rounded shapes, vivid but not neon colors" ;;
esac
export STYLE_TEXT
work="$out/.codex"
mkdir -p "$work/logs" "$work/cwd"

gen_one() {
  local name="$1" subject="$2" out="$3" work="$4"
  [ -f "$out/$name.png" ] && { echo "skip $name"; return 0; }
  local prompt="Use your image generation tool to create exactly ONE app icon image. Do not run any shell commands and do not create or modify any files.
Icon subject: ${subject}.
Style (must stay consistent across a whole icon set): ${STYLE_TEXT}.
Requirements: fully transparent background (no backdrop, plate, circle, frame, floor or ground shadow), one centered object filling about 80% of a square canvas, no text, letters or numbers.
Final reply: only the absolute Windows path of the generated PNG file."
  codex exec --skip-git-repo-check --ephemeral -s read-only -C "$work/cwd" \
    -o "$work/logs/$name.last.txt" "$prompt" > "$work/logs/$name.log" 2>&1 || true
  # Codex 는 생성 이미지를 ~/.codex/generated_images 에 저장하고 그 경로를 답한다.
  local src
  src=$(python -c "import re,sys;m=re.search(r'[A-Za-z]:.+?[.]png',open(sys.argv[1],encoding='utf-8',errors='replace').read());print(m.group(0) if m else '')" "$work/logs/$name.last.txt" 2>/dev/null || true)
  if [ -n "$src" ] && [ -f "$(cygpath -u "$src")" ]; then
    cp "$(cygpath -u "$src")" "$out/$name.png" && echo "ok   $name"
  else
    echo "FAIL $name (see $work/logs/$name.log)"
  fi
}
export -f gen_one

grep -v '^\s*#' "$list" | grep -v '^\s*$' | tr '\t' '\037' |
  xargs -d '\n' -P "$jobs" -I{} bash -c 'IFS=$'"'"'\037'"'"' read -r n s <<< "$1"; gen_one "$n" "$s" "$2" "$3"' _ {} "$out" "$work"
