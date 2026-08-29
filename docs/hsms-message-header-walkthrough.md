# 코드 정독: `HsmsMessageHeader.cs`

`HsmsLite.Protocol/HsmsMessageHeader.cs`를 필드/메서드 단위로 뜯어서 "이게 무슨
의미이고, 어떻게 동작하고, 왜 이런 이름을 붙였는지"를 정리한 것입니다. GEM 레이어
쪽(`GemMessage`/`S1Messages`) 정리는 [gem-message-builder-walkthrough.md](gem-message-builder-walkthrough.md)에 있습니다.

---

## 왜 필드 이름이 `SessionId`, `Byte2`, `Byte3`, `PType`, `SType`, `SystemBytes`인가

가장 먼저 풀어야 할 의문: 다른 필드는 다 의미 있는 이름(`SessionId`, `SystemBytes`...)인데
왜 두 개만 `Byte2`, `Byte3`처럼 "그냥 위치"로 이름 붙였는가.

**핵심은 HSMS(SEMI E37) 10바이트 헤더가, 물리적으로는 항상 같은 레이아웃이지만 각
바이트의 "의미"는 메시지 종류(`SType`)에 따라 달라진다는 점입니다.**

필드를 두 그룹으로 나눠서 봐야 합니다.

### ① 의미가 항상 고정인 필드 → 이름도 의미대로 지음

| 필드 | 위치 | 항상 이 의미 |
|---|---|---|
| `SessionId` | [0-1], 2바이트 | 세션/디바이스 식별자 (제어 메시지에선 관례상 `0xFFFF`) |
| `PType` | [4], 1바이트 | Presentation Type — SECS-II 위에서는 항상 `0` |
| `SType` | [5], 1바이트 | **이 메시지가 무슨 종류인지** 나타내는 태그 (`DataMessage=0`, `SelectReq=1`, `SelectRsp=2`, `LinktestReq=5`, ...) |
| `SystemBytes` | [6-9], 4바이트 | 요청-응답을 매칭하는 transaction ID. 항상 같은 의미 |

### ② 의미가 `SType`에 따라 달라지는 필드 → 이름을 못 박지 않고 원시 위치 이름 그대로 둠

| 필드 | 위치 | `SType = DataMessage`일 때 | `SType`이 제어 메시지일 때 |
|---|---|---|---|
| `Byte2` | [2] | **Stream** (S1, S2, S6...) | 대부분 `0x00`, 사용 안 함 |
| `Byte3` | [3] | **Function + W-bit** (`function \| 0x80`) | `Select.rsp`에서는 "Select Status"(수락/거부 사유), `Reject.req`에서는 거부 이유 코드, 나머지는 보통 `0` |

`Byte2`를 그냥 `Stream`이라고 이름 붙이면, `Select.req` 같은 제어 메시지에서는 그 필드가
아무 의미가 없는데도 "Stream"이라는 이름표를 달고 있는 거짓말하는 코드가 됩니다.
`Byte3`을 `Function`이라 부르면 `Select.rsp`가 그 자리에 담는 "Select Status"와 이름이
안 맞습니다. 그래서 `HsmsMessageHeader`는 **프로토콜 레벨의 "로우 와이어
구조체"**로서 일부러 의미를 부여하지 않고 위치로만 이름 붙였고, 실제 의미 해석은 그
위 레이어가 맡습니다:
- 데이터 메시지 쪽 해석 → `HsmsLite.Gem/GemMessage.cs`(`Build`가 `Byte3`에 W-bit를
  얹고, `AssertStreamFunction`이 `Byte2`/`Byte3`을 Stream/Function으로 읽음)
- 제어 메시지 쪽 해석 → `HsmsMessage.Control(...)` / `HsmsSelectStatus` enum

즉 "필드 이름이 왜 이렇게 애매하냐"의 답은 "애매한 게 아니라, HSMS 스펙 자체가 그
바이트들에 다의미(multi-purpose) 슬롯을 배정했고, 헤더 구조체는 그 사실을 숨기지 않고
그대로 드러낸 것"입니다.

