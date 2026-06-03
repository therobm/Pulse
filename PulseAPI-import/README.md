# PulseAPI

Shared API objects and contracts for the Pulse music server and its clients (Thump).

This is the canonical wire contract for the `pulse/*` API: the request/response
objects both sides serialize against. Pulse forms its API responses from these
objects; clients deserialize into them. It is a pure contract assembly — no
transport, no business logic, no database concerns.

## Relationship to existing types

- Pulse's `*Info` classes (`TrackInfo`, `AlbumInfo`, …) are **database / runtime
  shapes**, not API objects. They stay where they are; the server maps them onto
  these contract objects when forming a response.
- Thump's `Pulse*` client types are superseded by these objects over time. For
  now PulseAPI sits alongside the existing hand-written types on both sides while
  the contract is designed and iterated.

## Consumption

Added to Pulse and Thump as a **git submodule** and referenced as a project
reference. The assembly targets `netstandard2.0` so both the `net8.0` server and
the `net10.0-android` client can reference it without friction.

## Docs

- [`Docs/API.md`](Docs/API.md) — endpoint surface (work in progress).
