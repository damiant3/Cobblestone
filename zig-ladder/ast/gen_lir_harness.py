# Generate LirHarness.codex + LirStubs.codex for the lir milestone: the
# first back-half rung. Unlike the front-half rungs there is no source
# subject and no self-reference -- X86_64Lir is instruction selection, so
# the harness hand-builds LirFunc programs, runs lir-emit-func over a
# fresh CodegenState, and prints the emitted machine-code bytes. The
# byte list IS the oracle output.

HARNESS = r"""Chapter: Lir Harness

 The functions below are data, chosen to reach the selector's branches:
 ALU on registers and immediates, a compare feeding a conditional branch,
 a spilled destination, the fixed-register remainder, and per-arm returns
 (rets = True) so each path carries its own epilogue.

Section: Programs

  lir-h-locs : List Location
  lir-h-locs = [LocReg 0, LocReg 1, LocReg 2, LocSlot 0]

  lir-h-add : LirFunc
  lir-h-add = LirFunc {
   name = "subj-add",
   nparams = 2,
   blocks = [LirBlock { id = 0, insns = [
    LiBin LoAdd 2 (LvReg 0) (LvReg 1),
    LiBin LoMul 3 (LvReg 2) (LvImm 10),
    LiRet (LvReg 3)
   ] }],
   result = LvImm 0,
   nvregs = 4,
   rets = True
  }

  lir-h-branch : LirFunc
  lir-h-branch = LirFunc {
   name = "subj-branch",
   nparams = 2,
   blocks = [
    LirBlock { id = 0, insns = [
     LiCmp (LvReg 0) (LvImm 100),
     LiBranch LcLt 1 2
    ] },
    LirBlock { id = 1, insns = [
     LiBin LoSub 2 (LvReg 0) (LvReg 1),
     LiRet (LvReg 2)
    ] },
    LirBlock { id = 2, insns = [
     LiBin LoRem 3 (LvReg 0) (LvReg 1),
     LiMove 2 (LvReg 3),
     LiRet (LvReg 2)
    ] }
   ],
   result = LvImm 0,
   nvregs = 4,
   rets = True
  }

Section: Driver

  lir-h-state : CodegenState
  lir-h-state =
   let ws = init-emit-workspace 65536 4096
   in __record-set empty-codegen-state "workspace" ws

  lir-h-bytes : CodegenState, Integer, List Text -> Text
  lir-h-bytes (st) (i) (acc) =
   if i >= st.code-len then text-concat-list acc
   else lir-h-bytes st (i + 1) (list-push acc (integer-to-text (peek-byte (st.workspace.code-buffer) i) & " "))

  lir-h-run : Text, LirFunc -> [Console] Nothing
  lir-h-run (label) (f) = act
   st <- lir-h-state
   done <- lir-emit-func st f lir-h-locs
   print-line-uni (label & " len " & integer-to-text (done.code-len))
   print-line-uni (lir-h-bytes done 0 [])
  end

  opening : [Console] Nothing
  opening = act
   lir-h-run "add" lir-h-add
   lir-h-run "branch" lir-h-branch
  end
"""

