---
name: broadcast
description: Put a short prompt in front of every agent in the fleet at once, through the AgentGrid coordinator. Run when the user invokes /broadcast. The argument is usually a REFERENCE to something already in this conversation ("that question you have", "the deck finding") rather than the text to send -- resolve it, compose the prompt, and show it before sending.
shell: powershell
---

A broadcast lands as a typed line in every other agent's terminal. It
interrupts four sessions mid-thought, so the bar is that it is worth
four interruptions, and the format is that it fits in one.

## What the argument is

**Usually a reference, not the message.** `/broadcast that question you
have` means: find the question, write it properly, send it. `/broadcast
the deck-headroom finding` means: find the finding, compress it, send it.

Resolve it from the conversation. If two things in the conversation fit
the reference, ask which -- do not broadcast the wrong one to four
agents. If the argument IS the message, still rewrite it to the format
below; a raw paste is almost never in it.

If `/broadcast` arrives with no argument at all, the most recent open
question or unreported finding in the conversation is what was meant.
Say which one you picked before sending.

## Step 1 -- Pick the form. There are two and only two.

**A QUESTION.** One question, answerable in one line, that you actually
need an answer to from someone other than the user.

```
Q: Does any lane depend on build/plug-ports.ps1 staying hand-written?
   Answer yes+why or no. -- fester
```

**A POINTER.** One line of what happened plus where the detail is. Use
this whenever the thing itself does not fit in three lines.

```
Deck-headroom now measures every codex/build chapter -- a generator CL
can go red without touching the seed. Detail in your inbox from fester.
-- fester
```

The pointer form is the reason this skill exists. The detail goes in a
normal fleet message first, then the broadcast says one line and names
it. **A broadcast is never the container for the detail.**

## Step 2 -- Cut it to the format

- **Three lines, hard.** If it needs four, it is a pointer.
- **Say what you want back, or say that you want nothing.** "No reply
  needed" is a real and useful ending. A broadcast that leaves four
  agents guessing whether they owe an answer costs four replies.
- **Name yourself at the end.** The terminal line carries the sender,
  the inbox copy carries it too, and it still reads better signed.
- **No status.** "I landed X" is not a broadcast unless another lane's
  work changes because of it.
- Plain ASCII, `--` never an em-dash, no markdown -- it is typed into a
  terminal, not rendered.

## Step 3 -- Send the detail first, if there is any

For the pointer form, the detail is a normal per-agent message and it
must exist BEFORE the broadcast names it, or four agents go looking for
something that is not there yet. Send it to the specific agents who need
it, or to `fleet` if it is genuinely everyone's.

## Step 4 -- Send

Write ONE file into your own `outbox/`, `to` = `fleet`. AgentGrid routes
it: a `[fleet message from <you>]` line into every other agent's
terminal, and a durable copy into every inbox. **A broadcast never
echoes back to you.**

```powershell
$mbox = (Get-Content .agentgrid -Raw | ConvertFrom-Json).coordinationDir
$body = @{ to = 'fleet'; text = '<the three lines>' } | ConvertTo-Json -Depth 3
Set-Content "$mbox\outbox\<short-slug>.json" -Value $body -Encoding utf8
```

**Write it into YOUR outbox and let AgentGrid route it.** Writing
straight into another agent's `inbox/` looks like it works and is not
the same thing: the file lands, no terminal line is ever typed, and the
addressee sees it at their next init instead of now. Measured
2026-08-14 -- two replies to a blocked agent sat unread in their inbox
for a quarter of an hour that way while they waited, and the other agent
was hand-dropping into mine the same afternoon.

**Confirm the send from `outbox/sent/`, which is the receipt.** The file
leaves `outbox/` for `outbox/sent/` within a few seconds. If it is still
sitting there, AgentGrid is not running and nobody got it -- say so
rather than assuming delivery.

That check and no other, because **the failure it catches is a sender
failure**: both agents that day believed they had answered, and neither
was wrong about what they wrote. Receiver-side tells cannot help you
here -- you are not the receiver. The schema (`at`, `from`, `text` is
what AgentGrid writes) diagnoses somebody else's message and is evidence
rather than proof; the filename is not a tell at all, since a hand-drop
can carry the routed naming convention exactly and one that day did.

## Step 5 -- Show the user what went out

Print the exact text sent. It reached four other people; the user is
entitled to see it verbatim rather than a summary of it.

## Answering somebody else's broadcast

Reply to the SENDER, not to `fleet`. Four agents each broadcasting an
answer is four interruptions per answer, and only the asker wanted it.
Delete the inbox copy once absorbed; the deletion is the acknowledgement.

## What does not go in a broadcast

- Anything only the user can decide. That is a conversation with the
  user, not four terminals.
- A conversation. The channel is a notification channel; two rounds
  means it should have been a message to one agent.
- The detail itself. See step 1.
- A red gate you are already fixing, unless someone is blocked behind
  it. If someone IS blocked, that is exactly what this is for.

## Where the record goes

The broadcast is the notification and it is not a record of anything.
The durable fact goes in the doc that owns the subject the moment it is
verified -- a reference doc, the owning design, `LESSONS.md`, or the
relevant backlog. `docs/PM/CurrentPlan.md` takes cross-lane open work.
The workplans do not: they are empty by design and hold only the current
session's lane state.