---

## 코드 본문

```csharp
public readonly struct HsmsMessageHeader
{
    public const int Length = 10;

    public ushort SessionId { get; }
    public byte Byte2 { get; }
    public byte Byte3 { get; }
    public byte PType { get; }
    public HsmsSType SType { get; }
    public uint SystemBytes { get; }

    public HsmsMessageHeader(ushort sessionId, byte byte2, byte byte3, HsmsSType sType, uint systemBytes, byte pType = 0)
    { ... }

    public byte[] ToBytes() { ... }
    public static HsmsMessageHeader Parse(ReadOnlySpan<byte> buf) { ... }
    public static HsmsMessageHeader Control(HsmsSType sType, uint systemBytes, byte byte3 = 0) { ... }
    public override string ToString() { ... }
}
```

### 타입 선언: `public readonly struct HsmsMessageHeader`

**의미**: 10바이트 헤더 하나를 표현하는 값 타입(value type).

**문법 포인트**
- **`struct`(클래스가 아니라)**: 힙이 아니라 스택/인라인에 값으로 저장되는 타입. 헤더는
  "값 6개를 담은 작은 묶음"일 뿐, 참조 동일성(reference identity)이 의미 있는 객체가
  아니라서 struct가 자연스럽다. 메시지마다 새 헤더가 계속 만들어지고 버려지는데(매
  `Build` 호출마다), struct면 GC 힙 할당 없이 가볍게 오간다.
- **`readonly struct`**: 모든 필드가 생성 이후 절대 안 바뀐다는 걸 컴파일러에게
  보장(불변, immutable). 컴파일러는 이 보장을 믿고 방어적 복사(defensive copy) 같은
  최적화를 더 적극적으로 할 수 있다. 실제로 모든 프로퍼티가 `{ get; }`만 있고 `set`이
  없어서 생성자 이후 값이 바뀔 방법이 없다.
- **`public const int Length = 10`**: 컴파일 타임 상수. 인스턴스 없이 `HsmsMessageHeader.Length`로
  바로 접근 가능하고, "10바이트"라는 매직 넘버를 코드 여러 곳(`ToBytes`, `Parse`)에서
  반복하지 않고 한 곳에서 정의.

### 생성자

```csharp
public HsmsMessageHeader(ushort sessionId, byte byte2, byte byte3, HsmsSType sType, uint systemBytes, byte pType = 0)
{
    SessionId = sessionId;
    Byte2 = byte2;
    Byte3 = byte3;
    PType = pType;
    SType = sType;
    SystemBytes = systemBytes;
}
```

**의미**: 6개 필드 값을 그대로 받아서 채우기만 하는 순수 초기화 생성자.

**문법 포인트**
- **`byte pType = 0` 기본 인자**: 실무에서 `PType`은 SECS-II 위에서 거의 항상 `0`이라,
  호출부 대부분이 이 인자를 생략한다(위 예시의 `GemMessage.Build`도 생략).
- **get-only 자동 프로퍼티**(`public ushort SessionId { get; }`)에 생성자 본문에서
  대입: get-only 자동 프로퍼티는 선언부 또는 생성자 안에서만 값을 대입할 수 있고, 그
  뒤로는 읽기 전용이 된다 — `readonly struct`와 짝을 이루는 불변성 패턴.

### `ToBytes()` — 인코딩

```csharp
public byte[] ToBytes()
{
    var buf = new byte[Length];
    BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0, 2), SessionId);
    buf[2] = Byte2;
    buf[3] = Byte3;
    buf[4] = PType;
    buf[5] = (byte)SType;
    BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(6, 4), SystemBytes);
    return buf;
}
```

**의미**: 이 구조체를 실제로 네트워크에 흘려보낼 10바이트 배열로 직렬화한다. 필드
순서 그대로 `[SessionId(2) | Byte2(1) | Byte3(1) | PType(1) | SType(1) | SystemBytes(4)]`
레이아웃을 그대로 만든다.

