# Convergence: Vision + Helm + Clarifier

**The stack becomes the nervous system.**

Vision, Helm, and the Clarifier are three separate apps that
become something qualitatively different when integrated. Vision
is the circulation — ideas and status flowing through the
hierarchy. Helm is the senses — real-time chat and voice at
scale. The Clarifier is the brain — AI that understands what's
being said and routes it to where it needs to go.

Together they form a command stack where information percolates
upward automatically, decisions flow downward clearly, and the
AI does the routing so humans can focus on judgment.

---

## The Integration

### Layer 1: Helm (Raw Signal)

Workers are on the ground. They're chatting, talking, solving
problems. Helm clusters their chat into currents and manages
their voice hierarchy. This is the raw signal — hundreds of
conversations happening simultaneously.

### Layer 2: Clarifier (Understanding)

The Clarifier watches the Helm streams. Not to surveil — to
understand. It runs inference on the currents and voice
transcripts, extracting:

- **Needs**: "We're stuck on X, need help from Y"
- **Alerts**: "This thing just broke / appeared / changed"
- **Decisions**: "We decided to do X instead of Y"
- **Risks**: "If we don't fix X by Thursday, Y fails"
- **Completions**: "X is done, moving to Y"

The Clarifier doesn't guess intent from scratch. It uses the
structured types from Vision (Signal weights, Response kinds)
and the structured communication from Helm (rank, current topic,
attention score) as context. It's classifying structured data,
not performing open-ended language understanding.

### Layer 3: Vision (Routing)

When the Clarifier identifies something that should percolate
upward, it creates or updates a Vision Signal. The signal enters
the cascade at the appropriate tier — not at the top. A team's
problem doesn't go to the general; it goes to their squad leader.
If the squad leader can't resolve it, they escalate. Vision's
cascade handles the routing.

```
Team chat on Helm:
  "The auth service is down"
  "We can't deploy without it"
  "Anyone know who owns auth?"
      |
      v
Clarifier detects:
  - Need: team is blocked
  - Topic: auth service
  - Urgency: blocking deployment
  - Domain: infrastructure
      |
      v
Vision Signal created:
  - Weight: Suggestion (auto-generated, not mandate)
  - Content: "Auth service outage blocking deployment.
    Team Alpha needs infra support."
  - Domain: infrastructure
  - Routed to: team's direct lead
      |
      v
Lead sees signal, has context.
  - Can resolve directly ("I'll restart the service")
  - Can escalate to their VP with summary
  - Can open a joint Helm channel between Team Alpha
    and the infra team
```

---

## The General's View

A military general using the full stack. Action across the entire
front. Hundreds of units reporting, detecting, engaging.

### What Happens at the Bottom

**Squad level.** A patrol spots enemy movement. The squad leader
reports on Helm voice to their platoon leader. The Clarifier
hears the report, extracts: contact type, grid reference,
estimated size, direction of movement. This becomes a structured
alert in Vision.

Simultaneously, three other squads in different sectors report
similar sightings. The Clarifier correlates: four contacts,
converging on grid sector 7, estimated company-strength force.
This correlation wouldn't be visible to any individual squad
leader — they each see one contact. The Clarifier sees the
pattern.

### What Happens in the Middle

**Battalion level.** The battalion commander's Vision dashboard
shows four alerts from four sectors, auto-correlated into a
single assessed threat. The commander didn't ask for a report —
the reports found them. The Clarifier has already drafted a
summary: "Company-strength force approaching from NW, converging
on sector 7. Four independent contacts confirm. Estimated
contact in 45 minutes."

The commander can:
- Tap "Acknowledge" — the alert shows green in the general's view
- Open a joint Helm voice channel between the four affected squads
- Issue a directive via Vision: "All units in sector 5-9, prepare
  defensive positions"
- Talk directly to any squad leader via Helm's hierarchical voice

### What Happens at the Top

**General level.** The general sees the entire front as a
portfolio of active situations, each with:

- Status (quiet / active / critical)
- Confidence level (how reliable is the intel)
- Response state (unacknowledged / acknowledged / responding)
- Cascade health (is the chain of command functioning)