STUBS = r"""Chapter: Lir Stubs

 The real X86_64Builtins.codex cannot be bundled -- its table is typed
 over the whole tree emitter -- and empty-codegen-state reads these two
 names. Empty is honest here: the harness drives lir-emit-func directly
 and nothing on that path consults a builtin.

Section: Stubs

  BuiltinX86Emitter = record {
   name : Text
  }

  x86-builtin-emitters : List BuiltinX86Emitter
  x86-builtin-emitters = []

  sorted-builtin-names : SkipListText
  sorted-builtin-names = skip-list-text-empty

 Faithful copies from unbundled pages of the X86-64 Code Generator
 chapter, kept byte-for-byte with their originals so the subject's
 behavior is the compiler's: tco-list-contains from X86_64Compound,
 emit-call-to from X86_64, buf-write-i32 from X86_64Chapter.

  tco-list-contains : List Integer, Integer, Integer -> Boolean
  tco-list-contains (xs) (v) (i) =
   if i == list-length xs then False
   else if list-at xs i == v then True
   else tco-list-contains xs v (i + 1)

  emit-call-to : CodegenState, Text -> CodegenState
  emit-call-to (st0) (target) =
   let st = fc-flush st0
   in let patch-pos = st.code-len
   in let st1 = st-append-code st (x86-call 0)
   in let cp-o = list-push (st1.cp-offsets) patch-pos
   in let cp-t = list-push (st1.cp-targets) target
   in let st2 = __record-set st1 "cp-offsets" cp-o
   in __record-set st2 "cp-targets" cp-t

  buf-write-i32 : Integer, Integer, Integer -> Integer
  buf-write-i32 (buf) (off) (v) =
   let w0 = __buf-write-byte buf off (int-mod v 256)
   in let w1 = __buf-write-byte buf (off + 1) (int-mod (floor-div v 256) 256)
   in let w2 = __buf-write-byte buf (off + 2) (int-mod (floor-div v 65536) 256)
   in let w3 = __buf-write-byte buf (off + 3) (int-mod (floor-div v 16777216) 256)
   in off + 4

  is-builtin : SkipListText, Text -> Boolean
  is-builtin (names) (name) =
   skip-list-text-has names name

  int-log2 : Integer -> Integer
  int-log2 (v) = if v <= 1 then 0 else 1 + int-log2 (bit-shru v 1)

  emit-sub-rsp-imm32 : Integer -> List Integer
  emit-sub-rsp-imm32 (imm) =
   [72, 129, 236] & write-i32 imm

  bsearch-x86-arity-pos : List UserArity, Text, Integer, Integer -> Integer
  bsearch-x86-arity-pos (entries) (name) (lo) (hi) =
   if lo >= hi then lo
   else let mid = lo + (hi - lo) / 2
   in if text-compare name (list-at entries mid).name <= 0 then bsearch-x86-arity-pos entries name lo mid
      else bsearch-x86-arity-pos entries name (mid + 1) hi

  lookup-x86-arity : List UserArity, Text -> Integer
  lookup-x86-arity (entries) (name) =
   let len = list-length entries
   in let pos = bsearch-x86-arity-pos entries name 0 len
   in if pos >= len then 0 - 1
      else let e = list-at entries pos
      in if e.name == name then e.arity else 0 - 1

  return-bound-of : CodexType, Integer -> CodexType
  return-bound-of (ty) (n) =
   if n <= 0 then ty
   else when ty
    is FunTy (p) (row) (r) -> return-bound-of r (n - 1)
    is ForAllTy (id) (b) -> return-bound-of b n
    is ForAllEff (id) (b) -> return-bound-of b n
    is otherwise -> ty

 RenameEntry comes from Semantics/ChapterScoper.codex, which TypeEnv
 names in a field type without a cite.

  RenameEntry = record {
   original : Text,
   mangled : Text
  }

 These two come from Types/Unifier.codex, which TypeEnv reads without a
 cite; bundling the whole unifier for a list copy is the tail wagging
 the dog.

  copy-list-with-headroom : List a, Integer -> List a
  copy-list-with-headroom (src) (extra-cap) =
   let len = list-length src
   in let dst = __list-with-capacity (len + extra-cap)
   in copy-list-loop src dst 0 len

  copy-list-loop : List a, List a, Integer, Integer -> List a
  copy-list-loop (src) (dst) (i) (len) =
   if i == len then dst
   else copy-list-loop src (list-push dst (list-at src i)) (i + 1) len
"""

import pathlib
here = pathlib.Path(__file__).parent
(here / "LirHarness.codex").write_text(HARNESS)
(here / "LirStubs.codex").write_text(STUBS)
print("wrote LirHarness.codex + LirStubs.codex")
