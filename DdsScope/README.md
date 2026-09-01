# DdsScope

A read-only, Wireshark-style debug viewer for RTI Connext DDS.

It joins a domain, discovers every topic and writer, creates DynamicData readers on the fly,
captures samples into a memory-bounded store, and lets you search, filter and export them —
without publishing anything and without perturbing the system it observes.

Built against the target baseline **.NET 8 + WPF + DevExpress WPF 24.2 + RTI Connext DDS 7.3.0**,
developed on DevExpress 26.1 / Connext 7.7. See [docs/architecture.md](docs/architecture.md) for
the version-compatibility strategy.

---

## Build and run

```
dotnet build DdsScope.sln
dotnet run --project src/DdsScope.App
```

The app also takes `--domain <id>` and `--connect` to start already capturing:

```
src\DdsScope.App\bin\Debug\net8.0-windows\DdsScope.exe --domain 0 --connect
```

`NDDSHOME` must be set so Connext can find `rti_license.dat`.

---

## Layout

| Project | Contents | May reference |
| --- | --- | --- |
| `src/DdsScope.Core` | Payload model, capture record, capture store, pipeline, statistics | nothing |
| `src/DdsScope.Dds.Abstractions` | `IDdsRuntime` / `IDdsConnection` and the discovery model | Core |
| `src/DdsScope.Dds.Rti` | **The only project that references RTI Connext** | Abstractions |
| `src/DdsScope.Filtering` | Display-filter lexer/parser/evaluator, quick search | Core |
| `src/DdsScope.Export` | CSV export | Core |
| `src/DdsScope.App` | **The only project that references DevExpress** | all of the above |

The test publisher lives outside this solution entirely, in `simulator/` — see below.

---

## Using it

**Connect** joins the domain and starts discovery. Connection and capture are separate:

- **View Pause** freezes the grid only. Reception and capture keep running.
- **Capture Pause** stops storing samples. The domain stays joined, discovery keeps running,
  and skipped samples are counted separately.
- **Live Follow** off means new rows are counted but not added, so the viewport and the
  selected row cannot move. The `↑ N new samples` button jumps back to the newest sample.

**Selecting a topic or writer** in the left tree filters the grid. It is a separate mechanism
from the display filter, so clicking around never rewrites the expression you typed. When a
single topic is selected, the grid replaces the generic Key/Payload columns with one column
per payload field.

The final display condition is `selection AND display filter AND quick search`. None of them
affect what is captured — everything the tool receives is stored regardless.

### Display filter

```
topic == "C_Rotational_Mount"
writer contains "Writer01"
data.SourceID == 3
data.Info >= 10 && (data.SourceID == 1 || data.SourceID == 3)
data.Position.X > 0
data.Status == Tracking
!(topic contains "Mount")
data.Samples                     // bare field: true when the sample carries it
```

Fields: `topic`, `writer`, `writerid`, `type`, `key`, `seq`, and `data.<path>` with dotted
paths into nested structs. Operators: `== != > >= < <= && || ! ( )` plus
`contains`, `startsWith`, `endsWith`. Enum fields compare against their label or their
number. A field the record does not carry makes the comparison false rather than an error, so
one topic's filter cannot break the all-topics view.

An invalid filter is reported next to the box and the previous good filter stays in effect —
capture is never interrupted by a half-typed expression.

Right-clicking a value in the **Sample Detail** tree offers `Filter == this value` /
`Filter != this value`, which are ANDed onto the existing expression.

### Quick search

Matches topic, writer, type, payload field names, and string/enum values. Numeric values are
matched only when the search term itself is a number — formatting every numeric field of every
record on each keystroke would cost more than it is worth.

### CSV export

`Save CSV` exports what the current selection and filters show. Payload fields become columns
(`Position.X` keeps its dotted path); arrays and sequences go into a single cell as
`[a; b; c]` with the true length. Export runs on a background task and never touches the
receive path.

---

## Things worth knowing

**The publisher must propagate type information.** Connext 7.x defaults
`resource_limits.type_code_max_serialized_length` to `0`, which means discovery carries the
topic and type *names* but no type description. DdsScope then lists the topic as
`Type unavailable` and cannot decode its samples — the same limitation applies to RTI's own
Admin Console. For a system to be introspectable, its publishers need:

```xml
<participant_qos>
  <resource_limits>
    <type_code_max_serialized_length>8192</type_code_max_serialized_length>
  </resource_limits>
</participant_qos>
```

DdsScope sets the same policy on its own participant so it can store what it receives.
8192 is the maximum Connext accepts for this policy.

**Reader QoS is derived from the writers found.** A single fixed QoS cannot match everything:
a RELIABLE reader is incompatible with a BEST_EFFORT writer, and OWNERSHIP must match exactly.
DdsScope therefore picks the reader QoS per topic from what discovery reports and rebuilds the
reader when a new writer changes the answer. Readers are always VOLATILE, so no publisher is
ever asked to resend history because a debug tool joined.

**Windows Firewall.** DDS discovery needs inbound UDP. The first run raises the usual prompt;
until it is allowed, the tool joins the domain but discovers nothing.

**Hard-killing DDS processes can poison the host** on Windows. After repeatedly force-killing
participants, .NET Connext processes on this machine stopped discovering each other on *any*
domain — including a twenty-line probe with default QoS and no DdsScope code in it — while
RTI's own C++ `rtiddsspy` on the same domain still saw everything. Publishing kept working;
only reception broke. Changing domain, discovery peers (unicast, multicast, shared memory) and
QoS made no difference, and the firewall allowed UDP inbound for every binary involved.

If discovery goes quiet across the board, reboot. To confirm the machine rather than the tool,
run RTI's own spy against the same publisher:

```
rtiddsspy -domainId <id>
```

To rule the tool out, point `rtiddsspy` and DdsScope at the same publisher: if the spy sees
traffic and DdsScope does not, the difference is worth investigating; if neither sees it, the
machine is the problem.

---

## Simulator

`simulator/` is a separate, self-contained solution: a DDS publisher for trying DdsScope when
the real system is not available. It shares no code with the viewer and has its own
`Directory.Build.props`, so the folder can be copied elsewhere and built on its own.

```
dotnet build simulator/DdsSimulator.sln
simulator\DdsSimulatorin\Debug
et8.0\DdsSimulator.exe --domain 0 --rate 200
```

It publishes three topics that resemble the target traffic:

| Topic | Shape | Reliability |
| --- | --- | --- |
| `C_Rotational_Mount` | keyed struct, ints, doubles, an enum | RELIABLE |
| `C_Platform_State` | keyed struct, string, nested `Position` struct | BEST_EFFORT |
| `C_Track_Report` | keyed struct, `sequence<long, 16>` | RELIABLE |

The mixed reliability is deliberate: it exercises the viewer's per-topic reader QoS derivation.

| Option | Meaning |
| --- | --- |
| `--domain N` | domain id (default 0) |
| `--rate N` | samples per second per topic (default 200) |
| `--seconds N` | stop after N seconds (default: run until Ctrl+C) |
| `--no-typecode` | publish without advertising the type, to exercise the viewer's `Type unavailable` path |

Then start the viewer on the same domain:

```
src\DdsScope.Appin\Debug
et8.0-windows\DdsScope.exe --domain 0 --connect
```
