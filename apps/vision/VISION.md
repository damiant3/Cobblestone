# Vision

**Ideas flow down. Responses flow up. Meetings disappear.**

Vision is an organizational intelligence platform that replaces
the machinery of corporate communication — the status meetings,
the vision statements, the cascading email chains, the "can you
put together a deck on this?" — with a structured conversation
about values and implementation.

An executive posts an idea at 3am. Nobody gets woken up. But by
9am, the idea has been assessed by two VPs, pushed to the director
who owns the relevant domain, and that director's team lead has a
concrete task with a response deadline. By Thursday, the response
flows back up: "Here's what we'd do, here's what it costs, here's
the timeline." The executive reads it over coffee. No meeting was
held. No slide was made.

The formality didn't disappear because someone decided to be
informal. It disappeared because the structure made it unnecessary.

---

## The Problem, Restated

Corporate information flow has three failure modes:

**Failure 1: The broadcast.** An executive has an idea and posts
it to Slack, or says it in an all-hands, or puts it in a memo.
Everyone sees it. Nobody knows if they're supposed to act on it.
The idea sits in the ambient noise until someone brave enough
asks "did you want us to do something about this?" Three weeks
later, in a different meeting, the executive asks "whatever
happened with that resumes idea?" and nobody remembers.

**Failure 2: The fire drill.** An executive has an idea and their
chief of staff interprets it as a directive. Suddenly 40 people
are in a war room on a Sunday because someone three levels down
interpreted "I think resumes suck" as "rebuild our entire hiring
pipeline by Monday." The executive didn't want that. But the
telephone game of corporate hierarchy amplified a musing into a
crisis.

**Failure 3: The black hole.** An executive has a genuinely
important idea, communicates it clearly, and it enters the
organizational hierarchy where it is acknowledged, discussed,
put on a roadmap, deprioritized against existing commitments,
and quietly forgotten. Nobody is accountable because everyone
touched it but nobody owned it.

Vision solves all three by making the flow explicit, the
accountability clear, and the response structured.

---

## Core Concept: The Cascade

An idea enters the system. We call it a **Signal**. A Signal has:

- An author (who said it)
- A weight (musing, suggestion, directive, mandate)
- A domain hint (what area it touches)
- Content (what was actually said)

The weight is critical. It replaces the ambiguity that causes
fire drills:

| Weight | Meaning | Expected Response |
|--------|---------|-------------------|
| **Musing** | "I've been thinking about this" | Read and consider. No action required. |
| **Suggestion** | "We should look into this" | Assess feasibility. Report back with options. |
| **Directive** | "Make this happen" | Plan, resource, and execute. Report progress. |
| **Mandate** | "This is non-negotiable" | Execute immediately. Escalate blockers. |

Most executive ideas are musings or suggestions. The current system
treats them all as mandates because nobody knows how to ask "how
serious are you?" Vision makes the weight explicit from the start.

### The Cascade Flow

```
Signal enters (author: CEO, weight: Suggestion, domain: HR)
    |
    v
Tier 1: CEO's direct reports see the signal
    |  - VP Engineering: "Not my domain" → passes
    |  - VP People: "This is mine" → PICKS UP
    |
    v
Tier 2: VP People pushes to their reports
    |  - Dir Recruiting: PICKS UP (domain match)
    |  - Dir People Ops: passes
    |
    v
Tier 3: Dir Recruiting assigns to team
    |  - "Assess current resume process, propose alternatives"
    |  - Deadline: 5 business days
    |
    v
Response flows UP
    |  - Team lead → Dir Recruiting: "Here's our analysis + 3 options"
    |  - Dir Recruiting → VP People: "Recommendation: Option B, 6-week pilot"
    |  - VP People → CEO: "Team recommends X, cost Y, timeline Z"
    |
    v
CEO reads response. No meeting. Decision made or deferred.
```

Every step is recorded. Every handoff is explicit. If the signal
sits at any tier for too long, Vision nudges the holder — not the
originator. The originator never has to ask "whatever happened
with...?" because they can see exactly where the signal is.

### Nobody Gets Woken Up

Signals are asynchronous. A musing posted at 3am doesn't trigger
notifications. It appears in tier-1 holders' Vision feed when they
next check. Only mandates trigger immediate notifications, and
only to the direct tier — not the whole chain.

