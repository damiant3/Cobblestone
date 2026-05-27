Now I have a thorough picture. Let me write the formal grammar for Codex Prose — the load-bearing subset of English that is mathematically unambiguous.Now I have everything I need. Let me write the formal grammar specification.Damian. Here it is. The razor applied to English. I've read every word of the book, and every line of those two codex files. This grammar is derived from first principles, not patched onto existing English.

---

# Codex Prose Language (CPL) — Formal Grammar Specification

## Preamble: The Founding Axiom

English is ambiguous because it permits **implicit binding** — a word can refer to something unnamed, in an unnamed quantity, at an unnamed position in time. CPL is a proper subset of English from which all three forms of implicit binding are surgically removed. Every other English feature is inherited unchanged. The result reads like careful technical English. It is also formally unambiguous.

**The Three Axioms:**

1. **No implicit referent.** Every pronoun must be eliminated or resolved at the point of use. "It fails if it is negative" is forbidden. "The deposit fails if the amount is negative" is required.
2. **No implicit quantity.** "Some," "many," "a few," "the list" without a bound are forbidden. Every collection reference must carry either an explicit cardinality or an explicit universal/existential quantifier.
3. **No implicit order.** In multi-step procedures, each step must identify its position or its dependency on a named prior state. "Then do X, then do Y" with ambiguous nesting is forbidden.

---

## Part I: Lexical Grammar

```
-- CPL Lexical Tokens (extends English, restricts it)

IDENTIFIER    ::= [A-Za-z][A-Za-z0-9-]*
                  -- Kebab-case. Hyphens permitted. No underscores.
                  -- Every identifier introduced must be unique in its scope.

TYPE-NAME     ::= [A-Z][A-Za-z0-9]*
                  -- PascalCase. Marks a type-level name throughout.

KEYWORD       ::= one of the reserved words listed in Part III.
                  -- Keywords are not valid IDENTIFIERs.

NUMBER-LIT    ::= [0-9]+ | [0-9]+ "." [0-9]+

TEXT-LIT      ::= '"' [^"]* '"'
                  -- Double-quote delimited. No escape sequences in prose;
                  -- the notation layer handles escaping.

QUANTIFIER    ::= "all" | "every" | "each" | "no" | "exactly one"
                  | "at most one" | "at least one" | "zero or more"
                  | "one or more" | "none"
```

### Lexical Prohibition

The following English words are **banned** at the lexical level in load-bearing prose because they carry irreducible ambiguity that no subsequent grammar rule can resolve:

| Banned word | Reason | Required substitute |
|---|---|---|
| `it` | Referent ambiguity | The named value itself |
| `this` | Proximity reference | The named value itself |
| `they` | Plural referent ambiguity | The named collection + field path |
| `some` | Quantity ambiguity | A quantifier from QUANTIFIER |
| `many` | Quantity ambiguity | A quantifier or explicit bound |
| `few` | Quantity ambiguity | A quantifier or explicit bound |
| `etc.` | Incompleteness | Exhaust the list or use a type |
| `etc` | Same | Same |
| `and so on` | Same | Same |
| `so` | Causal ambiguity between "therefore" and "in order to" | `therefore` or `in order to` |
| `since` | Temporal/causal ambiguity | `because` (causal) or `after` (temporal) |
| `while` | Temporal/causal ambiguity | `during` or `at the same time as` |
| `may` | Modal ambiguity (permission vs. possibility) | `can` (possibility) or `is permitted to` (permission) |
| `might` | Same | Same |
| `should` | Normative ambiguity (obligation vs. recommendation) | `must` (obligation) or `is recommended to` (recommendation) |

---

## Part II: Phrase Grammar

### 2.1 Noun Phrase (NP)

A noun phrase in CPL names exactly one thing. It cannot be ambiguous about what it names.

```
NP ::= IDENTIFIER
     | TYPE-NAME
     | QUANTIFIER NP            -- "every Account", "exactly one Person"
     | NP "of" NP               -- "the balance of the account"
     | NP "in" NP               -- "each transaction in the history"
     | NP "named" IDENTIFIER    -- introduces a binding: "an account named a"
     | "the" NP                 -- definite: must refer to something already in scope
     | "a" NP                   -- indefinite: introduces a new binding
     | "an" NP                  -- same
     | NP "(" NP ")"            -- type application in prose: "a List of Transaction"
```

**Rule NP-1: Definiteness must match scope.** "The account" is only valid if exactly one account is in scope at that point. "A new account" introduces one. "The balance of the account" requires exactly one account in scope.

**Rule NP-2: Field access is explicit.** You cannot say "the balance" alone when the referent is a record. You must say "the balance of the account" or "account.balance" (notation form). The field access path must be complete.

