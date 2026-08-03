# Helm

**Clarity at scale. For text and voice.**

Helm is two tools in one shell: a chat system that makes massive
rooms readable, and a voice system that makes large groups
audible. Both solve the same problem -- when hundreds of people
talk at once, how do you hear what matters?

Chat rooms today scale to about 30 active participants before
they become unreadable. Voice channels scale to about 6 before
they become crosstalk. Helm pushes both numbers to thousands.

---

## Part I: The River (Chat)

### The Problem

A thousand people in a chat room watching a live event. A goal
is scored. 400 people type some variant of "GOOOAL!" -- 400
individual messages that say the same thing. Meanwhile, 30 people
are having a tactical discussion about the formation change that
led to the goal. And 8 people are arguing about the referee. And
2 people are talking about what to have for dinner.

In Discord or Twitch chat, all of this is a wall of text scrolling
at 50 lines per second. The tactical discussion is invisible. The
dinner conversation is noise. The 400 goal reactions are redundant.
Nobody can read anything.

### The Solution: Statement Clustering

Helm doesn't show individual messages in a flood. It clusters
semantically similar statements into **Currents** -- named groups
that represent a single sentiment or topic within a time window.

```
┌─────────────────────────────────────────────────┐
│ LIVE: Championship Final                        │
├─────────────────────────────────────────────────┤
│                                                 │
│ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ GOAL! (412 people)        │
│   "GOOOAL" "YES!" "GET IN!" "WHAT A STRIKE"    │
│   ● 98% positive                                │
│                                                 │
│ ▓▓▓▓▓▓ Formation analysis (34 people)           │
│   "The 4-3-3 switch opened the right side"      │
│   "Watch the overlapping run from the fullback" │
│   ● Discussion: 3 sub-threads                   │
│                                                 │
│ ▓▓▓ Referee complaints (8 people)               │
│   "That was offside" vs "Clean goal"            │
│   ● Split: 5 offside / 3 clean                  │
│                                                 │
│ ░ dinner plans (2 people)                       │
│   ░ barely visible -- low relevance              │
│                                                 │
└─────────────────────────────────────────────────┘
```

### How Clustering Works

Every message enters a pipeline:

1. **Embed**: Extract semantic vector from the message text.
   Short messages ("GOAL!") cluster by keyword + emoji. Longer
   messages cluster by topic similarity.

2. **Match**: Compare the vector against active currents in the
   room. If similarity exceeds the merge threshold, the message
   joins that current. If not, it starts a new current.

3. **Decay**: Currents that stop receiving messages fade over time.
   A burst of "GOAL!" messages fades in 30 seconds. A sustained
   discussion stays visible for minutes.

4. **Rank**: Currents are sorted by attention score -- a combination
   of participant count, recency, and explicit clicks.

### Attention Sorting

Not all currents are equal. Helm ranks them by **attention** --
how many people are engaging with a topic relative to the room
size.

```
AttentionScore = record {
  as-participants : Integer,
  as-clicks : Integer,
  as-recency : Integer,
  as-velocity : Integer
}
```

- **Participants**: How many unique people contributed to this
  current. 412 > 34 > 8 > 2.
- **Clicks**: Users click a current to "tune in" -- they want to
  see its messages expanded. Clicks are explicit attention signals.
- **Recency**: When was the last message in this current? Recent
  currents rank higher.
- **Velocity**: How fast are messages arriving? A sudden burst
  ranks higher than a steady trickle.

The dinner conversation between 2 people has near-zero attention.
It's still there -- you can find it -- but it's visually minimized.
The 412-person goal reaction dominates the view. The 34-person
tactical discussion is the second thing you see.

### Expanding a Current

Click a current to expand it. You see individual messages, newest
first. You can reply directly -- your reply joins that current.
You can also "break out" a sub-topic into its own current.

### Sentiment Hierarchy

Within a current, Helm detects sentiment splits. The referee
current shows "5 offside / 3 clean" -- two sub-sentiments. You
can expand either sub-sentiment to see the individual arguments.