**동작 흐름**
1. 길이 10짜리 빈 배열 `buf`를 만든다.
2. `SessionId`(`ushort`, 2바이트)를 **빅엔디안**(big-endian, 네트워크 바이트 순서 —
   상위 바이트가 먼저)으로 `buf[0..2]`에 써넣는다.
3. `Byte2`, `Byte3`, `PType`은 원래 1바이트짜리라 그대로 대입.
4. `SType`은 enum이라 `(byte)`로 캐스팅해서 원래의 숫자 값(`byte` 기반 enum이므로
   그대로 1바이트)으로 되돌려서 대입.
5. `SystemBytes`(`uint`, 4바이트)도 빅엔디안으로 `buf[6..10]`에 써넣는다.

**문법 포인트**
- **`BinaryPrimitives.WriteUInt16BigEndian` / `WriteUInt32BigEndian`**:
  `System.Buffers.Binary` 네임스페이스의 헬퍼. `ushort`/`uint` 같은 멀티바이트 정수를
  지정한 바이트 순서로 메모리에 직접 써준다. 직접 시프트 연산(`>> 8`, `& 0xFF`)으로
  구현할 수도 있지만, 이 API가 안전하고 의도가 분명하다.
  `BinaryPrimitives`: (원시 데이터 타입(short, int, long 등)을 바이트 배열로 변환할 때 엔디안(Endianness, 바이트 저장 순서)을 명확하게 지정)
  `WriteUInt16BigEndian`: (네트워크 표준 통신 방식인 빅 엔디안 순서로 변환하여 버퍼의 첫 2바이트 위치에 기록)
- **`buf.AsSpan(0, 2)`**: 배열의 일부 구간을 가리키는 `Span<byte>`(복사 없는 뷰)를
  만든다. `WriteUInt16BigEndian`은 이 span 안에 직접 쓴다 — 배열을 통째로 슬라이싱해서
  새 배열을 만드는 것보다 훨씬 저렴하다.
  (buf.AsSpan(0, 2): buf 배열 전체 중 0번 인덱스부터 시작해서 2바이트만큼의 영역만 슬라이싱하여 BinaryPrimitives에 전달하겠다는 의미)
- **`(byte)SType`**: enum → 밑바탕 타입(underlying type, 여기선 `byte`)으로의 명시적
  캐스팅. `HsmsSType : byte`로 선언돼 있으므로(아래 `Parse` 설명 참고) 이 캐스팅은
  단순히 enum 값의 숫자를 그대로 꺼내는 것.

### `Parse(...)` — 디코딩

```csharp
public static HsmsMessageHeader Parse(ReadOnlySpan<byte> buf)
{
    if (buf.Length != Length)
        throw new ArgumentException($"HSMS header must be exactly {Length} bytes, got {buf.Length}.", nameof(buf));

    var sessionId = BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(0, 2));
    var byte2 = buf[2];
    var byte3 = buf[3];
    var pType = buf[4];
    var sType = (HsmsSType)buf[5];
    var systemBytes = BinaryPrimitives.ReadUInt32BigEndian(buf.Slice(6, 4));

    return new HsmsMessageHeader(sessionId, byte2, byte3, sType, systemBytes, pType);
}
```

**의미**: `ToBytes()`의 정반대 — 소켓에서 읽은 원시 10바이트를 다시 구조화된
`HsmsMessageHeader` 값으로 복원한다. 정적 팩터리 메서드(static factory method) 패턴.

**동작 흐름**
1. 길이가 정확히 10바이트가 아니면 즉시 예외 — 나머지 로직이 "정확히 10바이트"라고
   가정하고 인덱스를 하드코딩(`buf[2]`, `buf.Slice(6, 4)` 등)하기 때문에, 여기서
   걸러내지 않으면 뒤에서 인덱스 초과 예외로 훨씬 알아보기 어렵게 죽는다.
2. `ToBytes()`와 정확히 대칭되는 순서로 각 필드를 읽어낸다.
3. `buf[5]`(숫자 1바이트)를 `(HsmsSType)`로 캐스팅해서 enum 값으로 되돌린다.
4. 읽은 값들로 새 `HsmsMessageHeader`를 생성해서 반환.

