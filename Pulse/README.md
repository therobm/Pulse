# Pulse

A self-hosted music server built in C#. Serves your library to its own clients — the Thump app and a built-in tablet web client. Runs as a single executable with no external service dependencies.

I built this because Spotify kept changing how playlists worked without asking, and I wanted a music server I actually controlled.

## What It Does

Pulse scans a local music library, serves it over an HTTP API, and layers on features most music servers don't natively support.

**Server**

- Kestrel HTTP server, standalone single-executable deployment
- JSON HTTP API (~25 endpoints) — browse, search, stream, playlists, cover art, ratings, play reporting
- SQLite database via Microsoft.Data.Sqlite — hand-written parameterized SQL, no ORM, no external database server
- TagLib# for audio metadata extraction
- Spotify OAuth sync — imports playlists with fuzzy artist/title matching (Levenshtein distance)
- Lidarr integration — automatically requests missing artists found during playlist sync
- Per-user play tracking via stream events — listen-through ratio, skip detection, no reliance on the broken scrobble protocol
- Bayesian-weighted scoring per track and per artist, with confidence damping so new tracks aren't buried
- Smart playlist generation — weighted random sampling from scored tracks, with unplayed track discovery slots that factor in artist-level scores
- Composite playlist cover art via SkiaSharp (stitches multiple album covers into a single image)
- Stats dashboard — genre/decade breakdowns, per-user listening data, skip ratios
- External access via Cloudflare Tunnel

**Web Client**

- Vanilla HTML/JS/CSS, no frameworks, no build tools
- Designed for tablets — large touch targets, 16:9 landscape layout

Player — `{server}:{port}/web/pulse.html`

![Player](../Docs/player.png)

Stats — `{server}:{port}/web/stats.html`

![Stats Dashboard](../Docs/stats.png)

## Requirements

- Windows (runs as a service or standalone)
- .NET 8
- A local music library on disk (or a network share)
- TagLibSharp (NuGet)

## Configuration

On first run, Pulse writes a blank `pulse.config.json` next to the executable and prints a hint. Fill in your paths and any optional integration keys:

- `MusicPath` — path to your music library (local or UNC)
- Spotify client ID/secret (optional, for playlist sync)
- Lidarr URL and API key (optional, for missing artist requests)

## Building

```
dotnet build
```

Single project, no solution file gymnastics. The output is one executable.

## Philosophy

This is not a framework project. There are no dependency injection containers, no middleware pipelines, no abstract factory patterns. Objects are created explicitly and dependencies are passed through constructors. Every SQL query is hand-written. Every type is explicit. The code reads top to bottom without requiring a mental model of hidden infrastructure.

If you're looking for a project that follows conventional .NET patterns, this isn't it. If you're looking for a music server where you can read any file and understand exactly what it does without chasing through six layers of abstraction, you're in the right place.

That all being said - I am a game developer not a web developer, the stuff in html/js is pure slop so don't pickup any habits from it.

## License

MIT
