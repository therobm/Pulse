package com.therobm.thump.detail

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Shuffle
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.therobm.thump.ThumpColors
import com.therobm.thump.art.ArtImage
import com.therobm.thump.data.Album
import com.therobm.thump.data.Artist
import com.therobm.thump.data.ThumpData
import com.therobm.thump.data.ThumpDataNotConfigured
import com.therobm.thump.data.Track
import com.therobm.thump.playback.PlaybackSource
import com.therobm.thump.playback.PlaybackSourceKind
import kotlinx.coroutines.launch
import java.io.IOException

private const val ARTIST_ART_REQUEST_SIZE: Int = 400
private const val ALBUM_ROW_ART_REQUEST_SIZE: Int = 200
private const val NO_SERVER_CONFIGURED_MESSAGE: String = "No server configured"

@Composable
fun ArtistDetailScreen(
    artistId: String,
    thumpData: ThumpData,
    onBackPressed: () -> Unit,
    onAlbumSelected: (String) -> Unit,
    onPlayTracks: (tracks: List<Track>, startIndex: Int, source: PlaybackSource?) -> Unit,
    contentPadding: PaddingValues,
    modifier: Modifier,
) {
    var loadState: DetailLoadState<Artist> by remember(artistId) {
        mutableStateOf<DetailLoadState<Artist>>(DetailLoadState.Loading)
    }
    var isLoadingTracks: Boolean by remember(artistId) { mutableStateOf(false) }
    var loadTracksError: String? by remember(artistId) { mutableStateOf<String?>(null) }

    LaunchedEffect(artistId, thumpData) {
        try {
            val artist: Artist = thumpData.getArtist(artistId)
            loadState = DetailLoadState.Loaded(artist)
        } catch (notConfigured: ThumpDataNotConfigured) {
            loadState = DetailLoadState.Failed(NO_SERVER_CONFIGURED_MESSAGE)
        } catch (transportFailure: IOException) {
            loadState = DetailLoadState.Failed("Network error: " + transportFailure.javaClass.simpleName)
        }
    }

    val coroutineScope = rememberCoroutineScope()

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ThumpColors.Background)
            .padding(contentPadding),
    ) {
        DetailTopBar(title = "Artist", onBackPressed = onBackPressed)

        val currentLoadState: DetailLoadState<Artist> = loadState
        when (currentLoadState) {
            is DetailLoadState.Loading -> {
                CenteredSpinner()
            }
            is DetailLoadState.Failed -> {
                Text(
                    text = currentLoadState.message,
                    color = ThumpColors.TextSecondary,
                    modifier = Modifier.padding(16.dp),
                )
            }
            is DetailLoadState.Loaded -> {
                val loadedArtist: Artist = currentLoadState.value
                ArtistDetailContent(
                    artist = loadedArtist,
                    thumpData = thumpData,
                    isLoadingTracks = isLoadingTracks,
                    loadTracksError = loadTracksError,
                    onAlbumSelected = onAlbumSelected,
                    onPlayClicked = {
                        loadTracksError = null
                        isLoadingTracks = true
                        coroutineScope.launch {
                            val source: PlaybackSource = PlaybackSource(PlaybackSourceKind.Artist, loadedArtist.name)
                            try {
                                val tracks: List<Track> = thumpData.getArtistTracks(loadedArtist.artistId)
                                if (tracks.isNotEmpty()) {
                                    onPlayTracks(tracks, 0, source)
                                }
                            } catch (notConfigured: ThumpDataNotConfigured) {
                                loadTracksError = NO_SERVER_CONFIGURED_MESSAGE
                            } catch (transportFailure: IOException) {
                                loadTracksError = "Network error: " + transportFailure.javaClass.simpleName
                            }
                            isLoadingTracks = false
                        }
                    },
                    onShuffleClicked = {
                        loadTracksError = null
                        isLoadingTracks = true
                        coroutineScope.launch {
                            val source: PlaybackSource = PlaybackSource(PlaybackSourceKind.Artist, loadedArtist.name)
                            try {
                                val tracks: List<Track> = thumpData.getArtistTracks(loadedArtist.artistId)
                                if (tracks.isNotEmpty()) {
                                    onPlayTracks(tracks.shuffled(), 0, source)
                                }
                            } catch (notConfigured: ThumpDataNotConfigured) {
                                loadTracksError = NO_SERVER_CONFIGURED_MESSAGE
                            } catch (transportFailure: IOException) {
                                loadTracksError = "Network error: " + transportFailure.javaClass.simpleName
                            }
                            isLoadingTracks = false
                        }
                    },
                )
            }
        }
    }
}