**문법 포인트**
- **`static` 팩터리 메서드**: 생성자 대신 `Parse`라는 이름의 정적 메서드를 쓴 이유는
  "바이트 배열을 검증하고 해석해서 만든다"는 게 단순 필드 대입 생성자와는 성격이 달라서
  (실패할 수 있는 변환 로직 포함). 이름이 의도를 드러낸다 — `new HsmsMessageHeader(bytes)`보다
  `HsmsMessageHeader.Parse(bytes)`가 "이건 파싱이다"라고 더 명확히 말해준다.
- **`ReadOnlySpan<byte> buf`**: 매개변수 타입이 `byte[]`가 아니라 `ReadOnlySpan<byte>`인
  이유는, 호출자가 배열 전체든 배열의 일부(`someBiggerBuffer.AsSpan(offset, 10)`)든
  복사 없이 넘길 수 있게 하기 위함. 소켓에서 받은 큰 버퍼 중 헤더 부분 10바이트만
  떼어서 넘기는 상황에 최적화돼 있다.
- **`nameof(buf)`**: 문자열 `"buf"`를 직접 쓰는 대신 `nameof`로 컴파일 타임에
  매개변수 이름을 얻는다. 나중에 매개변수 이름을 바꾸면 이 부분도 컴파일러가 강제로
  같이 바뀌게(또는 리팩터 도구가 같이) 만들어주는 안전장치.
- **`(HsmsSType)buf[5]`**: `byte` → enum으로의 명시적 캐스팅. `ToBytes()`의
  `(byte)SType`와 정반대 방향. C#의 enum 캐스팅은 값 검사를 하지 않으므로, `buf[5]`가
  `HsmsSType`에 정의되지 않은 값(예: `8`)이어도 예외 없이 그냥 그 숫자를 담은 enum 값이
  된다 — 뒤에서 `switch`가 처리 못 하는 값으로 나타날 수 있다는 뜻(이 구조체 자체는
  그 검증까지는 안 한다).

### `Control(...)` — 제어 메시지 전용 팩터리

```csharp
public static HsmsMessageHeader Control(HsmsSType sType, uint systemBytes, byte byte3 = 0)
    => new(0xFFFF, 0, byte3, sType, systemBytes);
```

**의미**: `Select.req`, `Linktest.req` 같은 HSMS 제어 메시지는 항상 `SessionId = 0xFFFF`,
`Byte2 = 0`이라는 규칙이 있다(제어 메시지는 특정 세션에 속하지 않으므로). 이 반복되는
패턴을 캡슐화한 편의 생성 메서드.

**동작**: `byte3`만 호출자가 선택적으로 지정할 수 있게 열어두고(`Select.rsp`의 Select
Status 같은 경우), 나머지는 고정값으로 채운 새 헤더를 만든다.

**문법 포인트**
- **식 본문 멤버** `=> new(...)`: 한 줄로 끝나는 메서드라 `{ return ...; }` 대신 `=>` 사용.
- **target-typed `new`(`new(...)`)**: C# 9부터 가능한 문법으로, 반환 타입이
  `HsmsMessageHeader`라고 이미 메서드 시그니처에 정해져 있으니 `new HsmsMessageHeader(...)`
  대신 타입 이름을 생략한 `new(...)`만 써도 컴파일러가 추론한다.
- **`byte byte3 = 0` 기본 인자**: 대부분의 제어 메시지(`Select.req`, `Linktest.req`,
  `Deselect.req` 등)는 `Byte3`도 안 쓰므로 생략 가능하게 해뒀고, `Select.rsp`처럼 진짜
  상태 코드를 실어야 하는 경우만 명시적으로 넘긴다.

### `ToString()` — 디버그 출력

```csharp
public override string ToString()
    => $"[SessionId=0x{SessionId:X4} Byte2=0x{Byte2:X2} Byte3=0x{Byte3:X2} PType={PType} SType={SType} SystemBytes={SystemBytes}]";
```

