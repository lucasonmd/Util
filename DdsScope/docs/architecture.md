# DdsScope architecture

Design notes for the decisions that are not obvious from the code.

Target baseline: **.NET 8 + WPF + DevExpress WPF 24.2 + RTI Connext DDS Professional 7.3.0**.
The prototype machine runs DevExpress 26.1 and Connext 7.7; nothing newer than the baseline is
used outside the two adapter layers.

---

## 1. Layering

```
                 DdsScope.App          (WPF + DevExpress — the only UI-library consumer)
                      |
   +------------------+------------------+
   |                  |                  |
DdsScope.Filtering  DdsScope.Export   DdsScope.Dds.Rti   (the only Connext consumer)
   |                  |                  |
   +------------------+------------------+
                      |
              DdsScope.Core            (payload model, capture store, pipeline)
```

`DdsScope.Dds.Abstractions` sits between the adapter and the app: it defines `IDdsRuntime`,
`IDdsConnection` and the discovery model in vendor-neutral terms.

The spec suggested splitting presentation into `.Wpf` and `.Wpf.DevExpress`. That split was not
taken: DevExpress *is* the WPF layer here, and a second assembly would only add indirection
without isolating anything. The isolation that matters — Core, capture, filtering and export
knowing nothing about DevExpress — is enforced by the project references above.

---

## 2. Thread and task model

```
RTI receive threads
        │  (never touched by application code)
        ▼
ddsscope-discovery thread ── polls the publication built-in reader (250 ms)
        │                     creates/replaces dynamic readers, tracks writers
        ▼
ddsscope-receive thread ──── WaitSet over all dynamic readers' status conditions
        │                     Take() → decode → ICaptureSink.Submit (non-blocking)
        ▼
Channel<CaptureRecord>  (bounded, DropWrite, counted)
        │
        ▼
capture consumer task ────── batched append into CaptureStore
        │
        ▼
UI DispatcherTimer (100 ms) ─ drains discovery events, projects new rows, updates status bar
        │
        ▼
DevExpress GridControl

CSV export ───────────────── its own Task over an immutable store snapshot
```

**Discovery polls; data reception waits.** A WaitSet on the *built-in* publication reader's
StatusCondition does not work: attaching it produces no wake-ups and no samples, while plain
polling of the same reader returns the announcements immediately — RTI drives the built-in
readers through its own internal machinery. Discovery events arrive a handful at a time, so a
250 ms poll costs nothing and behaves identically across Connext versions. The *dynamic*
readers, which do carry load, use a WaitSet as described below.

**Listeners were rejected in favour of WaitSets.** A listener callback runs on a middleware
thread; anything slow there stalls reception for every reader in the participant. A WaitSet
delivers the same wake-up on a thread we own, whose work we can bound
(`MaxSamplesPerTake` per reader per wake-up, so one noisy topic cannot starve the others).

**The WaitSet is only ever touched by its own thread.** Discovery runs on a different thread,
so attaching and detaching reader conditions is posted as work items and the receive thread is
woken through a `GuardCondition`.

**What the receive thread does:** take a batch, convert each sample into a `PayloadSnapshot`,
hand it to the sink. **What it never does:** update the UI, write CSV, evaluate filters, build
tree nodes, or allocate a dictionary per sample.

The channel is bounded with `DropWrite`, and a failed `TryWrite` is counted as a queue drop.
The producer is a DDS thread, so it must never block — back-pressure onto the middleware is
worse than a counted drop, and the count makes the loss visible instead of silent.

---

## 3. Payload representation

RTI `DynamicData` is loaned memory valid only during the take. Capture history has to outlive
it, so each sample is copied — but copying is the hot path, so its shape matters.

A `PayloadSchema` is built **once per discovered type**: the type tree is flattened into leaf
fields with dotted paths (`Position.X`), each assigned a slot. Per sample, a `PayloadSnapshot`
holds only:

- `long[]` for every numeric, bool, char and enum field (doubles are bit-cast, not boxed)
- `string[]` for string fields
- `CollectionValue[]` for arrays and sequences
- a presence bitmap, so union branches and optional members are distinguishable from zero

At the design load of 4,000 samples/s with 10–20 fields each, a `Dictionary<string, object>`
per sample would allocate tens of megabytes per second and dominate GC time. The flat-slot
form allocates four small arrays and boxes nothing for primitives.

Decoding walks a precomputed plan (`ReadNode` tree) rather than re-interpreting the
`DynamicType`, so no field is ever resolved by name lookup through the type system.

**Large collections are recorded by length only.** The element count comes from
`DynamicData.GetMemberInfo`, and elements are copied only while the count stays under
`MaxCollectionElements` (default 64). That is what stops a `sequence<octet, 3000000>` from
being materialised on the receive thread.

Strings that would be expensive and are rarely read — the instance key, the payload summary —
are computed lazily by `CaptureRecord`, when a grid cell or CSV row actually asks.

---

## 4. Capture store

Append-only, chunked (4096 records per chunk), bounded by **memory** rather than sample count.
Each record carries an estimated footprint; when the budget is exceeded the oldest chunk is
dropped whole, keeping eviction O(1) instead of walking records one at a time. The chunk being
filled is never evicted.

**Records are never merged by instance key.** Three samples of the same key are three records
in receive order. The key is search and display metadata, not a dictionary key — this is a
capture log, not a state cache.

Readers get a `CaptureSnapshotView`: an immutable array of chunk references taken under a short
lock. The UI and CSV export enumerate it freely while the capture thread keeps appending.

### Four kinds of loss, counted separately

