# 메시지 빌더 코드 정독: `GemMessage` / `S1Messages` / `GemMessageException`

이 문서는 `HsmsLite.Gem/GemMessage.cs`, `HsmsLite.Gem/S1Messages.cs`,
`HsmsLite.Gem/GemMessageException.cs` 세 파일을 메서드 단위로 뜯어서 "이게 무슨 의미이고,
어떻게 동작하고, 어떤 C# 문법을 썼는지"를 정리한 것입니다. 설계 이유는
[gem-layer.md](gem-layer.md)에 있으니 여기서는 코드 자체에 집중합니다.

---

## 1. `GemMessageException.cs`

```csharp
public sealed class GemMessageException : Exception
{
    public GemMessageException(string message) : base(message) { }
}
```

### 의미
GEM 메시지를 다루다가 생기는 "이 코드베이스만의" 오류를 표준 `Exception`과 구분하기 위한
전용 예외 타입. 두 가지 상황에서 던져진다 (`GemMessage.AssertStreamFunction`에서 사용):
- 받은 메시지의 Stream/Function이 기대한 것과 다를 때
- (앞으로) SECS-II 아이템 구조가 그 메시지가 가져야 할 형태와 다를 때

### 동작
생성자에 문자열 메시지 하나만 받아서 base 클래스인 `Exception`의 생성자로 그대로
전달한다. 그 외의 동작은 전혀 없다 — 커스텀 필드도, 오버라이드도 없는 "이름표만 다른
예외"다. `catch (GemMessageException ex)`처럼 다른 예외(예: 네트워크 오류)와 구분해서
잡을 수 있게 해주는 게 존재 이유의 전부다.

### 문법 포인트
- **`sealed class`**: 이 클래스를 다시 상속할 수 없게 막는다. "GEM 메시지 검증 실패"라는
  단일 의미만 표현하면 충분하고, 하위 클래스로 세분화할 계획이 없다는 의도를 코드로
  드러낸 것.
- **`: Exception`**: `System.Exception`을 상속. .NET에서 커스텀 예외를 만드는 표준
  방식이다.
- **`: base(message)`**: 생성자 뒤에 콜론으로 베이스 클래스 생성자를 호출하는 문법.
  `Exception(string message)` 오버로드를 호출해서 `ex.Message` 프로퍼티가 채워지게 한다.
- **`public GemMessageException(string message) { }`**: 본문이 빈 중괄호인 생성자.
  실제 초기화 작업은 전부 `base(message)`가 하기 때문에 본문에서 할 일이 없다.

---

## 2. `GemMessage.cs` — 공통 배관(plumbing)

```csharp
internal static class GemMessage
```

### 클래스 자체
- **`internal`**: `HsmsLite.Gem` 어셈블리(프로젝트) 밖에서는 이 클래스가 아예 보이지
  않는다. `S1Messages`/`S2Messages`/`S6Messages`(모두 `public`)를 통해서만 간접적으로
  쓰이는, 순수 내부 구현 디테일이라는 뜻. 외부 사용자가 실수로 `GemMessage.Build(...)`를
  직접 호출해서 W-bit 계산을 잘못 쓰는 일을 원천 차단한다.
- **`static class`**: 인스턴스를 만들 수 없고(`new GemMessage()` 불가), 모든 멤버가
  정적 메서드다. 상태를 전혀 갖지 않는 순수 함수 모음이라는 뜻 — 스트림 5쌍이 공유하는
  "헤더 만들기"와 "검증하기" 로직만 담는다.

### 2-1. `Build`

```csharp
public static HsmsMessage Build(ushort sessionId, byte stream, byte function, bool replyExpected,
    uint systemBytes, Secs2Item? body = null)
{
    var byte3 = replyExpected ? (byte)(function | 0x80) : function;
    var header = new HsmsMessageHeader(sessionId, stream, byte3, HsmsSType.DataMessage, systemBytes);
    return new HsmsMessage(header, body?.Encode() ?? Array.Empty<byte>());
}
```

**의미**: SECS-II 메시지 하나(`S1F1`, `S1F14` 등)를 HSMS 프레임(`HsmsMessage`)으로
포장하는 공통 조립 라인. `S1Messages`의 모든 `BuildS*` 메서드가 결국 이 메서드 하나로
수렴한다.

