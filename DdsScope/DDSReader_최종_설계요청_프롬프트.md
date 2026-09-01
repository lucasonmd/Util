# DDSReader 시스템 설계 요청 프롬프트

다음 요구사항을 만족하는 사내용 DDS Debug Viewer 프로그램의 전체 시스템 설계를 작성해줘.

단순 예제 수준이 아니라 실제 구현 가능한 수준으로 아키텍처, 클래스 구조, 스레드/비동기 처리, DDS Discovery 및 DynamicData 처리, 메모리 관리, 검색/필터링, DevExpress UI 구성, 성능 최적화, 버전 호환성 전략까지 구체적으로 설계해줘.

특히 고속 DDS 데이터 수신 중 UI 또는 검색/필터 기능 때문에 Sample이 유실되지 않도록 하는 것을 가장 중요한 설계 목표 중 하나로 삼아줘.

---

## 1. 개발 환경 및 최종 수행 환경

### 현재 개발/프로토타입 환경

- Language: C#
- Framework: .NET 8.0
- UI: WPF
- UI Library: DevExpress WPF 26.1
- DDS: RTI Connext DDS 7.7
- 현재는 무료/개발용 라이선스 환경을 사용 중

### 최종 프로젝트 수행 환경

- Language: C#
- Framework: .NET 8.0
- UI: WPF
- UI Library: DevExpress WPF 24.2
- DDS: RTI Connext DDS Professional 7.3.0
- 실제 프로젝트에서 사용하는 정식 라이선스 환경

### 핵심 호환성 요구사항

현재 DevExpress 26.1 + RTI DDS 7.7 환경에서 개발하더라도,
나중에 프로젝트를 옮긴 뒤 다음과 같이 라이브러리를 교체했을 때 최소한의 수정만으로 정상 빌드 및 실행되어야 한다.

- DevExpress 26.1 → DevExpress 24.2
- RTI Connext DDS 7.7 → RTI Connext DDS Professional 7.3.0

따라서 기능/API 선택 기준은 최신 버전이 아니라 최종 수행환경의 공통분모인:

**.NET 8.0 + WPF + DevExpress 24.2 + RTI Connext DDS 7.3.0**

을 Target Baseline으로 삼아줘.

다음 원칙을 반드시 지켜줘.

- DevExpress 26.1 전용 API 사용 금지
- RTI DDS 7.7 전용 API 사용 금지
- RTI DDS 7.7에서만 제공되는 DynamicType / TypeObject / TypeLookup 동작에 과도하게 의존하지 않기
- C# API Signature가 7.3과 7.7 사이에 다르면 7.3 기준 구현
- DevExpress Property/Event/API가 24.2와 26.1 사이에 다르면 24.2 기준 구현
- 버전 조건부 컴파일이 필요하면 Adapter 계층 안으로 한정
- Core/Application 계층은 RTI 및 DevExpress 버전 차이를 몰라야 함

---

## 2. 프로그램 목적

프로그램 이름은 임시로 `DDSReader`라고 한다.

Wireshark와 유사하게 특정 DDS Domain에 존재하는 Topic/Writer를 자동 Discovery하고,
수신되는 DDS Sample을 실시간 Capture하여 분석하는 사내용 Debug Viewer다.

이 프로그램은 Read Only Tool이다.

DDS에 데이터를 Write하지 않는다.

주요 기능:

- DDS Domain 연결
- Topic 자동 Discovery
- Writer 자동 Discovery
- 새로 등장하는 Topic 자동 감지
- 자동 DataReader 생성
- 모든 Topic Sample 수신
- 실시간 Capture
- Payload 분석
- Payload 내부 검색
- Wireshark 스타일 Display Filter
- Topic/Writer별 Selection Filter
- Capture History 조회
- 선택 데이터 CSV Export

---

## 3. 핵심 DDS 요구사항

사전에 특정 Topic의 IDL 또는 C# Generated Type/Class를 알고 있다고 가정하면 안 된다.

예:

- C_Rotational_Mount
- VehicleState
- WeaponState

같은 Topic 이름이나 클래스가 프로그램에 하드코딩되어 있어서는 안 된다.

프로그램 실행 후 DDS Discovery를 통해 발견되는 Topic을 Generic하게 처리해야 한다.

RTI Connext DDS의 다음 개념을 적극적으로 활용하는 방향을 검토해줘.

- Built-in Topic Discovery
- PublicationBuiltinTopicData
- DynamicType
- DynamicData
- TypeObject
- TypeLookup
- Generic DataReader

사전 IDL 없이 Discovery된 Topic의 Type 정보를 얻고,
DynamicData 기반 DataReader를 Runtime에 생성할 수 있는 구조로 설계해줘.

