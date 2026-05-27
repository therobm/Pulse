package com.therobm.thump.library

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.therobm.thump.ThumpColors
import com.therobm.thump.art.ArtImage
import com.therobm.thump.data.Album
import com.therobm.thump.data.AlbumSort
import com.therobm.thump.data.Artist
import com.therobm.thump.data.Genre
import com.therobm.thump.data.Playlist
import com.therobm.thump.data.ThumpData
import com.therobm.thump.data.ThumpDataNotConfigured

private const val ALBUM_LIST_PAGE_SIZE: Int = 500
private const val ROW_ART_REQUEST_SIZE_PX: Int = 150
private const val ROW_THUMB_SIZE_DP: Int = 56
private const val NO_SERVER_CONFIGURED_MESSAGE: String = "No server configured"

/**
 * The Library tab. Chip row at the top picks between Artists, Albums, Playlists, and Genres;
 * the selected chip drives the list below. Each tab's data loads lazily on first selection
 * through ThumpData and stays cached for the lifetime of the screen.
 *
 * Skipped for this iteration (will come in follow-ups): alphabetical section headers, fast
 * scroll for long lists. Genre rows render the flat placeholder tile because the Subsonic and
 * Pulse wires do not expose a canonical genre cover-art id today; server-side genre composites
 * land in a separate Pulse-side bug.
 */
@Composable
fun LibraryScreen(
    thumpData: ThumpData,
    onArtistSelected: (String) -> Unit,
    onAlbumSelected: (String) -> Unit,
    onPlaylistSelected: (String) -> Unit,
    onGenreSelected: (String) -> Unit,
    contentPadding: PaddingValues,
    modifier: Modifier,
) {
    // Save the chip ordinal so navigating into a detail screen and back restores the user's
    // last-selected chip. Storing the ordinal (Int) avoids needing a custom Saver for the enum.
    var selectedChipOrdinal by rememberSaveable { mutableStateOf(0) }
    val selectedChip: LibraryChip = LibraryChip.values()[selectedChipOrdinal]

    var artistsState: LibraryLoadState<List<Artist>> by remember(thumpData) {
        mutableStateOf(LibraryLoadState.Idle)
    }
    var albumsState: LibraryLoadState<List<Album>> by remember(thumpData) {
        mutableStateOf(LibraryLoadState.Idle)
    }
    var playlistsState: LibraryLoadState<List<Playlist>> by remember(thumpData) {
        mutableStateOf(LibraryLoadState.Idle)
    }
    var genresState: LibraryLoadState<List<Genre>> by remember(thumpData) {
        mutableStateOf(LibraryLoadState.Idle)
    }

    LaunchedEffect(selectedChip, thumpData) {
        when (selectedChip) {
            LibraryChip.Artists -> {
                if (artistsState is LibraryLoadState.Idle) {
                    artistsState = LibraryLoadState.Loading
                    try {
                        val loaded: List<Artist> = thumpData.getAllArtists()
                        artistsState = LibraryLoadState.Loaded(loaded)
                    } catch (notConfigured: ThumpDataNotConfigured) {
                        artistsState = LibraryLoadState.Failed(NO_SERVER_CONFIGURED_MESSAGE)
                    }
                }
            }
            LibraryChip.Albums -> {
                if (albumsState is LibraryLoadState.Idle) {
                    albumsState = LibraryLoadState.Loading
                    try {
                        val loaded: List<Album> = thumpData.getAllAlbums(
                            sort = AlbumSort.AlphabeticalByName,
                            limit = ALBUM_LIST_PAGE_SIZE,
                            offset = 0,
                        )
                        albumsState = LibraryLoadState.Loaded(loaded)
                    } catch (notConfigured: ThumpDataNotConfigured) {
                        albumsState = LibraryLoadState.Failed(NO_SERVER_CONFIGURED_MESSAGE)
                    }
                }
            }
            LibraryChip.Playlists -> {
                if (playlistsState is LibraryLoadState.Idle) {
                    playlistsState = LibraryLoadState.Loading
                    try {
                        val loaded: List<Playlist> = thumpData.getAllPlaylists()
                        playlistsState = LibraryLoadState.Loaded(loaded)
                    } catch (notConfigured: ThumpDataNotConfigured) {
                        playlistsState = LibraryLoadState.Failed(NO_SERVER_CONFIGURED_MESSAGE)
                    }
                }
            }
            LibraryChip.Genres -> {
                if (genresState is LibraryLoadState.Idle) {
                    genresState = LibraryLoadState.Loading
                    try {
                        val loaded: List<Genre> = thumpData.getGenres()
                        genresState = LibraryLoadState.Loaded(loaded)
                    } catch (notConfigured: ThumpDataNotConfigured) {
                        genresState = LibraryLoadState.Failed(NO_SERVER_CONFIGURED_MESSAGE)
                    }
                }
            }
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ThumpColors.Background)
            .padding(contentPadding),
    ) {
        LibraryChipRow(
            selectedChip = selectedChip,
            onChipSelected = { newChip: LibraryChip -> selectedChipOrdinal = newChip.ordinal },
        )
        when (selectedChip) {
            LibraryChip.Artists -> {
                ArtistsList(
                    state = artistsState,
                    thumpData = thumpData,
                    onArtistSelected = onArtistSelected,
                )
            }
            LibraryChip.Albums -> {
                AlbumsList(
                    state = albumsState,
                    thumpData = thumpData,
                    onAlbumSelected = onAlbumSelected,
                )
            }
            LibraryChip.Playlists -> {
                PlaylistsList(
                    state = playlistsState,
                    thumpData = thumpData,
                    onPlaylistSelected = onPlaylistSelected,
                )
            }
            LibraryChip.Genres -> {
                GenresList(
                    state = genresState,
                    thumpData = thumpData,
                    onGenreSelected = onGenreSelected,
                )
            }
        }
    }
}

