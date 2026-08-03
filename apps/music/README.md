# Codex Music

A Spotify-style music player with a sidebar (nav + playlists), an album grid and track list content area, and a persistent bottom player bar with transport controls.

## Features

- Sidebar: Home/Search/Library nav items, four named playlists
- Content area: gradient header, 6-album grid with emoji art, "Recently Played" track list
- Player bar: art thumbnail, track title/artist, transport buttons (Shuffle, Prev, Play/Pause, Next, Repeat), progress and volume bars
- Play/Pause toggle is live (changes glyph and shows/hides now-playing info)

## Completeness

40% -- Layout is polished and play/pause toggle is live. Clicking a track does not update now-playing, albums are not navigable, search absent, playlist contents not defined. Progress/volume bars are static CSS widths. Shuffle/Prev/Next/Repeat return 0. No audio playback pathway.

## Codex Conformance

Full -- Pure Codex. Audio playback is a backend plug responsibility.