@Composable
private fun ArtistDetailContent(
    artist: Artist,
    thumpData: ThumpData,
    isLoadingTracks: Boolean,
    loadTracksError: String?,
    onAlbumSelected: (String) -> Unit,
    onPlayClicked: () -> Unit,
    onShuffleClicked: () -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(bottom = 24.dp),
    ) {
        item {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                val portraitModifier: Modifier = Modifier
                    .size(180.dp)
                    .clip(CircleShape)
                ArtImage(
                    thumpData = thumpData,
                    artId = artist.coverArtId,
                    sizePx = ARTIST_ART_REQUEST_SIZE,
                    contentDescription = null,
                    modifier = portraitModifier,
                )

                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    text = artist.name,
                    style = MaterialTheme.typography.titleLarge,
                    color = ThumpColors.OnBackground,
                    textAlign = TextAlign.Center,
                )
                if (artist.albumCount > 0) {
                    Text(
                        text = artist.albumCount.toString() + " albums",
                        style = MaterialTheme.typography.bodySmall,
                        color = ThumpColors.TextSecondary,
                        textAlign = TextAlign.Center,
                    )
                }
            }
        }

        item {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Button(
                    onClick = onPlayClicked,
                    enabled = !isLoadingTracks,
                    colors = ButtonDefaults.buttonColors(containerColor = ThumpColors.Accent),
                    modifier = Modifier.weight(1f),
                ) {
                    Icon(imageVector = Icons.Filled.PlayArrow, contentDescription = null)
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(text = "Play")
                }
                OutlinedButton(
                    onClick = onShuffleClicked,
                    enabled = !isLoadingTracks,
                    modifier = Modifier.weight(1f),
                ) {
                    Icon(imageVector = Icons.Filled.Shuffle, contentDescription = null)
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(text = "Shuffle")
                }
            }
        }

        if (loadTracksError != null) {
            item {
                Text(
                    text = loadTracksError,
                    color = ThumpColors.TextSecondary,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                )
            }
        }

        items(items = artist.albums, key = { album: Album -> album.albumId }) { album: Album ->
            ArtistAlbumRow(
                album = album,
                thumpData = thumpData,
                onTapped = { onAlbumSelected(album.albumId) },
            )
        }
    }
}

@Composable
private fun ArtistAlbumRow(
    album: Album,
    thumpData: ThumpData,
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
        val artModifier: Modifier = Modifier
            .size(64.dp)
            .clip(RoundedCornerShape(8.dp))
        ArtImage(
            thumpData = thumpData,
            artId = album.coverArtId,
            sizePx = ALBUM_ROW_ART_REQUEST_SIZE,
            contentDescription = null,
            modifier = artModifier,
        )
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = album.name,
                style = MaterialTheme.typography.bodyMedium,
                color = ThumpColors.OnBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            val subtitle: String = buildAlbumRowSubtitle(album)
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

private fun buildAlbumRowSubtitle(album: Album): String {
    val parts: ArrayList<String> = ArrayList<String>(2)
    if (album.year != null) {
        parts.add(album.year.toString())
    }
    if (album.songCount != null && album.songCount > 0) {
        parts.add(album.songCount.toString() + " tracks")
    }
    return parts.joinToString(separator = " • ")
}