@Composable
private fun LibraryChipRow(
    selectedChip: LibraryChip,
    onChipSelected: (LibraryChip) -> Unit,
) {
    val chips: Array<LibraryChip> = LibraryChip.values()
    LazyRow(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        items(items = chips.toList(), key = { chip: LibraryChip -> chip.name }) { chip: LibraryChip ->
            val isSelected: Boolean = chip == selectedChip
            FilterChip(
                selected = isSelected,
                onClick = { onChipSelected(chip) },
                label = { Text(text = chip.label) },
                colors = FilterChipDefaults.filterChipColors(
                    containerColor = ThumpColors.Surface,
                    labelColor = ThumpColors.OnSurface,
                    selectedContainerColor = ThumpColors.Accent,
                    selectedLabelColor = ThumpColors.OnBackground,
                ),
            )
        }
    }
}

@Composable
private fun ArtistsList(
    state: LibraryLoadState<List<Artist>>,
    thumpData: ThumpData,
    onArtistSelected: (String) -> Unit,
) {
    when (state) {
        is LibraryLoadState.Idle, LibraryLoadState.Loading -> {
            CenteredSpinner()
        }
        is LibraryLoadState.Failed -> {
            ErrorText(message = state.message)
        }
        is LibraryLoadState.Loaded -> {
            if (state.value.isEmpty()) {
                EmptyText(message = "No artists yet")
                return
            }
            LazyColumn(modifier = Modifier.fillMaxSize()) {
                items(items = state.value, key = { artist: Artist -> artist.artistId }) { artist: Artist ->
                    LibraryArtistRow(
                        artist = artist,
                        thumpData = thumpData,
                        onTapped = { onArtistSelected(artist.artistId) },
                    )
                }
            }
        }
    }
}

