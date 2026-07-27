# CLAUDE.md

이 파일은 이 저장소에서 작업하는 Claude Code(claude.ai/code)에게 제공하는 가이드다.

## 프로젝트 개요

`gosthunter`(귀신게임)는 비대칭 멀티플레이 공포/추리 게임을 만드는 Unity 프로젝트다: 저택에서 귀신 1명 vs 다수의 퇴마사가 대결한다. 진영 구성, 도구/탐지 시스템, 제단 조합 시스템, 공포스킬(영혼 흡수), 로비부터 사냥 단계까지의 게임 흐름, 승리 조건, 조절 가능한 밸런스 파라미터 등 전체 게임 디자인은 [Docs/ghost_game_scenario.md](Docs/ghost_game_scenario.md)에 정리돼 있다 — 게임플레이 시스템을 구현하기 전엔 반드시 이 문서를 읽을 것. 특히 탐지 결과의 모호성(문서 3-2)이나 페널티/재은신 루프(문서 3-5) 같은 메커니즘은 문서 없이 구현하면 미묘하게 틀리기 쉽다.

기술 스택 결정, 네트워크 아키텍처, 조작 체계, 시스템별 구현 설계는 [Docs/technical_design.md](Docs/technical_design.md)에 정리돼 있다 — 게임플레이 코드를 짜기 전에 읽을 것. **특히 2번(네트워킹) 절은 반드시 읽어야 한다.** 이 게임은 은닉 정보 게임이라 "귀신의 위치"와 "약점 도구 3종"을 특정 클라이언트에 흘리지 않는 것이 설계의 축이다.

⚠️ **귀신 위치만은 렌더러 제어로 바꿨다** (2-2-1). `NetworkHide`는 서버 자신에게 쓸 수 없어 호스트가 퇴마사가 되면 뚫리기 때문이며, 사전과제라 화면상 은닉으로 충분하다는 판단이다. **약점(`ReadPermission.Owner`)과 탐지 결과(`RpcTarget.Single`)는 그대로 유지**되므로 이 둘을 렌더러나 UI 필터링으로 처리하지 말 것. 데이터가 도착한 순간 뚫린 것이다.

씬 구성, 조작, 이미 밟은 함정은 [Docs/setup_guide.md](Docs/setup_guide.md)에 있다. **정적 배칭·비활성 오브젝트 검색·소켓 누수·홀드형 입력** 네 가지는 반복해서 문제를 냈으니 관련 작업 전에 볼 것.

게임 루프는 대부분 구현돼 있다 (이동·도구·탐지·제단·단계 전이·승패·문·애니메이션·이모트). 미구현은 관전, 사망자 채팅, 로비 UI, Relay/배포 — 목록은 progress.md 최하단.

**이 프로젝트에는 외부 제출 요건이 있다** — 웹 빌드를 호스팅해 **링크 클릭만으로 브라우저에서 바로 플레이**할 수 있어야 하고, **심사자가 별도 유료 라이선스 없이 실행**할 수 있어야 한다. 유료 서비스나 네이티브 전용 기능을 도입하려 할 땐 이 요건과 충돌하는지 먼저 확인할 것. 자세한 내용은 technical_design.md 최상단 "제출 요건" 절.

## 문서 유지 (매 작업마다)

**세션 시작 시 [Docs/progress.md](Docs/progress.md)를 먼저 읽어** 이미 된 작업을 파악할 것. 그리고 작업이 끝날 때마다 아래 두 문서를 **계속 갱신한다.**

1. **[Docs/prompt_log.md](Docs/prompt_log.md)** — 사용자의 지시·질문을 **원문 그대로** 시간순으로 모은다. 제출용이므로 **요약하거나 다듬지 말 것.** 아직 안 들어간 것 중 **중요한 것만** 골라 추가한다: 수정 요청은 전부, 질문은 설계 판단이 갈린 것만. `[수정]`/`[질문]` 태그를 붙이고 결과·해설은 넣지 않는다.
2. **[Docs/progress.md](Docs/progress.md)** — 날짜별로 **최신이 위로** 오게, 항목당 **2~3줄로 짧게** 쓴다. 길어질 것 같으면 근거는 technical_design.md, 절차는 setup_guide.md로 보내고 여기엔 결론만 남긴다. 다 끝나고 몰아 쓰지 말고 그때그때 추가할 것.

