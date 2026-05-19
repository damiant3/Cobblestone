# De-Hoist TypeDefs

## Problem

Stage1 (self-compiled .codex output) hoists all ~100 type definitions to the top
of the file, creating a 900+ line gap between typedefs and the functions that use
them. Stage0 (the .codex source) keeps typedefs co-located with their module's
functions.

**Example:** `DiagnosticSeverity` appears at line 128 in stage1, but the functions
that use it (`make-error`, `severity-label`) don't start until line 1077.

This makes code review of self-compiled output hard. Reviewers already deal with
mangled names — scrolling 900 lines to find a type shouldn't be added to that.

## Root Cause

The typedef-to-module association exists in the AST (`TypeDef.SourceModule`, set
by the Desugarer) and survives through `ModuleScoper.Scope()` all the way to
`Lowering.Lower()`. But `Lower()` discards it — `IRModule.TypeDefinitions` is a
flat `Map<string, CodexType>` with no ordering or grouping.

The emitter then iterates `TypeDefinitions` first, `Definitions` second. Hoisted.

## Fix

**Stop discarding `SourceModule` at lowering time.** Add two optional properties
to `IRModule` that the CodexEmitter can use:

```
IRModule
  + TypeDefOrder : ImmutableArray<(string Name, string Module)>
  + DefinitionSourceModules : ImmutableArray<string>
```

Both are init-only (not positional) so no existing code breaks.

### Lowering

```
Lower(module):
  for each def in module.Definitions:
    lower(def), record def.SourceModule
  for each td in module.TypeDefinitions:
    record (td.Name, td.SourceModule)
  return IRModule { TypeDefOrder = ..., DefinitionSourceModules = ... }
```

### CodexEmitter

```
EmitInterleaved(module):
  build moduleTypeDefs: source module → [typedef names]
  currentModule = null
  for i in 0..definitions.length:
    defModule = DefinitionSourceModules[i]
    if defModule != currentModule:
      currentModule = defModule
      emit all typedefs belonging to defModule
    emit definitions[i]
  emit any remaining typedefs (modules with types but no functions)
```

When `TypeDefOrder` is default (single-file compilation), fall back to hoisting.

### Result

Before (stage1, hoisted):
```
DiagnosticSeverity =         ← line 128
  | Error
  | Warning
  | Info
  | Hint
... 949 lines of other types and functions ...
severity-label : ...         ← line 1077
```

After (stage1, de-hoisted):
```
... functions from earlier modules ...

DiagnosticSeverity =         ← right before its module's functions
  | Error
  | Warning
  | Info
  | Hint

Diagnostic = record {
  severity : DiagnosticSeverity,
  ...
}

make-error : ...
severity-label : ...         ← immediately below
```

## Files

| File | Change |
|------|--------|
| `src/Codex.IR/IRModule.cs` | Add `TypeDefOrder`, `DefinitionSourceModules` |
| `src/Codex.IR/Lowering.cs` | Populate from AST `SourceModule` |
| `src/Codex.Emit.Codex/CodexEmitter.cs` | Add `EmitInterleaved()` |

## Ordering Guarantee

1. Typedefs appear before their module's first function
2. Cross-module: foundational modules (Ast, Core) sort before consumers
   (Emit, Semantics) because source files are sorted lexicographically
3. Within a module, typedef order matches the source

## Follow-Up

The self-hosted `CodexEmitter.codex` needs the same logic for fixed-point
convergence. That requires updating `IRModule.codex` with the new fields.
Separate task from the C# implementation.