The notification model:

| Weight | Notification | Timing |
|--------|-------------|--------|
| Musing | None | Appears in feed |
| Suggestion | Feed + daily digest | Next business day |
| Directive | Feed + immediate to tier 1 | Now, but only tier 1 |
| Mandate | Feed + immediate to all tiers | Now, cascading |

---

## The Accountability Chain

When someone picks up a signal, they become the **holder**. The
holder is accountable for one of two things:

1. **Push down**: push the signal to the right person on their
   team and set a response deadline.
2. **Respond up**: provide a structured response to whoever
   pushed it to them.

If they do neither within the signal's response window, Vision
escalates — not with punishment, but with visibility. The signal
shows "awaiting response from [holder] for 3 days" in the
originator's view.

### The Anti-Micromanagement Guard

Vision explicitly prevents skip-level interference. An executive
cannot reach down past their direct reports to assign work. They
can post a signal; their directs can push it; the next tier can
push it further. But nobody can jump the chain. This prevents the
fire drill failure mode and preserves each manager's authority
over their team's priorities.

If an executive truly needs to reach three levels down, they
escalate the weight to Mandate, and the cascade happens
immediately through every tier. But the record shows they chose
to do that, and every tier in between is notified that they were
bypassed.

---

## Data Model

### Signal

```
SignalWeight = | Musing | Suggestion | Directive | Mandate

SignalState = | SigNew | SigPickedUp | SigPushed | SigResponded | SigClosed | SigExpired

Signal = record {
  sig-id : Text,
  sig-author : Text,
  sig-weight : SignalWeight,
  sig-domain : Text,
  sig-content : Text,
  sig-state : SignalState,
  sig-created : Integer,
  sig-response-window : Integer
}
```

### Cascade

A cascade is the chain of handoffs for a signal. Each node in the
cascade is a **Hold** — someone picked up the signal and is either
responding or pushing down.

```
HoldAction = | HoldAssessing | HoldPushedDown | HoldResponded | HoldPassed | HoldEscalated

Hold = record {
  hld-id : Text,
  hld-signal : Text,
  hld-holder : Text,
  hld-tier : Integer,
  hld-action : HoldAction,
  hld-pushed-to : Text,
  hld-response : Text,
  hld-deadline : Integer,
  hld-picked-up : Integer,
  hld-completed : Integer
}
```

### Response

A structured reply that flows back up the cascade.

```
ResponseKind =
  | RspAnalysis
  | RspProposal
  | RspEstimate
  | RspDeclined
  | RspEscalation
  | RspCompletion

Response = record {
  rsp-id : Text,
  rsp-signal : Text,
  rsp-hold : Text,
  rsp-author : Text,
  rsp-kind : ResponseKind,
  rsp-summary : Text,
  rsp-detail : Text,
  rsp-cost : Text,
  rsp-timeline : Text,
  rsp-confidence : Integer,
  rsp-created : Integer
}
```

### Person

```
PersonRole = | RoleExec | RoleVP | RoleDirector | RoleLead | RoleIC

Person = record {
  per-id : Text,
  per-name : Text,
  per-role : PersonRole,
  per-reports-to : Text,
  per-domain : Text,
  per-domains : List Text
}
```

### Domain

```
Domain = record {
  dom-id : Text,
  dom-name : Text,
  dom-parent : Text,
  dom-owner : Text,
  dom-keywords : List Text
}
```

---

## Domain Routing

When a signal has a domain hint, Vision suggests which tier-1
person should pick it up. This isn't automatic assignment — it's
a suggestion based on domain ownership. The routing considers:

1. **Domain match**: Which VP owns the domain mentioned in the
   signal? If the CEO says "resumes suck," the domain hint is
   "hiring" which maps to VP People.
2. **Keyword match**: If no explicit domain, Vision scans the
   signal content for domain keywords. "resumes" → hiring →
   VP People. "latency" → infrastructure → VP Engineering.
3. **History**: If this VP has picked up similar signals before,
   weight their suggestion higher.

The suggestion appears as a highlight in the tier-1 feed: "This
signal might be for you" with a confidence score. Nobody is
auto-assigned. The VP decides whether to pick it up.

---

## Usage Scenarios