**동작 흐름**
1. `replyExpected`가 `true`면 `function`의 최상위 비트(0x80)를 세운다. 이게 SEMI
   E37에서 정의하는 **W-bit** — "이 메시지에 응답(reply)이 필요하다"는 신호다.
   예를 들어 function이 `1`(S1F1)이면 `0x01 | 0x80 = 0x81`이 된다.
2. 그렇게 만든 `byte3`(Function+W-bit)로 `HsmsMessageHeader`를 만든다. `stream`은
   헤더의 `Byte2`, `byte3`는 `Byte3`, `SType`은 항상 `DataMessage`(제어 메시지가 아닌
   실제 SECS-II 데이터라는 뜻)로 고정.
3. `body`(SECS-II 아이템, 예: `Secs2List`)가 있으면 `Encode()`로 바이트 배열로
   직렬화하고, 없으면(`null`, 예: S1F1처럼 바디가 없는 메시지) 빈 배열을 쓴다.
4. 완성된 헤더 + 바디를 `HsmsMessage`로 감싸서 반환.

**문법 포인트**
- **`Secs2Item? body = null`**: nullable reference type(`?`)과 기본 인자(default
  parameter)의 조합. `S1F1`처럼 바디가 없는 메시지는 `Build(...)` 호출 시 `body`를
  아예 생략할 수 있다(`S1Messages.BuildS1F1` 참고 — 마지막 인자를 안 넘김).
- **삼항 연산자** `replyExpected ? (byte)(function | 0x80) : function`: if/else 없이
  한 줄로 값을 고르는 표현식. 결과가 `byte` 타입이어야 하므로 `|` 연산 결과(`int`로
  승격됨)를 `(byte)`로 명시적 캐스팅했다.
- **비트 OR `|`**: `function`과 `0x80`을 비트 단위로 합쳐서 최상위 비트만 켠다.
  `function`의 다른 비트는 SEMI 스펙상 0~127 범위(0x7F 이하)라 건드리지 않는다.
- **`?.` (null 조건부 연산자)** + **`??` (null 병합 연산자)**: `body?.Encode()`는
  `body`가 `null`이면 평가를 멈추고 `null`을 반환하고, `null`이 아니면 `Encode()`를
  호출한다. 그 결과가 `null`이면(즉 `body`가 애초에 `null`이었으면) `??`가
  `Array.Empty<byte>()`(길이 0인 공유 배열, 매번 새로 할당하지 않는 최적화된 빈 배열)로
  대체한다. `if (body == null) ... else ...`를 한 줄로 압축한 것.
- **`var`**: 지역 변수 `byte3`의 타입을 컴파일러가 우변(`(byte)(...)`)에서 추론하게
  둔다.

### 2-2. `AssertStreamFunction`

```csharp
public static void AssertStreamFunction(HsmsMessage msg, byte expectedStream, byte expectedFunction)
{
    var actualFunction = (byte)(msg.Header.Byte3 & 0x7F);
    if (msg.Header.Byte2 != expectedStream || actualFunction != expectedFunction)
        throw new GemMessageException(
            $"Expected S{expectedStream}F{expectedFunction} but received S{msg.Header.Byte2}F{actualFunction}.");
}
```

**의미**: 상대방에게서 받은 메시지가 "내가 파싱하려는 그 메시지"가 맞는지 확인하는
가드(guard). 예를 들어 `S1Messages.ParseS1F2`는 이 메서드로 먼저 "이거 진짜 S1F2
맞아?"를 검증한 뒤에야 바디를 SECS-II 타입으로 캐스팅한다.

**동작 흐름**
1. `msg.Header.Byte3`(Function + W-bit)에서 `& 0x7F`로 W-bit(최상위 비트)를 지우고
   순수 function 값만 뽑아낸다. `0x7F` = `0111 1111`이므로 최상위 비트를 0으로 마스킹.
   응답 메시지든 요청 메시지든, W-bit가 세워져 있든 아니든 function 값만 비교하기
   위함.
2. `Byte2`(Stream)와 방금 뽑은 `actualFunction`(Function) 둘 중 하나라도 기대값과
   다르면 `GemMessageException`을 던진다.
