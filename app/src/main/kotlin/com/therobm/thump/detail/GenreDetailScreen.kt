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
import com.therobm.thump.data.ThumpData
import com.therobm.thump.data.ThumpDataNotConfigured
import com.therobm.thump.data.Track
import com.therobm.thump.playback.PlaybackSource
import com.therobm.thump.playback.PlaybackSourceKind
import java.io.IOException

private const val GENRE_SONG_PAGE_SIZE: Int = 100
private const val COVER_ART_REQUEST_SIZE: Int = 600
private const val ROW_ART_REQUEST_SIZE: Int = 150
private const val NO_SERVER_CONFIGURED_MESSAGE: String = "No server configured"

/**
 * Detail screen for a genre: a placeholder tile at the top (genre has no canonical cover-art id
 * today; the Pulse-side gap is tracked separately), Play and Shuffle across the loaded songs,
 * then a flat song list. Capped at GENRE_SONG_PAGE_SIZE for now — paging is a follow-up if real
 * libraries push past it.
 */
@Composable
fun GenreDetailScreen(
    genreName: String,
    thumpData: ThumpData,
    onBackPressed: () -> Unit,
    onPlayTracks: (tracks: List<Track>, startIndex: Int, source: PlaybackSource?) -> Unit,
    contentPadding: PaddingValues,
    modifier: Modifier,
) {
    var loadState: DetailLoadState<List<Track>> by remember(genreName) {
        mutableStateOf<DetailLoadState<List<Track>>>(DetailLoadState.Loading)
    }

    LaunchedEffect(genreName, thumpData) {
        try {
            val tracks: List<Track> = thumpData.getTracksByGenre(
                genre = genreName,
                limit = GENRE_SONG_PAGE_SIZE,
                offset = 0,
            )
            loadState = DetailLoadState.Loaded(tracks)
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
        DetailTopBar(title = "Genre", onBackPressed = onBackPressed)

        val currentLoadState: DetailLoadState<List<Track>> = loadState
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
                GenreDetailContent(
                    genreName = genreName,
                    tracks = currentLoadState.value,
                    thumpData = thumpData,
                    onPlayTracks = onPlayTracks,
                )
            }
        }
    }
}

@Composable
private fun GenreDetailContent(
    genreName: String,
    tracks: List<Track>,
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
                    artId = null,
                    sizePx = COVER_ART_REQUEST_SIZE,
                    contentDescription = null,
                    modifier = artModifier,
                )

                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    text = genreName,
                    style = MaterialTheme.typography.titleLarge,
                    color = ThumpColors.OnBackground,
                    textAlign = TextAlign.Center,
                )
                if (tracks.isNotEmpty()) {
                    Text(
                        text = tracks.size.toString() + " tracks",
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
                        if (tracks.isNotEmpty()) {
                            onPlayTracks(tracks, 0, PlaybackSource(PlaybackSourceKind.Genre, genreName))
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
                        if (tracks.isNotEmpty()) {
                            onPlayTracks(tracks.shuffled(), 0, PlaybackSource(PlaybackSourceKind.Genre, genreName))
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

        itemsIndexed(
            items = tracks,
            key = { rowIndex, track -> rowIndex.toString() + ":" + track.trackId },
        ) { rowIndex: Int, track: Track ->
            GenreTrackRow(
                track = track,
                thumpData = thumpData,
                onTapped = {
                    if (tracks.isNotEmpty()) {
                        onPlayTracks(tracks, rowIndex, PlaybackSource(PlaybackSourceKind.Genre, genreName))
                    }
                },
            )
        }
    }
}

@Composable
private fun GenreTrackRow(
    track: Track,
    thumpData: ThumpData,
    onTapped: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onTapped)
            .padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val thumbModifier: Modifier = Modifier
            .size(48.dp)
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
