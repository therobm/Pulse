package com.therobm.thump.detail

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
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
import com.therobm.thump.art.CompositeArtTile
import com.therobm.thump.playback.PlaybackQueueItem
import com.therobm.thump.playback.PlaybackSource
import com.therobm.thump.playback.PlaybackSourceKind
import com.therobm.thump.subsonic.StandardSongDetail
import com.therobm.thump.subsonic.SubsonicClient
import com.therobm.thump.subsonic.SubsonicResult

private const val GENRE_SONG_PAGE_SIZE: Int = 100
private const val COVER_ART_REQUEST_SIZE: Int = 600
private const val ROW_ART_REQUEST_SIZE: Int = 150

/**
 * Detail screen for a genre: a composite of up to 4 sample tracks at the top, Play and Shuffle
 * across the loaded songs, then a flat song list. Capped at GENRE_SONG_PAGE_SIZE for now —
 * paging is a follow-up if real libraries push past it.
 */
@Composable
fun GenreDetailScreen(
    genreName: String,
    subsonicClient: SubsonicClient,
    onBackPressed: () -> Unit,
    onPlayQueue: (List<PlaybackQueueItem>, Int, PlaybackSource?) -> Unit,
    contentPadding: PaddingValues,
    modifier: Modifier,
) {
    var loadState: DetailLoadState<List<StandardSongDetail>> by remember(genreName) {
        mutableStateOf(DetailLoadState.Loading)
    }

    LaunchedEffect(genreName, subsonicClient) {
        val result = subsonicClient.getSongsByGenre(
            genreName = genreName,
            count = GENRE_SONG_PAGE_SIZE,
            offset = 0,
        )
        when (result) {
            is SubsonicResult.Ok -> {
                loadState = DetailLoadState.Loaded(result.value)
            }
            else -> {
                loadState = DetailLoadState.Failed(describeSubsonicFailure(result))
            }
        }
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ThumpColors.Background)
            .padding(contentPadding),
    ) {
        DetailTopBar(title = "Genre", onBackPressed = onBackPressed)

        val currentLoadState = loadState
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
                    songs = currentLoadState.value,
                    subsonicClient = subsonicClient,
                    onPlayQueue = onPlayQueue,
                )
            }
        }
    }
}

@Composable
private fun GenreDetailContent(
    genreName: String,
    songs: List<StandardSongDetail>,
    subsonicClient: SubsonicClient,
    onPlayQueue: (List<PlaybackQueueItem>, Int, PlaybackSource?) -> Unit,
) {
    val coverArtIds = ArrayList<String>(songs.size)
    val songCount = songs.size
    for (songIndex in 0 until songCount) {
        val candidate = songs[songIndex].coverArt
        if (candidate != null) {
            coverArtIds.add(candidate)
        }
    }

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
                val artModifier = Modifier
                    .fillMaxWidth(0.8f)
                    .aspectRatio(1f)
                    .clip(RoundedCornerShape(12.dp))
                CompositeArtTile(
                    coverArtIds = coverArtIds,
                    subsonicClient = subsonicClient,
                    requestSizePx = COVER_ART_REQUEST_SIZE,
                    modifier = artModifier,
                )

                Spacer(modifier = Modifier.height(16.dp))
                Text(
                    text = genreName,
                    style = MaterialTheme.typography.titleLarge,
                    color = ThumpColors.OnBackground,
                    textAlign = TextAlign.Center,
                )
                if (songs.isNotEmpty()) {
                    Text(
                        text = songs.size.toString() + " tracks",
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
                        val queue = buildGenreQueue(songs, subsonicClient)
                        if (queue.isNotEmpty()) {
                            onPlayQueue(queue, 0, PlaybackSource(PlaybackSourceKind.Genre, genreName))
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
                        val queue = buildGenreQueue(songs, subsonicClient).shuffled()
                        if (queue.isNotEmpty()) {
                            onPlayQueue(queue, 0, PlaybackSource(PlaybackSourceKind.Genre, genreName))
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
            items = songs,
            key = { rowIndex, song -> rowIndex.toString() + ":" + song.id },
        ) { rowIndex: Int, song: StandardSongDetail ->
            GenreTrackRow(
                song = song,
                onTapped = {
                    val queue = buildGenreQueue(songs, subsonicClient)
                    if (queue.isNotEmpty()) {
                        onPlayQueue(queue, rowIndex, PlaybackSource(PlaybackSourceKind.Genre, genreName))
                    }
                },
            )
        }
    }
}

@Composable
private fun GenreTrackRow(
    song: StandardSongDetail,
    onTapped: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onTapped)
            .padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = song.title,
                style = MaterialTheme.typography.bodyMedium,
                color = ThumpColors.OnBackground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            val artistText = textOrFallback(song.artist, "")
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
            text = formatDurationSeconds(song.duration),
            style = MaterialTheme.typography.bodySmall,
            color = ThumpColors.TextSecondary,
        )
    }
}

private fun buildGenreQueue(
    songs: List<StandardSongDetail>,
    subsonicClient: SubsonicClient,
): List<PlaybackQueueItem> {
    val result = ArrayList<PlaybackQueueItem>(songs.size)
    val songCount = songs.size
    for (songIndex in 0 until songCount) {
        val song = songs[songIndex]
        val coverArtUrl: String?
        val coverArtId = song.coverArt
        if (coverArtId == null) {
            coverArtUrl = null
        } else {
            coverArtUrl = subsonicClient.buildCoverArtUrl(coverArtId, ROW_ART_REQUEST_SIZE)
        }
        result.add(
            PlaybackQueueItem(
                trackId = song.id,
                streamUrl = subsonicClient.buildStreamUrl(song.id),
                title = song.title,
                artist = textOrFallback(song.artist, ""),
                album = song.album,
                coverArtUrl = coverArtUrl,
            )
        )
    }
    return result
}
