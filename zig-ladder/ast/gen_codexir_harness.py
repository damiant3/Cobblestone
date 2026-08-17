#!/usr/bin/env python3
"""Generate CodexIrHarness.codex: the compiler as a program that emits IR.

zigc emits a CDX, which is what the Codex compiler is for. This emits IR-CCE
instead, which is what the PLUG is fed -- and that is the piece that takes the
seed out of the pipeline. With this and ZigEmitHosted, the chain

    prog.codex -> prog.ir -> prog.zig -> ELF

is three native processes and no VM at all.

The driver is emit-ir-cce's own sequence (opening.codex ~1694), minus the deck
and heap accounting and using the whole-text form rather than the streaming
one: a hosted process has no reason to avoid materialising the text, and
ir-print-defs exists to avoid exactly that.

Two things it must not skip, because they decide what the IR CONTAINS:

  ir-prune-unreachable-roots  -- the IR is what is reachable from the roots,
                                 not every def in the unit
  emit-ir-chapter's meta      -- chapter title, prose, section titles, ctor
                                 names, prose blocks, annotations and ground
                                 effects all ride the text wire

ir-emit-roots is copied from opening.codex:1316 rather than cited, because
opening.codex cannot be bundled beside a harness that defines `opening`. If
that list changes upstream, this copy is wrong and the IR will be missing a
root -- which is the cost of standing in for the driver.
"""
import pathlib

from emit_harness import frontend_source

HERE = pathlib.Path(__file__).parent

out = f'''Chapter: CodexIrHarness

Section: Roots

 opening.codex:1316, copied because that chapter cannot ride along.

  irc-emit-roots : List Text
  irc-emit-roots = ["opening", "vb-capacity-auto", "vb-read-auto", "vb-write-auto"]

Section: Driver

  opening : [Console, FileSystem] Nothing = act
    src <- read-file-uni "/dev/stdin"
    {frontend_source("src", True)}
    in let meta = IRTextMeta {{
      chapter-title = ch.chapter-title,
      prose = ch.prose,
      section-titles = ch.section-titles,
      ctor-names = rr.ctor-names,
      prose-blocks = ch.prose-blocks,
      annotations = ch.annotations,
      ground-effects = ch.ground-effects
    }}
    in print-text (emit-ir-chapter (ir-prune-unreachable-roots ir irc-emit-roots) meta (ch.type-defs))
  end
'''

dest = HERE / 'CodexIrHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes')
