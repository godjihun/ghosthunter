# 씬 세팅 가이드

**씬 세팅은 완료돼 있다.** 이 문서는 ①무엇이 어떻게 구성돼 있는지 ②테스트 방법 ③이미 밟은 함정을 기록한다.

설계 근거는 [technical_design.md](technical_design.md), 작업 이력은 [progress.md](progress.md) 참고.

---

## 0. 사전 준비

### Multiplayer Play Mode
혼자 2인 테스트하려면 필수: Package Manager → `+` → `Install package by name...` → `com.unity.multiplayer.playmode`

### FBX Exporter (선택)
Mixamo에 모델을 올릴 FBX를 뽑을 때 쓴다. Package Manager → Unity Registry → `FBX Exporter`.
블렌더 없이 유니티만으로 `.glb` → `.fbx` 변환이 된다.

---

## 1. 현재 구성

### 씬 오브젝트

| 오브젝트 | 내용 |
|---|---|
| `NetworkManager` | `NetworkManager` + `UnityTransport`(127.0.0.1:7777). Player Prefab, Network Prefabs(ToolPrefab) 등록됨 |
| `GameManager` | `GameManager` + `NetworkBootstrapUI` + `GameHudUI`. Config·스폰 지점 연결됨 |
| `ToolSpawner` | 도구 30개를 종류당 균등하게 뿌린다 |
| `Altar` | 제단. **`Prop` 레이어여야** 상호작용에 걸린다 |
| `DoorManager` | 문 27개의 열림 상태를 `ulong` 비트마스크 하나로 동기화 |
| `Mansion` | 저택. 본체 메시(`Wall`) + 문 27개(`Door`), 각각 경첩 오브젝트 아래에 있음 |
| `GhostSpawn` / `ExorcistSpawns`(4) | 문 주변에서 아래로 레이캐스트해 실제 바닥에 배치 |
| `ToolSpawnPoints`(86) | 본관 내부에만 배치 |
| `Main Camera` | `LobbyCamera` — 로비·결과 화면 담당 |

### 플레이어 프리팹 (`Assets/Characters/PlayerPrefab.prefab`)

```
PlayerPrefab
├─ ExorcistModel        ← 리깅된 priest (Animator + ExorcistAnimator.controller)
├─ GhostModel           ← ghost_girl_animated
├─ CameraPivot (Y 1.6)
│   └─ PlayerCamera     ← MainCamera 태그 + AudioListener
└─ RadiusVisual         ← 미사용 (테두리 원은 LineRenderer로 런타임 생성)
```

컴포넌트: `CharacterController`(H 1.8 / R 0.3 / C Y0.9), `NetworkObject`, `ClientNetworkTransform`,
`PlayerInput`(Send Messages), `NetworkPlayer`, `PlayerMovement`, `PlayerLook`, `PlayerRoleSetup`,
`PlayerEmote`, `PlayerAnimation`, `ExorcistInventory`, `PlayerInteractor`, `DetectionRadiusView`,
`GhostController`, `GhostVisibility`, `FearSkill`

### 레이어

```
Wall(8)  Floor(9)  Door(10)  Character(11)  Prop(12)  Soul(13)  LocalBody(14)
```

- `Soul` × (`Door`, `Character`) = **통과** / × (`Wall`, `Floor`, `Prop`) = 충돌
- `LocalBody` = 렌더링 전용. **모든 물리 충돌 꺼져 있음**

---

## 2. 조작

| 키 | 퇴마사 | 귀신 |
|---|---|---|
| `WASD` / 마우스 | 이동 / 시점 | 이동 / 시점 |
| `Shift` | 달리기 (×1.6) | 달리기 (×2.0) |
| `Space` | 점프 | 점프 |
| `F` | 도구 줍기 · 문 여닫기 · 제단 헌납 | — (문은 통과) |
| 좌클릭 | 도구 사용 | — |
| `G` | 도구 버리기 | — |
| `Q` | — | 영혼 분리 / 복귀 |
| `Ctrl` | — | 조사=흡수 / 사냥=처형 |
| `Tab` 홀드 → 좌클릭 | 이모트 (3인칭) | 이모트 (사냥 단계만) |

**모든 상호작용은 누르는 즉시 실행된다.** 홀드 게이지는 없다.

---

## 3. 테스트

1. **Window → Multiplayer → Multiplayer Play Mode**에서 Virtual Player 1개 활성화
2. 메인 에디터 Play → **"호스트로 시작"**
3. 가상 플레이어 창에서 **"클라이언트로 접속"**
4. 호스트에서 **"게임 시작"** (2명 이상 필요)

### 확인할 것

| 항목 | 기대 |
|---|---|
| 진영 배정 | 한쪽 Ghost, 한쪽 Exorcist (무작위) |
| **약점 표시** | **귀신 화면에만** 3종. 퇴마사는 안 보임 |
| **귀신 은신** | 퇴마사 화면에서 귀신이 **안 보임** |
| 은신 단계 | 퇴마사는 **움직일 수 없다**. 안내 문구와 카운트다운 |
| 도구 | `F` 한 번에 즉시 수집, 하단 중앙 아이템 창에 표시 |
| 탐지 | 좌클릭 → **본인 화면에만** 결과. 성공·실패 표시 시간 동일 |
| 제단 | `F`로 헌납, 우측에 목록 |
| 사냥 단계 | 귀신이 **보이기 시작**, `Ctrl`로 처형 가능 |
| 재시작 | 결과 화면 → "대기방으로 돌아가기" → 다시 시작 시 도구·제단·진영이 초기화 |

