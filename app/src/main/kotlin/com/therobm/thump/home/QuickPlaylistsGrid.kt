package com.therobm.thump.home

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.therobm.thump.ThumpColors
import com.therobm.thump.art.ArtImage
import com.therobm.thump.data.Playlist
import com.therobm.thump.data.ThumpData
import java.io.IOException

private const val QUICK_GRID_PLAYLIST_COUNT: Int = 8
private const val QUICK_TILE_ART_SIZE_DP: Int = 56
private const val QUICK_TILE_MIN_HEIGHT_DP: Int = 56
private const val QUICK_TILE_ART_REQUEST_SIZE_PX: Int = 112
private const val QUICK_PLAY_BUTTON_SIZE_DP: Int = 36

/**
 * A 2-column grid of up to 8 playlists, pinned to the top of Home above the carousels.
 *
 * Mirrors the Pulse web client's quick grid: art on the left, name in the middle, instant-play
 * button on the right. Tapping the row navigates to the playlist detail; tapping the play
 * button starts the playlist in place. Both callbacks are owned by the caller (MainActivity)
 * because the audio path still uses the SubsonicClient stream URL shape until the audio port
 * lands — this composable only renders, fetches metadata via ThumpData, and emits intents.
 *
 * Tiles render the playlist's server-supplied `coverArtId` via `ArtImage`. When the server
 * does not supply one, the tile shows the same surface-coloured placeholder ArtImage uses for
 * null ids — the architecture forbids client-side composite synthesis from entry covers.
 */
@Composable
fun QuickPlaylistsGrid(
    thumpData: ThumpData,
    onPlaylistSelected: (playlistId: String, playlistName: String) -> Unit,
    onPlayPlaylist: (playlistId: String, playlistName: String) -> Unit,
) {
    val playlistsState: MutableState<List<Playlist>> = remember(thumpData) {
        mutableStateOf<List<Playlist>>(emptyList<Playlist>())
    }
    val hasLoadedState: MutableState<Boolean> = remember(thumpData) {
        mutableStateOf(false)
    }

    LaunchedEffect(thumpData) {
        val allPlaylists: List<Playlist>
        try {
            allPlaylists = thumpData.getAllPlaylists()
        } catch (loadFailure: IOException) {
            hasLoadedState.value = true
            return@LaunchedEffect
        }
        val takeCount: Int
        if (allPlaylists.size < QUICK_GRID_PLAYLIST_COUNT) {
            takeCount = allPlaylists.size
        } else {
            takeCount = QUICK_GRID_PLAYLIST_COUNT
        }
        val firstN: ArrayList<Playlist> = ArrayList<Playlist>(takeCount)
        for (playlistIndex in 0 until takeCount) {
            firstN.add(allPlaylists[playlistIndex])
        }
        playlistsState.value = firstN
        hasLoadedState.value = true
    }

    if (!hasLoadedState.value) {
        return
    }
    val playlists: List<Playlist> = playlistsState.value
    if (playlists.isEmpty()) {
        return
    }

    val rowCount: Int = (playlists.size + 1) / 2
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        for (rowIndex in 0 until rowCount) {
            val leftPlaylistIndex: Int = rowIndex * 2
            val rightPlaylistIndex: Int = leftPlaylistIndex + 1
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                QuickPlaylistTile(
                    playlist = playlists[leftPlaylistIndex],
                    thumpData = thumpData,
                    onTapped = {
                        val tappedPlaylist: Playlist = playlists[leftPlaylistIndex]
                        onPlaylistSelected(tappedPlaylist.playlistId, tappedPlaylist.name)
                    },
                    onQuickPlayClicked = {
                        val targetPlaylist: Playlist = playlists[leftPlaylistIndex]
                        onPlayPlaylist(targetPlaylist.playlistId, targetPlaylist.name)
                    },
                    modifier = Modifier.weight(1f),
                )
                if (rightPlaylistIndex < playlists.size) {
                    QuickPlaylistTile(
                        playlist = playlists[rightPlaylistIndex],
                        thumpData = thumpData,
                        onTapped = {
                            val tappedPlaylist: Playlist = playlists[rightPlaylistIndex]
                            onPlaylistSelected(tappedPlaylist.playlistId, tappedPlaylist.name)
                        },
                        onQuickPlayClicked = {
                            val targetPlaylist: Playlist = playlists[rightPlaylistIndex]
                            onPlayPlaylist(targetPlaylist.playlistId, targetPlaylist.name)
                        },
                        modifier = Modifier.weight(1f),
                    )
                } else {
                    // Empty right cell when the row has an odd remainder so the grid keeps its
                    // column alignment instead of stretching the lone left tile.
                    Spacer(modifier = Modifier.weight(1f))
                }
            }
        }
    }
}

@Composable
private fun QuickPlaylistTile(
    playlist: Playlist,
    thumpData: ThumpData,
    onTapped: () -> Unit,
    onQuickPlayClicked: () -> Unit,
    modifier: Modifier,
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .height(QUICK_TILE_MIN_HEIGHT_DP.dp)
            .clip(RoundedCornerShape(8.dp))
            .background(ThumpColors.Surface)
            .clickable(onClick = onTapped),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        ArtImage(
            thumpData = thumpData,
            artId = playlist.coverArtId,
            sizePx = QUICK_TILE_ART_REQUEST_SIZE_PX,
            contentDescription = null,
            modifier = Modifier.size(QUICK_TILE_ART_SIZE_DP.dp),
        )
        Spacer(modifier = Modifier.width(12.dp))
        Text(
            text = playlist.name,
            style = MaterialTheme.typography.bodyMedium,
            color = ThumpColors.OnSurface,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        IconButton(
            onClick = onQuickPlayClicked,
            modifier = Modifier
                .padding(end = 8.dp)
                .size(QUICK_PLAY_BUTTON_SIZE_DP.dp)
                .clip(CircleShape),
            colors = IconButtonDefaults.iconButtonColors(
                containerColor = ThumpColors.Accent,
                contentColor = ThumpColors.Background,
            ),
        ) {
            Icon(imageVector = Icons.Filled.PlayArrow, contentDescription = "Play")
        }
    }
}