3. 둘 다 맞으면 아무 것도 하지 않고 정상 반환(`void`) — "검증만 하고 통과시키는"
   전형적인 assert 함수 패턴.

**문법 포인트**
- **비트 AND `&`**: OR가 비트를 "켜는" 연산이라면 AND는 특정 비트만 "남기고 나머지는
  지우는" 마스킹 연산. `Build`의 OR와 정확히 대칭되는 동작.
- **`||` (논리 OR)**: 두 조건 중 하나라도 참이면 전체가 참. 단락 평가(short-circuit)라
  왼쪽이 참이면 오른쪽은 평가하지 않는다(여기선 부수효과가 없어 상관없지만).
- **문자열 보간(string interpolation)** `$"Expected S{expectedStream}F{expectedFunction} ..."`:
  `$` 접두사가 붙은 문자열 안에서 `{}`로 감싼 표현식이 바로 값으로 치환된다.
  `string.Format`을 안 쓰고도 가독성 있게 메시지를 조립.
- **`throw`가 문(statement) 자리**: `if` 조건이 참일 때만 실행되는 단일 문으로 던진다
  (중괄호 없는 한 줄 `if`).

### 2-3. `ParseBody`

```csharp
public static Secs2Item? ParseBody(HsmsMessage msg) => msg.Body.Length == 0 ? null : Secs2Item.Decode(msg.Body);
```

**의미**: `HsmsMessage.Body`(raw byte 배열)를 SECS-II 아이템 트리(`Secs2Item`)로
디코딩하는 진입점. `S1Messages`의 모든 `Parse*` 메서드가 여기서 시작한다.

**동작**: 바디 길이가 0이면(S1F1처럼 빈 바디 메시지) `null`을 반환하고, 그렇지 않으면
`Secs2Item.Decode(msg.Body)`를 호출해서 포맷 바이트부터 재귀적으로 파싱한 결과를
반환한다.

**문법 포인트**
- **식 본문 멤버(expression-bodied member)**: `{ return ...; }` 대신 `=> 식;`으로
  메서드 전체를 한 줄 식으로 정의. 이 파일의 세 메서드 중 로직이 한 줄로 끝나는
  `ParseBody`에만 이 스타일을 썼다(`Build`/`AssertStreamFunction`은 여러 단계라 블록
  본문 사용).
- **반환 타입 `Secs2Item?`**: "바디가 없을 수도 있다"는 걸 nullable reference type으로
  타입 시스템에 드러낸다. 호출부(`S1Messages`)에서는 `null`이 아님을 알고 있는 지점에서
  `!`(null-forgiving)로 다시 non-null로 취급한다 (아래 3절 참고).

---

## 3. `S1Messages.cs` — Stream 1 메시지 빌더

```csharp
public static class S1Messages
```

`public static class`: `GemMessage`와 마찬가지로 정적 클래스지만 이번엔 `public` —
`HsmsLite.Gem`을 참조하는 다른 프로젝트(Host/Equipment 시뮬레이터)에서 직접
`S1Messages.BuildS1F1(...)`처럼 호출하는 게 목적이기 때문이다.

메서드는 항상 `Build`(내가 보낼 메시지 만들기)와 `Parse`(상대가 보낸 메시지 읽기)가
쌍으로 존재하고 세 그룹(F1/F2, F13/F14, F3/F4)으로 나뉜다. SEMI E30 스펙상 이 셋은
모두 "요청 → 응답" 한 쌍이라, 요청 쪽은 `replyExpected: true`(W-bit 켜짐), 응답 쪽은
`replyExpected: false`로 짝지어져 있다.

### 3-1. `BuildS1F1` — Are You There

```csharp
public static HsmsMessage BuildS1F1(ushort sessionId, uint systemBytes)
    => GemMessage.Build(sessionId, 1, 1, replyExpected: true, systemBytes);
```

**의미**: "거기 있니?" 요청. SEMI E30에서 바디가 없는(empty body) 가장 단순한 메시지.
Host나 Equipment가 상대가 살아있는지 확인할 때 쓴다.

**동작**: `GemMessage.Build`에 stream=1, function=1, `replyExpected: true`만 넘기고
`body`는 생략 — `Build`의 기본값 `null`이 적용되어 빈 바디로 인코딩된다.

