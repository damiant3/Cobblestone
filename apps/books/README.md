# Codex Books

An e-book reader application with a library browser and an in-app reading view. Presents a curated book collection and switches between a shelf/continue-reading layout and a full prose reader.

## Features

- Library view with a 10-book shelf grid and a "Continue Reading" strip with progress percentages
- Reader view with chapter navigation, prose body, and page/percentage footer
- Three navigation tabs: Library, Book Store, Audiobooks (latter two are stubs)
- Dark theme with purple accent and serif body font for the reader
- CSS book-cover spine effect and hover lift animation

## Completeness

40% -- Library and reader views render correctly and view-switching works. Book Store and Audiobooks tabs have no views. Search is non-interactive. No actual book content loading, pagination, bookmarks, or annotations. Progress bars are hardcoded.

## Codex Conformance

Full -- Written entirely in Codex. UI built through Widget/Theme foreword. DOM/state primitives declared as local stubs representing the plug boundary.
