# Globe

A real-time Earth visualization and GIS app with 16 toggleable data overlay layers (aircraft, earthquakes, weather, ISS, volcanoes, wildfires, storms, fireballs, solar flares, ocean buoys, radar) and an integrated map/routing mode with Dijkstra-based turn-by-turn navigation, POI search, geocoding, and bookmarks.

## Modules

- **GlobeTypes** -- Geographic primitives, integer trig, 16 OverlayKind variants, typed data-point records, color-ramp functions
- **GlobeData** -- Public API feed URLs (OpenSky, USGS, Open-Meteo, NASA EONET, JPL, NDBC, RainViewer); 36-city dataset
- **GlobeScene** -- GlobeCamera with orbit/zoom, OverlayState per feed with refresh intervals, auto-rotate, grid/borders/labels toggles
- **MapTypes** -- Road network graph types: 10 RoadClasses, TravelMode, Route/RouteStep with 17-maneuver enum, 23 POI categories, ElevationProfile, TrafficSegment, MapBookmark
- **MapNetwork** -- RoadGraph with Dijkstra routing, edge weight computation, turn-by-turn direction generation, sample NYC road network
- **MapSearch** -- PoiStore (name/category/radius search), route-corridor POI search, city geocoding, BookmarkStore
- **MapMode** -- MapState integrating all GIS subsystems; mode switching (Earth/Map/Route/Search/POI/Elevation)
- **TestGlobe** -- Unit test harness covering overlays, camera, state initialization, color ramps, city dataset

## Completeness

60% -- Types, data feeds, scene management, map state machine, POI/search/bookmark, and test harness are complete. Major gaps: extract-path (Dijkstra result to node path) is stubbed; determine-maneuver returns MnContinue unconditionally; no `opening` entry point; no 3D rendering (camera and overlay state exist but no framebuffer draw calls); data feed URLs defined but no HTTP fetch integration.

## Codex Conformance

Partial -- All types and logic are in Codex. The network fetch layer and 3D rendering pipeline are absent -- a Network effect plug and a renderer module need to be written. Fully spec'd but not yet executable.