```
Current = record {
  cur-id : Text,
  cur-topic : Text,
  cur-summary : Text,
  cur-messages : List Message,
  cur-count : Integer,
  cur-attention : AttentionScore,
  cur-sentiment : Sentiment,
  cur-sub-currents : List Current,
  cur-started : Integer,
  cur-last-active : Integer,
  cur-decay : Integer
}

Sentiment =
  | SentPositive (Integer)
  | SentNegative (Integer)
  | SentNeutral
  | SentSplit (Integer) (Integer)
  | SentMixed
```

### Time Bands

Currents exist within time bands. A time band is a window
(configurable -- 1 minute, 5 minutes, 15 minutes) that groups
activity. When you scroll back in time, you see bands:

```
[14:32-14:37] 3 currents, 460 participants
  - GOAL! (412) → Formation discussion (34) → Referee (8)

[14:28-14:32] 2 currents, 45 participants
  - Corner kick analysis (38) → Kit complaints (7)

[14:20-14:28] 1 current, 12 participants
  - Pre-match predictions
```

This makes a 3-hour chat session browseable in seconds. You see
the shape of the conversation -- what topics dominated when, how
the crowd's attention moved. History becomes a timeline of
collective attention, not a scroll of individual messages.

### Small Rooms

In a room with 5 people, clustering is unnecessary. Helm detects
room size and falls back to traditional sequential chat. The
clustering activates when message velocity exceeds a threshold
(configurable -- default: 10 messages per minute from 10+ unique
senders). Below that, it's just chat.

---

## Part II: The Bridge (Voice)

### The Problem

12 people on a voice channel trying to coordinate a fleet battle
in a space sim. The admiral is giving orders. Two wing commanders
are directing their pilots. A scout is reporting enemy positions.
A logistics player is asking about ammunition. Everyone hears
everyone. The admiral's orders are drowned in chatter about ammo.
The scout's critical intel is lost under two pilots arguing about
formation.

In Discord, the "solution" is to split into separate channels.
But then the admiral can't hear the wing commanders, the wing
commanders can't hear each other when they need to coordinate,
and nobody can dynamically reorganize when the situation changes.

### The Solution: Hierarchical Voice

Helm voice implements a military-style communication hierarchy
where **rank determines who you hear and who hears you**.

```
                    ┌─────────┐
                    │ Admiral │ ← Free speak, everyone below hears
                    └────┬────┘
              ┌──────────┼──────────┐
          ┌───┴───┐  ┌───┴───┐  ┌───┴───┐
          │Capt A │  │Capt B │  │Capt C │ ← Hear admiral + peer talk
          └───┬───┘  └───┬───┘  └───┬───┘
         ┌────┼────┐    ┌┴┐     ┌───┼───┐
         │    │    │    │ │     │   │   │
        p1   p2   p3  p4 p5   p6  p7  p8  ← Hear their captain only
```

### Voice Rules

| Speaker | Who Hears Them | Notes |
|---------|---------------|-------|
| Admiral | Everyone below | Free speak at all times |
| Captain | Their squad + admiral (if listening) | Can peer-talk with other captains |
| Pilot | Their squad only | Cannot reach other squads or admiral |

### Key Principle: Downward Free, Upward Gated

You can always talk freely to anyone below you in the chain.
Your subordinates always hear you. But talking upward is gated:

- A pilot can **request** to speak to the admiral. The captain
  relays or grants a temporary uplink.
