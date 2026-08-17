#!/usr/bin/env python3
"""Generate ZigcHarness.codex: a Codex compiler that runs as a normal process.

The whole rung proves the transpiled compiler emits the right bytes. It
cannot prove the transpiled compiler is usable, because its subject is a Text
literal baked in at bundle time and its output is a decimal dump. This is the
same compiler with a real I/O boundary: source in on stdin, CDX out on
stdout.

It stands in for opening.codex, which needs an operating system -- its effect
row is [Console, FileSystem, Device.Block], it paints progress on a
framebuffer and it reads and writes a FAT16 volume. This keeps the half that
compiles and drops the half that needs a machine, which is the difference
between a driver and a kernel.

Verification is not a ladder oracle: nothing bare-metal runs this harness.
The check is direct and stronger -- compile a program with the zig binary,
compile the same program with the seed, and the two CDX files must be equal.
"""
import pathlib

from emit_harness import pipeline_source

HERE = pathlib.Path(__file__).parent

out = f'''Chapter: ZigcHarness

Section: Driver

 read-file-uni takes a path, and "/dev/stdin" is how a hosted process names
 the stream it was handed. The C# plug ignores the path and reads the stream
 unconditionally; reading the path as given costs nothing and keeps the
 builtin meaning what its signature says.

  opening : [Console, FileSystem] Nothing = act
    src <- read-file-uni "/dev/stdin"
    {pipeline_source("src", True)}
    in act
      write-binary (res.header-bytes)
      write-binary-buf (res.content-buf) 0 (res.content-len)
      write-binary (res.tail-bytes)
    end
  end
'''

dest = HERE / 'ZigcHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes')
