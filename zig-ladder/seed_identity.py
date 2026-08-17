#!/usr/bin/env python3
"""What the ladder is running against, derived rather than declared.

A banked truth is only meaningful together with the seed that produced it. The
Update number is a label people say out loud; the seed's SHA-256 is the thing
that actually decides whether a banked byte is still the right byte. So the
label is derived FROM the seed here, never typed alongside it.

The obvious derivation -- take the highest-numbered GitHubUpdate file -- is
wrong, and wrong today. Update 45's release commit creates GitHubUpdate46.md as
the accumulator for the next cycle, so the highest file names an Update that has
not shipped and whose seed does not exist. Instead: the release note that names
our seed's hash IS our Update. That cannot drift, because a note only names a
hash once the release built it.

A seed no note names is not an error, but it is not Update N either -- it is an
unreleased or locally rebuilt seed, and it says so. Banking truth against one is
fine; pretending it was a release is not.
"""

import hashlib
import pathlib
import re

from ladder_root import CODEX, LADDER

SEED = CODEX / 'seed' / 'Codex.cdx'
NOTES = CODEX / 'docs' / 'PM' / 'Active' / 'GitHubUpdates'

# Release notes name the seed by its leading eight hex digits, uppercased
# ("seed 270227BE at 2,827,487 bytes"). Damian's top-level README carries the
# full digest; either spelling identifies it, so match on the prefix both share.
PREFIX_LEN = 8


class SeedMismatch(RuntimeError):
    """The seed on disk is not the seed a truth was banked against."""


def seed_sha256():
    return hashlib.sha256(SEED.read_bytes()).hexdigest()


def update_label(sha):
    """The Update whose release note names this seed, or None if none does."""
    prefix = sha[:PREFIX_LEN].upper()
    for note in sorted(NOTES.glob('GitHubUpdate*.md')):
        if prefix in note.read_text(errors='replace').upper():
            m = re.search(r'GitHubUpdate(\d+)\.md$', note.name)
            if m:
                return int(m.group(1))
    return None


def stamp():
    sha = seed_sha256()
    label = update_label(sha)
    return {
        'sha256': sha,
        'bytes': SEED.stat().st_size,
        'update': label,
        # What a truth directory is named. An unreleased seed gets its hash
        # rather than a number, so it can never be mistaken for a release.
        'slug': f'u{label}' if label is not None else f'seed-{sha[:PREFIX_LEN]}',
    }


def truth_dir(slug=None):
    """Where truths for a given seed live. Banked truths are the one durable
    artifact per rung; everything else the ladder writes is regenerable and
    stays unversioned beside the rung."""
    return LADDER / 'truth' / (slug or stamp()['slug'])


def require_match(banked_sha):
    """Refuse, do not warn. A sweep that reports green against a seed it was
    not banked against is the failure this whole exercise exists to prevent."""
    actual = seed_sha256()
    if actual != banked_sha:
        raise SeedMismatch(
            f'seed on disk is {actual[:16]}, truth was banked against '
            f'{banked_sha[:16]}; re-bank or point CODEX_ROOT at the right checkout')


if __name__ == '__main__':
    s = stamp()
    named = f"Update {s['update']}" if s['update'] is not None else 'no release note names it'
    print(f"seed    {s['sha256']}")
    print(f"bytes   {s['bytes']:,}")
    print(f"update  {named}")
    print(f"truth   {truth_dir(s['slug'])}")
