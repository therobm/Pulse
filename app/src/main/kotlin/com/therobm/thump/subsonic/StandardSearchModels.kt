package com.therobm.thump.subsonic

import kotlinx.serialization.Serializable

/**
 * Wire shape for /rest/search3.
 *
 * The endpoint returns three parallel result lists. Each list reuses an existing per-domain
 * type so the search UI doesn't have to map to a separate set of models. Lists default to
 * empty for servers that omit a category entirely (e.g. only songs match a query).
 */
@Serializable
data class StandardSearchResult3Payload(
    val artist: List<StandardLibraryArtist> = emptyList(),
    val album: List<StandardAlbumSummary> = emptyList(),
    val song: List<StandardSongDetail> = emptyList(),
)
