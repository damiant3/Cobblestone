---
name: clarify
description: Find what a piece of English leaves unbound (a pronoun with no named antecedent, an unbound quantity, a comparison with no baseline, an unstated domain or time) by calling the cobblestone MCP tool codex_clarify, then resolve the gaps one question at a time, referent first. Run when the user invokes /clarify. The argument is the text, or a reference to text in this conversation ("my last message", "that draft", "the plan"); resolve the reference and clarify the text it names.
shell: powershell
---

## Step 1 -- Resolve the text

The argument is either the text itself or a pointer to text already in the
conversation. Resolve a pointer to the exact words it names, and clarify
those words, not a paraphrase. A second word after the text naming a
register (`child`, `casual`, `technical`, `formal`) is passed through;
otherwise the tool detects the register.

## Step 2 -- Call the tool

Call `mcp__cobblestone__codex_clarify` with `text` and, when given,
`register`. The text ceiling is 16384 characters: clarify a longer text by
paragraphs and say so. When the tool is not listed, load it with ToolSearch
(`select:mcp__cobblestone__codex_clarify`); when the cobblestone server
offers no `codex_clarify`, say that its installed bundle predates the tool
and stop. A JSON-RPC error is reported with its message; never substitute
your own scan for a failed call.

## Step 3 -- Read the answer

The answer holds `status` (`ok`), `register` and `register_source`
(`requested` or `detected`), `sentences`, `solid` (sentences with no flagged
word), `unresolved` (each flagged word with its kind, sentence number and
question, referents first), `unresolved_total` (the count before the 64-row
cap), `next_question` and `fully_resolved`.

The scan is lexical: it flags WORDS, not meaning. A pronoun the previous
sentence already binds is still flagged. Judge each row against the
conversation, and drop a row the context binds, naming the binding.

## Step 4 -- Act on it

- **Text the user wrote, as an instruction to you:** ask the user ONE question,
  the top row the context does not bind (referent questions first; an
  answer to a referent question can settle the other rows). After the
  answer, re-run the tool on the restated text.
- **Text you wrote (a reply, a report, a CL description):** rewrite every
  unbound row with the named value, quantifier or baseline, then run the
  tool again on the rewrite and show the user the rewrite.
- **Text quoted for study:** show the SOLID sentences, then the unresolved rows
  as a table (word, kind, sentence, question).

Nothing in the answer is a fact about the world; it is a list of words that
need a binding.