설계가 바뀌면 [Docs/technical_design.md](Docs/technical_design.md)의 해당 절도 같이 고친다 — 문서가 코드와 어긋나면 다음 세션이 틀린 전제로 작업한다.

## 환경

- Unity 에디터 버전: **6000.5.4f1** (`ProjectSettings/ProjectVersion.txt` 참조) — 프로젝트 파일이 버전에 고정돼 있으므로 사용자 확인 없이 에디터 버전을 업그레이드하지 말 것.
- 렌더 파이프라인: Universal Render Pipeline(URP). PC용/모바일용 렌더러·퀄리티 에셋이 `Assets/Settings/` 아래 별도로 존재.
- **빌드 타겟: WebGL(Unity Web)**. 링크 하나로 브라우저에서 바로 플레이하는 게 목표다. 이 때문에 따라오는 제약: C# 멀티스레딩 불가, UDP 불가(WebSocket 전용), 브라우저는 서버가 될 수 없어 Relay가 필수. 조명·텍스처 작업 시 WebGL 비용을 염두에 둘 것 (technical_design.md "WebGL 성능과 배포" 절).
- 입력: 새 Input System 사용 (`Assets/InputSystem_Actions.inputactions`), 레거시 Input Manager 아님. 이 게임에 맞게 재편돼 있다(`Interact`=F, `Skill`/`Detach`=Ctrl/Q, `EmoteWheel`=Tab 등). 키 표는 technical_design.md 4번.
  - ⚠️ `PlayerInput`은 **Send Messages** 방식이라 **누를 때만 통지가 오고 뗄 때는 오지 않는다.** 홀드형 입력(Shift 달리기, Tab 휠)은 반드시 `InputAction.IsPressed()`로 **매 프레임 상태를 읽을 것** — 이 함정을 두 번 밟았다.
- 주요 설치 패키지 (`Packages/manifest.json`): `com.unity.inputsystem`, `com.unity.ai.navigation`, `com.unity.timeline`, `com.unity.visualscripting`, `com.unity.test-framework`, `com.unity.multiplayer.center`, `com.unity.multiplayer.playmode`.
- **NGO는 2.13.1이 설치돼 있다. 버전을 내리지 말 것** — 2.7.0은 Unity 6000.5.4에서 패키지 내부가 컴파일되지 않는다. UGS Relay/Lobby는 아직 미설치(로컬 `127.0.0.1:7777`로 테스트 중). Playroom Kit이나 Mirror를 추가하지 말 것.
- **NGO는 2.x 기준으로 작성한다.** RPC 문법이 1.x(`[ServerRpc]`/`[ClientRpc]`)와 2.x(`[Rpc(SendTo.X)]`)가 다른데 인터넷 예제 대부분이 1.x다 — 참고 코드를 가져올 때 버전을 먼저 확인할 것.
- 서버 권한(client-server) 모델을 쓴다. distributed authority 모드는 쓰지 않는다 — 권한이 분산되면 비밀을 독점할 주체가 사라진다.

## 이 저장소에서 작업할 때

- 커맨드라인 빌드/테스트 파이프라인은 없다. 빌드, Play 모드 실행, 테스트 실행은 모두 Unity 에디터 UI를 통해 이뤄진다 (Unity Test Framework는 설치돼 있지만 아직 테스트 어셈블리/테스트 코드는 없음).
- `Assets/`, `Packages/`, `ProjectSettings/` 바깥에 있는 파일(예: `Docs/`, `CLAUDE.md`)은 Unity 에셋 파이프라인이 무시한다 — 에디터에 영향 없이 자유롭게 문서를 추가해도 됨.
- `Library/`, `Temp/`, `Logs/`, `UserSettings/`는 Unity가 자동 생성하며 gitignore 대상이다 — 직접 수정하거나 내용에 의존하지 말 것.
- **사용자가 테스트하는 동안 씬을 건드리지 말 것.** 진단한답시고 오브젝트를 꺼봤다가 사용자 화면에 가짜 증상을 만들어낸 적이 있다. 씬을 바꿨으면 **저장하고 디스크에서 다시 열어 확인**한다 — 로그 문구는 증거가 아니다. `GameObject.Find()`는 비활성 오브젝트를 못 찾으므로 되돌리기 스크립트가 조용히 실패한다.
- 씬·레이어·태그를 바꾸면 **가상 플레이어(MPPM) 창을 재시작**해야 반영된다(별도 프로세스). 스크립트만 바꿨으면 불필요. 자세한 표는 setup_guide.md 3번.