### Scenario 1: The 3am Musing

**7:47 PM, Tuesday.** Elon posts a Signal:
- Weight: Musing
- Content: "I think resumes suck. Nobody reads them. The
  entire concept of summarizing yourself on paper is broken."
- Domain hint: (none — Vision infers "hiring")

**Nothing happens.** No notifications. The signal sits in the
system. Vision tags it with inferred domain "hiring" and
suggests VP People as the likely holder.

**8:15 AM, Wednesday.** VP People opens Vision, sees the signal
highlighted in her feed. She reads it, thinks about it. She has
three options:
- **Pass**: "Not actionable right now" — signal shows as read.
- **Pick up as Suggestion**: She escalates the weight and pushes
  to Dir Recruiting: "CEO is thinking about resume reform. Can
  you assess our current process and come back with 2-3
  alternatives? Response by Friday."
- **Respond directly**: "We're actually already piloting a
  portfolio-based assessment in the Austin office. Here are
  the preliminary results."

She picks option 2. Dir Recruiting gets a hold notification.

**9:30 AM, Wednesday.** Dir Recruiting sees the hold. She assigns
her team lead to research alternatives. Deadline: Thursday EOD.

**4:00 PM, Thursday.** Team lead responds to Dir Recruiting:
"Current process screens 400 resumes/week. Three alternatives:
(A) skills-based assessment, $120K implementation, 8-week pilot;
(B) portfolio review for technical roles, $40K, 4-week pilot;
(C) AI screening with human final review, $200K, 12-week pilot."

**9:00 AM, Friday.** Dir Recruiting adds her recommendation
(Option B for technical, keep resumes for non-technical) and
responds to VP People.

**10:30 AM, Friday.** VP People reviews, adds strategic context
("Option B aligns with our Q3 technical hiring push"), and
responds to Elon.

**Saturday morning.** Elon reads the response over coffee. Two
sentences: "Team recommends portfolio-based assessment for
technical roles, $40K 4-week pilot. Already piloting in Austin
with positive results." He taps "Approved" or "Let's discuss"
or "Interesting, not now." Done.

**Total elapsed: 3 business days. Meetings held: 0. Slides
made: 0. People woken up: 0.**

### Scenario 2: The Urgent Directive

**2:00 PM, Monday.** CEO posts a Signal:
- Weight: Directive
- Content: "Our checkout conversion dropped 15% this weekend.
  I want to know why and what we're doing about it by EOD."
- Domain hint: "commerce"

**2:01 PM.** VP Commerce gets an immediate notification. She picks
up the signal. She pushes to Dir Product and Dir Engineering
simultaneously: "CEO wants root cause + action plan. EOD today."

**2:15 PM.** Dir Engineering pulls the incident timeline. His team
finds a deploy on Friday introduced a payment form regression. Fix
is already in staging.

**3:30 PM.** Dir Product confirms analytics: regression affects
mobile checkout only, desktop is flat. 15% drop is real but
contained.

**4:00 PM.** VP Commerce assembles the responses and sends up:
"Root cause: Friday deploy broke mobile payment form. Fix in
staging, deploying at 5 PM. Expected recovery by tomorrow.
Recommend: add mobile checkout to smoke test suite."

**4:15 PM.** CEO reads it. "Deploy the fix. Add the smoke test.
Good work." Signal closed.

**Total elapsed: 2 hours 15 minutes. Status meeting: not needed.
War room: not needed. The structure handled the urgency without
the theater.**

### Scenario 3: The Cross-Domain Idea

**Wednesday.** VP Engineering posts a Signal:
- Weight: Suggestion
- Content: "Our internal tools are all different apps with
  different logins. We should build a unified internal portal."
- Domain hint: "infrastructure, IT, design"

Vision suggests three holders (cross-domain signal):
- Dir IT Infrastructure
- Dir Internal Tools
- Dir UX Design

All three pick up. Each assesses from their domain:
- IT: "SSO integration, 3 months, $80K."
- Tools: "We have 14 internal apps. Audit shows 6 are redundant.
  Consolidate first, then portal."
- UX: "Current internal NPS is 34. Portal mockup attached.
  User research needed, 4 weeks."

