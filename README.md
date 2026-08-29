# HsmsLiteSimulator

반도체 장비 통신 표준인 **SEMI E37 (HSMS)** 의 핵심 동작을 재구현한 미니 시뮬레이터입니다.
Host(상위 제어 시스템)와 Equipment(장비) 양쪽 역할을 각각 콘솔 앱으로 만들어, TCP 위에서
HSMS 세션이 맺어지고 데이터를 주고받고 종료되는 전체 흐름을 눈으로 확인할 수 있습니다.

## HSMS란?

HSMS(High-Speed SECS Message Services)는 반도체/디스플레이 장비와 상위 제어 시스템(Host)이
TCP/IP로 통신할 때 쓰는 SEMI 표준 프로토콜입니다. SECS-II 메시지를 담는 "봉투" 역할을 하며,
연결 상태를 `NOT-CONNECTED → CONNECTED → SELECTED` 3단계로 관리하고, `Select`/`Linktest`/
`Separate` 같은 제어 메시지로 세션을 관리합니다. 이 프로젝트는 그중 실제로 자주 쓰이는
핵심 경로(Select → Data 교환 → Linktest → Separate)를 다룹니다.

## 프로젝트 구조

```
HsmsLiteSimulator/
├── HsmsLite.Protocol/        # 소켓에 의존하지 않는 순수 프로토콜 레이어
│   ├── HsmsMessageHeader.cs  # 10바이트 HSMS 헤더 (SEMI E37)
│   ├── HsmsMessage.cs        # 헤더 + 바디
│   ├── HsmsFraming.cs        # TCP 프레이밍 (4바이트 length-prefix, partial read 처리)
│   ├── HsmsStateMachine.cs   # 연결 상태 전이 및 위반 검증
│   ├── HsmsSType.cs          # 메시지 타입 / Select 상태 코드
│   ├── HsmsConnectionState.cs
│   ├── SystemBytesGenerator.cs
│   ├── HsmsRequestResponder.cs # SystemBytes 기준 요청/응답 상관관계 매칭 (Host/Equipment 공용)
│   └── HsmsProtocolException.cs
├── HsmsLite.Gem/              # GEM(SEMI E30) 메시지 레이어 - SECS-II 위에서 동작
│   ├── Secs2Item.cs           # SECS-II 아이템 인코더/디코더 (List/ASCII/Boolean/U4)
│   ├── Secs2List.cs / Secs2Ascii.cs / Secs2Boolean.cs / Secs2U4.cs
│   ├── Secs2FormatException.cs
│   ├── GemMessage.cs          # S/F 헤더 구성 및 검증 공통 로직
│   ├── GemMessageException.cs
│   ├── GemMessageNames.cs     # SxFy / 제어 메시지 이름 매핑 (통신 로그용)
│   ├── HsmsCommTrace.cs       # RECV/SEND 통신 트레이스 포맷 (hex dump + SECS-II 트리 + 트랜잭션 마커)
│   ├── S1Messages.cs          # S1F1/F2, S1F13/F14, S1F3/F4
│   ├── S2Messages.cs          # S2F41/F42 (Host Command)
│   └── S6Messages.cs          # S6F11/F12 (Event Report)
├── HsmsLite.Host/             # Active 역할: Equipment에 접속하는 클라이언트
├── HsmsLite.Equipment/        # Passive 역할: 접속을 기다리는 서버
├── HsmsLite.Protocol.Tests/   # 프로토콜 레이어 유닛 테스트 (xUnit)
├── HsmsLite.Gem.Tests/        # SECS-II 인코딩 / GEM 메시지 유닛 테스트 (xUnit)
└── docs/                      # 설계 노트 및 코드 정독 문서
```

- **`HsmsLite.Protocol`** 은 `Stream` 기반으로만 동작해서 실제 소켓 없이도 유닛 테스트가
  가능합니다. `Host`/`Equipment`는 이 위에 `TcpClient`/`TcpListener`를 얹은 애플리케이션입니다.
- **`HsmsLite.Gem`** 은 `Protocol`이 모르는 SECS-II/GEM 의미를 다루는 별도 레이어입니다.
  `Protocol`은 SEMI E37 전송 전용을 유지하고, `Host`/`Equipment`가 둘 다 참조합니다.
- HSMS-SS(Single Session) 특성상 Equipment는 한 번에 하나의 연결만 처리합니다. 세션이 끝나야
  다음 접속을 받는 것은 버그가 아니라 스펙에 맞는 동작입니다.

## 시연 시나리오

1. Equipment가 지정한 포트에서 대기
2. Host가 접속 후 `Select.req` 전송 → `SELECTED` 상태 진입
3. Host가 `S1F13`(Establish Communications)를 보내고 Equipment가 `S1F14`(COMMACK)로 응답
4. Host가 `S1F1`(Are You There) → Equipment가 `S1F2`(MDLN/SOFTREV)로 장비 식별 응답
5. Host가 `S1F3`(Status Request)로 SVID 목록을 조회 → Equipment가 `S1F4`로 값 응답
6. Host가 `S2F41`(Host Command "START") → Equipment가 `S2F42`(HCACK)로 응답
7. Equipment가 4초 간격으로 `S6F11`(Event Report)을 자발적으로 송신 → Host가 `S6F12`로 ack
8. Host가 `Linktest.req`로 연결 상태 확인
9. Host가 `Separate.req`를 보내 세션 종료