단, 구현 기준은 RTI Connext DDS 7.3.0 호환성을 최우선으로 해야 한다.

Type 정보를 얻지 못하는 Topic이 있더라도 프로그램 전체가 실패해서는 안 된다.

예:

`TopicXYZ - Type unavailable`

상태로 표시하고 나머지 Topic은 정상적으로 계속 동작해야 한다.

---

## 4. RTI DDS 의존성 격리

Application/Core 로직에서 RTI Connext DDS API를 직접 광범위하게 사용하지 않도록 설계해줘.

가능하면 다음과 같은 추상화 계층을 둔다.

```text
Application / UI
        ↓
Capture / Search Core
        ↓
IDdsRuntime
IDdsConnection
IDdsDiscoveryService
IDdsDynamicReader
IDdsSample
IDdsWriterInfo
        ↓
RtiConnextAdapter
        ↓
RTI Connext DDS C# API
```

실제 RTI 타입:

- DomainParticipant
- DataReader
- DynamicData
- DynamicType
- PublicationBuiltinTopicData
- SampleInfo

등은 가능한 한 `RtiConnextAdapter` 또는 Infrastructure 계층 내부에 가둔다.

CaptureStore, SearchEngine, FilterEngine, CSV Export, ViewModel에서는 RTI 전용 타입을 직접 참조하지 않는 방향으로 설계해줘.

목표는 향후 RTI 버전을 교체할 때 수정 범위를 DDS Adapter 계층으로 최대한 한정하는 것이다.

---

## 5. DynamicData Snapshot 분리

RTI `DynamicData` 객체 자체를 CaptureStore에 장기간 저장하지 않는 방향을 우선 검토해줘.

DDS `Take()` 이후 RTI 객체의 Lifetime 또는 Loan 처리 방식과 Capture History의 Lifetime을 분리해야 한다.

예:

```text
RTI DynamicData
      ↓
RTI Adapter
      ↓
Generic Payload Snapshot
      ↓
CaptureRecord
      ↓
CaptureStore
```

Generic Snapshot은 RTI 버전과 독립적인 내부 데이터 모델이어야 한다.

예:

```csharp
public sealed class CaptureRecord
{
    public long Sequence { get; init; }
    public DateTimeOffset ReceiveTime { get; init; }
    public string TopicName { get; init; }
    public WriterIdentity Writer { get; init; }
    public PayloadSnapshot Payload { get; init; }
}
```

Filter/Search/UI가 RTI DynamicData에 직접 의존하지 않도록 해줘.

다만 전체 DynamicData를 무조건 깊은 복사하는 것이 성능상 문제가 될 수 있으므로 다음 대안을 비교해줘.

- Lazy Snapshot
- Compact field storage
- Schema cache
- Primitive field optimized storage
- pooled buffer
- immutable snapshot
- field-offset/index cache

---

## 6. DevExpress 의존성 격리

Capture/Search/Core 계층이 DevExpress에 의존하면 안 된다.

구조는 다음을 목표로 한다.

```text
RTI DDS
  ↓
DDS Adapter
  ↓
Capture Core
  ↓
Search / Filter Core
  ↓
Presentation Adapter
  ↓
DevExpress WPF
```

다음 계층에서는 DevExpress Assembly를 참조하지 않도록 해줘.

- DDS Core
- CaptureStore
- CapturePipeline
- Search Engine
- Display Filter Engine
- CSV Export
- Data Model

DevExpress 의존성은 WPF Presentation 프로젝트로 한정한다.

예:

```text
DDSReader.Core
DDSReader.Dds.Abstractions
DDSReader.Dds.Rti
DDSReader.Search
DDSReader.Export
DDSReader.Wpf
DDSReader.Wpf.DevExpress
```

더 나은 프로젝트 분리 방식이 있다면 제안해줘.

---

## 7. Domain 연결

사용자가 Domain ID를 직접 입력할 수 있어야 한다.

예:

`Domain ID [ 0 ] [ Connect ]`

한 번에 하나의 Domain에 연결하는 구조면 충분하다.

Connect 이후:

1. DomainParticipant 생성
2. 기존 Topic/Writer Discovery
3. Runtime DataReader 자동 생성
4. 이후 새롭게 생성되는 Topic/Writer도 자동 Discovery
5. 발견 즉시 자동 수신 시작

`Connect`와 `Capture` 상태를 개념적으로 분리해줘.

Disconnect 전까지 DDS Discovery 자체는 유지되어야 한다.

---

## 8. 예상 데이터 부하

현재 예상되는 부하는 대략 다음과 같다.

- 약 20개의 Topic
- 각 Topic 수신 주기 약 5ms
- Topic당 약 200 Samples/sec
- 전체 약 4,000 Samples/sec