**문법 포인트**
- **이름 붙은 인자(named argument)** `replyExpected: true`: 인자 목록에서 위치가
  아니라 이름으로 값을 지정. `bool` 하나만 덜렁 `true`라고 쓰면 호출부만 봐서는 무슨
  의미인지 알기 어려운데(`Build(sessionId, 1, 1, true, systemBytes)`), 이름을 붙이면
  "아, 응답을 기대한다는 뜻이구나"가 바로 읽힌다. 이 파일 전체에서 `replyExpected`는
  항상 이 방식으로 호출된다.
- **인자 생략**: `Build`의 `body` 매개변수가 `= null` 기본값을 가지므로 마지막 인자를
  아예 안 쓸 수 있다.

### 3-2. `BuildS1F2` / `ParseS1F2` — On Line Data

```csharp
public static HsmsMessage BuildS1F2(ushort sessionId, uint systemBytes, string mdln, string softRev)
    => GemMessage.Build(sessionId, 1, 2, replyExpected: false, systemBytes,
        new Secs2List(new Secs2Ascii(mdln), new Secs2Ascii(softRev)));

public static (string Mdln, string SoftRev) ParseS1F2(HsmsMessage msg)
{
    GemMessage.AssertStreamFunction(msg, 1, 2);
    var list = (Secs2List)GemMessage.ParseBody(msg)!;
    return (((Secs2Ascii)list.Items[0]).Value, ((Secs2Ascii)list.Items[1]).Value);
}
```

**의미**: S1F1에 대한 응답. "나 온라인이고, 모델명은 X, 소프트웨어 버전은 Y"라고
알려주는 메시지. SECS-II 구조는 `L,2 { MDLN(A), SOFTREV(A) }` — 길이 2짜리 리스트
안에 ASCII 문자열 두 개.

**`BuildS1F2` 동작**: `Secs2List` 하나를 만드는데, 그 안에 `Secs2Ascii(mdln)`과
`Secs2Ascii(softRev)`를 순서대로 담는다. `replyExpected: false`인 이유는 이 메시지
자체가 응답이라 또 응답을 기다릴 필요가 없기 때문(SEMI E37 규칙상 응답 메시지는
W-bit를 세우지 않음).

**`ParseS1F2` 동작**
1. `AssertStreamFunction`으로 진짜 S1F2인지 먼저 확인(아니면 예외).
2. `ParseBody`로 얻은 `Secs2Item?`을 `(Secs2List)`로 캐스팅. S1F2 바디는 항상
   최상위가 리스트라는 걸 스펙으로 알고 있으므로, 다른 타입이면 `InvalidCastException`이
   자연스럽게 터진다(이 코드베이스는 그 이상의 방어적 검증은 하지 않음 — "필요한 만큼만"
   원칙).
3. `list.Items[0]`, `list.Items[1]`을 각각 `(Secs2Ascii)`로 캐스팅해서 `.Value`(실제
   문자열)를 꺼내고, 튜플로 묶어서 반환.

**문법 포인트**
- **named tuple 반환 타입** `(string Mdln, string SoftRev)`: C# 7부터 지원하는 튜플에
  필드 이름을 붙이는 문법. 호출부에서 `result.Mdln`, `result.SoftRev`처럼 의미 있는
  이름으로 접근 가능(그냥 `.Item1`/`.Item2`보다 훨씬 읽기 좋음). 반환문의
  `return (a, b);`는 이 이름 붙은 튜플 타입에 맞춰 값을 채워 넣는 튜플 리터럴.
- **null-forgiving 연산자 `!`**: `GemMessage.ParseBody(msg)`의 반환 타입은
  `Secs2Item?`이지만, S1F2는 스펙상 바디가 절대 비어있지 않다는 걸 코드 작성자가 알고
  있으므로 `!`를 붙여 "나는 이게 null이 아님을 안다"고 컴파일러에게 선언하고 nullable
  경고를 끈다. 그 직후 바로 `(Secs2List)`로 캐스팅하기 위해 non-null이 필요하다.
- **이중 캐스팅** `((Secs2Ascii)list.Items[0]).Value`: `list.Items[0]`의 정적 타입은
  `Secs2Item`(추상 베이스 클래스)이므로, `.Value`(각 하위 타입 고유 프로퍼티)에 접근하려면
  먼저 `(Secs2Ascii)`로 다운캐스트해야 한다. 바깥쪽 괄호는 `.Value`보다 캐스트가 먼저
  적용되게 하는 연산자 우선순위 조정용.