@Composable
private fun AlbumsList(
    state: LibraryLoadState<List<Album>>,
    thumpData: ThumpData,
    onAlbumSelected: (String) -> Unit,
) {
    when (state) {
        is LibraryLoadState.Idle, LibraryLoadState.Loading -> {
            CenteredSpinner()
        }
        is LibraryLoadState.Failed -> {
            ErrorText(message = state.message)
        }
        is LibraryLoadState.Loaded -> {
            if (state.value.isEmpty()) {
                EmptyText(message = "No albums yet")
                return
            }
            LazyColumn(modifier = Modifier.fillMaxSize()) {
                items(items = state.value, key = { album: Album -> album.albumId }) { album: Album ->
                    LibraryAlbumRow(
                        album = album,
                        thumpData = thumpData,
                        onTapped = { onAlbumSelected(album.albumId) },
                    )
                }
            }
        }
    }
}

@Composable
private fun PlaylistsList(
    state: LibraryLoadState<List<Playlist>>,
    thumpData: ThumpData,
    onPlaylistSelected: (String) -> Unit,
) {
    when (state) {
        is LibraryLoadState.Idle, LibraryLoadState.Loading -> {
            CenteredSpinner()
        }
        is LibraryLoadState.Failed -> {
            ErrorText(message = state.message)
        }
        is LibraryLoadState.Loaded -> {
            if (state.value.isEmpty()) {
                EmptyText(message = "No playlists yet")
                return
            }
            LazyColumn(modifier = Modifier.fillMaxSize()) {
                items(items = state.value, key = { playlist: Playlist -> playlist.playlistId }) { playlist: Playlist ->
                    LibraryPlaylistRow(
                        playlist = playlist,
                        thumpData = thumpData,
                        onTapped = { onPlaylistSelected(playlist.playlistId) },
                    )
                }
            }
        }
    }
}

@Composable
private fun GenresList(
    state: LibraryLoadState<List<Genre>>,
    thumpData: ThumpData,
    onGenreSelected: (String) -> Unit,
) {
    when (state) {
        is LibraryLoadState.Idle, LibraryLoadState.Loading -> {
            CenteredSpinner()
        }
        is LibraryLoadState.Failed -> {
            ErrorText(message = state.message)
        }
        is LibraryLoadState.Loaded -> {
            if (state.value.isEmpty()) {
                EmptyText(message = "No genres yet")
                return
            }
            LazyColumn(modifier = Modifier.fillMaxSize()) {
                items(items = state.value, key = { genre: Genre -> genre.name }) { genre: Genre ->
                    LibraryGenreRow(
                        genre = genre,
                        thumpData = thumpData,
                        onTapped = { onGenreSelected(genre.name) },
                    )
                }
            }
        }
    }
}

@Composable
private fun LibraryArtistRow(
    artist: Artist,
    thumpData: ThumpData,
    onTapped: () -> Unit,
) {
    LibraryListRow(
        title = artist.name,
        subtitle = buildArtistSubtitle(artist),
        leading = {
            val thumbModifier: Modifier = Modifier
                .size(ROW_THUMB_SIZE_DP.dp)
                .clip(CircleShape)
            ArtImage(
                thumpData = thumpData,
                artId = artist.coverArtId,
                sizePx = ROW_ART_REQUEST_SIZE_PX,
                contentDescription = null,
                modifier = thumbModifier,
            )
        },
        onTapped = onTapped,
    )
}

@Composable
private fun LibraryAlbumRow(
    album: Album,
    thumpData: ThumpData,
    onTapped: () -> Unit,
) {
    val artistText: String
    if (album.artistName == null) {
        artistText = ""
    } else {
        artistText = album.artistName
    }
    LibraryListRow(
        title = album.name,
        subtitle = artistText,
        leading = {
            val thumbModifier: Modifier = Modifier
                .size(ROW_THUMB_SIZE_DP.dp)
                .clip(RoundedCornerShape(8.dp))
            ArtImage(
                thumpData = thumpData,
                artId = album.coverArtId,
                sizePx = ROW_ART_REQUEST_SIZE_PX,
                contentDescription = null,
                modifier = thumbModifier,
            )
        },
        onTapped = onTapped,
    )
}