Payload는 대부분 비교적 작은 Struct다.

예:

- SourceID
- ReferenceID
- Info
- Status
- 기타 int / uint / enum 계열 필드

한 Topic에 대략 10~20개의 Primitive 계열 Field가 포함된다.

향후 다음 구조도 고려해야 한다.

- Nested Struct
- Array
- Sequence
- Union

---

## 9. 핵심 성능 요구사항

DDS 수신부는 UI와 완전히 분리되어야 한다.

다음과 같은 구조는 금지한다.

```text
DDS callback
→ DynamicData 전체 변환
→ ObservableCollection 추가
→ Grid Refresh
→ CSV 기록
```

DDS Receive Thread 또는 Listener에서 다음 작업을 수행하지 않도록 한다.

- WPF UI Update
- DevExpress Grid Update
- CSV 쓰기
- 무거운 DynamicData Tree 생성
- 전체 Payload 문자열 변환
- 검색 인덱스 전체 생성
- 복잡한 Filter Evaluation

DDS Sample은 가능한 한 빠르게 `Take()`한 뒤 프로그램 내부 Capture Pipeline으로 넘겨야 한다.

기본 방향:

```text
DDS Network
→ Discovery Manager
→ Dynamic Reader Manager
→ Fast Sample Take
→ Internal Channel / Queue
→ Capture Processor
→ Capture Store
→ Search / Filter
→ UI
```

다음 기술을 비교하고 적절한 방식을 추천해줘.

- DDS Listener
- WaitSet
- StatusCondition
- System.Threading.Channels
- ConcurrentQueue
- bounded/unbounded Channel
- dedicated consumer thread
- batch Take
- batch processing
- batch UI update
- ArrayPool / ObjectPool
- 최소 Lock 구조

특히 DDS Sample 유실 방지를 우선해야 한다.

---

## 10. DDS Sample 저장 정책

DDS Instance Key가 동일하더라도 절대 기존 데이터를 덮어쓰면 안 된다.

예:

```text
Sample 1: SourceID = 3
Sample 2: SourceID = 3
Sample 3: SourceID = 3
```

이면 3개의 Capture Record가 모두 별도로 저장되어야 한다.

Key는 최신 상태를 관리하기 위한 Dictionary Key가 아니다.

Key는 검색/필터/표시용 Metadata다.

각 Capture Record에는 프로그램 내부에서 고유한 Capture Sequence 또는 Capture ID를 부여한다.

예:

- CaptureSequence
- ReceiveTime
- SourceTimestamp
- Topic
- Writer
- InstanceKey
- Payload
- SampleInfo

메모리 보존 순서는 Capture Sequence / Receive Order 기준이다.

---

## 11. Capture Store 메모리 정책

모든 데이터를 영구적으로 메모리에 보관할 필요는 없다.

최대한 Sample 유실 없이 Capture하되 프로그램 메모리 한도를 넘으면 오래된 데이터부터 제거해도 된다.

Sample 개수 기준이 아니라 메모리 사용량 기준으로 관리해줘.

기본값 예:

`Max Capture Memory = 1 GB`

설정 가능 예:

- 256 MB
- 512 MB
- 1 GB
- 2 GB
- 4 GB
- Custom

메모리 한도를 넘으면:

`Oldest Sample → Evict`

방식으로 제거한다.

동일 DDS Key라고 해서 최신 데이터로 덮어쓰면 안 된다.

CaptureRecord가 실제로 차지하는 메모리를 효율적으로 추적/추정하는 구조도 제안해줘.

---

## 12. 데이터 유실 상태 구분

다음 상황을 반드시 서로 구분해서 관리해줘.

1. DDS 자체에서 Sample Lost 발생
2. DDS 수신은 되었지만 프로그램 내부 Queue Overflow 등으로 Sample Drop
3. 정상적으로 Capture한 뒤 Memory Limit 때문에 오래된 Sample Eviction
4. Capture Pause 때문에 의도적으로 저장하지 않은 Sample

이 네 가지는 의미가 다르다.

별도 Counter/Status로 관리해줘.

---

## 13. Pause 기능

Pause 기능은 두 종류를 제공한다.

### View Pause

DDS 수신:
계속

Capture Store 저장:
계속

UI Grid 갱신:
일시 정지

Resume하면 최신 상태를 다시 표시한다.

### Capture Pause

DDS Domain 연결:
유지

Discovery:
유지

Sample 수신:
가능하면 계속

Capture Store 신규 저장:
중단

Resume하면 이후 Sample부터 다시 Capture한다.

Capture Pause 상태에서 저장되지 않은 Sample 수는 별도로 카운트해줘.

---

## 14. 전체 UI 구조

대략 다음과 같은 3-Pane 구조를 사용한다.

상단:

- Domain ID
- Connect / Disconnect
- View Pause
- Capture Pause
- Clear
- Save
- Quick Search
- Display Filter

좌측:

`Topic / Writer Tree`

중앙:

`Capture Grid`

우측:

`Sample Detail / Writer Detail`

예시:

```text
┌────────────────────────────────────────────────────────────────────┐
│ Domain [0] Connect | View Pause | Capture Pause | Save            │
│ Search [                ] Filter [                              ] │
├──────────────────┬───────────────────────────────┬─────────────────┤
│ TOPICS           │ CAPTURE                       │ SAMPLE DETAIL   │
│                  │                               │                 │
│ ▼ Topic A        │ Time   Topic Writer Key ...   │ ▼ Payload       │
│   ├ Writer 1     │ ...                           │   SourceID = 3  │
│   └ Writer 2     │ ...                           │   Info = 10     │
│                  │                               │                 │
│ ▼ Topic B        │                               │ ▼ NestedData    │
│   └ Writer 1     │                               │   ...           │
└──────────────────┴───────────────────────────────┴─────────────────┘
```

DevExpress GridControl과 TreeListControl 등을 활용하는 방향으로 설계해줘.

---

## 15. Topic / Writer Tree

좌측 Tree는 Topic을 Root로 하고 Writer를 Child로 표시한다.

예:

```text
▼ C_Rotational_Mount
   ├ Writer A
   ├ Writer B
   └ Writer C

▼ C_Platform_State
   └ Writer A
```

Topic 클릭:
해당 Topic Capture만 표시

Writer 클릭:
해당 Topic + Writer Capture만 표시

Topic 또는 Writer 선택에 의한 Filter는 Display Filter와 별도로 동작하는 `Selection Filter`로 구현해줘.

최종 표시 조건:

```text
Selection Filter
AND
Display Filter
AND
Quick Search
```

사용자가 직접 작성한 Display Filter 문자열을 Topic 선택 때문에 수정하면 안 된다.

---

## 16. Writer 정보

Writer 정보를 적극적으로 분석할 수 있어야 한다.

Discovery로 얻을 수 있는 범위에서 다음 정보를 보여줘.

예:

- Topic Name
- Type Name
- Writer Key
- Participant Key
- GUID
- Partition
- Reliability
- Durability
- Ownership
- Deadline
- DataRepresentation
- UserData
- TopicData
- GroupData
- 기타 유용한 Writer QoS

RTI 7.3.0에서 Publication Built-in Topic으로 실제 얻을 수 있는 항목을 구분해서 설명해줘.

Writer가 망에서 사라진 경우 즉시 Tree에서 삭제하기보다는:

`Writer A [Offline]`

상태로 표시하는 방향을 우선 검토해줘.

해당 Writer의 Capture History는 여전히 메모리에 남아 있을 수 있기 때문이다.

같은 Writer가 다시 Discovery되면 Online 상태로 전환할 수 있는지도 검토해줘.

---

## 17. Capture Grid

전체 Topic을 보는 경우 기본 컬럼은 다음 정도로 한다.

- Receive Time
- Topic
- Writer
- Key
- Payload Summary

불필요한 컬럼은 최소화한다.

Payload Size 등의 정보는 필수가 아니다.

Topic 하나를 선택한 경우에는 DynamicType을 분석하여 해당 Payload Field를 Grid Column으로 자동 생성하는 기능을 제공한다.

예:

Topic 구조:

- SourceID
- ReferenceID
- Info
- Azimuth
- Elevation
- Status

이면 Grid를:

```text
ReceiveTime
Writer
SourceID
ReferenceID
Info
Azimuth
Elevation
Status
```

처럼 표시할 수 있어야 한다.

DevExpress 기본 기능을 최대한 활용한다.

예:

- Column Sort
- Column Filter
- Column Search
- Column Reorder
- Column Hide
- Grouping

Writer 선택 시에도 같은 Field Column 구조를 유지하고 해당 Writer 데이터만 표시한다.

DynamicData를 매 Sample마다 `Dictionary<string, object>`로 전부 변환하는 것이 성능상 적절한지도 검토하고 더 좋은 구조가 있으면 제안해줘.

---

## 18. 최신 Sample이 위에 표시되는 Grid

Capture Grid는 최신 Sample이 가장 위에 표시된다.

하지만 신규 Sample이 들어올 때마다 사용자의 Scroll 위치가 맨 위로 튀면 안 된다.

이 요구사항은 매우 중요하다.

### 사용자가 Grid 최상단을 보고 있는 상태

Live Follow = ON

새로운 Sample이 들어오면 최신 Sample을 위에 추가하고 계속 최신 데이터를 보여준다.

### 사용자가 아래로 Scroll한 상태

