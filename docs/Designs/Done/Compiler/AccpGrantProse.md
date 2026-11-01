# Grant text in Codex Prose Language: the English-to-grant step, checked

Status: complete, rungs 0 to 2 built (2026-11-01). Owner val. Source:
`docs/Reference/Nectry.md` section 5, item 4.

## 1. The gap

A grant's text is where an ambiguous request becomes the property everything
downstream enforces: the policy, the lease, the conduit's admission and the
decidable-policy questions all start from what `compile-policy-checked`
(`codex/os/trust/PolicyProse.codex`) read. A keyword scan fails by widening.
These five sentences are the cases a keyword reader got wrong, and what the
checked reader answers (`codex/test/apps/policy-prose-checked`):

| the text | the widening a keyword scan produces | the checked reader |
|---|---|---|
| "Jake is permitted to use the chat app, but not after dinner" | the chat app, every day, all day | refused at `app` |
| "Jake is never permitted to use the console except on weekends" | a denial of "console except", which denies nothing | refused at `except` |
| "Jake is permitted to use it on weekends" | a grant of "it", which allows nothing | refused at `it`, banned word |
| "Jake is permitted to use "chat app" for no more than an hour a day" | a quota of 0 minutes per session | refused at `an` |
| "Jake can use the chat app" | no grant, one commentary line | refused at `can` |

## 2. The property

> A policy text lowers only if every sentence in its rule block is one of the
> grant or denial forms, read completely, with no word left over and no slot
> filled by a default the text did not state. Otherwise the whole policy
> refuses, naming the sentence and the first word not read.