## 실행 방법

.NET 8 SDK가 필요합니다.

```bash
# 터미널 1 - Equipment(서버) 먼저 실행
dotnet run --project HsmsLite.Equipment

# 터미널 2 - Host(클라이언트) 실행
dotnet run --project HsmsLite.Host
```

기본 포트는 `5000`입니다. 인자 구성은 두 앱이 다릅니다.

- `Equipment.exe [port]` — 첫 번째 인자가 포트입니다. (`dotnet run --project HsmsLite.Equipment -- 6000`)
- `Host.exe [targetHost] [port]` — 첫 번째 인자가 접속할 host, 두 번째 인자가 포트입니다.
  (`dotnet run --project HsmsLite.Host -- 127.0.0.1 6000`)

로그는 콘솔과 `<앱 실행 폴더>/Log/log-yyyyMMdd.txt`에 함께 기록됩니다(Serilog,
`Log.ForContext<Program>()`으로 `SourceContext`까지 채워서 남깁니다).

## 통신 로그 포맷

RECV/SEND 메시지는 한 줄 요약이 아니라, 실제 SECS/GEM 드라이버 트레이스 로그처럼
"요약 줄 + hex dump + 디코딩된 SECS-II 트리"를 함께 남깁니다(`HsmsCommTrace`). 또한 Host가
보내는 모든 요청은 `GemMessageNames`로 이름을 붙이고, 응답을 기다리는 요청/응답 구간을
`---- OPEN ----` / `---- CLOSE (ms) ----` 마커로 감싸서 트랜잭션 경계를 눈으로 바로 알 수
있게 했습니다.

```
---- OPEN  S1F13 Establish Communications Request ----
(SEND) S1  F13 (SType:0)  Establish Communications Request  - 08/29/26 18:55:59
0000 >> 00 00 00 0c 00 01 01 8d 00 00 00 00 00 02 01 00
    <L  0
    > ..
(RECV) S1  F14 (SType:0)  Establish Communications Request Acknowledge  - 08/29/26 18:55:59
0000 >> 00 00 00 26 00 01 01 0e 00 00 00 00 00 02 01 02 25 01 01 01 02 41 0c 48 53 4d 53 4c 49 54 45 2d
0020 >> 45 51 50 41 05 31 2e 30 2e 30
    <L  2
        <B  1 01 >
        <L  2
            <A  12 'HSMSLITE-EQP'>
            <A  5 '1.0.0'>
        > ..
    > ..
---- CLOSE S1F13 Establish Communications Request (12 ms) ----
```

## 테스트

```bash
dotnet test HsmsLite.Protocol.Tests
dotnet test HsmsLite.Gem.Tests
```

`HsmsLite.Protocol.Tests`는 헤더/프레이밍 라운드트립, 상태 전이 정상/위반 케이스, SystemBytes
단조 증가성을 검증합니다. `HsmsLite.Gem.Tests`는 SECS-II 아이템 인코딩/디코딩 라운드트립과
S1/S2/S6 GEM 메시지의 빌드→파싱 라운드트립을 검증합니다.

## 알려진 스코프 (Lite 구현이 다루지 않는 것)

- SECS-II 아이템 인코딩은 GEM(SEMI E30) "표준" 메시지 세트(S1F1/F2, S1F13/F14, S1F3/F4,
  S2F41/F42, S6F11/F12)를 표현하는 데 필요한 최소한의 타입만 지원합니다: List, ASCII,
  Boolean, U4. Binary, I1~I8, U1/U2/U8, F4/F8, JIS-8, 2-byte 문자열은 지원하지 않으며,
  디코더가 인식하지 못하는 포맷 코드를 만나면 `Secs2FormatException`을 던집니다.
- COMMACK(S1F14)/HCACK(S2F42)/ACKC6(S6F12)은 실제 SEMI 표준의 Binary 1바이트 ack 코드 대신,
  수락/거부 두 가지만 표현하는 SECS-II `Boolean`으로 단순화했습니다.
- 멀티블록 메시지(하나의 SECS-II 메시지가 여러 HSMS 프레임에 걸치는 경우)는 지원하지 않습니다.
- `Deselect.req/rsp`, `Reject.req`는 상태 머신 검증 로직과 열거형에는 존재하지만, 데모
  시나리오에서 실제로 생성/전송되지는 않습니다(정상 경로는 Select → Separate만 사용).
- T3/T5/T6/T7 등 SEMI E37의 conformance 타이머는 구현하지 않았습니다. 요청/응답 타임아웃은
  `HsmsRequestResponder`에서 간단히 처리합니다.
