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
│   └── HsmsProtocolException.cs
├── HsmsLite.Host/            # Active 역할: Equipment에 접속하는 클라이언트
├── HsmsLite.Equipment/       # Passive 역할: 접속을 기다리는 서버
└── HsmsLite.Protocol.Tests/  # 프로토콜 레이어 유닛 테스트 (xUnit)
```

- **`HsmsLite.Protocol`** 은 `Stream` 기반으로만 동작해서 실제 소켓 없이도 유닛 테스트가
  가능합니다. `Host`/`Equipment`는 이 위에 `TcpClient`/`TcpListener`를 얹은 애플리케이션입니다.
- HSMS-SS(Single Session) 특성상 Equipment는 한 번에 하나의 연결만 처리합니다. 세션이 끝나야
  다음 접속을 받는 것은 버그가 아니라 스펙에 맞는 동작입니다.

## 시연 시나리오

1. Equipment가 지정한 포트에서 대기
2. Host가 접속 후 `Select.req` 전송 → `SELECTED` 상태 진입
3. Host가 `StatusRequest`를 보내고 Equipment가 상태 정보로 응답
4. Equipment가 4초 간격으로 이벤트 리포트(Unsolicited Data Message)를 자발적으로 송신
5. Host가 `Linktest.req`로 연결 상태 확인
6. Host가 `Separate.req`를 보내 세션 종료

## 실행 방법

.NET 8 SDK가 필요합니다.

```bash
# 터미널 1 - Equipment(서버) 먼저 실행
dotnet run --project HsmsLite.Equipment

# 터미널 2 - Host(클라이언트) 실행
dotnet run --project HsmsLite.Host
```

기본 포트는 `5000`이며, 각 앱의 첫 번째 인자로 포트를 바꿀 수 있습니다
(`dotnet run --project HsmsLite.Equipment -- 6000`). 로그는 콘솔과
`<앱 실행 폴더>/Log/log-yyyyMMdd.txt`에 함께 기록됩니다(Serilog).

## 테스트

```bash
dotnet test HsmsLite.Protocol.Tests
```

헤더/프레이밍 라운드트립, 상태 전이 정상/위반 케이스, SystemBytes 단조 증가성을 검증합니다.

## 알려진 스코프 (Lite 구현이 다루지 않는 것)

- SECS-II 아이템(가변 길이 바이너리 인코딩) 대신 UTF-8 텍스트를 바디로 사용합니다. 실제
  SECS-II 아이템 인코딩/디코딩은 범위 밖입니다.
- `Deselect.req/rsp`, `Reject.req`는 상태 머신 검증 로직과 열거형에는 존재하지만, 데모
  시나리오에서 실제로 생성/전송되지는 않습니다(정상 경로는 Select → Separate만 사용).
- T3/T5/T6/T7 등 SEMI E37의 conformance 타이머는 구현하지 않았습니다. 요청/응답 타임아웃은
  Host 쪽에서 별도로 간단히 처리합니다.
