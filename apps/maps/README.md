# Codex Maps

A full-viewport map viewer with a simulated tile background, search bar, zoom/locate/layer controls, a compass, a toggleable layer selector, and an info panel.

## Features

- Map canvas rendered as CSS gradient with absolute-positioned road lines and pin markers
- Search bar, zoom in/out, locate, layers toggle
- Compass widget
- Layer panel (Roads, Satellite, Terrain, Traffic) with show/hide toggle
- Info panel hardcoded to Portland, Oregon with lat/lon, temperature, elevation, and time

## Completeness

25% — Visual chrome of a map application is present and layer toggle is functional. Map itself is a pure CSS gradient with no tile loading, no coordinate system, no pan/zoom. Pins are fixed-position. Search, locate, and routing are absent.

## Codex Conformance

Full — Codex only. Map tile fetching and geolocation are backend/plug concerns.