```
┌──────────────────────────────────────────────────┐
│ FRONT COMMAND — Gen. Morrison                     │
├──────────────────────────────────────────────────┤
│                                                  │
│ ████ SECTOR 7: Company-strength approach          │
│      Status: ACTIVE   Confidence: HIGH (4 src)   │
│      Response: Bn-2 acknowledging                │
│      ETA contact: 42 min                         │
│      [Listen] [Hail Bn-2] [Broadcast]            │
│                                                  │
│ ▓▓▓ SECTOR 3: Patrol contact, single vehicle      │
│     Status: MONITORING  Confidence: MED (1 src)  │
│     Response: Plt-7 investigating                │
│                                                  │
│ ░░ SECTOR 12: Quiet                               │
│    Last report: 22 min ago                       │
│                                                  │
│ ░░ SECTOR 1-2, 4-6, 8-11: Quiet                  │
│    All units reporting normal                    │
│                                                  │
│ ── Recent Signals ─────────────────────────────  │
│ 14:32 [Auto] Correlation: 4 contacts → assessed │
│       company approach sector 7                  │
│ 14:28 [Bn-2] Sector 7 squad reports movement    │
│ 14:15 [Plt-7] Routine patrol, sector 3 vehicle  │
│ 13:50 [Auto] All sectors quiet, last 2 hours    │
│                                                  │
└──────────────────────────────────────────────────┘
```

The general didn't attend a briefing. Nobody prepared slides.
The front reported itself. The Clarifier correlated the raw
reports into assessed situations. Vision routed the assessments
through the chain of command. Helm provides the voice channels
to act on them. The general's entire job is judgment: which
situations need intervention, which are being handled, where
to allocate reserves.

---

## The Clarifier's Role

The Clarifier is not a generic AI watching chat. It is a formal
tool that already exists in the Codex OS (`codex/os/core/Clarifier.codex`).
It parses utterances into **solid nodes** (what was unambiguously
communicated) and **unresolved nodes** (what needs clarification),
using the same CPL (Codex Prose Language) grammar that the compiler
uses to parse prose blocks. The difference: the compiler throws
errors; the Clarifier asks questions.

### The CPL Backbone

Every message that flows through Helm gets a Clarifier parse.
The parse produces an `Utterance`:

```
Utterance = record {
  solid : List SolidNode,       -- what was clear
  unresolved : List UnresolvedNode  -- what needs help
}
```

An unresolved node has a **kind** that determines question priority:

```
QuestionKind =
  | ReferentQuestion      -- "who/what is 'it'?" — always first
  | DomainQuestion        -- "what area are we talking about?"
  | CardinalityQuestion   -- "how many?"
  | ThresholdQuestion     -- "compared to what?"
  | TemporalQuestion      -- "when?"
```

Referents first. Always. Until "it," "they," "that thing" bind
to a specific entity, every other question is floating. This
mirrors CPL Rule NP-1 exactly — the Clarifier enforces it
conversationally instead of as a compile error.

### Register-Aware Output

The Clarifier code-switches on **Register** — it reads the
complexity of the input and matches its clarifying questions:

| Register | Example output |
|----------|---------------|
| Child | "Who do you mean by 'he'?" |
| Casual | "When you say 'the team,' who specifically?" |
| Technical | "Unresolved referent: 'the service' (3 candidates)" |
| Formal | "NP-1: referent of 'the policy' is unresolved" |

Same underlying parse. Same question. Completely different surface.
A squad leader gets casual register. A general gets formal. An
automated alert gets technical.

### How It Feeds the Stack

**1. Disambiguation Before Routing**

When a message enters Helm and the Clarifier finds unresolved
nodes, it can prompt the speaker before the message flows upward.
"You said 'the system is down' — which system? Auth, payments,
or search?" The resolved version — "Auth service is down" — is
what enters the cascade. This means signals that reach leadership
are already disambiguated. No more "what did they mean by that?"

**2. Structured Signal Extraction**

The Clarifier's solid nodes map directly to Vision signal types:

| Solid node pattern | Vision signal |
|-------------------|---------------|
| `want : Desire` + `blocked : State` | Need signal (Suggestion weight) |
| `detected : Event` + `location : Domain` | Alert signal |
| `decided : Decision` + `action : Plan` | Decision record |
| `risk : Claim` + `condition : Threshold` | Risk signal |
| `completed : State` + `deliverable : Named` | Completion signal |

The Clarifier doesn't guess — it classifies based on the same
parse that the CPL compiler uses. If the parse has a solid
`blocked` node with a resolved referent, that's a need. If it
has unresolved referents, the Clarifier asks first.

**3. Tier-Appropriate Summaries**

When a signal flows up the cascade, each tier needs context at
the right register. The Clarifier generates summaries by
re-rendering the same `Utterance` at different registers:

- Squad leader (Casual): "Auth service is down, team can't deploy,
  need infra help"
- Battalion commander (Technical): "Auth service outage blocking
  CI/CD pipeline. 4 engineers investigating. Root cause: connection
  pool exhaustion. ETA fix: 30 min."
- General (Formal): "Infrastructure incident. Root cause identified.
  Resolution in progress. No customer escalation required."

Same solid nodes, same data, different register.

