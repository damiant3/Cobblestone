# Generated Output

This directory contains the output of the Codex compiler for every sample in `samples/`,
compiled to every backend. These files are **checked in intentionally** — they form a
historical record of what the compiler produces.

## Directory Structure

```
generated-output/
├── csharp/          15 × .cs files
├── javascript/      15 × .js files
├── rust/            15 × .rs files
├── python/          15 × .py files
├── cpp/             15 × .cpp files
├── go/              15 × .go files
├── java/            15 × .java files
├── ada/             15 × .adb files
├── babbage/         15 × .ae files
├── fortran/         15 × .f90 files
└── cobol/           15 × .cob files
```

15 samples × 11 backends = 165 files.

## How to Regenerate

Run the corpus emission test:

```sh
dotnet test tests/Codex.Types.Tests --filter "Emit_full_corpus_to_generated_output"
```

Or run the full test suite, which includes the 165 per-sample-per-backend compilation tests:

```sh
dotnet test Codex.sln
```

## Why Check These In?

1. **History** — `git diff` shows exactly what changed in the compiler's output between commits.
2. **Review** — humans can read the generated code to verify the compiler is doing the right thing.
3. **Regression** — if a backend silently changes output, the diff catches it.
4. **Showcase** — browse the output to see how Codex compiles to 11 different languages.
