Ha. You caught it before I could. "Consent of the governed" fails immediately:

```
ERROR [NP-1]: "governed" — referent ambiguity.
ERROR [NP-1]: "consent" — TYPE-NAME not declared.
ERROR [QE-1]: What threshold of consent? 51%? 
              Unanimous? Supermajority?
ERROR [FD-2]: "of the governed" — no gives-clause. 
              Consent produces... what legal effect?
```

So let's bootstrap it properly. That's actually the whole problem in one place — the foundational axiom of democratic legitimacy has never been formally specified. Let's fix that first, then build the compiler plan on top of it.

---

# The CPL Constitutionalism Project
## A Formal Plan for Compiling Law

---

## Stage 0: Bootstrap — Define "Consent of the Governed"

Before the compiler can run on any law, it needs its own axiom layer. Every formal system has one. Euclid had five postulates. We need ours.

```
Chapter: Axioms of Legitimate Law

  We say:

    A Citizen is a record containing:
      - identity    : UniqueIdentifier
      - jurisdiction : Jurisdiction
      - status      : CitizenStatus

    CitizenStatus is one of:
      - Eligible
      - Ineligible containing (- reason : DisqualificationReason)

    A Jurisdiction is a record containing:
      - name      : Text
      - territory : TerritoryDefinition
      - citizens  : List of Citizen
        such that every citizen in citizens has jurisdiction 
        equal to this Jurisdiction

    A Proposition is a record containing:
      - text      : CPLStatement
      - domain    : JurisdictionScope
      - effect    : LegalEffect

    A Vote is one of:
      - Assent  containing (- citizen : Citizen) (- proposition : Proposition)
      - Dissent containing (- citizen : Citizen) (- proposition : Proposition)
      - Abstain containing (- citizen : Citizen) (- proposition : Proposition)

    A ConsentThreshold is one of:
      - SimpleMajority    -- more than 50% of votes cast are Assent
      - Supermajority     containing (- numerator : Integer) 
                                     (- denominator : Integer)
                                     where numerator is greater than denominator
                                     divided by 2
      - Unanimous         -- every Eligible Citizen votes Assent
      - Quorum            containing (- minimum : Integer)
                                     (- threshold : ConsentThreshold)
                                     where minimum is at least 1

    A ConsentRecord is a record containing:
      - proposition  : Proposition
      - votes        : List of Vote
      - threshold    : ConsentThreshold
      - result       : ConsentResult
        such that result equals the evaluation of votes 
        against threshold

    ConsentResult is one of:
      - Consented  containing (- record : ConsentRecord)
      - Rejected   containing (- record : ConsentRecord)
      - Void       containing (- reason : VoidReason)

    VoidReason is one of:
      - QuorumNotMet
      - ProcessViolated containing (- violation : ProcessViolation)
      - PropositionMalformed containing (- errors : List of CPLError)
```

Now "consent of the governed" becomes a function call with a declared type:

```
    To establish (proposition : Proposition) 
    as (law : Law) 
    within (jurisdiction : Jurisdiction)
    gives a ConsentResult,
    failing if proposition.text fails CPL validation,
    failing if the number of Eligible Citizens in jurisdiction 
              is less than 1.

    A Law is valid if and only if:
      the ConsentResult for law equals Consented
      and the ConsentRecord for law is available for inspection
      by every Citizen in law.domain.
```

The bootstrap is now typed. "Consent of the governed" is `ConsentResult.Consented` — a value, not a slogan.

---

## Stage 1: The Compiler Architecture

```
Chapter: CPL Law Compiler

  The compiler takes a natural-language legal document as input
  and produces one of:

    - a CompileResult containing a validated CPLDocument, or
    - a CompileResult containing a List of CPLError

  The compiler runs in exactly five passes, in order.
  Each pass either succeeds or halts with errors.
  No pass is skipped.
  No pass is re-ordered.
```---

## Stage 2: The Five Passes — Specified

### Pass 1 — Lexical Audit

