package com.therobm.thump.detail

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
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
import com.therobm.thump.data.ThumpData
import com.therobm.thump.data.ThumpDataNotConfigured
import com.therobm.thump.data.Track
import com.therobm.thump.playback.PlaybackSource
import com.therobm.thump.playback.PlaybackSourceKind
import java.io.IOException

private const val COVER_ART_REQUEST_SIZE: Int = 600
private const val ROW_ART_REQUEST_SIZE: Int = 150
private const val NO_SERVER_CONFIGURED_MESSAGE: String = "No server configured"

@Composable
fun AlbumDetailScreen(
    albumId: String,
    thumpData: ThumpData,
    onBackPressed: () -> Unit,
    onPlayTracks: (tracks: List<Track>, startIndex: Int, source: PlaybackSource?) -> Unit,
    contentPadding: PaddingValues,
    modifier: Modifier,
) {
    var loadState: DetailLoadState<Album> by remember(albumId) {
        mutableStateOf<DetailLoadState<Album>>(DetailLoadState.Loading)
    }

    LaunchedEffect(albumId, thumpData) {
        try {
            val album: Album = thumpData.getAlbum(albumId)
            loadState = DetailLoadState.Loaded(album)
        } catch (notConfigured: ThumpDataNotConfigured) {
            loadState = DetailLoadState.Failed(NO_SERVER_CONFIGURED_MESSAGE)
        } catch (transportFailure: IOException) {
            loadState = DetailLoadState.Failed("Network error: " + transportFailure.javaClass.simpleName)
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ThumpColors.Background)
            .padding(contentPadding),
    ) {
        DetailTopBar(title = "Album", onBackPressed = onBackPressed)

        val currentLoadState: DetailLoadState<Album> = loadState
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
                AlbumDetailContent(
                    album = currentLoadState.value,
                    thumpData = thumpData,
                    onPlayTracks = onPlayTracks,
                )
            }
        }
    }
}

@Composable
private fun AlbumDetailContent(
    album: Album,
    thumpData: ThumpData,
    onPlayTracks: (tracks: List<Track>, startIndex: Int, source: PlaybackSource?) -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(bottom = 24.dp),
        verticalArrangement = Arrangement.spacedBy(0.dp),
    ) {
        item {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                val artModifier: Modifier = Modifier
                    .fillMaxWidth(0.8f)
                    .aspectRatio(1f)
                    .clip(RoundedCornerShape(12.dp))
                ArtImage(
                    thumpData = thumpData,
                    artId = album.coverArtId,
                    sizePx = COVER_ART_REQUEST_SIZE,
                    contentDescription = null,
                    modifier = artModifier,
                )

                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    text = album.name,
                    style = MaterialTheme.typography.titleLarge,
                    color = ThumpColors.OnBackground,
                    textAlign = TextAlign.Center,
                )
                Text(
                    text = textOrFallback(album.artistName, "Unknown artist"),
                    style = MaterialTheme.typography.bodyLarge,
                    color = ThumpColors.TextSecondary,
                    textAlign = TextAlign.Center,
                )
                val metadataLine: String = buildAlbumMetadataLine(album)
                if (metadataLine.isNotEmpty()) {
                    Text(
                        text = metadataLine,
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
                    onClick = {
                        if (album.tracks.isNotEmpty()) {
                            onPlayTracks(album.tracks, 0, PlaybackSource(PlaybackSourceKind.Album, album.name))
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = ThumpColors.Accent),
                    modifier = Modifier.weight(1f),
                ) {
                    Icon(imageVector = Icons.Filled.PlayArrow, contentDescription = null)
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(text = "Play")
                }
                OutlinedButton(
                    onClick = {
                        if (album.tracks.isNotEmpty()) {
                            onPlayTracks(album.tracks.shuffled(), 0, PlaybackSource(PlaybackSourceKind.Album, album.name))
                        }
                    },
                    modifier = Modifier.weight(1f),
                ) {
                    Icon(imageVector = Icons.Filled.Shuffle, contentDescription = null)
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(text = "Shuffle")
                }
            }
        }

        itemsIndexed(items = album.tracks, key = { _, track -> track.trackId }) { rowIndex: Int, track: Track ->
            AlbumTrackRow(
                track = track,
                thumpData = thumpData,
                rowNumber = rowIndex + 1,
                onTapped = {
                    if (album.tracks.isNotEmpty()) {
                        onPlayTracks(album.tracks, rowIndex, PlaybackSource(PlaybackSourceKind.Album, album.name))
                    }
                },
            )
        }
    }
}

@Composable
private fun AlbumTrackRow(
    track: Track,
    thumpData: ThumpData,
    rowNumber: Int,
    onTapped: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onTapped)
            .padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = rowNumber.toString(),
            style = MaterialTheme.typography.bodyMedium,
            color = ThumpColors.TextSecondary,
            modifier = Modifier.width(28.dp),
            textAlign = TextAlign.Start,
        )
        val thumbModifier: Modifier = Modifier
            .size(40.dp)
            .clip(RoundedCornerShape(6.dp))
        ArtImage(
            thumpData = thumpData,
            artId = track.coverArtId,
            sizePx = ROW_ART_REQUEST_SIZE,
            contentDescription = null,
            modifier = thumbModifier,
        )
        Spacer(modifier = Modifier.width(12.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = track.title,
                style = MaterialTheme.typography.bodyMedium,
                color = ThumpColors.OnBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            val artistText: String = textOrFallback(track.artistName, "")
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
        Spacer(modifier = Modifier.width(8.dp))
        Text(
            text = formatDurationSeconds(track.durationSeconds),
            style = MaterialTheme.typography.bodySmall,
            color = ThumpColors.TextSecondary,
        )
    }
}

private fun buildAlbumMetadataLine(album: Album): String {
    val parts: ArrayList<String> = ArrayList<String>(3)
    if (album.year != null) {
        parts.add(album.year.toString())
    }
    if (album.songCount != null && album.songCount > 0) {
        parts.add(album.songCount.toString() + " tracks")
    }
    val durationText: String = formatDurationSeconds(album.durationSeconds)
    if (durationText.isNotEmpty()) {
        parts.add(durationText)
    }
    return parts.joinToString(separator = " • ")
}