**4. Correlation via Shared Parse Structure**

When four squads independently report "movement detected," each
report parses to the same solid node structure:
`detected : Event, type : Movement, location : Grid`. The
Clarifier's correlation engine matches on solid-node structure,
not on text similarity. Four independent parses with the same
shape but different location values → assessed pattern.

This is more reliable than keyword matching or embeddings because
the parse is structural. "Enemy spotted moving east" and "I see
vehicles heading toward sector 7" parse to the same structure
even though they share no words.

**5. The Question Priority Queue**

When multiple things are unresolved in a signal or report, the
Clarifier doesn't bombard. It asks the question whose answer
unblocks the most other resolutions. Referents first — because
until "it" binds, nothing else resolves. Then domain (what area),
then cardinality (how many), then threshold (compared to what),
then temporal (when).

This means the cascade is efficient. Instead of a vague report
going up and three levels of "what did they mean?" coming back
down, the Clarifier resolves ambiguity at the source in one or
two questions.

### What It Doesn't Do

- **It doesn't make decisions.** It parses, classifies, and
  asks clarifying questions. Humans decide.
- **It doesn't assign work.** It creates signals; the cascade
  determines who acts.
- **It doesn't surveil.** It parses message structure, not
  individual behavior. There is no productivity score.
- **It doesn't override the hierarchy.** Clarifier-generated
  signals enter at the lowest appropriate tier.
- **It doesn't fabricate.** Every solid node comes from the
  utterance. Every unresolved node is a genuine ambiguity in
  what was said. The provenance is the parse tree.

### The Model as Judgment, Not Processing

The Clarifier parses at chat speed — formal grammar, deterministic,
microseconds per message. The model never sees the firehose.

But there's a class of things a parser can't catch: insight that
looks like noise. A message with low traction, no upvotes, buried
in a current of 400 people agreeing with each other — but it's
the one person who identified the actual risk. The parser sees it
as a fully-resolved utterance with no unresolved nodes. Structurally
complete. Semantically unremarkable. Low attention score. It sinks.

This is where the model enters. Not on every message — on the
structurally complete, low-attention messages that the parser has
already resolved. The set is small. The model evaluates them for
qualities that attention scoring misses:

| Quality | What the model looks for |
|---------|-------------------------|
| **Insight** | "Good point" — a structurally sound observation that advances the discussion but isn't popular |
| **Contrarian truth** | An unpopular claim that is well-formed and addresses a real risk others are avoiding |
| **Novel framing** | A restatement of a known problem from an angle nobody else has taken |
| **Unacknowledged risk** | A risk claim with resolved referents and a plausible causal chain, getting zero attention |
| **Clever synthesis** | Connects two separate currents that nobody else has linked |

The model doesn't flag these as "correct" — it flags them as
**worth attention**. The signal that surfaces is:

```
Signal: [Model] Low-traction insight detected
Source: Helm current "budget discussion" (2 participants)
Content: "If we ship the hardware before the firmware
  update is ready, every unit in the field needs a
  recall. That's $4M we don't have."
Quality: Unacknowledged risk
Parse: fully resolved, 3 solid nodes, 0 unresolved
Attention: 0.02 (bottom 5% of room)
Model assessment: "Structurally sound risk claim with
  specific cost estimate. No responses. The current
  consensus (ship on schedule) does not address this."
```

The model is the exception handler, not the main loop. It runs
on the long tail — the messages that are structurally perfect
but socially invisible. The parser did the work of understanding
what was said. The model does the work of understanding whether
anyone should have listened.

This inverts the usual AI-in-chat pattern. Most systems stream
every message to a model and hope it finds something useful.
Helm sends the model *only* the messages that are clear,
unpopular, and potentially important. The model's job isn't
comprehension — it's taste.

### Confidence and Transparency

Every Clarifier-generated signal carries its parse:

```
Signal: Auth service outage blocking deployment
Source: [Auto] Clarifier
Parse:
  SOLID:
    - service-down : Event (referent: "auth service")
    - team-blocked : State (referent: "deployment pipeline")
    - need-help : Desire (domain: "infrastructure")
  UNRESOLVED: (none — all referents resolved)
Confidence: structural (parse complete, no unresolved nodes)
```

If the parse was wrong, a human corrects it. Corrections feed
back: "this pattern was misclassified" improves future parses.

---

## Corporate Scenario

Not just military. A tech company using the full stack:

**9:15 AM.** A production monitoring alert fires. The on-call
engineer starts debugging in the team's Helm chat channel. Other
engineers join. The Clarifier detects: incident in progress,
severity unknown, 4 engineers engaged.