Every word in the document is checked against the CPL lexicon. This pass produces no CPL output — only a flagged corpus ready for Pass 2.

```
Pass 1 produces for every sentence:
  - SentenceClass : one of (LoadBearing | Commentary | Ambiguous)
  - BannedWords   : List of (word : Text, position : DocumentPosition)
  - UndefinedTerms: List of (term : Text, position : DocumentPosition)

A sentence is LoadBearing if it contains a CPL keyword.
A sentence is Commentary if it contains no CPL keywords.
A sentence is Ambiguous if it could be either,
  such that the human review queue receives every Ambiguous sentence
  before Pass 2 begins.
```

**The human review queue is not optional.** The compiler cannot fully automate Pass 1 on legacy text. A human — or a supervised model with a human confirming — must classify every `Ambiguous` sentence. This is the only human gate in the process. Everything after it is mechanical.

---

### Pass 2 — Type Declaration Extraction

Every noun that carries legal weight must become a TYPE-NAME. This is the hardest pass. It is where most of the intellectual work lives.

```
Pass 2 rules:

  Rule 2.1: Every noun used in a LoadBearing sentence that is not
  already a CPL primitive (Integer, Text, Boolean) must be declared
  as a TYPE-NAME before first use or flagged UNDEFINED.

  Rule 2.2: Every adjective modifying a legal noun must be either:
    (a) absorbed into the TYPE-NAME as a variant
        ("cruel punishment" → CruelPunishment : Punishment)
    (b) expressed as a Constraint on the type
        ("excessive bail" → bail : Amount, such that bail 
         exceeds BaselineBail for the offense)
    (c) flagged UNDECIDABLE and sent to the Amendment Queue.

  Rule 2.3: The Amendment Queue is a formal record.
  Every UNDECIDABLE produces one entry:
    - the original text
    - the two or more competing CPL interpretations
    - the ConsentRecord required to resolve it
```

The Amendment Queue is the compiler's formal output for genuine ambiguity. It is not a bug report. It is a **work order for democratic process** — each entry requires a new ConsentRecord to resolve. This is how you fix a type error in the Constitution without a civil war: you make the error visible, name the competing interpretations precisely, and run the consent process on a well-formed proposition.

---

### Pass 3 — Constraint Resolution

Every constraint in the document must have a bound.

```
Pass 3 rules:

  Rule 3.1: Every constraint must name:
    - its subject (the NP being constrained)
    - its comparator (from the CPL COMPARATOR set)
    - its threshold (a QE — Exact, Bounded, or Parameterized)
    - its measuring authority (who evaluates the constraint at runtime)

  Rule 3.2: "Measuring authority" is a TYPE-NAME.
  It must be declared in the document.
  An authority with no declared process for measurement is 
  flagged ORACLE — the worst error class.

  ORACLE errors are the source of judicial power creep.
  "Cruel and unusual" with SCOTUS as measuring authority and
  no declared measurement process is an ORACLE.
  The compiler flags it. The amendment queue receives it.
  It cannot be silently compiled.
```

---

### Pass 4 — Consent Binding

Every legal effect — every right granted, every power authorized, every prohibition imposed — must trace to a `ConsentRecord`.

```
Pass 4 rules:

  Rule 4.1: Every LegalEffect has a type:
    LegalEffect is one of:
      - Right     containing (- holder : CitizenClass) 
                             (- content : Permission)
      - Power     containing (- holder : GovernmentEntity)
                             (- scope  : JurisdictionScope)
      - Prohibition containing (- subject : CitizenClass)
                               (- conduct : ConductType)
      - Obligation  containing (- subject : CitizenClass)
                               (- conduct : ConductType)

  Rule 4.2: Every LegalEffect must have a ConsentRecord
  with result equal to Consented.
  A LegalEffect without a ConsentRecord is flagged UNRATIFIED.
  An UNRATIFIED LegalEffect has no legal force in the compiled document.

  Rule 4.3: ConsentRecords are content-addressed.
  They are immutable once recorded.
  A superseding ConsentRecord does not delete the prior one.
  The history is permanent.
  (This is the Codex repository model applied to law.)
```

