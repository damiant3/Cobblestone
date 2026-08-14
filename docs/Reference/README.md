# Reference Library

External documentation, and our briefs of it. Everything here states what
somebody outside this project says: a specification, a datasheet, a paper, a
standard, another project's code. A brief belongs here when it carries a
`Source` line and adds no claim of ours.

Our own output does not live here, whatever its subject. A measurement, a
mapping of Codex onto a standard, a position doc or a proposal is our work
and goes to `docs/Designs/`.

## What is in here

| | |
|---|---|
| Papers and specs | The original PDF or text, archived (see Convention below) |
| Service model notes | What a device's spec guarantees, with a section number per claim: `xHCI_ServiceModel_Notes.md`, `E1000_ServiceModel_Notes.md` |
| Spec summaries | `UEFI_*_Summary.md`, `BrotliFormat.md`, `SIMD_Architecture_References.md` |
| Surveys of external projects | See the table at the bottom |
| Archived codebases | `AiComp/`, with its own `PROVENANCE.md` |
| Readings of external work | `Chlipala-StructureAndGuarantees.md`, `CornellReview.md`, `MiddleEndLiterature.md`, `IRISA_Research_Harvest.md`, `Optimal_Bounds_Open_Addressing.md`, `AMD_SVM_Hypervisor_Patterns.md` |
| Somebody else's reading of us | `CopilotWebCritique.md` |
| `IoT/` | Briefs of the IoT standards, protocols and parts. Has its own README |

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
| 2026 | Cesario, Zakhour, Weisenburger, Salvaneschi -- *Versioned E-Graphs* (PLDI) | Verifier design |

## Archived codebases

Same rationale as the papers -- an unlicensed, single-author repo can go dark
without notice, and a citation without a local copy is a hyperlink waiting to
break. Each archive carries a `PROVENANCE.md` stating where it came from and
**what we are and are not permitted to do with it. Read that first.**

| Archive | What it is | Our reading of it |
|---|---|---|
| `AiComp/` | fiigii/ai-comp -- an optimizing compiler (HIR→LIR→MIR→VLIW, 16 passes, 5 analyses) written for Anthropic's published performance take-home. **Unlicensed: reimplement clean-room, do not copy.** | `AiComp/OPPORTUNITIES.md` -- the middle end Codex does not have, and what it would actually cost to get one. |

## Surveys of external projects

A survey is a reading of somebody else's published documentation. It carries
no local copy, so every citation in one is a hyperlink that can break, and it
states up front which of its claims are verified. Promote a survey to an
archive above if the project turns out to be load-bearing.

| Survey | Project | What it argues |
|---|---|---|
| `HaleSurvey.md` | hale-lang/hale (Apache-2.0), a concurrent systems language with per-component arenas and a model-checked runtime | Four independent arrivals at our own decisions, and three instruments we do not have: negative-controlled concurrency models, compile-time topology checking, and an allocation budget. Nothing in it is verified against their code. |