Live Follow = OFF

백그라운드에서는 계속 Sample을 Capture한다.

하지만 현재 사용자가 보고 있는 Row 및 Scroll 위치는 유지되어야 한다.

신규 Sample이 위에 들어와도 현재 화면이 이동하면 안 된다.

예:

`↑ 327 new samples`

같은 표시만 증가시킨다.

사용자가 이 표시를 클릭하면 최신 Sample 위치로 이동하고 Live Follow를 다시 ON으로 한다.

특정 Row를 클릭하고 Sample Detail을 보고 있는 동안에도 신규 데이터 때문에 해당 Row Selection 및 Viewport가 움직이면 안 된다.

DevExpress WPF GridControl에서 이를 안정적으로 구현하기 위한 구체적인 방법을 제안해줘.

단순히 ObservableCollection의 Index 0에 Insert하고 Grid 전체 Refresh하는 방식은 피한다.

CaptureSequence 같은 고유 Record ID를 이용한 Anchor 방식 등을 검토해줘.

---

## 19. Sample Detail

우측에는 선택된 Sample의 Payload를 Tree 형태로 표시한다.

예:

```text
Payload
├ Header
│  ├ SourceID = 3
│  └ ReferenceID = 12
├ Position
│  ├ X = 10
│  └ Y = 20
└ Status = Enabled
```

DynamicData의 다음 구조를 모두 고려해줘.

- Primitive
- Enum
- Struct
- Nested Struct
- Array
- Sequence
- Union

Array, Sequence 등 큰 데이터는 전부 즉시 TreeNode로 만들지 않는다.

Lazy Loading을 사용한다.

예:

`Data : byte[3000000]`

초기에는 길이만 표시하고 사용자가 Expand할 때 필요한 범위만 생성한다.

Raw Hex View는 필요 없다.

---

## 20. Quick Search

Quick Search와 Display Filter는 둘 다 제공한다.

Quick Search는 사용자가 문법을 몰라도 사용할 수 있는 단순 검색 기능이다.

예:

```text
Search: SourceID
Search: 15
Search: Mount
```

검색 대상 후보:

- Topic Name
- Writer
- Key
- Payload Field Name
- Payload Value

성능 문제를 고려해 어떤 항목까지 실시간 검색하는 것이 적절한지 설계해줘.

---

## 21. Wireshark 스타일 Display Filter

별도의 Display Filter Engine을 구현하고 싶다.

예:

```text
topic == "C_Rotational_Mount"
writer == "Writer01"
data.SourceID == 3
data.Info >= 10
data.ReferenceID == 5 && data.Info > 20
topic contains "Mount"
data.SourceID == 3 && (data.Info == 1 || data.Info == 2)
```

지원 연산자 후보:

- ==
- !=
- >
- >=
- <
- <=
- &&
- ||
- !
- ()
- contains
- startsWith
- endsWith

필드 접근 문법:

```text
data.SourceID
data.Position.X
```

Filter Engine 설계 시 다음을 제안해줘.

- Tokenizer
- Parser
- AST
- Expression Compiler
- Runtime Evaluation
- DynamicData / PayloadSnapshot Field Access
- Type Conversion
- Enum 비교
- Null / Missing Field 처리
- 잘못된 Filter 입력 오류 표시
- Filter 자동완성 가능성
- 필드명 자동완성
- Topic마다 다른 DynamicType을 가진 경우 처리 방법

매 Sample마다 문자열 기반 Reflection을 반복하지 않도록 다음 최적화를 적극 검토해줘.

- Field Accessor Cache
- Schema Cache
- Compiled Predicate
- field-path resolution cache

---

## 22. Payload 값에서 자동 Filter 생성

Sample Detail Tree에서 Payload Field를 우클릭했을 때 다음 기능을 제공한다.

```text
Filter == this value
Filter != this value
```

예:

`SourceID = 3`

우클릭:

`Filter: data.SourceID == 3`

기존 Display Filter가 있을 경우 AND 조건으로 자연스럽게 추가할 수 있는 UX도 제안해줘.

---

## 23. Filter와 Capture의 관계

Display Filter는 DDS 수신 자체를 제한하는 용도로 사용하지 않는다.

ContentFilteredTopic을 주 Display Filter 기능으로 사용하지 않는다.

예를 들어:

`data.SourceID == 3`

Filter를 적용하더라도 SourceID가 1, 2, 4인 Sample 역시 Capture Store에는 계속 저장되어야 한다.

Filter는:

`Capture된 데이터 중 무엇을 화면에 보여줄 것인가`

를 결정한다.

기본 흐름:

```text
DDS Capture
→ 전체 데이터 저장
→ Selection Filter
→ Display Filter
→ Quick Search
→ Grid
```

