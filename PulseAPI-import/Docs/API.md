# Pulse API

The `pulse/*` API surface. **Work in progress** — this currently lists candidate
endpoints only. Request/response objects will be defined as the contract is
designed.

## System

- `pulse/ping`
- `pulse/version`
- `pulse/me`

## Library — reads

- `pulse/artists`
- `pulse/artist` (+ `/{id}`)
- `pulse/albums`
- `pulse/album` (+ `/{id}`)
- `pulse/track` (+ `/{id}`)
- `pulse/genres`
- `pulse/genreTracks`
- `pulse/artistTracks`
- `pulse/search`

## Discovery / ranked

- `pulse/popularTracks`
- `pulse/popularArtists` — rename for consistency with the `popular*` family (was `topTracks`/`topPlaylists`-style naming)
- `pulse/popularPlaylists`
- `pulse/recentlyPlayed` — includes all library object types
- `pulse/recentPlaylists`

## Playlists

- `pulse/playlists`
- `pulse/playlist` (+ `/{id}`)
- `pulse/createPlaylist`
- `pulse/updatePlaylist`
- `pulse/deletePlaylist`
- `pulse/markPlaylistPlayed`

## Favorites / ratings

- `pulse/favorites`
- `pulse/favorite`
- `pulse/unfavorite`

## Media delivery

- `pulse/stream`
- `pulse/download`
- `pulse/coverArt`

## Analytics

- `pulse/reportTrackAnalytics`

## Admin / users

- `pulse/listUsers`
- `pulse/createUser`
- `pulse/updateUser`
- `pulse/deleteUser`

## Other

- `pulse/podcasts`
- `pulse/stats`