- **object/collection initializer 문법의 `new Secs2List(new Secs2Ascii(mdln), ...)`**:
  `Secs2List`의 `params Secs2Item[] items` 생성자(아래 4절에서 다시 설명)를 가변 인자로
  호출.

### 3-3. `BuildS1F13` — Establish Communications Request

```csharp
public static HsmsMessage BuildS1F13(ushort sessionId, uint systemBytes)
    => GemMessage.Build(sessionId, 1, 13, replyExpected: true, systemBytes, new Secs2List());
```

**의미**: "통신을 시작하자"는 요청(Host가 보냄). SEMI 스펙상 바디는 빈 리스트
`L,0`이다 — S1F1처럼 완전히 비어있는 게 아니라, "리스트인데 원소가 0개"라는 점이 다르다
(와이어 포맷상 포맷 바이트+길이 바이트는 있고 길이 값이 0).

**문법 포인트**: `new Secs2List()`는 `params Secs2Item[] items` 생성자를 인자 없이
호출한 것 — 결과적으로 길이 0인 배열이 `Items`가 된다.

### 3-4. `BuildS1F14` / `ParseS1F14` — Establish Communications Acknowledge

```csharp
public static HsmsMessage BuildS1F14(ushort sessionId, uint systemBytes, bool commAccepted, string mdln, string softRev)
    => GemMessage.Build(sessionId, 1, 14, replyExpected: false, systemBytes,
        new Secs2List(new Secs2Boolean(commAccepted), new Secs2List(new Secs2Ascii(mdln), new Secs2Ascii(softRev))));

public static (bool CommAccepted, string Mdln, string SoftRev) ParseS1F14(HsmsMessage msg)
{
    GemMessage.AssertStreamFunction(msg, 1, 14);
    var list = (Secs2List)GemMessage.ParseBody(msg)!;
    var commAccepted = ((Secs2Boolean)list.Items[0]).Value;
    var idList = (Secs2List)list.Items[1];
    return (commAccepted, ((Secs2Ascii)idList.Items[0]).Value, ((Secs2Ascii)idList.Items[1]).Value);
}
```

**의미**: S1F13에 대한 응답(Equipment가 보냄). "통신 수락 여부 + 내 모델명/버전"을
같이 실어 보낸다. SECS-II 구조는 `L,2 { COMMACK(Boolean), L,2 { MDLN(A), SOFTREV(A) } }`
— 리스트 안에 Boolean 하나와, 그 안에 또 리스트(중첩 리스트)가 들어있는 구조.

**`BuildS1F14` 동작**: `Secs2List`를 중첩해서 만든다 — 바깥 리스트의 두 번째 원소가
`BuildS1F2`에서 만든 것과 똑같은 모양의 `Secs2List(mdln, softRev)`. 실제로 SEMI 스펙이
"모델/버전 정보"를 두 메시지에서 재사용하는 구조라, 코드도 자연스럽게 같은 모양이 된다.

**`ParseS1F14` 동작**: `list.Items[0]`을 Boolean으로, `list.Items[1]`을 다시
`Secs2List`(`idList`)로 캐스팅한 뒤 그 안에서 두 ASCII 값을 꺼낸다 — 중첩 구조를
한 겹씩 벗겨내는 전형적인 트리 파싱.

**문법 포인트**
- **중간 변수 `idList`**: `((Secs2List)list.Items[1])`를 두 번 캐스팅하지 않으려고
  `var idList = (Secs2List)list.Items[1];`로 한 번만 캐스팅해서 재사용. `BuildS1F2`의
  이중 인라인 캐스팅과 달리 여기서는 가독성을 위해 이름을 붙인 것 — 중첩이 한 단계 더
  깊어지면 인라인이 너무 읽기 어려워지기 때문.
- **3-필드 named tuple** `(bool CommAccepted, string Mdln, string SoftRev)`: 필드 수만
  다를 뿐 `ParseS1F2`와 같은 패턴.

### 3-5. `BuildS1F3` / `ParseS1F3` — Selected Equipment Status Request

