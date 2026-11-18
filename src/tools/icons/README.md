# 기본 아이콘 만들기 (Codex)

버튼 편집 창의 **아이콘 모음**에 들어가는 이미지는 `assets/icons/` 에 있고, 빌드할 때 exe 안에 포함된다.

| 파일 | 내용 |
|---|---|
| `assets/icons/manifest.tsv` | 아이콘 목록 — `이름<TAB>한글 이름<TAB>분류<TAB>스타일(입체/플랫)` (이 순서로 아이콘 모음에 표시, 플랫은 이름 끝 `-2d`) |
| `assets/icons/<이름>.png` | 128px 투명 PNG |
| `tools/icons/codex_generate.sh` | Codex 이미지 생성으로 원본 만들기 |
| `tools/icons/process_icons.py` | 원본을 128px 로 다듬기 (여백 정리·잡티 제거) |

## 아이콘 추가 순서

1. 만들 아이콘 목록 작성 (`이름<TAB>영문 설명`)

   ```
   coffee	a coffee cup with saucer and steam
   ```

2. Codex 로 원본 생성 (Codex CLI 로그인 필요, 장당 약 1분, ChatGPT 사용량 차감)

   ```bash
   bash tools/icons/codex_generate.sh 목록.tsv 원본폴더
   ```

3. `assets/icons/manifest.tsv` 에 `이름<TAB>한글 이름<TAB>분류<TAB>입체` 추가 (플랫 세트는 `이름-2d ... 플랫`)

4. 다듬어서 넣기 (Python + Pillow 필요) — 미리보기 이미지로 배경색별 확인

   ```bash
   python tools/icons/process_icons.py 원본폴더 assets/icons --sheet 미리보기.png
   ```

   플랫(2D) 세트는 `ICON_STYLE=flat` 으로 만든 원본을 `-2d` 이름으로 넣는다.

   ```bash
   python tools/icons/process_icons.py 플랫원본폴더 assets/icons --suffix -2d --sheet 미리보기.png
   ```

5. `build.bat` → 아이콘 모음에 반영

## 스타일 기준

- 입체(기본): Windows 11 Fluent 3D 이모지 스타일, 부드러운 그라데이션, 왼쪽 위 조명, 은은한 그림자
- 플랫(`ICON_STYLE=flat`): 2D 벡터 스타일, 단색 면(최대 2톤), 그라데이션·그림자 없음, 둥근 모서리
- 공통: 투명 배경, 글자·숫자 없음, 캔버스의 약 80% 크기. 같은 문구를 써야 기존 아이콘과 어울린다.
