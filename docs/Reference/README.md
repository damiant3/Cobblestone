# Reference Library

External research papers archived in the repo so they survive paywalls,
URL rot, and editorial revisions.

## Convention

Filename: `YYYY-AuthorList-Short-Title-Venue.pdf`.

When an agent reads a paper that post-dates its training and decides
the paper is load-bearing for Codex's design space, it stores the
**original PDF** here (verbatim, not a transcription) and adds a
citation block in the most relevant design doc:

- A 1–3 sentence summary of the contribution
- Why it's relevant to that doc's design (concrete fit, not vague)
- A pointer to the local copy in `docs/Reference/`

Don't summarize-and-discard. The PDF is the artifact that compiles
to the same bits the citation refers to; without it the citation
is a hyperlink waiting to break.

## Index

| Year | Citation | Cited from |
|------|----------|------------|
| 2026 | Cesario, Zakhour, Weisenburger, Salvaneschi — *Versioned E-Graphs* (PLDI) | `docs/Designs/Codex.OS/Verifier.md` |
