# GEM(SEMI E30) 레이어 추가 — 설계 노트

이 문서는 `HsmsLite.Gem` 프로젝트와 그에 딸린 변경들을 왜 이런 모양으로 만들었는지 정리한
것입니다. "무엇을 바꿨는지"는 코드가 보여주니, 여기서는 "왜 이렇게 했는지"만 남깁니다.

## 왜 GEM 레이어가 필요했나

기존에는 HSMS(SEMI E37) 전송 계층만 있고, 실제 데이터는
`HsmsMessage.DataText`로 만든 임시 텍스트("StatusRequest", "EventReport#1;...")였다.
전송 프레이밍/상태 전이는 진짜지만 알맹이는 가짜였던 셈이라, 다음 개발 방향으로 SECS-II
인코딩과 GEM 메시지(S1F1/F2, S1F13/F14, S1F3/F4, S2F41/F42, S6F11/F12)를 실제로 태워서
Host-Equipment 시나리오를 다시 구성했다.

범위는 두 가지로 확정했다:
- **메시지 세트: "표준"** — 위 5개 스트림/펑션 쌍. GEM의 핵심 시나리오(통신 확립 → 식별 →
  상태 조회 → 커맨드 → 이벤트 리포트)를 한 바퀴 다 도는 정도.
- **SECS-II 인코딩: "필요한 만큼만"** — List, ASCII, Boolean, U4 4종. Binary/I1~I8/U1~U8/F4/F8은
  이 5개 메시지를 표현하는 데 안 쓰이므로 구현하지 않았다.

## 왜 `HsmsLite.Gem`을 별도 프로젝트로 뺐나

`HsmsLite.Protocol`은 원래부터 "소켓도 모르고 SECS-II도 모르는, HSMS 전송 전용" 레이어라는
경계를 지키고 있었다(README에도 명시돼 있던 원칙). SECS-II/GEM은 SEMI E5/E30이라는 별개
표준이라, 여기에 섞으면 그 경계가 흐려진다. 그래서 `Protocol`을 참조하는 새 프로젝트로
분리했다.

반대로 SECS-II 코덱(`Secs2Item` 계열)을 또 별도 프로젝트로 쪼개지는 않았다 — 타입 4종에
코드량이 적어서 그렇게까지 나누면 실속 없는 레이어만 하나 늘어난다. 코덱 파일들과 메시지
빌더 파일들을 `HsmsLite.Gem` 안에 나란히 뒀다.

## 왜 `HsmsRequestResponder`를 `Protocol`로 추출했나

GEM 이전에는 "요청 보내고 응답 기다리기"(`SendAndWaitAsync` + SystemBytes로 매칭하는
`ConcurrentDictionary` + 쓰기 동시성 보호용 `SemaphoreSlim`) 패턴이 Host에만 있었다.
Equipment는 항상 응답만 했지 스스로 요청을 보내고 기다릴 일이 없었기 때문이다.

S6F11(이벤트 리포트)부터는 Equipment도 보내고 S6F12 ack을 기다려야 하는 입장이 됐다. 이때
Host의 패턴을 그대로 복사-붙여넣기 할 수도 있었지만, 이 로직은 상태(대기 중인 요청 목록)를
갖고 있는 동시성 코드라서 두 벌로 나뉘면 한쪽만 버그를 고치고 다른 쪽은 못 고치는 식으로
갈라질 위험이 있다. (`ConfigureLogging`/`ParsePort`처럼 상태 없는 코드는 지금도 그냥
복붙 유지 — 그런 코드는 갈라져도 위험하지 않다.) 그래서 공유 클래스로 뽑아 `Protocol`에
뒀다. SystemBytes 상관관계 매칭 자체가 SEMI E37(HSMS) 개념이지 GEM 전용이 아니라는 점도
`Gem`이 아니라 `Protocol`에 두는 근거였다.

## 메시지 빌더를 스트림별 정적 클래스로 나눈 이유

메시지 5쌍을 파일 10개로 쪼개는 것도, 전부 하나의 클래스에 뭉치는 것도 이 코드베이스의
"작고 초점이 분명한 파일" 스타일과 안 맞아서, `S1Messages`/`S2Messages`/`S6Messages`처럼
스트림 단위로 나눴다. 헤더 구성(W-bit 반영)과 스트림/펑션 검증처럼 5쌍이 공통으로 쓰는
로직은 `GemMessage`(internal)로 한 번만 작성했다.

## ack 코드를 Boolean으로 단순화한 이유

실제 SEMI E30의 COMMACK(S1F14)/HCACK(S2F42)/ACKC6(S6F12)은 여러 값을 가지는 Binary(1바이트)
코드다. 하지만 Binary 타입 자체가 이번 범위 밖이고, 이 데모에서는 "수락/거부" 두 가지
결과만 있으면 충분해서 `Secs2Boolean`으로 대체했다. 스펙을 문자 그대로 따르기보다 이
시뮬레이터의 목적(흐름을 보여주는 것)에 맞춘 의도적 단순화다. README의 "알려진 스코프"에도
명시해 뒀다.

## 검증 중 발견해서 고친 것: `Secs2Item.ToString()`

Equipment/Host를 실제로 붙여서 로그를 눈으로 확인하는 과정에서, Host 쪽 S6F11 unsolicited
이벤트 로그가 `values=[HsmsLite.Gem.Secs2Ascii,HsmsLite.Gem.Secs2Ascii,HsmsLite.Gem.Secs2U4]`
처럼 타입 이름만 찍히는 걸 발견했다. `Secs2Item` 하위 타입들이 `ToString()`을 오버라이드하지
않아서 기본 `object.ToString()`(전체 타입 이름)이 찍힌 것. `Secs2Ascii`/`Secs2Boolean`/
`Secs2U4`/`Secs2List`에 각각 값을 보여주는 `ToString()`을 추가해서
`values=["RUN","LOT-1001",25]`처럼 실제 값이 보이게 고쳤다. (`HsmsMessage`가 이미
`ToString()`을 오버라이드해서 로그를 읽기 좋게 만드는 것과 같은 이유.)

## 여전히 범위 밖인 것

- Binary, I1~I8, U1/U2/U8, F4/F8, JIS-8, 2-byte 문자열 SECS-II 타입
- 멀티블록 메시지(하나의 SECS-II 메시지가 여러 HSMS 프레임에 걸치는 경우)
- T3/T5/T6/T7 SEMI E37 conformance 타이머(요청/응답 타임아웃은 `HsmsRequestResponder`가
  단순 처리)