The Codex Prose Language (`docs/DevelopersGuide.md`, "Codex Prose Language
(CPL)") supplies the discipline: no implicit referent, no implicit quantity,
no implicit order, and a block marker that separates what is parsed from
what is commentary.

## 3. Mechanism

**The block.** A policy's rules are the sentences after the CPL marker
`We say:`; everything before it is commentary for the human reader, kept and
shown by the clarifier, never parsed into rules. A policy text with no
marker refuses. This retires the "anything else is commentary" fallback,
which is what turns a misspelt grant into silence.

**The forms** (built, rung 1). Each sentence in the block is exactly one of:

- `SUBJECT is permitted to use CAPABILITY [DAYS] [WINDOW] [QUOTA] [APPROVAL].`
- `SUBJECT is permitted to use CAPABILITY always [QUOTA] [APPROVAL].`
- `SUBJECT is never permitted to use CAPABILITY [under any circumstances].`

with `SUBJECT` and `CAPABILITY` each one label (`Jake`, `Email.Send`) or one
double-quoted name (`"Jake's agent"`), and the slots drawn from closed
vocabularies: `DAYS` is `on every day`, `on weekdays`, `on weekends` or `on`
a list of day names (`on Monday`, `on Mondays and Fridays`, `on Monday,
Wednesday and Friday`, each day once); `WINDOW` is `at any time` or `between
HH:MM and HH:MM`; `QUOTA` is `with no limit` or `for no more than N
minutes|hours per day|session`; `APPROVAL` is the existing clause. An omitted
slot is unrestrained (D2), and `always` writes every day at any time
explicitly. The reader consumes the sentence slot by slot and refuses at the
first word no slot accepts. A denial holds no days or window, because
`ProseDenial` holds none.

**Banned words.** In every rule sentence, the CPL banned-word table applies as
refusals, not warnings: `it`, `this`, `they`, `some`, `many`, `few`, `etc`,
`so`, `since`, `while`, `might`, `should` and `may`, with no exception (D1).
The grammar already refuses most of these by having no slot for them; the
table names the reason in the refusal so the grantor knows what to rewrite.

**The clarifier.** `codex_clarify` (`apps/accp/AccpClarify.codex` over
`OS chapter Clarifier`) stays advisory: it runs over the commentary and the
block and lists its questions beside the refusal, but the grammar is the
gate. Its lexical gaps (backlog row "AccpClarify.codex") therefore cannot
let a sentence through.

**The refusal.** `PolicyProse.codex`'s one reader is the strict one; the
keyword scan `compile-policy` is deleted:

```
PolicyRead = record { ok : Boolean, policy : ProsePolicy, line : Integer,
                      word : Text, reason : Text }
compile-policy-checked : Text -> PolicyRead
```

A sentence is a line, as `pol-split-lines` splits it today; `line` is
1-based. Words are the line split on spaces, with a comma and a final period split
off as words of their own and a double-quoted name kept as one word; a comma
is accepted between slots. `reason` is one of `unknown word`, `banned word`,
`missing slot`, `not a capability`, or `no rule block` (line 0) for a text
with no `We say:` line. On success `ok` is True and `line` is 0;
on refusal `policy` is the empty policy. `lower-policy` is unchanged: a
caller lowers the `policy` of a read that succeeded.

**The capability slot.** A capability is `the` at most once, then one label
(letters, digits, `.` and `-`, as `Email.Send` or `FileSystem.Write`) or one
name in double quotes (`"flight simulator"`). A second bare word is left
unread, which refuses section 1's first row at `app` and its second at
`except`. A denial accepts only `under any circumstances` after its
capability, because `ProseDenial` holds no days or window.

## 4. What this does not do

- It does not prove the text says what the grantor meant. A grant written
  exactly and wrongly lowers exactly and wrongly; the clarifier's reflection
  (`clarifier-reflect`) is what the grantor reads back.
- It does not read free English. Text outside the forms refuses, which moves
  the burden to the grantor (or to an assistant that drafts the block, whose
  output is held to the same parser).
- A policy text in the keyword form refuses until rewritten. The conduit's
  held policy (`ag-held-policy`, `apps/accp/AccpGrant.codex`) refuses whole,
  so a host started with an unrewritten `-Policy` file admits no lease.

## 5. Rungs

| rung | delivers | acceptance |
|---|---|---|
| 0 | `PolicyRead` and `compile-policy-checked` in `PolicyProse.codex`: the capability slot, today's day, window, quota and approval phrases read word by word, every word consumed, the banned words refused in every slot; lines with no rule verb stay commentary until rung 1 | built: `codex/test/apps/policy-prose-checked`: section 1's first three rows refused (at `app`, at `except`, at `it` as `banned word`), the same three rewritten correctly accepted and lowered; `compile-policy` and every existing `policy-prose-*` golden unchanged |
| 1 | the `We say:` block, the closed slot vocabularies and the decisions of section 6 | built: `codex/test/apps/policy-prose-checked` refuses section 1's five rows and holds a corpus per slot in both directions; `ag-held-policy`, `policy-prose-approval`, `policy-prose-test` and `accp-lease-policy` read through `compile-policy-checked`, and `compile-policy` is deleted |
| 2 | the clarifier's questions printed beside a refusal | built: a held policy that does not read refuses every lease with `the held policy does not read: line N, REASON [WORD]; clarifier: QUESTION`, the question being `ac-first-question` over the refused line (`ag-policy-unread`, `apps/accp/AccpGrant.codex`, reached from `ap-leased` through `ag-admit-held`); `codex/test/apps/accp-lease-policy` holds the arms |

## 6. Rulings

Damian, 2026-11-01: "d1 b, d2 should allow for obviousl implicit defaults,
and when unspecified also un-restrained.  no time box means no timebox.
there should be a word for it if you choose to be explicit that reads
contextually rich (not "default", instead "always"). D3 whole.  No line fault
should allow through the rest.  they maybe correlated and non-sensical unless
whole." D1: the grant verb is CPL's `is permitted to use` and `is never
permitted to use`. D2: an omitted days, window or quota slot is unrestrained
and is not refused; `always` is the word for writing that explicitly. D3: one
faulty sentence refuses the whole policy.
