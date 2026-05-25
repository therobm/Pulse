package com.therobm.thump.search

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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextFieldDefaults
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
import coil.compose.AsyncImage
import com.therobm.thump.ThumpColors
import com.therobm.thump.playback.PlaybackQueueItem
import com.therobm.thump.playback.PlaybackSource
import com.therobm.thump.subsonic.StandardAlbumSummary
import com.therobm.thump.subsonic.StandardLibraryArtist
import com.therobm.thump.subsonic.StandardSearchResult3Payload
import com.therobm.thump.subsonic.StandardSongDetail
import com.therobm.thump.subsonic.SubsonicClient
import com.therobm.thump.subsonic.SubsonicResult
import kotlinx.coroutines.delay

private const val DEBOUNCE_MS: Long = 300L
private const val RESULTS_PER_CATEGORY: Int = 20
private const val ROW_THUMB_SIZE_DP: Int = 56
private const val ROW_ART_REQUEST_SIZE_PX: Int = 150

/**
 * Real Search tab. Single text input at the top; below it, three sections (Artists / Albums /
 * Songs) populated by /rest/search3. Input is debounced 300ms so we don't fire a request on
 * every keystroke.
 *
 * Empty query renders the placeholder prompt. Tapping a row opens the matching detail screen,
 * except for songs which play immediately as a single-track queue.
 */
@Composable
fun SearchScreen(
    subsonicClient: SubsonicClient,
    onArtistSelected: (String) -> Unit,
    onAlbumSelected: (String) -> Unit,
    onPlayQueue: (List<PlaybackQueueItem>, Int, PlaybackSource?) -> Unit,
    contentPadding: PaddingValues,
    modifier: Modifier,
) {
    var query by rememberSaveable { mutableStateOf("") }
    var loadState: SearchLoadState by remember(subsonicClient) {
        mutableStateOf(SearchLoadState.Empty)
    }

    LaunchedEffect(query, subsonicClient) {
        if (query.isBlank()) {
            loadState = SearchLoadState.Empty
            return@LaunchedEffect
        }
        delay(DEBOUNCE_MS)
        loadState = SearchLoadState.Loading
        val result = subsonicClient.search3(
            query = query.trim(),
            artistCount = RESULTS_PER_CATEGORY,
            albumCount = RESULTS_PER_CATEGORY,
            songCount = RESULTS_PER_CATEGORY,
        )
        loadState = when (result) {
            is SubsonicResult.Ok -> {
                SearchLoadState.Loaded(result.value)
            }
            else -> {
                SearchLoadState.Failed(describeFailure(result))
            }
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ThumpColors.Background)
            .padding(contentPadding),
    ) {
        OutlinedTextField(
            value = query,
            onValueChange = { newValue: String -> query = newValue },
            placeholder = { Text(text = "Search artists, albums, songs") },
            singleLine = true,
            colors = TextFieldDefaults.colors(
                focusedContainerColor = ThumpColors.Surface,
                unfocusedContainerColor = ThumpColors.Surface,
                focusedTextColor = ThumpColors.OnSurface,
                unfocusedTextColor = ThumpColors.OnSurface,
                focusedPlaceholderColor = ThumpColors.TextSecondary,
                unfocusedPlaceholderColor = ThumpColors.TextSecondary,
            ),
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
        )

        val currentLoadState = loadState
        when (currentLoadState) {
            is SearchLoadState.Empty -> {
                CenteredHint(message = "Start typing to search your library.")
            }
            is SearchLoadState.Loading -> {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    CircularProgressIndicator(color = ThumpColors.Accent)
                }
            }
            is SearchLoadState.Failed -> {
                CenteredHint(message = currentLoadState.message)
            }
            is SearchLoadState.Loaded -> {
                val payload = currentLoadState.value
                if (payload.artist.isEmpty() && payload.album.isEmpty() && payload.song.isEmpty()) {
                    CenteredHint(message = "No matches.")
                } else {
                    SearchResultsList(
                        payload = payload,
                        subsonicClient = subsonicClient,
                        onArtistSelected = onArtistSelected,
                        onAlbumSelected = onAlbumSelected,
                        onPlayQueue = onPlayQueue,
                    )
                }
            }
        }
    }
}

@Composable
private fun SearchResultsList(
    payload: StandardSearchResult3Payload,
    subsonicClient: SubsonicClient,
    onArtistSelected: (String) -> Unit,
    onAlbumSelected: (String) -> Unit,
    onPlayQueue: (List<PlaybackQueueItem>, Int, PlaybackSource?) -> Unit,
) {
    LazyColumn(modifier = Modifier.fillMaxSize()) {
        if (payload.artist.isNotEmpty()) {
            item(key = "header-artists") {
                SectionHeader(title = "Artists")
            }
            items(items = payload.artist, key = { artist -> "artist:" + artist.id }) { artist ->
                SearchArtistRow(
                    artist = artist,
                    subsonicClient = subsonicClient,
                    onTapped = { onArtistSelected(artist.id) },
                )
            }
        }
        if (payload.album.isNotEmpty()) {
            item(key = "header-albums") {
                SectionHeader(title = "Albums")
            }
            items(items = payload.album, key = { album -> "album:" + album.id }) { album ->
                SearchAlbumRow(
                    album = album,
                    subsonicClient = subsonicClient,
                    onTapped = { onAlbumSelected(album.id) },
                )
            }
        }
        if (payload.song.isNotEmpty()) {
            item(key = "header-songs") {
                SectionHeader(title = "Songs")
            }
            items(items = payload.song, key = { song -> "song:" + song.id }) { song ->
                SearchSongRow(
                    song = song,
                    subsonicClient = subsonicClient,
                    onTapped = {
                        val queueItem = buildQueueItemForSong(song, subsonicClient)
                        onPlayQueue(listOf(queueItem), 0, null)
                    },
                )
            }
        }
    }
}

