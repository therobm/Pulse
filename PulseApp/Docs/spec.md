# Pulse

Music player for Android, built to pair with a Pulse server.

## Server first via passthrough cache

The client always sources its local cache for any data it needs. The server (if available) should always be called to bust local cache when available. The goal is to have an always up to date experience when online, while still having a fallback path for times the server isn't available.

This applies to everything: metadata, playlists, home screen content, cover art. No separate online/offline mode. No sync. No merge. Server response replaces local data, always.

## Audio cache

Audio files are written to disk as they stream, keyed by track ID. Same track ID always means the same audio, so cached files never go stale.

When the cache exceeds the size cap (user configurable, default 500 MB), the least recently played files get deleted first to make room.

When a playlist or album is playing, the client should fetch the next N tracks ahead in the background (configurable, default 10) so they're ready when needed. This means gapless playback actually works on spotty connections, and going into a tunnel mid-playlist isn't an immediate problem.

No pin/download feature — the cache fills from normal playback and lookahead.

## API

### Core API (required)

The full list of endpoints the client calls against the server:

**System:** ping, getLicense, getUser, getMusicFolders

**Browsing:** getArtists, getArtist, getAlbum, getAlbumList2, getGenres, getIndexes, getMusicDirectory, getSong, search3

**Playlists:** getPlaylists, getPlaylist, createPlaylist, updatePlaylist, deletePlaylist

**Favorites:** getStarred2, star, unstar, setRating

**Playback:** stream, getCoverArt

### Pulse endpoints

Ranked content used by the home screen carousels.

- `pulse/recentlyPlayed` — recent tracks, newest first
- `pulse/popularArtists` — artists ranked by listening score
- `pulse/topPlaylists` — playlists ranked by relevance

## UI

### Navigation

Bottom bar: Home, Library, Search, Settings.

### Home screen

Scrollable list of horizontal carousels:

1. Recently Played — `pulse/recentlyPlayed`
2. Your Playlists — `pulse/topPlaylists`
3. Popular Artists — `pulse/popularArtists`
4. Recently Added — `getAlbumList2?type=newest`
5. Favorites — `getStarred2`

Tap an item to open it. No filters, no sort controls.

### Album / Playlist detail

Large cover art, track list, play and shuffle buttons. Per-track menu: play next, add to queue, star/unstar.

### Now playing

Full screen from tapping the mini player. Cover art, track info, seek bar, play/pause, skip, shuffle, repeat, favorite, queue button (reorderable list).

### Mini player

Bottom bar when something is playing. Art, title, artist, play/pause. Tap to expand.

### Android Auto

Browse tree: Home, Playlists, Artists, Albums, Recently Played. Android Auto renders its own UI from this.

## Playback

- Gapless playback
- Report track plays / skips to the server (Pulse `reportTrackAnalytics`) on finish or skip
- Volume normalization if the audio files have the tags for it (ReplayGain)
- Queue survives app restarts
- Resume last track, position, and queue on restart

## Settings

- Server URL
- Username and password
- Prefetch limit — how many upcoming tracks to pre-download during playback (default 10)
- Audio cache size (default 500 MB, 100 MB – 5 GB)
- Clear cache
- Normalize volume — off / per track / per album

## Not included

- Local file playback
- Playlist editing on the client
- Tag editing
- EQ or audio processing
- Multiple servers
- Podcasts
- Casting (maybe later)
- Cloud storage
- Explicit offline/download management

## Tech stack

- C# / .NET MAUI (`net10.0-android`), UI built in C# (no XAML)
- Media3 (ExoPlayer + MediaLibrarySession) for playback
- Android Auto via MediaLibraryService
- Min SDK 23 (Android 6.0), target latest stable
- MIT license, public repo