### 2.2 Type Expression (TE)

```
TE ::= TYPE-NAME
     | TYPE-NAME "of" TE                -- "List of Transaction"
     | TE "or" TE                       -- union: "Account or Error"
     | TE "and" TE                      -- intersection
     | "a function from" TE "to" TE     -- function type in prose
     | "(" TE ")"
```

**Rule TE-1: Type names are always PascalCase.** This is the only syntactic distinction CPL adds to English orthography. If you see a PascalCase word, it is a type. If you see a lowercase word, it is a value binding or keyword.

### 2.3 Quantity Expression (QE)

```
QE ::= NUMBER-LIT
     | "zero"
     | IDENTIFIER                        -- refers to a bound numeric value
     | QE "plus" QE
     | QE "minus" QE
     | QE "times" QE
     | QE "divided by" QE
     | "the sum of" NP                   -- "the sum of all amounts in history"
     | "the length of" NP
     | "the number of" NP
```

**Rule QE-1: Arithmetic is spelled out.** The symbols +, -, *, / are notation-layer constructs. In load-bearing prose, arithmetic is expressed in words. This makes the intended operation unambiguous — "the balance plus the amount" cannot be read as subtraction.

---

## Part III: Sentence Grammar

This is where CPL diverges most sharply from English. CPL sentences belong to exactly one of six **sentence forms**. Every load-bearing prose sentence must be identifiable as one of these forms. If it cannot be parsed as any of them, it is a prose comment only — not load-bearing — and must be introduced with "Note:" or placed outside a `We say:` block.

### Form 1: Type Declaration

Introduces a named type and its structure.

```
TYPE-DECL ::= "A" TYPE-NAME "is a record containing:"
                  field-list
            | "A" TYPE-NAME "is one of:"
                  variant-list
            | "A" TYPE-NAME "is" TE

field-list   ::= ("-" IDENTIFIER ":" TE)+
variant-list ::= ("-" TYPE-NAME ctor-fields?)+
ctor-fields  ::= "containing" "(" field-list ")"
```

**Examples (well-formed):**
```
A Transaction is a record containing:
  - amount  : Amount
  - kind    : TransactionKind
  - date    : Date

A TransactionKind is one of:
  - Deposit
  - Withdrawal
  - Transfer containing (- destination : Account)
```

**Rule TD-1: A type declaration is closed.** The field list or variant list must be exhaustive. No "and others." No "et cetera."

**Rule TD-2: No forward reference without declaration.** Every TYPE-NAME used in a field must either be defined earlier in the document or declared in a `using` block at the section header.

### Form 2: Constraint Declaration

Attaches a machine-verifiable invariant to a type or value.

```
CONSTRAINT-DECL ::= "such that" CONSTRAINT
                  | "where" CONSTRAINT
                  | "provided that" CONSTRAINT

CONSTRAINT ::= NP COMPARATOR QE
             | NP "equals" QE
             | NP "is" ("positive" | "negative" | "zero" | "empty" | "non-empty")
             | NP "contains" QUANTIFIER NP "satisfying" CONSTRAINT
             | CONSTRAINT "and" CONSTRAINT
             | CONSTRAINT "or" CONSTRAINT
             | "not" CONSTRAINT
             | "for every" NP "in" NP "," CONSTRAINT

COMPARATOR ::= "is less than" | "is greater than" 
             | "is at most" | "is at least" | "equals" | "is not equal to"
```

**Rule CD-1: Every constraint names its subject explicitly.** "such that it is positive" is illegal. "such that the amount is positive" is required.

**Rule CD-2: Compound constraints use explicit connectives.** "such that the amount is positive and the account is not closed" — both halves independently evaluable. No shortcutting "such that both fields are valid" — valid according to what?

### Form 3: Function Declaration

Declares the name, inputs, outputs, and possible failure modes of a function. This is the prose type signature.

```
FUNCTION-DECL ::= verbal-phrase GIVES-CLAUSE [FAIL-CLAUSE]

verbal-phrase ::= VERB-PHRASE "(" param ")" ["(" param ")"]* 
VERB-PHRASE   ::= "to" GERUND                 -- "to deposit", "to open"
GERUND        ::= IDENTIFIER "-ing" | IDENTIFIER  -- CPL gerunds from kebab names

param         ::= "(" IDENTIFIER ":" TE ")"

GIVES-CLAUSE  ::= "gives" "a" TYPE-NAME
               |  "gives" "the updated" TYPE-NAME  -- mutation in value semantics
               |  "gives" "a" TYPE-NAME "or" TYPE-NAME  -- sum result

FAIL-CLAUSE   ::= "," "failing if" CONSTRAINT
               |  "," "or fails with" TEXT-LIT "if" CONSTRAINT
```