@Composable
private fun SectionHeader(title: String) {
    Text(
        text = title,
        style = MaterialTheme.typography.titleMedium,
        color = ThumpColors.OnBackground,
        modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
    )
}

@Composable
private fun SearchArtistRow(
    artist: StandardLibraryArtist,
    subsonicClient: SubsonicClient,
    onTapped: () -> Unit,
) {
    val coverArtId = artist.coverArt
    val coverArtUrl: String?
    if (coverArtId == null) {
        coverArtUrl = null
    } else {
        coverArtUrl = subsonicClient.buildCoverArtUrl(coverArtId, ROW_ART_REQUEST_SIZE_PX)
    }
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onTapped)
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        val thumbModifier = Modifier
            .size(ROW_THUMB_SIZE_DP.dp)
            .clip(CircleShape)
            .background(ThumpColors.Surface)
        if (coverArtUrl == null) {
            Box(modifier = thumbModifier)
        } else {
            AsyncImage(
                model = coverArtUrl,
                contentDescription = null,
                modifier = thumbModifier,
            )
        }
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = artist.name,
                style = MaterialTheme.typography.bodyMedium,
                color = ThumpColors.OnBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            val subtitle: String
            if (artist.albumCount != null && artist.albumCount > 0) {
                subtitle = artist.albumCount.toString() + " albums"
            } else {
                subtitle = ""
            }
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
private fun SearchAlbumRow(
    album: StandardAlbumSummary,
    subsonicClient: SubsonicClient,
    onTapped: () -> Unit,
) {
    val coverArtId = album.coverArt
    val coverArtUrl: String?
    if (coverArtId == null) {
        coverArtUrl = null
    } else {
        coverArtUrl = subsonicClient.buildCoverArtUrl(coverArtId, ROW_ART_REQUEST_SIZE_PX)
    }
    val artistText: String
    if (album.artist == null) {
        artistText = ""
    } else {
        artistText = album.artist
    }
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onTapped)
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        val thumbModifier = Modifier
            .size(ROW_THUMB_SIZE_DP.dp)
            .clip(RoundedCornerShape(8.dp))
            .background(ThumpColors.Surface)
        if (coverArtUrl == null) {
            Box(modifier = thumbModifier)
        } else {
            AsyncImage(
                model = coverArtUrl,
                contentDescription = null,
                modifier = thumbModifier,
            )
        }
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = album.name,
                style = MaterialTheme.typography.bodyMedium,
                color = ThumpColors.OnBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (artistText.isNotEmpty()) {
                Text(
                    text = artistText,
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
private fun SearchSongRow(
    song: StandardSongDetail,
    subsonicClient: SubsonicClient,
    onTapped: () -> Unit,
) {
    val coverArtId = song.coverArt
    val coverArtUrl: String?
    if (coverArtId == null) {
        coverArtUrl = null
    } else {
        coverArtUrl = subsonicClient.buildCoverArtUrl(coverArtId, ROW_ART_REQUEST_SIZE_PX)
    }
    val artistText: String
    if (song.artist == null) {
        artistText = ""
    } else {
        artistText = song.artist
    }
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onTapped)
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        val thumbModifier = Modifier
            .size(ROW_THUMB_SIZE_DP.dp)
            .clip(RoundedCornerShape(8.dp))
            .background(ThumpColors.Surface)
        if (coverArtUrl == null) {
            Box(modifier = thumbModifier)
        } else {
            AsyncImage(
                model = coverArtUrl,
                contentDescription = null,
                modifier = thumbModifier,
            )
        }
        Column(modifier = Modifier.fillMaxWidth()) {
            Text(
                text = song.title,
                style = MaterialTheme.typography.bodyMedium,
                color = ThumpColors.OnBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (artistText.isNotEmpty()) {
                Text(
                    text = artistText,
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
private fun CenteredHint(message: String) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text = message, color = ThumpColors.TextSecondary)
    }
}

private fun buildQueueItemForSong(
    song: StandardSongDetail,
    subsonicClient: SubsonicClient,
): PlaybackQueueItem {
    val coverArtUrl: String?
    val coverArtId = song.coverArt
    if (coverArtId == null) {
        coverArtUrl = null
    } else {
        coverArtUrl = subsonicClient.buildCoverArtUrl(coverArtId, ROW_ART_REQUEST_SIZE_PX)
    }
    val artistText: String
    if (song.artist == null) {
        artistText = ""
    } else {
        artistText = song.artist
    }
    return PlaybackQueueItem(
        trackId = song.id,
        streamUrl = subsonicClient.buildStreamUrl(song.id),
        title = song.title,
        artist = artistText,
        album = song.album,
        coverArtUrl = coverArtUrl,
    )
}

private sealed interface SearchLoadState {
    object Empty : SearchLoadState
    object Loading : SearchLoadState
    data class Loaded(val value: StandardSearchResult3Payload) : SearchLoadState
    data class Failed(val message: String) : SearchLoadState
}

private fun describeFailure(result: SubsonicResult<*>): String {
    when (result) {
        is SubsonicResult.Ok -> {
            return "Unexpected success"
        }
        is SubsonicResult.ServerError -> {
            return "Server error " + result.code + ": " + result.message
        }
        is SubsonicResult.TransportError -> {
            return "Network error: " + result.cause.javaClass.simpleName
        }
        is SubsonicResult.MalformedResponse -> {
            return "Bad response: " + result.cause.javaClass.simpleName
        }
    }
}