VP Engineering sees three responses side by side. She combines
them into a phased proposal: Phase 1 (audit + SSO, 2 months),
Phase 2 (consolidation, 3 months), Phase 3 (portal, 4 months).
Posts as a new Directive signal.

**This scenario would have been a 6-meeting, 3-month "initiative"
in the old world. Vision compressed it to a week of asynchronous
structured communication.**

### Scenario 4: The Values Conversation

**Quarterly.** CEO posts a Signal:
- Weight: Musing
- Content: "What are the three things we should stop doing?"

Every VP picks up. They don't push down — this is a values
question for their level. Each responds with their three things.
CEO reads 6 responses (6 VPs × 3 items = 18 things). Patterns
emerge: 4 VPs independently say "stop requiring approval for
purchases under $5K." CEO escalates that specific item to a
Directive: "Raise the approval threshold to $10K. Finance, make
it happen."

**What used to be a 2-day offsite with a facilitator and sticky
notes became a 48-hour asynchronous exercise with a concrete
outcome.**

### Scenario 5: The Accountability Spotlight

**Three weeks pass.** CEO posted a Suggestion about improving
onboarding. VP People picked it up, pushed to Dir People Ops.
Dir People Ops... hasn't responded. No excuse, no update, nothing.

Vision doesn't send angry emails. It does three things:

1. The signal shows "awaiting response, 15 days overdue" in the
   CEO's signal view. The CEO can see exactly where it stalled.
2. VP People sees the same thing in her cascade view. She can
   nudge Dir People Ops directly.
3. If the CEO asks "what happened with onboarding?" the answer
   is one click away, not a scramble.

Nobody is punished by the system. But nobody can hide either.
The transparency is the accountability.

---

## What Vision Is Not

**Not a task manager.** Vision doesn't manage tickets, sprints,
or kanban boards. It manages the flow of ideas from inception to
response. The task manager is whatever tool the team already uses.

**Not a chat app.** There are no threads, no reactions, no GIFs.
Signals are structured publications. Responses are structured
replies. If you need to discuss, use your team's channel. Vision
records the outcome.

**Not a surveillance tool.** Vision doesn't track keystrokes,
monitor Slack, or measure "productivity." It tracks signal flow
and response times. An IC who never touches Vision because their
lead handles the cascade is invisible to the system — and that's
fine.

**Not mandatory at every level.** A team that doesn't want to use
Vision simply doesn't pick up signals. Their VP handles the
response directly. Vision works whether 10 people use it or
10,000.

---

## The Portfolio Layer

Vision's second mode: project tracking. This runs alongside the
signal cascade but serves a different need — understanding where
projects stand across the organization.

### Projects, Milestones, Dependencies

Teams that opt in publish structured facts about their projects:
milestones hit, risks identified, dependencies declared. Vision
computes over these facts:

- **Cascade analysis**: If milestone X slips, what downstream
  projects are affected?
- **Resource contention**: Two projects need the same team in
  the same two-week window.
- **Portfolio confidence**: Aggregated milestone health across
  all projects in a domain.

### Layered Opt-In

| Layer | What you publish | What you see |
|-------|-----------------|--------------|
| **Heartbeat** | "We're alive, here's our cadence" | Portfolio presence |
| **Milestones** | Key dates and deliverables | Timeline, dependency graph |
| **Risk** | Identified risks with probability/impact | Risk heat map |
| **Resources** | Team allocation and capacity | Contention detection |
| **Metrics** | KPIs, velocity, quality signals | Trend dashboards |
| **Dependencies** | What we need from / provide to others | Critical path |

No team is forced to any layer. Each layer adds value
independently.

### Signal + Portfolio Integration

The two modes connect naturally. A CEO signal "Why is Project X
delayed?" auto-links to Project X's portfolio data. The response
can reference specific milestones, risks, and dependencies instead
of narrative. "Project X is delayed because Milestone 3 (API
migration) slipped 2 weeks due to Dependency D7 (Auth team
capacity). Auth team is at 140% allocation in weeks 18-20. Options:
defer Milestone 4 by 2 weeks, or move Auth team off Project Y
temporarily."

That response is computable from the portfolio data. The person
responding is composing structured facts, not writing a story.

---

## Architecture