**9:22 AM.** The Clarifier correlates the Helm discussion with
a spike in the error metrics from Vision's portfolio layer. Auto-
generates a signal: "Production incident — auth service latency
spike. 4 engineers investigating. Customer-facing impact
unknown." Routes to the engineering director.

**9:25 AM.** The director sees the signal. Opens a Helm joint
channel between the on-call team and the infrastructure team.
Listens in for 2 minutes, understands the situation. Adds context
to the Vision signal: "Root cause identified — database
connection pool exhaustion. Fix estimated 30 minutes." Pushes
a summary to the VP.

**9:27 AM.** The VP sees a one-line signal: "Production incident,
root cause found, fix in 30 min. No customer escalation needed."
She marks it "Monitoring" and moves on.

**9:55 AM.** The fix deploys. The Clarifier detects the Helm
current shifting from "debugging" to "verifying fix" to "all
clear." It updates the Vision signal state to "Resolved."

**10:00 AM.** The VP's signal shows green. The director's
dashboard shows the incident is closed. Nobody held a war room.
Nobody made a slide. The stack handled it.

---

## Data Flow

```
              Helm (real-time)
              ┌─────────────────────────────┐
              │ Chat currents   Voice flows  │
              └──────────┬──────────────────┘
                         │ streams
                         v
              Clarifier (AI inference)
              ┌─────────────────────────────┐
              │ Need detection              │
              │ Alert extraction            │
              │ Correlation engine          │
              │ Summary generation          │
              └──────────┬──────────────────┘
                         │ signals
                         v
              Vision (structured cascade)
              ┌─────────────────────────────┐
              │ Signal routing              │
              │ Cascade management          │
              │ Portfolio tracking          │
              │ Accountability chain        │
              └─────────────────────────────┘
```

Each layer can function independently. Helm works without
Vision or the Clarifier — it's still a better chat and voice
system. Vision works without Helm — signals can be manually
entered. The Clarifier works without either — it can process
any text stream. But together, they form a closed loop where
raw communication becomes structured intelligence becomes
coordinated action.

---

## Architecture Addition

### New Modules (Convergence Layer)

| Module | Purpose |
|--------|---------|
| `BridgeTypes.codex` | Shared types across Vision + Helm + Clarifier |
| `BridgeIngest.codex` | Helm → Clarifier adapter, current/voice stream ingestion |
| `BridgeClassify.codex` | Need/alert/decision/risk/completion classification |
| `BridgeCorrelate.codex` | Multi-source correlation, pattern detection |
| `BridgeSummarize.codex` | Tier-appropriate summary generation |
| `BridgeEmit.codex` | Clarifier → Vision adapter, signal creation |
| `BridgeConfig.codex` | Domain templates, keyword rules, alert thresholds |

### Signal Provenance

Every signal in Vision now carries provenance:

```
SignalSource =
  | SrcHuman (Text)
  | SrcClarifier (Integer)
  | SrcCorrelation (List Text)
  | SrcEscalation (Text)

SignalEvidence = record {
  se-helm-currents : List Text,
  se-voice-segments : List Text,
  se-metrics : List Text,
  se-confidence : Integer,
  se-model-version : Text
}
```

Human-authored signals have `SrcHuman`. Clarifier-generated
signals have `SrcClarifier` with a confidence score. Correlated
signals reference their constituent source signals. Escalated
signals reference the original. The provenance chain is always
traceable — you can drill from the general's one-line summary
all the way down to the specific chat messages that triggered it.

---

## Why This Matters

The military has AWACS, JSTARS, and satellite feeds — sensors
that watch the battlefield and present a common operating
picture. But the link from individual soldier reports to the
general's COP is still manual: radio calls transcribed by
operators, marked on maps by staff, briefed by aides. The
information is hours old by the time it reaches decision-makers.

The corporate world is worse. Information flows through email,
Slack, Jira, PowerPoint, and word of mouth. Correlation is done
by humans in meetings. Summaries are opinions dressed as data.
By the time leadership learns about a problem, it's either
already fixed or already a crisis.

The Convergence stack eliminates the manual translation layer.
Raw communication becomes structured intelligence in real time.
The Clarifier is the translator. Vision is the routing. Helm is
the pipe. The hierarchy is preserved — subordinates still report
to their leaders, leaders still make decisions. But the mechanics
of "I need to tell my boss, who needs to tell their boss" are
handled by infrastructure, not by humans spending hours in
meetings figuring out what to say to whom.

The general sees the front. The VP sees the portfolio. The
director sees the team. Each at the right level of abstraction,
each in real time, each with the provenance chain intact so they
can drill down when judgment requires detail.

No briefings. No slides. No "let me get back to you on that."
The answer is already there. The question is what to do about it.