**"귀신이 퇴마사 화면에 안 보인다"가 이 게임의 핵심이다.**

### ⚠️ Player 2 재시작이 필요한 경우

| 바꾼 것 | 재시작 |
|---|---|
| **씬** (오브젝트, 컴포넌트, 정적 플래그) | **필요** |
| **레이어·태그** | **필요** (시작 시 한 번만 읽음) |
| **`.inputactions`** | 권장 |
| C# 스크립트 | 불필요 (양쪽 자동 재컴파일) |
| ScriptableObject·프리팹·애니메이션 | 대개 불필요 |

가상 플레이어는 **별도 프로세스**라 메모리에 올린 씬을 자동으로 다시 읽지 않는다.

---

## 4. 이미 밟은 함정

### 정적 배칭 (`BatchingStatic`)
**움직이는 오브젝트에 걸면 안 된다.** 정점을 월드 좌표로 구워 합치므로, 트랜스폼이 돌아도 화면의 형상은 고정되거나 파편으로 터진다. 저택 문이 안 열리던 것과 저택이 산산조각 나 보이던 것 **둘 다 이것 때문**이었다. 현재 저택 전체가 해제돼 있다.

### `GameObject.Find()`는 비활성 오브젝트를 못 찾는다
저택을 잠시 꺼두고 되돌리려다 `Find`가 `null`을 반환해 꺼진 채로 저장된 사고가 있었다.
**씬을 바꾼 뒤에는 저장하고 디스크에서 다시 열어 확인할 것** — 로그 문구는 증거가 아니다.

### `NetworkConfig.NetworkTransport` 참조
`UnityTransport` 컴포넌트를 붙이는 것만으로는 부족하고 **참조 필드에 따로 연결**해야 한다.
스크립트로 `AddComponent`만 하면 비어 있다. 증상: `No transport has been selected!` + `StartClient()` NRE.

### UDP 소켓 누수 — "address already in use"
**증상**: 양쪽 다 미접속인데 호스트 시작이 실패하고, Play를 껐다 켜도 안 풀린다(에디터 종료만 답).

**원인은 정리 도중에 `Shutdown()`을 부르는 것.** 두 군데서 겪었다:
- `OnClientDisconnectCallback` 안에서 호출 → **다음 프레임으로 미뤄** 해결
- `OnDestroy`에서 호출 → **아예 제거.** Play 모드 종료 시 NGO가 스스로 정리하는데 끼어들면 소켓이 남는다

포트를 바꾸는 건 회피일 뿐이다. 확인:
```
Get-NetUDPEndpoint -LocalPort 7777 | ForEach-Object { Get-Process -Id $_.OwningProcess }
```

같은 PC에서는 **호스트가 하나뿐**이다. 나머지는 클라이언트로 접속해야 한다.

### 홀드형 입력은 상태를 물어야 한다
`PlayerInput`의 Send Messages는 **누를 때만 통지가 오고 뗄 때는 오지 않는다.**
Shift 달리기와 Tab 이모트 휠에서 각각 한 번씩 이 함정을 밟았다(달리기가 안 먹거나, 휠이 안 닫힘).
`InputAction.IsPressed()`로 **매 프레임 현재 상태를 읽는다.**

### Mixamo 애니메이션 임포트
- 캐릭터는 **T-Pose**로, 동작은 **`Without Skin`**으로 받는다(메시 중복 방지 — WebGL 용량)
- 전부 `Generic`으로 들어오므로 **`Humanoid` + 아바타 연결**(`Copy From Other Avatar`)을 해야 붙는다
- **정지 포즈 클립**(앉기 등)은 `Loop Time`을 켜야 자세가 유지되고, **`Root Transform Position (Y)` → `Bake Into Pose`**를 켜야 실제로 몸이 내려간다. 안 켜면 높이차가 루트 모션으로 빠지는데 Root Motion이 꺼져 있어 서 있는 채로 다리만 접힌다
- 텍스처는 FBX에 안 딸려가지만 **UV가 보존**되므로 원본 머티리얼을 다시 씌우면 복원된다
- **Root Motion은 끈다** — `CharacterController`와 싸워 미끄러진다
- 임포트가 멈춰 임포터가 `null`이 되는 일이 있다. **유니티 창에 포커스를 주면** 진행된다

### 모델 정렬 진단
눈으로 보기 전에 경계상자 수치로 잡을 수 있다:
- **최장축이 Y가 아니면** 누워 있는 것
- **수평 중심(X, Z)이 0이 아니면** 회전 시 공전한다

### URP 투명 재질
`Translucent_M` / `Glass_Common` / `GLASS_CLEAR`는 **알파가 0인데도 반사광 때문에 뿌옇게 보였다.**
반사광 계산이 없는 Unlit 투명 머티리얼(`Assets/Environments/MainHouse/Invisible.mat`)로 교체했다.
콜라이더는 그대로라 맵 경계 역할은 유지된다.

---

## 5. 아직 구현되지 않은 것

- 관전 시스템 (사망자 → 생존자 3인칭, 좌우 화살표) — **닉네임 선행 필요**
- 사망자 전용 채팅
- 방 개설 UI (닉네임·인원제한·비밀번호), 방장 밸런스 조절
- 연출·사운드 전반 (현재 `Debug.Log`만)
- 귀신 애니메이션 (기본 클립 1개뿐)
- Relay/Lobby 연동, WebGL 빌드·배포