```csharp
public static HsmsMessage BuildS1F3(ushort sessionId, uint systemBytes, IReadOnlyList<uint> svids)
    => GemMessage.Build(sessionId, 1, 3, replyExpected: true, systemBytes,
        new Secs2List(svids.Select(id => (Secs2Item)new Secs2U4(id))));

public static IReadOnlyList<uint> ParseS1F3(HsmsMessage msg)
{
    GemMessage.AssertStreamFunction(msg, 1, 3);
    var list = (Secs2List)GemMessage.ParseBody(msg)!;
    return list.Items.Cast<Secs2U4>().Select(item => item.Value).ToArray();
}
```

**의미**: Host가 "이 SVID(상태 변수 ID)들의 값을 알려줘"라고 요청. 개수가 가변적인
`L,n { SVID(U4), ... }` 구조 — 앞서 본 F2/F14는 원소 개수가 고정이었지만 이건 요청자가
넘기는 리스트 길이만큼 달라진다.

**`BuildS1F3` 동작**: `IReadOnlyList<uint> svids`를 LINQ `Select`로 각각
`Secs2U4`(SECS-II U4 타입)로 감싸고, `Secs2List`의 `IEnumerable<Secs2Item>` 생성자
오버로드(4절 참고)로 넘긴다.

**`ParseS1F3` 동작**: 반대로 리스트의 각 원소를 `Secs2U4`로 캐스팅해서 `.Value`만 뽑아
`uint[]`로 만든다.

**문법 포인트**
- **`IReadOnlyList<uint>` 파라미터 타입**: 호출자가 배열이든 `List<uint>`든 뭘 넘기든
  받을 수 있게 인터페이스로 받는다("읽기 전용"이라는 계약도 명시 — 이 메서드가 리스트를
  수정하지 않는다는 뜻).
- **LINQ `Select` + 람다** `svids.Select(id => (Secs2Item)new Secs2U4(id))`: 각
  `uint id`를 `Secs2U4`로 감싼 뒤 `(Secs2Item)`으로 업캐스트. 업캐스트가 명시적으로
  필요한 이유는 `Secs2List(IEnumerable<Secs2Item> items)` 오버로드를 타입 추론이
  확실히 고르게 하려는 것 — `Select`의 결과 시퀀스 타입을 `IEnumerable<Secs2Item>`으로
  맞춰야 원하는 생성자 오버로드가 선택된다(안 그러면 `IEnumerable<Secs2U4>`가 되어
  `params Secs2Item[]` 쪽과 헷갈릴 수 있음).
- **`Cast<Secs2U4>()`**: LINQ의 비제네릭 타입 변환 메서드. `list.Items`의 정적 타입은
  `IReadOnlyList<Secs2Item>`이므로, 각 원소를 실제 런타임 타입인 `Secs2U4`로 캐스팅한
  시퀀스로 바꿔준다(하나라도 `Secs2U4`가 아니면 열거 시점에 `InvalidCastException`).
- **메서드 체이닝** `.Cast<Secs2U4>().Select(item => item.Value).ToArray()`: LINQ의
  지연 실행(lazy evaluation) 연산자들을 점(`.`)으로 연결. `Cast` → `Select` → `ToArray`
  순서로 "타입 바꾸기 → 값 꺼내기 → 배열로 확정"이 한 줄에 표현된다. `ToArray()`가
  호출되는 순간에야 실제로 열거가 실행된다.

### 3-6. `BuildS1F4` / `ParseS1F4` — Selected Equipment Status Data

```csharp
public static HsmsMessage BuildS1F4(ushort sessionId, uint systemBytes, IReadOnlyList<uint> values)
    => GemMessage.Build(sessionId, 1, 4, replyExpected: false, systemBytes,
        new Secs2List(values.Select(v => (Secs2Item)new Secs2U4(v))));

public static IReadOnlyList<uint> ParseS1F4(HsmsMessage msg)
{
    GemMessage.AssertStreamFunction(msg, 1, 4);
    var list = (Secs2List)GemMessage.ParseBody(msg)!;
    return list.Items.Cast<Secs2U4>().Select(item => item.Value).ToArray();
}
```

