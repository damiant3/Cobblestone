# Codex Piano

A two-octave virtual piano keyboard playable from the computer keyboard, with waveform selection, octave/volume controls, and an on-screen key-map guide.

## Features

- Two octaves (C3-B3 mapped Z-M, C4-B4 mapped Q-U) rendered as white key rows
- Controls: Octave shift, Volume, Waveform selector (Sine/Triangle/Square), Sustain toggle
- Note display area with current note and frequency
- Key-map guide showing four rows of keyboard caps

## Completeness

35% -- Visual structure is thorough and keyboard layout is correct. The click handler is a stub returning 0 for all inputs. No note plays, no state changes, no waveform selection takes effect. Audio synthesis entirely absent. Black keys exist in CSS but not as interactive widget nodes.

## Codex Conformance

Partial -- Written in Codex; all audio would need a plug. Handler section present but unimplemented.