---

## 24. CSV Save / Export

실시간 CSV Logging은 하지 않는다.

사용자가 원할 때만 CSV를 생성한다.

`Save All Capture` 기능은 필요 없다.

저장 가능한 범위는 다음과 같다.

1. 선택한 Topic
2. 선택한 Writer
3. 현재 Filter 결과

예:

Topic:
`C_Rotational_Mount`

Writer:
`Writer B`

를 선택하고 Save하면:

`C_Rotational_Mount_WriterB_20260901_103500.csv`

같은 파일을 생성한다.

CSV에는 가능한 한 Payload의 Primitive Field를 Column으로 Flatten한다.

예:

- CaptureSequence
- ReceiveTime
- SourceTimestamp
- Writer
- SourceID
- ReferenceID
- Info
- Status

Nested Struct의 CSV Column Naming 방식도 제안해줘.

예:

- Position.X
- Position.Y

Sequence/Array를 CSV로 Export할 경우 어떤 형태가 좋은지도 제안해줘.

Save는 현재 Capture Store에 존재하는 데이터를 Export하는 작업이다.

Save 시 DDS 수신 Thread에 영향을 주면 안 된다.

대량 CSV Export 중에도 DDS Capture가 계속 정상 동작하도록 별도 Background Task/Thread 구조를 설계해줘.

---

## 25. 상태 정보

하단 상태바에는 불필요한 통계를 너무 많이 보여주지 않는다.

Bandwidth 등은 기본 화면에서는 필요하지 않다.

예:

```text
Topics 20
Writers 23
Rx 4,012/s
Memory 620 MB / 1 GB
DDS Lost 0
Queue Drop 0
Evicted 12000
```

정도면 충분하다.

꼭 필요한 정보와 Debug 화면에서만 보여줄 정보를 구분해서 추천해줘.

---

## 26. DevExpress 적극 활용

DevExpress WPF의 기능을 적극 활용해줘.

단, 반드시 DevExpress 24.2에서 지원되는 API를 기준으로 한다.

특히 다음을 검토해줘.

- GridControl
- TableView
- TreeListControl
- Search Panel
- Column Filtering
- Virtualization
- Instant Feedback
- Server Mode 적용 가능 여부
- CollectionView
- Async Data Loading
- Custom Data Source
- Unbound Column
- Dynamic Column Generation
- Row Virtualization
- Scroll Position 유지
- Selection 유지
- 대량 데이터 Refresh 최적화

DevExpress 기능을 사용하기 위해 전체 Capture를 ObservableCollection에 그대로 넣는 식의 비효율적 구조는 피한다.

Capture Store와 Grid Presentation Layer 사이에 어떤 Adapter/ViewModel/Data Provider 계층을 두는 것이 좋은지 설계해줘.

---

## 27. MVVM 및 클래스 구조

MVVM을 기본으로 하되, 성능 때문에 모든 것을 순수 MVVM 명령/ObservableCollection에 억지로 넣지 않아도 된다.

실제 구현 가능한 클래스 구조를 제안해줘.

예시 개념:

### DDS 계층

- DdsConnectionManager
- DdsDiscoveryService
- DdsTopicRegistry
- DdsWriterRegistry
- DynamicReaderFactory
- DynamicDataReceiver

### Capture 계층

- CapturePipeline
- CaptureChannel
- CaptureProcessor
- CaptureRecord
- CaptureStore
- CaptureMemoryManager
- CaptureStatistics

### Search 계층

- QuickSearchService
- DisplayFilterLexer
- DisplayFilterParser
- FilterAst
- FilterCompiler
- DynamicFieldAccessorCache
- CaptureQueryEngine

### UI 계층

- MainViewModel
- TopicTreeViewModel
- CaptureGridViewModel
- SampleDetailViewModel
- WriterDetailViewModel

### Export

- CsvExportService

각 클래스의 책임과 주요 인터페이스를 설명해줘.

---

## 28. Thread / Task 구조

프로그램 내부 Thread/Task 모델을 매우 구체적으로 작성해줘.

예:

```text
RTI 내부 Receive Thread / Listener
    ↓
최소 작업
    ↓
Channel Producer

Capture Consumer Task
    ↓
CaptureStore

UI Projection Task / Dispatcher Batch
    ↓
DevExpress Grid

CSV Export Task
```

다음 항목을 포함해줘.

- 실제로 별도 Thread가 필요한지 Task가 적절한지
- RTI 내부 Thread와 사용자 Thread의 관계
- DDS callback에서 무엇을 해야 하는가
- 무엇을 절대 하면 안 되는가
- Channel의 Producer/Consumer 구조
- Batch size
- UI update frequency
- CancellationToken
- Shutdown
- Dispose
- Domain Disconnect

