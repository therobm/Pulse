# Pulse

A self-hosted music ecosystem: a server that owns your library, and a client that plays it. This repository holds both halves plus the contract they share.

## Projects

### Pulse — server

A self-hosted music server built in C#. Scans a local library, serves it over HTTP to its own clients, and layers on playlist sync, scoring, smart playlists, and a tablet web client. Runs as a single executable with no external service dependencies.

See [Pulse/README.md](Pulse/README.md) for features, configuration, and build instructions.

### Thump — client

An Android music client built with .NET MAUI. Streams from a Pulse server, caches tracks for offline playback, and supports Android Auto. Lives in [Thump/](Thump).

## Shared

### PulseAPI — contract

The wire contract shared between server and client, with C# and JS bindings so both sides stay in sync. Lives in [PulseAPI/](PulseAPI).

## License

MIT — see [LICENSE](LICENSE).
