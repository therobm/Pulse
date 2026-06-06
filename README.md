<p align="center">
  <img src="Docs/player.png" alt="Pulse web player" width="100%">
</p>

# Pulse

Pulse is a self-hosted music ecosystem — a single app that serves your library through a webpage or an android client with android auto support.

One executable, no external services, no database server. It scans your library, serves it over its own API, and gets out of the way.

## What you get

**Music, podcasts, and audiobooks** in one place. Subscribe to podcast feeds with auto-download and backlog management. Import audiobooks and pick up where you left off across devices.

**Smart playlists** that learn what you actually listen to. Bayesian scoring weighted by listen ratio, skip rate, and play history — not what an algorithm thinks you should hear.

**Library stats** that tell you what's really going on — play coverage, session health, top artists, most played tracks, all at a glance.

<p align="center">
  <img src="Docs/stats.png" alt="Pulse stats dashboard" width="100%">
</p>

## Pulse — the server

A single C# executable. Scans your library, serves the Pulse API, hosts a tablet-optimized web player.

See [Pulse/README.md](Pulse/README.md) for configuration and build instructions.

<p align="center">
  <img src="Docs/podcasts.png" alt="Pulse podcast management" width="100%">
</p>

## Thump — the Android client

A native Android app built with .NET MAUI. Streams from your Pulse server, caches tracks for offline playback, and works with Android Auto. Music, podcasts, and audiobooks.

<p align="center">
  <img src="Docs/ThumpHome.png" alt="Thump home screen" width="32%">
  <img src="Docs/ThumpPlaylist.png" alt="Thump playlist" width="32%">
  <img src="Docs/ThumpAudiobook.png" alt="Thump audiobook" width="32%">
</p>
<p align="center">
  <img src="Docs/androidauto.png" alt="Thump podcast player" width="100%">
</p>
<p align="center">
  <img src="Docs/ThumpPodcast.png" alt="Thump podcast player" width="32%">
  <img src="Docs/ThumpPodcastDetail.png" alt="Thump podcast detail" width="32%">
</p>

See [Thump/README.md](Thump/README.md) for build instructions.

## License

MIT — see [LICENSE](LICENSE).