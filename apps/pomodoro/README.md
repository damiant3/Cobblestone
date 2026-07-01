# Codex Focus (Pomodoro)

A focus-timer app implementing the Pomodoro technique with work/short-break/long-break modes, session-dot progress tracking, stats, and a session history log.

## Features

- Mode tabs (Focus 25:00, Short Break 5:00, Long Break 15:00) with reactive timer display
- Circular timer ring with conic-gradient CSS
- Controls: reset, play/pause, skip
- Four session dots for Pomodoro set progress
- Stats grid and session history list

## Completeness

50% — Mode switching and timer label/time update correctly from state. Timer does not count down (no tick mechanism). Stats and history are static. Session dots do not advance. Play button does not start anything. Visual completeness is high; functional completeness is low.

## Codex Conformance

Partial — Codex source; a timer tick would require a platform event/interrupt stub not yet present.