| Counter | Meaning |
| --- | --- |
| `DDS Lost` | the middleware reported SAMPLE_LOST before we could take it |
| `Queue Drop` | received, but the internal channel was full |
| `Evicted` | captured, then dropped to stay inside the memory budget |
| `Paused` | received while capture was paused, deliberately not stored |

They mean very different things when debugging, so they are never summed into one number.

---

## 5. UI projection

The grid is refreshed on a 100 ms timer, in batches — never per sample.

**Steady state is incremental.** The projection walks the store newest-first and stops at the
last sequence it already projected, so its cost is proportional to new samples, not to the size
of the store. A full re-scan happens only when the filter, search or selection changes, and
runs on a background task with a generation counter so a stale result cannot overwrite a newer
one.

**Scroll and selection stability** is a guarantee, not a heuristic. When Live Follow is off,
nothing is added to the bound collection at all — new matches are counted for the
`↑ N new samples` banner and left in the store. A viewport cannot move if its collection does
not change. Live Follow turns off on any deliberate grid interaction (wheel, click, navigation
keys) and back on via the banner or the toolbar toggle. This deliberately avoids depending on
DevExpress scroll-position APIs, which keeps it identical on 24.2.

**Dynamic payload columns** bind to an indexer on the row view model (`[3]`), so a cell is
formatted only when the grid realises it, and sorting, filtering, reordering and hiding keep
working normally.

---

## 6. Reader QoS

The goal is to read as much as possible while being invisible to the system under observation.

No single fixed QoS can match every writer: a RELIABLE reader will not match a BEST_EFFORT
writer, and OWNERSHIP kind must match exactly. So the reader QoS is *derived* per topic from
the writers discovery reports, and the reader is rebuilt if a later writer changes the answer
(logged as an Info diagnostic).

| Policy | Choice | Why |
| --- | --- | --- |
| Reliability | RELIABLE unless any writer is BEST_EFFORT | lossless where possible, compatible always |
| Ownership | derived; SHARED when writers disagree | must match exactly; disagreement is logged |
| Durability | VOLATILE | never ask a publisher to resend history for a debug tool |
| History | KEEP_ALL with a deep resource limit | buffer bursts until the receive thread drains |
| Partition | subscriber uses `*` | see writers in any partition |

Content-filtered topics are deliberately **not** used for the display filter: filtering happens
after capture, so a filter change never loses data that was already on the wire.

---

## 7. Failure isolation

| Failure | Blast radius |
| --- | --- |
| a topic's type cannot be resolved | that topic shows `Type unavailable`; others unaffected |
| one sample fails to decode | that record is stored flagged, with the error text |
| a reader throws while draining | logged against that topic; other readers keep running |
| a malformed discovery announcement | logged; the rest of the announcements still process |
| an invalid display filter | reported inline; the previous filter stays; capture untouched |
| CSV export fails | reported in the status bar; capture never noticed |
| an unhandled UI exception | caught in `App`, shown, session continues |

---

## 8. Shutdown order

Outside-in, because the reverse order is what produces `ObjectDisposedException` races:

1. cancel the token and trigger the guard condition
2. join the discovery thread, then the receive thread (bounded waits)
3. dispose readers, then the WaitSets and guard condition
4. dispose the subscriber, then the participant
5. complete the capture channel and await the consumer task

---

## 9. Version compatibility strategy

### What is confined where

- **RTI** appears only in `DdsScope.Dds.Rti`. Everything above it sees `IDdsConnection`,
  `PayloadSnapshot` and `CaptureRecord`. Moving to Connext 7.3.0 is a package-version change
  plus, at worst, edits inside that one project.
- **DevExpress** appears only in `DdsScope.App`, and only through `GridControl` + `TableView`
  with explicit `GridColumn`s, and `TreeListControl` + `TreeListView` with `ChildNodesPath` —
  all present and unchanged in 24.2.
- There is no conditional compilation anywhere. If a version difference ever forces it, it
  belongs inside an adapter project, never in Core or the view models.

### Connext API surface actually used

`DomainParticipantFactory`, `DomainParticipant` (+ `BuiltinSubscriber`, `CreateTopic`,
`GetDynamicType`), `Subscriber.LookupDataReader<PublicationBuiltinTopicData>`,
`PublicationBuiltinTopicData` (including `DynamicType`), `DataReader<DynamicData>.Take`,
`WaitSet` + `StatusCondition` + `GuardCondition`, `DynamicType`/`DynamicData`, and the
`DataReaderQos.With*` builders. All of this exists in 7.3.0.

### Moving to the target environment

1. change `RtiConnextVersion` and `DevExpressVersion` in `Directory.Build.props`
2. point at the 7.3.0 native runtime (x64; `PlatformTarget` is already pinned)
3. rebuild, then run the viewer against `simulator/` on a spare domain

### What to re-test there

- discovery of every topic, and that types resolve (see the type-propagation note in the README)
- reader QoS adaptation against a BEST_EFFORT writer and an EXCLUSIVE-ownership writer
- decoding of nested structs, enums, arrays, sequences and unions
- sustained load: sample rate, queue drops, memory growth and eviction
- the grid under load: scroll and selection stability with Live Follow off
- CSV export of a large selection while capture continues
- clean shutdown and repeated connect/disconnect cycles

---

## 10. Known gaps

- **Union decoding** is implemented by enumerating the members actually present in a sample, but
  has not been exercised against a real union type — the simulator does not emit one.
- **Filter autocompletion** is not implemented; the filter grammar and field names are
  documented in the README instead.
- **Sorting a dynamic payload column** sorts by the formatted text, not the underlying numeric
  value.
- The capture store evicts at chunk granularity (4096 records), so the memory ceiling is
  respected within roughly one chunk's worth of samples.