---

## 29. Shutdown 안정성

프로그램 종료 또는 Disconnect 시 다음이 안전하게 정리되어야 한다.

- 신규 Discovery 중단
- DataReader 수신 중단
- Capture Channel 종료
- Background Consumer 종료
- UI Update 종료
- CSV Export Task 처리
- DDS Entity Dispose
- DomainParticipant Dispose

종료 중 다음 문제가 발생하지 않도록 순서를 제안해줘.

- ObjectDisposedException
- callback race condition
- cancellation race
- UI Dispatcher race

---

## 30. QoS

이 프로그램의 목적은 DDS Publisher에게 영향을 주지 않으면서 가능한 한 많은 Topic의 Sample을 읽는 것이다.

Discovered Writer의 QoS 정보를 분석하여 호환 가능한 Reader QoS를 결정하는 방법을 제안해줘.

특히 다음 항목을 고려해줘.

- Reliability
- Durability
- Partition
- History
- ResourceLimits
- DataRepresentation
- Ownership
- Deadline

모든 Writer를 하나의 고정 QoS로 읽으려고 할 때 발생할 수 있는 문제도 설명해줘.

필요하다면 Topic/Writer별 Reader QoS를 동적으로 구성하는 구조를 제안해줘.

DDSReader 때문에 기존 DDS 시스템의 Publisher가 Block되거나 정상 운용에 영향을 받지 않도록 주의해야 한다.

---

## 31. 성능 최적화

전체 약 4,000 Samples/sec를 기본 목표로 하되 더 높은 부하에도 버틸 수 있도록 설계해줘.

다음 부분을 적극적으로 분석해줘.

- DynamicData Copy 비용
- Loaned Samples 활용 가능 여부
- Take 후 객체 Lifetime
- Payload Snapshot 방법
- Memory Allocation
- GC Pressure
- LOH
- Object Pooling
- ArrayPool
- String Allocation
- Payload Summary 생성 비용
- Grid Refresh 비용
- Filter Evaluation 비용
- Lock Contention
- Reader별 Thread 구성 필요성
- Batch Processing
- UI Update Throttling

중요:

UI는 4,000개의 Sample을 초당 4,000번 Refresh할 필요가 없다.

Capture는 모든 Sample을 처리하되 UI는 예를 들어 50~200ms 단위로 Batch Refresh하는 식의 구조를 검토해줘.

---

## 32. 실패 격리

특정 Topic 또는 DynamicType 파싱 실패 때문에 전체 DDSReader가 멈추면 안 된다.

다음 단위로 실패를 격리해줘.

- Domain
- Topic
- Writer
- Sample
- Filter
- CSV Export

예:

```text
특정 Topic Type 해석 실패
→ 해당 Topic만 Warning

특정 Sample Decode 실패
→ 해당 Sample만 Error 표시

잘못된 Display Filter
→ Capture는 계속
→ 기존 정상 Filter 또는 Filter Off 상태 유지

CSV Export 실패
→ DDS Capture 영향 없음
```

---

## 33. 버전 호환성 전략

최종 설계 결과에 별도 `Version Compatibility Strategy` 장을 만들어 다음을 명확하게 설명해줘.

1. RTI DDS 7.7 개발환경에서 7.3.0 호환 코드를 작성하는 방법
2. DevExpress 26.1 개발환경에서 24.2 호환 코드를 작성하는 방법
3. 사용하면 안 되는 버전 종속 기능을 어떻게 식별할지
4. RTI API를 Adapter 뒤로 숨기는 방법
5. DevExpress API를 Presentation Layer로 제한하는 방법
6. DynamicData를 RTI 독립적인 Capture 모델로 변환하는 방법
7. 프로젝트/Assembly 구성
8. Debug 개발환경과 실제 Release 환경 분리 방법
9. DLL 및 Native Runtime 교체 시 수정해야 하는 예상 영역
10. 실제 최종 환경인 `DevExpress 24.2 + RTI DDS 7.3.0`에서 반드시 수행해야 할 호환성 테스트 목록

---

## 34. 프로젝트 이동 및 Runtime 배포

현재 개발 프로젝트를 최종 프로젝트로 이동할 때 이상적인 작업은 다음 정도여야 한다.

```text
1. DevExpress Reference
   26.1 → 24.2

2. RTI DDS Reference
   7.7 → 7.3.0

3. RTI Native DLL / Runtime 경로 변경

4. 필요 시 Adapter 계층의 소수 코드 수정

5. Rebuild
```

CaptureStore, SearchEngine, FilterEngine, ViewModel, CSV Export 등의 핵심 기능을 다시 작성하면 안 된다.

RTI Connext DDS는 Managed C# Assembly뿐 아니라 Native Runtime Library 의존성도 있을 수 있으므로 다음 항목을 설계해줘.