@Composable
private fun LibraryPlaylistRow(
    playlist: Playlist,
    thumpData: ThumpData,
    onTapped: () -> Unit,
) {
    LibraryListRow(
        title = playlist.name,
        subtitle = buildPlaylistSubtitle(playlist),
        leading = {
            val thumbModifier: Modifier = Modifier
                .size(ROW_THUMB_SIZE_DP.dp)
                .clip(RoundedCornerShape(8.dp))
            ArtImage(
                thumpData = thumpData,
                artId = playlist.coverArtId,
                sizePx = ROW_ART_REQUEST_SIZE_PX,
                contentDescription = null,
                modifier = thumbModifier,
            )
        },
        onTapped = onTapped,
    )
}

@Composable
private fun LibraryGenreRow(
    genre: Genre,
    thumpData: ThumpData,
    onTapped: () -> Unit,
) {
    LibraryListRow(
        title = genre.name,
        subtitle = buildGenreSubtitle(genre),
        leading = {
            val thumbModifier: Modifier = Modifier
                .size(ROW_THUMB_SIZE_DP.dp)
                .clip(RoundedCornerShape(8.dp))
            // No canonical genre cover-art id in Subsonic or Pulse today; pass null and let
            // ArtImage render its flat placeholder. Server-side genre composites are a separate
            // Pulse-side bug.
            ArtImage(
                thumpData = thumpData,
                artId = null,
                sizePx = ROW_ART_REQUEST_SIZE_PX,
                contentDescription = null,
                modifier = thumbModifier,
            )
        },
        onTapped = onTapped,
    )
}

@Composable
private fun LibraryListRow(
    title: String,
    subtitle: String,
    leading: @Composable () -> Unit,
    onTapped: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onTapped)
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        leading()
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = title,
                style = MaterialTheme.typography.bodyMedium,
                color = ThumpColors.OnBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (subtitle.isNotEmpty()) {
                Text(
                    text = subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = ThumpColors.TextSecondary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

@Composable
private fun CenteredSpinner() {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ThumpColors.Background),
        contentAlignment = Alignment.Center,
    ) {
        CircularProgressIndicator(color = ThumpColors.Accent)
    }
}

@Composable
private fun ErrorText(message: String) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text = message, color = ThumpColors.TextSecondary)
    }
}

@Composable
private fun EmptyText(message: String) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text = message, color = ThumpColors.TextSecondary)
    }
}

private fun buildArtistSubtitle(artist: Artist): String {
    if (artist.albumCount <= 0) {
        return ""
    }
    return artist.albumCount.toString() + " albums"
}

private fun buildPlaylistSubtitle(playlist: Playlist): String {
    val songCount: Int?
    songCount = playlist.songCount
    if (songCount == null || songCount <= 0) {
        return ""
    }
    return songCount.toString() + " tracks"
}

private fun buildGenreSubtitle(genre: Genre): String {
    val parts: ArrayList<String> = ArrayList<String>(2)
    val songCount: Int? = genre.songCount
    if (songCount != null && songCount > 0) {
        parts.add(songCount.toString() + " songs")
    }
    val albumCount: Int? = genre.albumCount
    if (albumCount != null && albumCount > 0) {
        parts.add(albumCount.toString() + " albums")
    }
    return parts.joinToString(separator = " • ")
}

private sealed interface LibraryLoadState<out T> {
    object Idle : LibraryLoadState<Nothing>
    object Loading : LibraryLoadState<Nothing>
    data class Loaded<T>(val value: T) : LibraryLoadState<T>
    data class Failed(val message: String) : LibraryLoadState<Nothing>
}

private enum class LibraryChip(val label: String) {
    Artists("Artists"),
    Albums("Albums"),
    Playlists("Playlists"),
    Genres("Genres"),
}
