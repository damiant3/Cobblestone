# Assist C# guidelines

The Assist's C# follows the C# reference compiler under `old/` (read it, never edit or build it): the root
`.editorconfig`, which already governs every `.cs` file in the tree, and the conventions its 200 source files share.
Damian, 2026-10-21: "you should follow the project in old\ the compiler written in c#"; "import the whole c# code
guidelines into this assist project and them apply them". The campaign that brings the Assist to them is
`docs/Designs/Active/Apps/AssistStructure.md`; a file it has moved follows every rule below, and a file it has not yet
moved is changed only by that campaign.

## From `.editorconfig`

- Spaces, indent 4, CRLF, final newline, no trailing whitespace, lines at most 120 characters.
- Private instance fields `m_camelCase`; static fields of any accessibility `s_camelCase`; `const` fields PascalCase.
  A public static value is a property over its `s_` field (`public static ImmutableArray<Timing> All => s_all;`).
- No `var`: every local's type is written out. `new()` where the type is on the left.
- No `this.` qualification. Default accessibility omitted (no `private` on members, no `internal` on types).
- File-scoped namespaces; `using` directives outside the namespace.
- Braces on their own lines (Allman), and required whenever a body spans more than one line.
- Expression bodies for properties, indexers, accessors and lambdas; block bodies for methods and constructors.
- `System.Threading.Lock` for a lock object.
- Pattern matching over `is` followed by a cast; `is null` over reference equality.

## From the code of `old/`

- One type per file, the file named for it; a small family of types (`Map`, `ValueMap`, `Set`) may share one.
- Data is a `sealed record`; a class is `sealed` or `static`.
- `#nullable enable`; a value that may be missing is `T?`, and a method that may find nothing returns `T?`.
- Immutable collections: `ImmutableArray<T>` for a fixed list, and `Map<TKey, TValue>`, `ValueMap<TKey, TValue>` and
  `Set<T>` (`Collections.cs`, copied from `old/src/Codex.Core`) for keyed data. `Dictionary` and `HashSet` are not used
  (Damian, 2026-10-21: "I forbid use of Dictionary go find Map from the old project and use Set, its sibling, for
  HashSet"). A change returns a new collection: `m_set = m_set.Add(x)`, never `m_set.Add(x);`, which compiles and
  keeps nothing. A field so replaced is not a lock object: lock a `Lock` beside it. A hot loop's tables (the walk
  planner's) are arrays over a dense index, not maps (`PlanGrid`).
- `using` directives instead of qualified names (Damian, 2026-10-21: "use using namespaces so you don't have to
  System.Text.Your.Tokens.Away").

## The Assist's own

- Namespace `Uoaix.Assist`, file-scoped, in every file; a PowerShell script names a type in full
  (`[Uoaix.Assist.AssistRelay]`).
- Every file is listed in the `Add-Type -Path` line of `assist.ps1`, which compiles them as one assembly; a new file
  joins that list, and a new framework assembly joins its `-ReferencedAssemblies`.
- Comments follow `CLAUDE.md` R-PROSE: Damian's rulings with their dates, the game's or the server's behaviour that
  the code depends on (with the log line or source that showed it), and magic numbers. Nothing that restates the code.