- Managed Assembly Reference
- Native DLL
- PATH
- x64 Architecture
- Runtime Deployment
- Project Reference
- NuGet 사용 여부
- 직접 Assembly Reference 사용 여부
- 환경별 Build Configuration

예:

```text
Debug-Prototype
    DevExpress 26.1
    RTI 7.7

Release-Target
    DevExpress 24.2
    RTI 7.3.0
```

같은 Configuration 분리가 필요한지도 검토해줘.

단, 서로 다른 API를 사용하는 두 개의 별도 프로그램을 만드는 방향은 원하지 않는다.

가능하면 하나의 Source Code를 유지한다.

---

## 35. DevExpress 24.2 기준 UI 호환성

현재 개발 PC에서는 DevExpress 26.1을 사용하지만 실제 프로젝트에서는 DevExpress 24.2를 사용한다.

따라서 다음 기능 사용 시 24.2 지원 여부를 반드시 확인해줘.

- GridControl
- TableView
- TreeListControl
- Search Panel
- Dynamic Column
- Row Virtualization
- Data Virtualization
- Instant Feedback
- Server Mode
- Scroll Position 제어
- Selection 유지
- Custom Data Source
- Filtering

DevExpress 26.1에서 동작하는 코드를 작성하더라도 24.2에서 동일한 Property/Event/API를 사용할 수 있는지를 우선 고려해줘.

---

## 36. 버전 의존 코드 최소화

다음과 같은 코드가 Application 전체에 퍼지지 않도록 한다.

```csharp
#if RTI_77
...
#else
...
#endif
```

또는:

```csharp
#if DEVEXPRESS_261
...
#endif
```

버전 조건부 컴파일이 필요하더라도 Adapter 계층 내부에 한정한다.

예:

```text
RtiConnextAdapter
 ├─ Common
 └─ Version Compatibility

DevExpressPresentationAdapter
 └─ Version Compatibility
```

Application/Core 코드는 버전 차이를 알 필요가 없어야 한다.

---

## 37. 최종 산출물 요청

최종 답변은 단순 설명이 아니라 실제 개발 설계 문서 수준으로 작성해줘.

다음 순서로 작성해줘.

1. 전체 아키텍처 개요
2. 핵심 설계 원칙
3. DDS Discovery 및 DynamicData 처리 방식
4. DDSReader 자동 생성 전략
5. RTI DDS Adapter 및 버전 격리 전략
6. QoS 처리 전략
7. 데이터 수신 Pipeline
8. Thread / Task / Channel 구성
9. CaptureRecord 데이터 모델
10. PayloadSnapshot / Schema 구조
11. Memory Bounded Capture Store 설계
12. Sample Eviction 알고리즘
13. Search Architecture
14. Wireshark 스타일 Filter Engine 설계
15. Topic/Writer Selection Filter 설계
16. DevExpress Capture Grid 설계
17. Scroll Anchor / Live Follow 구현 방법
18. Sample Detail Lazy Loading
19. Writer Detail 화면
20. CSV Export 구조
21. View Pause / Capture Pause 동작
22. Error Handling 및 Failure Isolation
23. Shutdown / Dispose 순서
24. 클래스 다이어그램 수준의 클래스 구조
25. 주요 Interface 및 Method Signature 예시
26. 프로젝트/Assembly 분리 구조
27. Thread-safe 구현 시 주의점
28. 예상 병목 구간
29. 성능 최적화 포인트
30. RTI Connext DDS 7.3.0 호환성 관련 주의사항
31. DevExpress 24.2 호환성 관련 주의사항
32. Version Compatibility Strategy
33. 구현 우선순위
34. 1차 MVP 범위
35. 실제 최종 환경에서 수행할 호환성 테스트 계획
36. 향후 확장 기능

핵심 Pipeline과 클래스 관계는 ASCII Diagram 또는 Mermaid Diagram으로 시각화해줘.

단순한 이상적인 설계가 아니라 실제:

**C# / .NET 8 + WPF + DevExpress 24.2 + RTI Connext DDS Professional 7.3.0**

환경에서 구현 가능한 방향을 우선해줘.

현재 개발환경인:

**DevExpress 26.1 + RTI DDS 7.7**

은 Target이 아니라 단지 개발/프로토타입 환경으로 취급한다.

최우선 목표는 다음 두 가지다.

**1. 빠른 DDS Topic을 동시에 수신하더라도 DDS Sample 유실을 최대한 방지할 것**

**2. Wireshark처럼 사용자가 실시간 데이터를 검색하고 필터링하고 과거 Capture를 분석할 수 있으면서도 UI가 DDS 수신 성능에 영향을 주지 않을 것**
