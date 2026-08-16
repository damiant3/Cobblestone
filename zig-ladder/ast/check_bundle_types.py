#!/usr/bin/env python3
"""Which type names does a milestone bundle reference without defining?

A full bare-metal compile costs minutes, and the compiler reports missing
names a dozen at a time, so chasing chapter dependencies one compile per
round is slow. This narrows the search: it reads the chapter list out of
bundle_<m>.ps1 (so the list cannot drift from what actually gets bundled),
collects capitalised names each chapter defines and references, and reports
the references nothing defines, grouped by the chapter that would supply
them.

Types only. Function names, locals and constructors introduced by sugar are
out of scope, so a clean report here does not promise a clean compile -- the
compiler stays the authority.

Prose lines are skipped when collecting references: codex indents code by
two spaces and prose by one, so a capitalised English word in a paragraph
does not get reported as a missing type.
"""
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent.parent
HERE = pathlib.Path(__file__).parent

BUILTIN = {'Integer', 'Text', 'Boolean', 'Nothing', 'List', 'LinkedList',
           'Maybe', 'Vector', 'True', 'False', 'Just', 'None', 'Console',
           'Network', 'Set', 'Map', 'Char', 'Number'}

DECL = re.compile(r'^  ([A-Z][A-Za-z0-9]*)\s*[(=]')
CTOR = re.compile(r'^\s*\| ([A-Z][A-Za-z0-9]*)')
WORD = re.compile(r'\b([A-Z][A-Za-z0-9]*)\b')


def is_prose(line):
    # Codex indents code by two spaces and prose by one.
    return line.startswith(' ') and not line.startswith('  ')


def declares(line):
    m = DECL.match(line) or CTOR.match(line)
    return m.group(1) if m else None


def bundle_chapters(milestone):
    """The chapter list as bundle_<m>.ps1 actually spells it."""
    text = (HERE / f'bundle_{milestone}.ps1').read_text()
    block = text.split('foreach ($ch in @(', 1)[1].split('))', 1)[0]
    return re.findall(r"'([^']+\.codex)'", block)


def main(milestone):
    chapters = bundle_chapters(milestone)
    defined, used = set(), set()
    for rel in chapters:
        for line in (REPO / rel).read_text(errors='replace').splitlines():
            name = declares(line)
            if name:
                defined.add(name)
            if not is_prose(line):
                used.update(WORD.findall(line))
    missing = sorted(used - defined - BUILTIN)

    home = {}
    for p in (REPO / 'codex').rglob('*.codex'):
        if '/plugs/' in str(p) or 'build-output' in str(p):
            continue
        for line in p.read_text(errors='replace').splitlines():
            name = declares(line)
            if name:
                home.setdefault(name, p.relative_to(REPO))

    groups = {}
    for name in missing:
        groups.setdefault(str(home.get(name, 'UNKNOWN')), []).append(name)

    print(f'{milestone}: {len(chapters)} chapters bundled')
    # Only names that resolve to a chapter are actionable. UNKNOWN collects
    # section headings, diagnostic codes and stray capitalised words, and is
    # reported as a count so it cannot bury the signal.
    unknown = groups.pop('UNKNOWN', [])
    if not groups:
        print('  no undefined type names traceable to a chapter')
    for source in sorted(groups):
        print(f'  {source}\n      {" ".join(groups[source])}')
    print(f'  (UNKNOWN, not traceable to any chapter: {len(unknown)} names)')


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'parse')