**의미**: 로그에 헤더를 찍을 때 사람이 읽기 좋은 형태로 보여준다. `HsmsMessage.ToString()`도
이 위에 `Header + Body`를 붙여서 로그를 만든다.

**문법 포인트**
- **`override`**: `object.ToString()`을 재정의. 재정의하지 않으면 기본값인 타입
  전체 이름(`HsmsLite.Protocol.HsmsMessageHeader`)만 찍혀서 디버깅에 쓸모가 없다.
- **문자열 보간 서식 지정자** `{SessionId:X4}`, `{Byte2:X2}`: `:X4`/`:X2`는 "대문자
  16진수로, 각각 최소 4자리/2자리, 부족하면 앞에 0을 채워서" 표시하라는 서식
  문자열(format string). `SessionId`(2바이트라 최대 4자리 hex)와 `Byte2`(1바이트라
  최대 2자리 hex)의 자릿수에 맞춰 다르게 지정했다.

---

## 예시: 데이터 메시지 vs 제어 메시지, 같은 구조체 다른 의미

`S1Messages.BuildS1F1(sessionId: 5, systemBytes: 1001)`을 호출하면 내부적으로
`GemMessage.Build(5, stream:1, function:1, replyExpected:true, 1001)` → 아래 헤더가
만들어집니다.

```
byte3 = function(1) | 0x80 = 0x81   // W-bit(응답 필요) 켜짐
```

`ToBytes()`가 만드는 10바이트(16진수):

```
오프셋   값        필드         해석 (SType=DataMessage 기준)
[0-1]  00 05     SessionId    세션 5
[2]    01        Byte2        Stream = 1  → S1F..
[3]    81        Byte3        0x81 & 0x7F = 1 → Function 1, 최상위비트 1 → W-bit 켜짐(응답 필요)
[4]    00        PType        0 (고정)
[5]    00        SType        0 = DataMessage
[6-9]  00 00 03 E9   SystemBytes   1001 (요청-응답 매칭용 ID)
```

→ 이어붙이면 `00 05 01 81 00 00 00 00 03 E9` = **S1F1 요청**.

이번엔 **같은 구조체**로 완전히 다른 의미를 담는 제어 메시지,
`HsmsMessage.Control(HsmsSType.SelectReq, systemBytes: 1002)` (내부적으로
`HsmsMessageHeader.Control(HsmsSType.SelectReq, 1002)` 호출):

```
오프셋   값        필드         해석 (SType=SelectReq 기준)
[0-1]  FF FF     SessionId    제어 메시지 관례값 0xFFFF (세션 무관)
[2]    00        Byte2        의미 없음(항상 0) — Stream 아님!
[3]    00        Byte3        의미 없음(항상 0) — Function도 W-bit도 아님!
[4]    00        PType        0 (고정)
[5]    01        SType        1 = SelectReq
[6-9]  00 00 03 EA   SystemBytes   1002
```

→ 이어붙이면 `FF FF 00 00 00 01 00 00 03 EA` = **Select.req**.

두 예시 모두 3번째 바이트(`Byte3`)가 결과적으로 `0x00`이 될 수도 있고 `0x81`이 될 수도
있는데, **그 의미는 5번째 바이트(`SType`)를 봐야만 정확히 알 수 있습니다.** `Byte2`/`Byte3`이라는
이름이 "위치"로만 붙어 있는 이유가 바로 이것 — 헤더 구조체 혼자서는 그 바이트의 의미를
확정할 수 없고, `SType`이라는 타입 태그와 함께 봐야 비로소 뜻이 정해지기 때문입니다.
의미 해석(Stream/Function으로 읽을지, Select Status로 읽을지)은 `HsmsMessageHeader`
바깥, 즉 `GemMessage`(데이터 메시지 쪽)나 `HsmsMessage.Control`/`HsmsSelectStatus`
(제어 메시지 쪽)의 몫으로 넘겨둔 것입니다.