**의미**: S1F3에 대한 응답. "요청받은 순서 그대로" 값들을 돌려준다(SVID와 값의 매칭은
위치로만 이뤄지고, 이 메시지 자체에는 SVID가 다시 실리지 않는다 — SEMI E30 스펙 그대로).
`replyExpected: false`인 이유는 F2/F14와 동일하게 이게 응답 메시지이기 때문.

**동작/문법**: `BuildS1F3`/`ParseS1F3`와 구조적으로 완전히 동일하다 — 파라미터 이름이
`svids`에서 `values`로 바뀐 것 외엔 같은 패턴(요청과 응답이 둘 다 "U4 리스트"라서).
이 대칭성 자체가 "메시지 5쌍마다 파일을 쪼갰다면 이 중복 패턴이 파일 경계에 가려 안
보였을 것"이라는, 스트림 단위 분할을 택한 근거를 코드로 보여주는 예다.

---

## 4. 참고: 호출되는 `Secs2*` 타입들의 관련 문법 (요약)

`S1Messages`를 이해하려면 `Secs2Item` 계열의 생성자/캐스팅 규칙을 알아야 하므로 짧게
짚는다 (전체 설계는 `Secs2Item.cs` 자체가 문서화하고 있음).

- **`Secs2Item`은 `abstract class`**: 직접 인스턴스화 불가, `Encode()`만
  추상 메서드로 강제하고 각 하위 타입(`Secs2List`/`Secs2Ascii`/`Secs2Boolean`/
  `Secs2U4`)이 구현. `S1Messages`가 항상 구체 타입(`Secs2List` 등)으로 만들고
  베이스 타입(`Secs2Item`)으로 주고받는 것도 이 다형성(polymorphism) 덕분.
- **`Secs2List`의 두 생성자**: `Secs2List(params Secs2Item[] items)`(개수 고정일 때,
  `new Secs2List(a, b)`처럼 직접 나열)와 `Secs2List(IEnumerable<Secs2Item> items)`
  (개수 가변일 때, LINQ 결과를 그대로 전달). `S1F2`/`S1F14`는 전자, `S1F3`/`S1F4`는
  후자를 쓴다 — 원소 개수가 스펙상 고정인지 가변인지에 정확히 대응.
- **`private protected const byte FormatXxx`**: `Secs2Item`에 정의된 포맷 코드
  상수들. `private protected`는 "같은 어셈블리 안에서, 그리고 상속 관계에 있는
  클래스에서만" 접근 가능하다는 뜻 — `GemMessage`나 `S1Messages`에서는 못 쓰고
  오직 `Secs2Item`을 상속한 하위 타입들의 `Encode()` 내부에서만 쓰인다.

---

## 5. 전체 데이터 흐름 한 번에 보기

`S1Messages.BuildS1F2("MODEL-X", "1.0")` 호출부터 상대방이 `ParseS1F2`로 값을 꺼내기까지:

```
BuildS1F2
  └─ new Secs2List(new Secs2Ascii("MODEL-X"), new Secs2Ascii("1.0"))   // SECS-II 트리 생성
  └─ GemMessage.Build(..., body: 위 리스트)
       ├─ byte3 = 2 (W-bit 없음, replyExpected=false)
       ├─ new HsmsMessageHeader(sessionId, stream=1, byte3=2, DataMessage, systemBytes)
       └─ body.Encode() → SECS-II 와이어 포맷 바이트 배열
            (포맷바이트 L,2 → 포맷바이트 A,len "MODEL-X" → 포맷바이트 A,len "1.0")
  └─ new HsmsMessage(header, bytes)   // 최종 HSMS 프레임

... 네트워크로 전송 ...

ParseS1F2(수신한 HsmsMessage)
  ├─ AssertStreamFunction: Byte2==1 && (Byte3 & 0x7F)==2 확인
  ├─ ParseBody → Secs2Item.Decode(bytes) → 재귀적으로 Secs2List(Secs2Ascii, Secs2Ascii) 복원
  └─ 캐스팅해서 ("MODEL-X", "1.0") 튜플로 반환
```

`GemMessage`는 이 흐름의 1단계(헤더 조립)와 3단계(검증)만 담당하고, 실제 SECS-II 트리
구성/파싱은 각 `S*Messages` 메서드와 `Secs2Item` 하위 타입들이 나눠서 맡는 구조다.