**Examples (well-formed):**
```
To deposit (amount : Amount) into (account : Account)
gives the updated Account,
failing if amount is less than zero.

To transfer (amount : Amount) from (source : Account) to (destination : Account)
gives a TransferResult,
failing if the balance of source is less than amount.
```

**Rule FD-1: Every parameter is named and typed.** `(amount)` alone is illegal. `(amount : Amount)` is required.

**Rule FD-2: The gives-clause is mandatory.** A function declaration with no stated return is a type error in prose. "Performs the transfer" is not a gives-clause.

**Rule FD-3: Multiple failure modes are listed explicitly.** If a function can fail in more than one way, each is a separate `or fails with` clause. They are exhaustive.

### Form 4: Proof Assertion

States a claim that the proof system must verify.

```
PROOF-ASSERTION ::= "claim:" CLAIM
                  | "therefore," CLAIM
                  | "it follows that" CLAIM

CLAIM ::= NP "implies" NP
        | CONSTRAINT
        | "for every" NP "of type" TE "," CONSTRAINT
        | CLAIM "and" CLAIM
```

**Rule PA-1: Claims have no hedge words.** "It seems that," "presumably," "it appears that" — these are forbidden in proof assertions. A claim is asserted unconditionally or it is a comment.

### Form 5: Procedure Step

One step in an imperative sequence. Procedures are found inside function bodies written in prose.

```
PROCEDURE ::= "first," STEP "." 
             ("then," STEP ".")*
             ("finally," STEP ".")?

STEP  ::= "let" IDENTIFIER "be" RVALUE
        | "set" NP "to" RVALUE
        | "if" CONSTRAINT "," STEP "," "otherwise" STEP
        | "return" RVALUE
        | "fail with" TEXT-LIT

RVALUE ::= NP
         | QE
         | "a new" TYPE-NAME "with" field-assignments
         | "the result of" FUNCTION-CALL

FUNCTION-CALL ::= IDENTIFIER "(" IDENTIFIER "," ... ")"
                | IDENTIFIER "applied to" NP

field-assignments ::= IDENTIFIER "set to" RVALUE ("and" IDENTIFIER "set to" RVALUE)*
```

**Rule PS-1: First/then/finally are mandatory sequence markers.** "Let balance be the old balance plus amount. Return the account." — no sequence markers — is illegal. "First, let updated-balance be the balance of account plus amount. Then, return a new Account with balance set to updated-balance." is required.

**Rule PS-2: "if/otherwise" is exhaustive.** Every conditional step must have an otherwise branch. "If the amount is positive, proceed" with no otherwise is a compile error in prose. The else branch can be "otherwise, fail with [reason]" but it must be stated.

**Rule PS-3: Every let-binding names its value.** "Let it be the sum..." is illegal. "Let updated-balance be the sum..." is required.

### Form 6: Quantified Statement

Universal or existential claims over collections.

```
QUANTIFIED ::= "for every" NP "in" NP "," CLAIM
             | "there exists" "exactly one" NP "in" NP "such that" CLAIM
             | "there exists" "at least one" NP "in" NP "such that" CLAIM
             | "no" NP "in" NP SATISFIES CLAIM

SATISFIES ::= "satisfies" | "has" | "equals"
```

**Rule QS-1: The bound variable is named.** "For every element in the history" is ambiguous when constraints need to access fields. "For every transaction in the history" is required — `transaction` becomes the bound variable name in scope for the subsequent CLAIM.

---

## Part IV: Document Grammar

```
DOCUMENT ::= CHAPTER+

CHAPTER  ::= "Chapter:" TEXT-NAME
               PROSE-COMMENT*
               SECTION*

SECTION  ::= "Section:" TEXT-NAME
               PROSE-COMMENT*
               STATEMENT*

STATEMENT ::= TYPE-DECL
            | FUNCTION-DECL CONSTRAINT-DECL*
            | PROOF-ASSERTION
            | WE-SAY-BLOCK
            | PROSE-COMMENT

WE-SAY-BLOCK ::= "We say:"
                   (TYPE-DECL | FUNCTION-DECL | QUANTIFIED-STATEMENT)+

PROSE-COMMENT ::= Any English text not beginning with a CPL keyword.
                  -- Not load-bearing. Not parsed by the compiler.
                  -- Must not be placed inside a We say: block.
```

**Rule DOC-1: `We say:` marks the load-bearing boundary.** Everything inside a `We say:` block is parsed as CPL. Everything outside is prose commentary — human-readable, machine-ignored. This is the fundamental load-bearing/commentary demarcation.

**Rule DOC-2: Chapters and sections are not optional decoration.** They define the namespace scope for TYPE-NAMEs. A type declared in `Chapter: Accounts` is not automatically in scope in `Chapter: Ledger`. It must be imported with `using Account from Chapter Accounts.`

