# FRET (NASA Formal Requirements Elicitation Tool) -- survey and integration assessment

**Provenance (red, 2026-08-27, at Damian's direction).** Repository facts
verified against `github.com/NASA-SW-VnV/fret` that day: Apache 2.0, version
3.1, ~460 stars, ~1,399 commits, actively maintained by NASA (contacts Andreas
Katis, Anastasia Mavridou), Electron/JavaScript application, contributions
under NASA CLAs. The technical description of FRETISH and the translation
pipeline below is from the assessing model's training knowledge of FRET's
published papers and manual, not re-verified against the current release:
**verify any sentence here against their documentation before building on it**
(the integration options are shaped so that none of them depends on a detail
that could drift).

## What FRET is

FRET is NASA's tool for writing system requirements in a restricted natural
language called FRETISH and compiling them into formal temporal logic with
unambiguous semantics. A FRETISH requirement has up to six fields -- scope,
condition, component, shall, timing, response -- e.g. "In landing mode, when
ice is detected, the autopilot shall, within 2 seconds, disengage." Each
field combination maps to a documented semantic template, and FRET generates:

- **Future-time and past-time metric temporal logic** formulas for the
  requirement, with the semantics explained back to the author through
  diagrams and a simulator (so the author confirms the formalization means
  what they meant).
- **Exports to verification back ends**: CoCoSpec/Lustre contracts for
  model checking (CoCoSim/Kind2 family), Copilot runtime monitors, SMV.
- **Test obligations** with coverage metrics, and consistency checks across
  a requirement set.

The load-bearing idea: a requirement written by a domain expert in
near-English becomes a formal artifact a machine can check, without the
expert writing logic, and with a reflect-back step so the formalization
cannot silently mean something else.

## Why this rhymes with Cobblestone (and where it does not)

| FRET has | We have | The relationship |
|---|---|---|
| FRETISH: restricted NL with compiled semantics | The parental-policy prose compiler is DESIGNED and unbuilt (`TheLongFlight.md` Ascent II; diagnostics CDX5001-5007 reserved; the Clarifier reflect-back step) | **FRET is a decade of prior art for exactly the hard part of Ascent II** -- a template grammar whose every sentence has one meaning, plus the confirm-back loop. Study before designing ours. |
| Requirements become proof obligations | `Claim`/`Proof` syntax, structural induction, the static bounds prover (`DevelopersGuide.md`) | A FRET-formalized requirement could compile to a Codex claim; our prover discharges or abstains honestly. |
| Copilot runtime monitors for properties not proven statically | Effects, capabilities, linear types, `punctual`/`bounded` discharge whole property classes AT COMPILE TIME | Our philosophy inverts FRET's default: what their flow monitors at runtime, ours refuses to compile. The residue that genuinely needs runtime observation is where a generated monitor would fit. |
| Requirement IDs traced to verification artifacts | The evidence plug: every catalog row classified BY-CONSTRUCTION / MECHANISM / DEPLOYMENT / ORGANIZATIONAL, signed per build (`ComplianceEvidence.md`) | The natural join, and the business case: a customer's FRETISH requirement set imported as catalog rows, each discharged against our mechanisms, re-verified every build. |
| Metric temporal logic ("within N seconds of X, Y") | `punctual` proves per-function WCET; nothing in the language states cross-component temporal properties | **The honest gap.** We have no temporal-logic layer. FRET's timing fields express system-level obligations our type system does not; anything imported with real temporal content lands as MECHANISM (a runtime check we generate) or ORGANIZATIONAL, not BY-CONSTRUCTION. |

## Integration options, cheapest first

1. **FRETISH as an authoring front end for the evidence catalog (recommended
   first step, no FRET code in our tree).** FRET is an Electron app a human
   runs outside; its requirement sets export as data. An import step maps
   each requirement to evidence-catalog rows and the package then carries
   the customer's own formalized requirement beside the mechanism that
   satisfies it and the classification of the residue. This is the
   "compliance evidence that rebuilds itself" story with NASA-credible
   formal semantics attached to the requirement end, and it strengthens
   the open notified-body question (`ComplianceEvidence.md`): supporting
   documentation whose requirements half arrives already formalized.
   Consistent with the outside-code discipline -- we take their ARTIFACTS
   as data, never their runtime into our stack.

2. **A `fret` plug that compiles imported requirements to Codex.** Consumes
   the exported formal form and emits, per requirement, either a Codex
   `Claim` (where the property is in our prover's reach) or a generated
   runtime monitor in Codex (where it is genuinely temporal), wired to the
   effect that observes the subject. Medium cost; requires pinning their
   export format's semantics against their manual first (the provenance
   caveat above becomes load-bearing here). The plug pattern is the
   project's standard shape for a foreign format, and the parser would be
   written under the Track D discipline for bytes we did not produce.

3. **Absorb the grammar design into Ascent II, not the tool.** When the
   parental-policy prose compiler is taken up, FRETISH's field structure
   (scope, condition, timing, response), its template-to-semantics mapping,
   and its simulator-style reflect-back are the studied answers to the
   three problems that design names (compile prose to a PolicyFact, confirm
   the understanding back to the author, refuse ambiguity). Zero code
   moves; the accommodation is reading their papers before writing our
   grammar.

4. **Not recommended: embedding or shipping FRET itself.** Electron and a
   JavaScript stack inside this tree is exactly the outside-toolchain
   dependency the project exists to exclude, and nothing in options 1-3
   needs it. FRET stays a tool a requirements author runs on their own
   machine, like a text editor.

Apache 2.0 permits everything above; the project's own discipline (no
outside code in the stack, artifacts as data, credit what is studied) is
the tighter constraint and options 1-3 satisfy it.

## What would make this real

One worked example, sized to a day: take an ETSI EN 303 645 provision the
evidence catalog already discharges BY-CONSTRUCTION, write it in FRETISH,
run FRET's formalization, and carry the formal artifact through the
evidence plug beside the existing mechanism row. That measures the whole
pipe at depth one -- authoring, export, import, package -- and produces
the artifact to put in front of a compliance consultant, which is the
question the business section of the landing page is already inviting.
Nothing in the tree moves until that example earns it.