---

### Pass 5 — Circular Reference Detection

```
Pass 5 rules:

  Rule 5.1: No TYPE-NAME may be defined in terms of itself
  without an explicit fixed-point declaration.

  Rule 5.2: No authority may be the measuring authority
  for constraints on its own powers.
  (SCOTUS cannot be the sole measuring authority for 
   the scope of judicial power. This is a circular reference.
   It requires an external authority or a fixed definition.)

  Rule 5.3: No law may use "this document" as a constraint
  without a grounding axiom that resolves the self-reference.
```

Pass 5 is where Marbury v. Madison gets caught. SCOTUS inserting itself as the final interpreter of its own scope is `Rule 5.2` — a circular authority reference. In a CPL legal system, that power must be declared in the document and ratified with a ConsentRecord, not discovered by judicial opinion.

---

## Stage 3: The Amendment Queue as Democratic Compiler Output

Here is the most important reframe. In the current system, ambiguities in law are resolved by:

- Courts (unelected, life-tenure, small group)
- Executive agencies (unelected, politically appointed)
- Legislative inaction (ambiguity persists indefinitely)

In the CPL system, every ambiguity produces a **well-formed amendment proposition** — a CPL statement that resolves exactly one type error, with the competing interpretations stated formally, submitted to the consent process.

```
An AmendmentProposition is a record containing:
  - error         : CPLError
  - interpretation-A : CPLStatement
  - interpretation-B : CPLStatement
  - difference    : ProofOfDivergence
    such that difference demonstrates at least one case
    where interpretation-A and interpretation-B 
    produce different LegalEffects for the same input

  A valid AmendmentProposition must have difference
  containing at least one concrete case.
  A proposition that cannot produce a concrete divergence case
  is not a genuine ambiguity — it is a PROSE-COMMENT
  and is removed from the queue.
```

This eliminates culture war legislation. If you cannot produce a concrete case where your interpretation differs from the other one in its legal effect on a real Citizen, you do not have a legal dispute. You have a rhetorical preference. The compiler rejects it.

---

## Stage 4: The Proof of Concept — What We'd Actually Build

A working proof of concept needs exactly four components:

**1. The CPL Axiom Library** — the bootstrap types from Stage 0. `Citizen`, `Jurisdiction`, `ConsentRecord`, `LegalEffect`, `AmendmentProposition`. These are the prelude that loads before any document compiles.

**2. The Lexical Auditor** — a model (Opus is appropriate) that reads legal text and classifies sentences as `LoadBearing`, `Commentary`, or `Ambiguous`. Outputs a structured corpus.

**3. The Type Extractor** — takes LoadBearing sentences, extracts TYPE-NAME candidates, matches against the Axiom Library, flags gaps. This is where the Amendment Queue gets populated.

**4. The Error Report Renderer** — produces two outputs from every compile run: a `CPLDocument` for everything that resolved cleanly, and a structured `ErrorReport` for everything that didn't — with every error linked to its Amendment Queue entry, its competing interpretations, and the ConsentRecord that would resolve it.

The Constitution would compile to roughly: 30% clean CPLDocument, 70% ErrorReport. That ratio is the honest answer to two centuries of constitutional law.

---

## The Deepest Implication

Every generation rewrites constitutional meaning through courts because the original document has unresolvable type errors and no formal amendment process for fixing them incrementally. The result is that enormous questions of rights and powers get decided by 5-4 votes of nine people, with no formal ConsentRecord, no declared measuring authority, and no proof of divergence.

CPL law doesn't make those decisions for you. It makes the decisions **visible, typed, and formally routable to consent**. The compiler's job is not to govern. It is to ensure that governing is done with sentences that mean one thing.

That is the proof of concept. The Constitution is the test corpus. The Amendment Queue is the output. Democratic consent is the runtime.