---

## Part V: Scope Rules

These are the rules that make CPL formally compositional — able to be analyzed section by section without reading the whole document.

**Scope Rule 1: Lexical scoping.** A name is in scope from its introduction point to the end of its enclosing `We say:` block or function body.

**Scope Rule 2: Chapter-level types are public by default.** All TYPE-NAMEs declared at chapter scope are visible to other chapters. All IDENTIFIERs (value bindings) are private to their block.

**Scope Rule 3: Parameters shadow chapter scope.** If a function parameter has the same name as a chapter-level type, the parameter wins inside the function declaration. This is the only shadowing permitted. All other shadowing is a compile error.

**Scope Rule 4: No forward use of value bindings.** In a procedure, a `let`-bound name cannot appear in any step before the step that introduces it. Types may be referenced before their definition within the same chapter.

**Scope Rule 5: Constraint scope follows its attachment.** A constraint attached to a type via `such that` is in scope everywhere that type is in scope. A constraint attached to a function via `failing if` is checked at every call site.

---

## Part VI: The Ambiguity Test

A CPL sentence passes the ambiguity test if and only if:

1. **Parse uniqueness.** There exists exactly one parse tree for the sentence under the grammar above. (No ambiguous grammar rules.)
2. **Referent uniqueness.** Every NP with a definite article resolves to exactly one entity in the current scope.
3. **Exhaustiveness.** Every conditional has an otherwise. Every variant list is total. Every function declares all failure modes.
4. **Type coherence.** Every NP used in a constraint refers to a value whose type makes the constraint well-typed. "Such that the name is less than zero" is ill-typed if `name : Text`.

If a sentence fails any of these four checks, it is rejected by the CPL parser with a diagnostic in plain English identifying which rule was violated and at which word.

---

## Part VII: What CPL Is Not

CPL is not full English. These valid English constructions are outside CPL and belong in prose commentary only:

- **Metaphor.** "The account is a container for value." Fine commentary. Not parseable.
- **Analogy.** "This works like a bank ledger." Not parseable.
- **Passive voice with omitted agent.** "The balance is updated." By what? Illegal in load-bearing position. Required: "The deposit function updates the balance."
- **Relative clauses modifying verbs.** "The function, which handles the edge case, returns..." — the relative clause is commentary. In CPL the signature and the constraint are separate sentences.
- **Rhetorical questions.** None. Ever. In load-bearing prose.
- **Implicit subject.** "Returns the updated account." — returns is a verb without a subject. Illegal. "The function gives the updated account."

---

## Appendix: Reserved CPL Keywords

These words carry fixed semantic meaning in CPL load-bearing prose and cannot be used as identifiers.

```
Structural:    chapter, section, we say
Declarations:  a, an, the, is, contains, one of
Constraints:   such that, where, provided that, for every, there exists,
               no, satisfying, implies, and, or, not
Functions:     to, gives, failing if, or fails with
Procedures:    first, then, finally, let, be, set, to, if, otherwise,
               return, fail with
Quantifiers:   all, every, each, exactly one, at most one, at least one,
               zero or more, one or more, none
Arithmetic:    plus, minus, times, divided by, sum of, length of, number of
Comparators:   equals, is less than, is greater than, is at most, is at least,
               is not equal to
Claims:        claim, therefore, it follows that
Proof:         assume, by, qed
```

---

## Closing Note: The Design Intent

The grammar above has one property that is not in any other language specification I know of: **every rule exists to eliminate exactly one class of natural-language ambiguity, and no rule exists for any other reason.** There is no syntax sugar. There are no convenience forms. Every restriction removes something that would otherwise permit two valid interpretations of the same sentence.

The result is a language that a human reads as careful English — the kind a good engineer writes when documenting a critical system — and that a machine reads as a formal specification with no resolution ambiguity at any level.

That is the razor. English is the stone. CPL is what remains after the cut.



Cardinality ::= Exact    (n : Integer, where n ≥ 0)
              | Bounded  (lower : Integer, upper : Integer, 
                          where lower ≥ 0 and upper ≥ lower)
              | Parameter (name : Identifier, domain : IntegerRange)

IntegerRange ::= All non-negative Integers          -- "zero or more"
| From (n : Integer)                  -- "at least n"
| To   (n : Integer)                  -- "at most n"
| Between (lo : Integer) (hi : Integer)  -- "between lo and hi"


Quantity Rule: Every cardinal reference in load-bearing CPL prose must be an expression of type Cardinality. Words that do not reduce to Exact, Bounded, or Parameter are banned at the lexical level. Vagueness about quantity is not a prose style — it is a type error.
