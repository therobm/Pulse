package com.therobm.thump.home

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
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
import com.therobm.thump.data.ThumpData
import kotlinx.coroutines.launch

/**
 * The home screen: a vertical scroll of horizontal carousels.
 *
 * Sections load in parallel and render independently. A failed section shows its error message
 * inline so the rest of the screen stays usable. All data comes from ThumpData — the screen
 * does not know which protocol the active server speaks.
 */
@Composable
fun HomeScreen(
    thumpData: ThumpData,
    contentPadding: PaddingValues,
    onItemTapped: (HomeCarouselItem) -> Unit,
    onPlaylistSelected: (playlistId: String, playlistName: String) -> Unit,
    onPlayPlaylist: (playlistId: String, playlistName: String) -> Unit,
    modifier: Modifier,
) {
    val repository: HomeRepository = remember(thumpData) {
        HomeRepository(thumpData)
    }

    val initialSections: List<HomeSection> = remember { buildInitialSections() }
    val sectionsState: MutableState<List<HomeSection>> = remember(repository) {
        mutableStateOf<List<HomeSection>>(initialSections)
    }

    LaunchedEffect(repository) {
        val sectionKeys: Array<HomeSectionKey> = HomeSectionKey.values()
        val keyCount: Int = sectionKeys.size
        for (keyIndex in 0 until keyCount) {
            val key: HomeSectionKey = sectionKeys[keyIndex]
            launch {
                val loaded: HomeSection = repository.loadSection(key)
                sectionsState.value = replaceSection(sectionsState.value, loaded)
            }
        }
    }

    LazyColumn(
        modifier = modifier
            .fillMaxSize()
            .background(ThumpColors.Background),
        contentPadding = contentPadding,
        verticalArrangement = Arrangement.spacedBy(28.dp),
    ) {
        item {
            Spacer(modifier = Modifier.height(4.dp))
        }
        item {
            QuickPlaylistsGrid(
                thumpData = thumpData,
                onPlaylistSelected = onPlaylistSelected,
                onPlayPlaylist = onPlayPlaylist,
            )
        }
        items(
            items = sectionsState.value,
            key = { section: HomeSection -> section.key.name },
        ) { section: HomeSection ->
            HomeSectionView(
                section = section,
                thumpData = thumpData,
                onItemTapped = onItemTapped,
            )
        }
        item {
            Spacer(modifier = Modifier.height(8.dp))
        }
    }
}

@Composable
private fun HomeSectionView(
    section: HomeSection,
    thumpData: ThumpData,
    onItemTapped: (HomeCarouselItem) -> Unit,
) {
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = section.title,
            style = MaterialTheme.typography.titleLarge,
            color = ThumpColors.OnBackground,
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
        )

        val loadState: HomeSectionLoadState = section.loadState
        when (loadState) {
            is HomeSectionLoadState.Loading -> {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(180.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    CircularProgressIndicator(color = ThumpColors.Accent)
                }
            }
            is HomeSectionLoadState.Failed -> {
                Text(
                    text = loadState.message,
                    color = ThumpColors.TextSecondary,
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                )
            }
            is HomeSectionLoadState.Loaded -> {
                if (loadState.items.isEmpty()) {
                    Text(
                        text = "Nothing here yet",
                        color = ThumpColors.TextSecondary,
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                    )
                } else {
                    HomeCarouselRow(
                        items = loadState.items,
                        thumpData = thumpData,
                        onItemTapped = onItemTapped,
                    )
                }
            }
        }
    }
}

@Composable
private fun HomeCarouselRow(
    items: List<HomeCarouselItem>,
    thumpData: ThumpData,
    onItemTapped: (HomeCarouselItem) -> Unit,
) {
    LazyRow(
        modifier = Modifier.fillMaxWidth(),
        contentPadding = PaddingValues(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        items(
            items = items,
            key = { tile: HomeCarouselItem -> tile.kind.name + ":" + tile.id },
        ) { tile: HomeCarouselItem ->
            HomeCarouselTile(
                item = tile,
                thumpData = thumpData,
                onTapped = { onItemTapped(tile) },
            )
        }
    }
}

@Composable
private fun HomeCarouselTile(
    item: HomeCarouselItem,
    thumpData: ThumpData,
    onTapped: () -> Unit,
) {
    Column(
        modifier = Modifier
            .width(140.dp)
            .clickable(onClick = onTapped),
    ) {
        val tileShapeModifier: Modifier
        if (item.kind == HomeItemKind.Artist) {
            tileShapeModifier = Modifier
                .size(140.dp)
                .clip(CircleShape)
        } else {
            tileShapeModifier = Modifier
                .size(140.dp)
                .clip(RoundedCornerShape(10.dp))
        }
        ArtImage(
            thumpData = thumpData,
            artId = item.coverArtId,
            sizePx = COVER_ART_REQUEST_SIZE,
            contentDescription = null,
            modifier = tileShapeModifier,
        )

        Spacer(modifier = Modifier.height(8.dp))
        Text(
            text = item.title,
            style = MaterialTheme.typography.bodyMedium,
            color = ThumpColors.OnBackground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (item.subtitle.isNotEmpty()) {
            Text(
                text = item.subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = ThumpColors.TextSecondary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

private fun buildInitialSections(): List<HomeSection> {
    val sections: ArrayList<HomeSection> = ArrayList<HomeSection>(5)
    sections.add(HomeSection(HomeSectionKey.RecentlyPlayed, "Recently Played", HomeSectionLoadState.Loading))
    sections.add(HomeSection(HomeSectionKey.Playlists, "Your Playlists", HomeSectionLoadState.Loading))
    sections.add(HomeSection(HomeSectionKey.PopularOrFrequent, "Popular Artists", HomeSectionLoadState.Loading))
    sections.add(HomeSection(HomeSectionKey.RecentlyAdded, "Recently Added", HomeSectionLoadState.Loading))
    sections.add(HomeSection(HomeSectionKey.Favorites, "Favorites", HomeSectionLoadState.Loading))
    return sections
}

private fun replaceSection(current: List<HomeSection>, replacement: HomeSection): List<HomeSection> {
    val result: ArrayList<HomeSection> = ArrayList<HomeSection>(current.size)
    val currentCount: Int = current.size
    for (sectionIndex in 0 until currentCount) {
        val existing: HomeSection = current[sectionIndex]
        if (existing.key == replacement.key) {
            result.add(replacement)
        } else {
            result.add(existing)
        }
    }
    return result
}

private const val COVER_ART_REQUEST_SIZE: Int = 300
