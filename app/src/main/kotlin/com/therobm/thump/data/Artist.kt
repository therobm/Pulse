package com.therobm.thump.data

/**
 * Thump-shaped artist record. The IProtocol implementations translate from their wire format
 * (Subsonic getArtist JSON, Pulse popularArtists JSON, etc.) into this single type. The rest of
 * the app sees only this; it never sees the wire shape.
 *
 * `albums` is populated by detail calls (`getArtist`) and left empty on summary calls
 * (`getAllArtists`, `getStarred`, Pulse popular-artists shelves).
 */
data class Artist(
    val artistId: String,
    val name: String,
    val albumCount: Int,
    val coverArtId: String?,
    val albums: List<Album>,
)