```
Browser (Codex widget UI)
    |
    v
Vision Server (CDX, JSON API)
    |
    +--> Signal Store (append-only signal + hold + response log)
    +--> Portfolio Store (milestones, risks, dependencies)
    +--> Cascade Engine (routing, nudging, escalation)
    +--> Org Graph (people, roles, domains, reporting chains)
    +--> View Engine (portfolio projections, signal timelines)
```

### Modules

| Module | Purpose |
|--------|---------|
| `VisionTypes.codex` | Signal, Hold, Response, Person, Domain types |
| `VisionSignal.codex` | Signal lifecycle, weight semantics, state machine |
| `VisionCascade.codex` | Cascade flow, routing, hold management, nudging |
| `VisionOrg.codex` | Org graph, reporting chains, domain ownership |
| `VisionPortfolio.codex` | Projects, milestones, risks, dependencies |
| `VisionGraph.codex` | Dependency graph, cascade analysis, critical path |
| `VisionStore.codex` | Append-only publication store |
| `VisionServer.codex` | JSON API |
| `VisionTheme.codex` | UI theme (extends dark-gold with signal weight colors) |
| `SignalPage.codex` | Signal feed + cascade view |
| `PortfolioPage.codex` | Executive portfolio dashboard |
| `TimelinePage.codex` | Gantt-like timeline with cascade overlay |
| `OrgPage.codex` | Org graph with signal flow visualization |

### Signal Weight Colors

| Weight | Color | Meaning |
|--------|-------|---------|
| Musing | `#8b949e` (muted grey) | Background thought |
| Suggestion | `#58a6ff` (blue) | Worth investigating |
| Directive | `#ffd700` (gold) | Make it happen |
| Mandate | `#f85149` (red) | Non-negotiable, now |

---

## The Volunteer Board

Not every signal is strategy. A director wants to host a BBQ for
the team. A VP wants someone to represent the company at a
conference. An IC has an idea for a hack day project and needs
three people. These are **Open Signals** — signals where the
response isn't pushed down through the hierarchy but offered up
by volunteers.

### Open Signal Flow

```
Signal enters (author: Dir Engineering, weight: Musing)
  Content: "Hamburger barbecue for lunch Friday. Who's in?"
  Type: Open (volunteer)
    |
    v
Signal appears in team feed with volunteer slots
    |
    +--> "Grill master" — Alice volunteers
    +--> "Bring sides" — Bob, Carol volunteer
    +--> "Setup/cleanup" — Dave volunteers
    |
    v
Director sees slots filling. No assignments needed.
```

### Open Signal Types

| Type | Example | Volunteers Needed |
|------|---------|-------------------|
| **Event** | Team BBQ, offsite, celebration | RSVPs + task volunteers |
| **Opportunity** | Conference talk, blog post, customer visit | 1-3 people |
| **Hack** | Prototype idea, research spike, tool improvement | Team of N |
| **Ask** | "Anyone know about X?" "Who's dealt with Y?" | Knowledge holders |

### How It Works

An open signal has **slots** — named roles that people can
volunteer for. Slots can have a capacity (1 person, 3 people,
unlimited). When someone volunteers, they appear on the slot.
The author can accept, waitlist, or adjust.

```
OpenSlot = record {
  os-name : Text,
  os-capacity : Integer,
  os-volunteers : List Text,
  os-filled : Boolean
}
```

Open signals don't cascade. They sit in the team's feed and
people opt in. No nudging, no deadlines (unless the author
sets one), no escalation. The accountability is social — if
you volunteered to bring sides, your team knows.

This is the same system handling "I think resumes suck" and
"who wants burgers Friday" — the difference is the weight,
the type, and whether the flow is push-down or volunteer-up.
The infrastructure serves both.

---

## Why This Works

The insight is that most corporate communication failures are
routing failures, not communication failures. The information
exists — the CEO had the idea, the team had the expertise to
respond. What was missing was the pipe: a structured channel
that carries the idea to the right people, sets expectations
about urgency, and carries the response back without requiring
anyone to schedule a meeting, prepare a deck, or send a
"per my last email."

Vision is that pipe. The ideas flow down through people who
decide whether to pass or pick up. The responses flow up through
people who add context at each level. The record is permanent,
the accountability is clear, and the meetings are optional.

The formality doesn't disappear. It becomes infrastructure.
