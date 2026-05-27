The 2b Bug: Root Cause Analysis
The self-hosted type checker's check-module doesn't build a type definition map (TDM) for user-defined types before type-checking definitions. The reference type checker (src/Codex.Types/TypeChecker.cs) does this:

Scans all ATypeDef entries (sum types, record types)
Builds SumTy / RecordTy values with constructor field types
Registers them in the type environment BEFORE checking any function definitions

The self-hosted check-module skips this — it registers builtin types (Integer, Text, etc.) but not user-defined ones like Result. So when resolve-type-name encounters Result, it falls through to lookup-type-def which returns ConstructedTy "Result" [] — a hollow shell that the lowerer maps to object.
Fix Plan
The fix goes in TypeChecker.codex's check-module:

Add build-type-def-map — walk AModule.type-defs, for each AVariantTypeDef build a SumTy, for each ARecordTypeDef build a RecordTy, resolve field types using the TDM itself (allowing mutual references)
Register these in the TypeEnv before calling check-all-defs
Pass the TDM to resolve-type-name so it returns real SumTy/RecordTy instead of hollow ConstructedTy

This is the same fix that was done in the lowerer (commit 9c0025f — "Type-def map in self-hosted type checker") but needs to also happen in the type checker's inference pass.