- A captain can talk to the admiral directly (peer level to the
  admiral's direct reports), but the admiral can toggle listening.
- Nobody below captain can talk to other captains' squads.

This eliminates crosstalk. A pilot in Squad A cannot accidentally
(or intentionally) distract Squad B or the admiral. The captain
is the gatekeeper.

### The Rank System

```
Rank = | Admiral | Captain | Lieutenant | Crew

VoiceNode = record {
  vn-id : Text,
  vn-name : Text,
  vn-rank : Rank,
  vn-parent : Text,
  vn-children : List Text,
  vn-muted : Boolean,
  vn-listening-up : Boolean,
  vn-speaking : Boolean
}
```

The hierarchy is configurable. It doesn't have to be naval:

| Template | Top | Mid | Bottom |
|----------|-----|-----|--------|
| Naval | Admiral | Captain | Crew |
| Military | Commander | Squad Lead | Soldier |
| Corporate | Director | Manager | Team |
| Gaming | Raid Lead | Group Lead | Player |
| Event | Host | Moderator | Attendee |

### Admiral's Controls

The admiral has a dashboard showing all squads:

```
┌─────────────────────────────────────────────┐
│ FLEET COMMAND                                │
├───────────┬───────────┬─────────────────────┤
│ Alpha Wing│ Beta Wing │ Charlie Wing        │
│ Capt: Ana │ Capt: Bob │ Capt: Carol         │
│ 3 crew    │ 2 crew    │ 3 crew              │
│ ● active  │ ● active  │ ○ quiet             │
│ [Listen]  │ [Listen]  │ [Listen]            │
│ [Hail]    │ [Hail]    │ [Hail]              │
└───────────┴───────────┴─────────────────────┘
│ [Broadcast All] [Mute Incoming] [Rec]       │
└─────────────────────────────────────────────┘
```

- **Broadcast All**: Admiral's voice goes to every person in the
  fleet.
- **Listen [squad]**: Toggle whether the admiral hears a specific
  squad's internal chatter.
- **Hail [captain]**: Direct 1:1 voice channel with a captain.
- **Mute Incoming**: Admiral stops hearing captain peer-chat.
  Only direct hails come through.

### Captain's Controls

A captain sees their squad and the command channel:

```
┌──────────────────────────────────┐
│ ALPHA WING                       │
│ Admiral: [●] (broadcasting)      │
│ ─────────────────────────────── │
│ p1: Ana Jr  ● speaking          │
│ p2: Dave    ○                    │
│ p3: Eve     ● speaking          │
│ ─────────────────────────────── │
│ Peer: [Bob ●] [Carol ○]         │
│ [Request Broadcast] [Hail Adm]  │
└──────────────────────────────────┘
```

- **Peer channel**: Captains can talk among themselves. The
  admiral can listen in or not.
- **Request Broadcast**: Ask the admiral for fleet-wide voice
  (e.g., "Enemy spotted, all wings converge").
- **Hail Admiral**: Direct line to the top.

### Joint Deployments

The killer feature. Two captains from different admirals need to
coordinate a joint operation. They create a **Joint Channel** --
a temporary voice link between specific squads.

```
Joint Channel: "Operation Anvil"
  Alpha Wing (Admiral 1, Capt Ana) ←→ Delta Wing (Admiral 2, Capt Dan)
  Duration: until closed by either captain
  Visibility: both admirals notified, can listen
```

In a joint channel:
- Both captains hear each other (peer level).
- Their crews hear their own captain + the other captain (one
  level up).
- Crews from different wings still cannot hear each other
  directly -- only through their captains.
- Both admirals can listen in but are not required to.

```
JointChannel = record {
  jc-id : Text,
  jc-name : Text,
  jc-wings : List Text,
  jc-created-by : Text,
  jc-active : Boolean,
  jc-started : Integer
}
```

### Voice Activity Indicators

Helm shows who's talking at every level:

- Green ring: speaking
- Yellow ring: speaking, high priority (commander)
- Red ring: speaking, emergency (override)
- Grey: muted or silent

Audio levels are normalized per-rank. The admiral's voice is
slightly louder than a pilot's when both are audible. This
mimics real command radios where the command frequency is
prioritized in the mix.

### Emergency Override

Any rank can trigger an emergency broadcast that punches through
all hierarchy gates for 10 seconds. "Enemy on our six, breaking
formation!" goes to everyone regardless of rank. The emergency
is visually and audibly distinct (different tone, red indicator).
Overuse of emergency is visible in the log.

```
EmergencyBroadcast = record {
  eb-speaker : Text,
  eb-message : Text,
  eb-timestamp : Integer,
  eb-reached : List Text
}
```

---

## Part III: Convergence

The chat and voice systems share infrastructure:

- **Identity**: Same user, same presence, same permissions.
- **Hierarchy**: The voice rank structure can inform chat
  current visibility -- a commander's message in chat is
  highlighted, not buried.
- **Attention**: Voice activity feeds into chat attention
  scoring. If the admiral speaks, and 50 people type about
  what they said, that current rises.
- **Recording**: Both text and voice are logged. The transcript
  shows chat currents alongside voice timeline.

### Event Mode

For live events (esports, launches, conventions), Helm combines
both systems:

- Voice: Commentators at admiral rank, moderators at captain,
  audience at crew (listen-only with request-to-speak).
- Chat: Thousands of messages clustered into currents. Moderators
  can pin currents, dismiss noise, and surface questions.

---

## Market Position

### Chat

| Feature | Discord | Twitch Chat | Zulip | Slack | **Helm** |
|---------|---------|-------------|-------|-------|----------|
| Max readable participants | ~30 | ~0 (wall of text) | ~50 | ~20 | **1000+** |
| Auto-clustering | No | No | No | No | **Yes** |
| Attention sorting | No | No | No | No | **Yes** |
| Sentiment analysis | No | No | No | No | **Yes** |
| Time-band browsing | No | No | No | Threads | **Yes** |
| Small room fallback | N/A | N/A | N/A | N/A | **Yes** |

### Voice

| Feature | Discord | TeamSpeak | Mumble | Squad | ACRE2 | **Helm** |
|---------|---------|-----------|--------|-------|-------|----------|
| Hierarchy enforcement | No | Partial | ACL | 2-tier | Radio sim | **N-tier** |
| Downward broadcast | No | No | Priority | Command net | Yes | **Yes** |
| Upward gating | No | No | ACL | No | No | **Yes** |
| Peer channels | N/A | N/A | Whisper | Numpad | Freq | **Yes** |
| Joint deployments | No | No | No | No | No | **Yes** |
| Emergency override | No | No | No | No | No | **Yes** |
| Dynamic hierarchy | No | No | No | Fixed | Fixed | **Yes** |

Nothing on the market combines auto-clustering chat with
hierarchical voice. Squad comes closest on voice (2-tier with
command net + direct squad-leader comms) but has no chat
clustering and the hierarchy is fixed at 2 levels.

---

## Architecture

```
Browser (Codex widget UI)
    |
    ├── Chat Engine ── Embedding ── Clustering ── Attention Scorer
    |
    ├── Voice Engine ── WebRTC / Opus ── Hierarchy Mixer ── Rank Router
    |
    v
Helm Server (CDX, JSON API + WebSocket)
    |
    +--> Room Store (currents, messages, time bands)
    +--> Voice State (hierarchy, channels, joints)
    +--> Identity (users, ranks, permissions)
    +--> Analytics (attention history, voice activity log)
```

### Modules

| Module | Purpose |
|--------|---------|
| `HelmTypes.codex` | Current, Message, Sentiment, AttentionScore, Rank, VoiceNode |
| `HelmCluster.codex` | Semantic embedding, similarity matching, current management |
| `HelmAttention.codex` | Attention scoring, decay, time band management |
| `HelmVoice.codex` | Hierarchy state, rank routing, joint channels |
| `HelmMixer.codex` | Audio mixing rules: who hears who at what volume |
| `HelmStore.codex` | Persistence for rooms, currents, voice state |
| `HelmServer.codex` | WebSocket API for real-time chat + voice signaling |
| `HelmTheme.codex` | UI theme with rank colors and attention heat |
| `RiverPage.codex` | Chat view with clustered currents |
| `BridgePage.codex` | Voice command dashboard |

---

## Why "Helm"

The helm is where you steer. In a storm of voices, you need
the helm to keep course. The chat view is the radar -- showing
you the shape of the conversation so you can navigate it. The
voice system is the comm -- giving orders and receiving reports
through a chain that doesn't devolve into noise.

At scale, communication isn't about talking more. It's about
hearing what matters. Helm is the instrument that makes that
possible.
