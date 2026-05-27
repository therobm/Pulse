package com.therobm.thump.playback

import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.session.LibraryResult
import androidx.media3.session.MediaLibraryService.LibraryParams
import androidx.media3.session.MediaLibraryService.MediaLibrarySession
import androidx.media3.session.MediaSession
import com.google.common.collect.ImmutableList
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import com.therobm.thump.data.Album
import com.therobm.thump.data.AlbumSort
import com.therobm.thump.data.Artist
import com.therobm.thump.data.HomeItem
import com.therobm.thump.data.HomeItemKind
import com.therobm.thump.data.Playlist
import com.therobm.thump.data.StarredCollection
import com.therobm.thump.data.ThumpData
import com.therobm.thump.data.ThumpDataNotConfigured
import com.therobm.thump.data.Track
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.guava.future
import java.io.IOException

private const val MEDIA_ID_ROOT: String = "thump-root"
private const val MEDIA_ID_HOME: String = "thump-root/home"
private const val MEDIA_ID_RECENTS: String = "thump-root/recents"
private const val MEDIA_ID_PLAYLISTS: String = "thump-root/playlists"
private const val MEDIA_ID_ARTISTS: String = "thump-root/artists"
private const val MEDIA_ID_PREFIX_PLAYLIST: String = "thump-root/playlist/"
private const val MEDIA_ID_PREFIX_ALBUM: String = "thump-root/album/"
private const val MEDIA_ID_PREFIX_ARTIST: String = "thump-root/artist/"
private const val MEDIA_ID_PREFIX_TRACK: String = "thump-track/"
// Synthetic playable IDs for the Play / Shuffle rows at the top of each album / playlist /
// artist children list. Auto sends one of these through onAddMediaItems when tapped; we
// resolve it to a full queue of real tracks and Auto plays the result in order.
private const val MEDIA_ID_PREFIX_PLAY_ALBUM: String = "thump-action/play/album/"
private const val MEDIA_ID_PREFIX_SHUFFLE_ALBUM: String = "thump-action/shuffle/album/"
private const val MEDIA_ID_PREFIX_PLAY_PLAYLIST: String = "thump-action/play/playlist/"
private const val MEDIA_ID_PREFIX_SHUFFLE_PLAYLIST: String = "thump-action/shuffle/playlist/"
private const val MEDIA_ID_PREFIX_PLAY_ARTIST: String = "thump-action/play/artist/"
private const val MEDIA_ID_PREFIX_SHUFFLE_ARTIST: String = "thump-action/shuffle/artist/"

private const val HOME_SECTION_ITEM_LIMIT: Int = 15
private const val RECENTS_TOTAL_LIMIT: Int = 15
private const val RECENTS_TRACK_SCAN_COUNT: Int = 60
private const val PLAYLISTS_FETCH_LIMIT: Int = 500
private const val COVER_ART_REQUEST_SIZE_PX: Int = 400

// Authority for the cover-art ContentProvider. Matches ThumpCoverArtProvider's manifest
// declaration. Android Auto fetches `setArtworkUri` URIs in its own process and cannot suspend
// into ThumpData; the ContentProvider bridges that gap by serving bytes out of the service-
// process ThumpData over a ParcelFileDescriptor pipe.
private const val COVER_ART_CONTENT_AUTHORITY: String = "com.therobm.thump.coverart"

// `thump://track/<id>` is the stable scheme ExoPlayer's MediaSource.Factory hands to
// `ThumpData.open(DataSpec)` for resolution. Auto rewrites each tapped MediaItem with this
// scheme in onAddMediaItems so the playback engine never sees a salted Subsonic URL.
private const val TRACK_URI_SCHEME_PREFIX: String = "thump://track/"

// Android Auto content-style hints. Attached to every browseable item's MediaMetadata extras so
// Auto knows how to render that item's children. We use grid for everything browseable
// (playlists / albums / artists / shelves) and list for playable children (tracks inside an
// album or playlist). Constants intentionally inlined as Strings/Ints — the canonical Media3
// names are not stable across versions.
private const val CONTENT_STYLE_BROWSABLE_HINT_KEY: String = "android.media.browse.CONTENT_STYLE_BROWSABLE_HINT"
private const val CONTENT_STYLE_PLAYABLE_HINT_KEY: String = "android.media.browse.CONTENT_STYLE_PLAYABLE_HINT"
private const val CONTENT_STYLE_LIST_ITEM: Int = 1
private const val CONTENT_STYLE_GRID_ITEM: Int = 2
// When set on a child, Auto renders a section header with this title above that child (and
// groups consecutive children sharing the title under one header). This is how the Home folder
// shows its five sections inline instead of forcing the user to drill into sub-folders.
private const val CONTENT_STYLE_GROUP_TITLE_HINT_KEY: String = "android.media.browse.CONTENT_STYLE_GROUP_TITLE_HINT"

private val ALL_HOME_ITEM_KINDS: Set<HomeItemKind> = setOf<HomeItemKind>(
    HomeItemKind.Track,
    HomeItemKind.Artist,
    HomeItemKind.Album,
    HomeItemKind.Playlist,
)

/**
 * Browse-tree callback for the MediaLibrarySession used by Android Auto and any other
 * MediaBrowser client. All async work is bridged from suspend code via kotlinx-coroutines-guava.
 *
 * Every wire call routes through the service-process ThumpData; the callback never sees the
 * active IProtocol implementation, never builds a URL, and never reaches SharedPreferences for
 * credentials. Cover-art URIs are `content://com.therobm.thump.coverart/<id>?size=<px>` so
 * Auto's cross-process image fetch resolves through the ContentProvider; playable URIs are
 * `thump://track/<id>` so ExoPlayer resolves bytes through ThumpData's DataSource path.
 *
 * The tree is intentionally flat one level deep — Auto users are driving, so deep nesting is a
 * non-starter. Search and per-item playback context (siblings auto-queue) are deferred follow-
 * ups.
 */
class ThumpMediaLibraryCallback(
    private val applicationCoroutineScope: CoroutineScope,
    private val thumpData: ThumpData,
    private val applicationPackageName: String,
) : MediaLibrarySession.Callback {

    override fun onGetLibraryRoot(
        session: MediaLibrarySession,
        browser: MediaSession.ControllerInfo,
        params: LibraryParams?,
    ): ListenableFuture<LibraryResult<MediaItem>> {
        val rootItem: MediaItem = buildBrowseableItem(
            mediaId = MEDIA_ID_ROOT,
            title = "Thump",
        )
        // Tell Auto explicitly that this root is neither a "recent" nor a "suggested" surface
        // and isn't an offline view. That nudges Auto's launcher heuristic toward showing the
        // browse tree on cold start when no media is loaded (when a blob is restored on
        // service start, the session reports a current item and Auto still lands on Now
        // Playing as expected).
        val rootParams: LibraryParams = LibraryParams.Builder()
            .setRecent(false)
            .setSuggested(false)
            .setOffline(false)
            .build()
        return Futures.immediateFuture(LibraryResult.ofItem(rootItem, rootParams))
    }

    override fun onGetItem(
        session: MediaLibrarySession,
        browser: MediaSession.ControllerInfo,
        mediaId: String,
    ): ListenableFuture<LibraryResult<MediaItem>> {
        // Auto sometimes asks for a single item by id (especially playables). Build a stub that
        // setMediaItem can immediately consume. For browseable shelves we return their header.
        return Futures.immediateFuture(LibraryResult.ofItem(stubForMediaId(mediaId), null))
    }

    override fun onGetChildren(
        session: MediaLibrarySession,
        browser: MediaSession.ControllerInfo,
        parentId: String,
        page: Int,
        pageSize: Int,
        params: LibraryParams?,
    ): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> {
        return applicationCoroutineScope.future {
            if (parentId == MEDIA_ID_ROOT) {
                return@future LibraryResult.ofItemList(buildRootChildren(), params)
            }
            if (parentId == MEDIA_ID_HOME) {
                return@future buildHomeChildren(params)
            }
            if (parentId == MEDIA_ID_RECENTS) {
                return@future buildRecentsChildren(params)
            }
            if (parentId == MEDIA_ID_PLAYLISTS) {
                return@future buildPlaylistsChildren(params)
            }
            if (parentId == MEDIA_ID_ARTISTS) {
                return@future buildArtistsChildren(params)
            }
            if (parentId.startsWith(MEDIA_ID_PREFIX_PLAYLIST)) {
                val playlistId: String = parentId.removePrefix(MEDIA_ID_PREFIX_PLAYLIST)
                return@future buildPlaylistChildren(playlistId, params)
            }
            if (parentId.startsWith(MEDIA_ID_PREFIX_ALBUM)) {
                val albumId: String = parentId.removePrefix(MEDIA_ID_PREFIX_ALBUM)
                return@future buildAlbumChildren(albumId, params)
            }
            if (parentId.startsWith(MEDIA_ID_PREFIX_ARTIST)) {
                val artistId: String = parentId.removePrefix(MEDIA_ID_PREFIX_ARTIST)
                return@future buildArtistChildren(artistId, params)
            }
            LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        }
    }

    /**
     * Auto resolves a tapped track by sending the original (browseable / pre-resolved) MediaItem
     * back through onAddMediaItems. We rewrite each entry to carry the actual stream URI so the
     * underlying player can fetch it.
     */
    override fun onAddMediaItems(
        mediaSession: MediaSession,
        controller: MediaSession.ControllerInfo,
        mediaItems: MutableList<MediaItem>,
    ): ListenableFuture<MutableList<MediaItem>> {
        return applicationCoroutineScope.future {
            val resolved: ArrayList<MediaItem> = ArrayList<MediaItem>(mediaItems.size)
            val incomingCount: Int = mediaItems.size
            for (incomingIndex in 0 until incomingCount) {
                val incoming: MediaItem = mediaItems[incomingIndex]
                val incomingId: String = incoming.mediaId
                val expanded: List<MediaItem>? = expandActionMediaId(incomingId)
                if (expanded != null) {
                    // Action row (Play / Shuffle) — fan out to the full queue. Auto plays the
                    // first item and queues the rest.
                    resolved.addAll(expanded)
                    continue
                }
                val trackId: String? = trackIdFromMediaId(incomingId)
                if (trackId == null) {
                    resolved.add(incoming)
                } else {
                    resolved.add(buildPlayableMediaItemWithUri(trackId, incoming.mediaMetadata))
                }
            }
            resolved
        }
    }

    /**
     * Resolve one of the synthetic Play / Shuffle action ids into the full queue of real
     * playable MediaItems. Returns null when the id is not an action (regular track id, or any
     * non-thump id).
     */
    private suspend fun expandActionMediaId(mediaId: String): List<MediaItem>? {
        if (mediaId.startsWith(MEDIA_ID_PREFIX_PLAY_ALBUM)) {
            val albumId: String = mediaId.removePrefix(MEDIA_ID_PREFIX_PLAY_ALBUM)
            return resolveAlbumQueue(albumId, shuffle = false)
        }
        if (mediaId.startsWith(MEDIA_ID_PREFIX_SHUFFLE_ALBUM)) {
            val albumId: String = mediaId.removePrefix(MEDIA_ID_PREFIX_SHUFFLE_ALBUM)
            return resolveAlbumQueue(albumId, shuffle = true)
        }
        if (mediaId.startsWith(MEDIA_ID_PREFIX_PLAY_PLAYLIST)) {
            val playlistId: String = mediaId.removePrefix(MEDIA_ID_PREFIX_PLAY_PLAYLIST)
            return resolvePlaylistQueue(playlistId, shuffle = false)
        }
        if (mediaId.startsWith(MEDIA_ID_PREFIX_SHUFFLE_PLAYLIST)) {
            val playlistId: String = mediaId.removePrefix(MEDIA_ID_PREFIX_SHUFFLE_PLAYLIST)
            return resolvePlaylistQueue(playlistId, shuffle = true)
        }
        if (mediaId.startsWith(MEDIA_ID_PREFIX_PLAY_ARTIST)) {
            val artistId: String = mediaId.removePrefix(MEDIA_ID_PREFIX_PLAY_ARTIST)
            return resolveArtistQueue(artistId, shuffle = false)
        }
        if (mediaId.startsWith(MEDIA_ID_PREFIX_SHUFFLE_ARTIST)) {
            val artistId: String = mediaId.removePrefix(MEDIA_ID_PREFIX_SHUFFLE_ARTIST)
            return resolveArtistQueue(artistId, shuffle = true)
        }
        return null
    }

    private suspend fun resolveAlbumQueue(
        albumId: String,
        shuffle: Boolean,
    ): List<MediaItem> {
        val album: Album
        try {
            album = thumpData.getAlbum(albumId)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        return buildQueueFromTracks(album.tracks, shuffle = shuffle)
    }

    private suspend fun resolvePlaylistQueue(
        playlistId: String,
        shuffle: Boolean,
    ): List<MediaItem> {
        val playlist: Playlist
        try {
            playlist = thumpData.getPlaylist(playlistId)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        return buildQueueFromTracks(playlist.tracks, shuffle = shuffle)
    }

    private suspend fun resolveArtistQueue(
        artistId: String,
        shuffle: Boolean,
    ): List<MediaItem> {
        val artistTracks: List<Track>
        try {
            artistTracks = thumpData.getArtistTracks(artistId)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        return buildQueueFromTracks(artistTracks, shuffle = shuffle)
    }

    private fun buildQueueFromTracks(tracks: List<Track>, shuffle: Boolean): List<MediaItem> {
        val items: ArrayList<MediaItem> = ArrayList<MediaItem>(tracks.size)
        val trackCount: Int = tracks.size
        for (trackIndex in 0 until trackCount) {
            items.add(buildPlayableMediaItemForTrack(tracks[trackIndex]))
        }
        if (shuffle) {
            items.shuffle()
        }
        return items
    }

    private fun buildPlayableMediaItemForTrack(track: Track): MediaItem {
        val artistText: String
        val trackArtist: String? = track.artistName
        if (trackArtist == null) {
            artistText = ""
        } else {
            artistText = trackArtist
        }
        val metadataBuilder: MediaMetadata.Builder = MediaMetadata.Builder()
            .setTitle(track.title)
            .setIsBrowsable(false)
            .setIsPlayable(true)
            .setMediaType(MediaMetadata.MEDIA_TYPE_MUSIC)
        if (artistText.isNotEmpty()) {
            metadataBuilder.setArtist(artistText)
        }
        val albumLabel: String? = track.albumName
        if (albumLabel != null) {
            metadataBuilder.setAlbumTitle(albumLabel)
        }
        val artworkUri: android.net.Uri? = buildCoverArtContentUriOrNull(track.coverArtId)
        if (artworkUri != null) {
            metadataBuilder.setArtworkUri(artworkUri)
        }
        return MediaItem.Builder()
            .setMediaId(MEDIA_ID_PREFIX_TRACK + track.trackId)
            .setUri(TRACK_URI_SCHEME_PREFIX + track.trackId)
            .setMediaMetadata(metadataBuilder.build())
            .build()
    }

    /**
     * Build one of the Play / Shuffle action rows. The id is the synthetic action id; Auto
     * sends it through onAddMediaItems when tapped and we expand it into the real queue there.
     *
     * iconResourceId is a local drawable (e.g. ic_action_play / ic_action_shuffle). We serve it
     * to Auto via an `android.resource://...` URI so the row gets a recognizable verb icon
     * instead of the contextual album / playlist cover.
     */
    private fun buildActionItem(
        mediaId: String,
        title: String,
        iconResourceId: Int,
    ): MediaItem {
        val metadataBuilder: MediaMetadata.Builder = MediaMetadata.Builder()
            .setTitle(title)
            .setIsBrowsable(false)
            .setIsPlayable(true)
            .setMediaType(MediaMetadata.MEDIA_TYPE_MUSIC)
            .setArtworkUri(buildResourceUri(iconResourceId))
        return MediaItem.Builder()
            .setMediaId(mediaId)
            .setMediaMetadata(metadataBuilder.build())
            .build()
    }

    private fun buildResourceUri(resourceId: Int): android.net.Uri {
        val packageName: String = applicationPackageName
        return android.net.Uri.parse("android.resource://" + packageName + "/" + resourceId)
    }

    private fun buildRootChildren(): ImmutableList<MediaItem> {
        val children: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        // Home gets a custom hint set: both browseable AND playable children render as grid,
        // so the inline Recently Played track tiles match the rest of the shelves instead of
        // collapsing into a list. Other top-level entries keep the default playable=list so
        // tracks inside playlists/albums still show as a list when the user drills in.
        children.add(buildHomeShelfItem())
        children.add(buildBrowseableItem(MEDIA_ID_RECENTS, "Recents"))
        children.add(buildBrowseableItem(MEDIA_ID_PLAYLISTS, "Playlists"))
        children.add(buildBrowseableItem(MEDIA_ID_ARTISTS, "Artists"))
        return children.build()
    }

    private fun buildHomeShelfItem(): MediaItem {
        val extras: android.os.Bundle = android.os.Bundle()
        extras.putInt(CONTENT_STYLE_BROWSABLE_HINT_KEY, CONTENT_STYLE_GRID_ITEM)
        extras.putInt(CONTENT_STYLE_PLAYABLE_HINT_KEY, CONTENT_STYLE_GRID_ITEM)
        val metadata: MediaMetadata = MediaMetadata.Builder()
            .setTitle("Home")
            .setIsBrowsable(true)
            .setIsPlayable(false)
            .setMediaType(MediaMetadata.MEDIA_TYPE_FOLDER_MIXED)
            .setExtras(extras)
            .build()
        return MediaItem.Builder()
            .setMediaId(MEDIA_ID_HOME)
            .setMediaMetadata(metadata)
            .build()
    }

    /**
     * Home: a flat folder whose children carry the section's name in their group-title extra,
     * so Auto renders "Recently Played" / "Your Playlists" / etc. as inline section headers
     * inside the Home tab (the way Spotify and YouTube Music do) instead of forcing the user
     * to drill into sub-folders.
     *
     * All five sections are fetched in parallel and concatenated in display order. Section
     * titles are constant — the active IProtocol hides Pulse-vs-Subsonic, so the callback no
     * longer renames the playlists / popular-artists shelves on protocol change.
     */
    private suspend fun buildHomeChildren(
        params: LibraryParams?,
    ): LibraryResult<ImmutableList<MediaItem>> {
        val sections: List<List<MediaItem>> = coroutineScope {
            val playlistsDeferred = async {
                fetchPlaylistsSection("Your Playlists")
            }
            val recentlyPlayedDeferred = async {
                fetchRecentlyPlayedSection("Recently Played")
            }
            val popularArtistsDeferred: Deferred<List<MediaItem>> = async {
                fetchPopularArtistsSection("Popular Artists")
            }
            val recentlyAddedDeferred = async {
                fetchRecentlyAddedSection("Recently Added")
            }
            val favoritesDeferred = async {
                fetchFavoritesSection("Favorites")
            }
            listOf(
                playlistsDeferred.await(),
                recentlyPlayedDeferred.await(),
                popularArtistsDeferred.await(),
                recentlyAddedDeferred.await(),
                favoritesDeferred.await(),
            )
        }

        val combined: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        val sectionCount: Int = sections.size
        for (sectionIndex in 0 until sectionCount) {
            combined.addAll(sections[sectionIndex])
        }
        return LibraryResult.ofItemList(combined.build(), params)
    }

    private suspend fun fetchRecentlyPlayedSection(sectionTitle: String): List<MediaItem> {
        val items: List<HomeItem>
        try {
            items = thumpData.getRecentlyPlayed(HOME_SECTION_ITEM_LIMIT, ALL_HOME_ITEM_KINDS)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        return tagWithGroupTitle(homeItemsToTiles(items), sectionTitle)
    }

    private suspend fun fetchPlaylistsSection(sectionTitle: String): List<MediaItem> {
        val items: List<HomeItem>
        try {
            items = thumpData.getTopPlaylists(HOME_SECTION_ITEM_LIMIT)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        return tagWithGroupTitle(homeItemsToTiles(items), sectionTitle)
    }

    private suspend fun fetchPopularArtistsSection(sectionTitle: String): List<MediaItem> {
        val items: List<HomeItem>
        try {
            items = thumpData.getPopularArtists(HOME_SECTION_ITEM_LIMIT)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        return tagWithGroupTitle(homeItemsToTiles(items), sectionTitle)
    }

    private suspend fun fetchRecentlyAddedSection(sectionTitle: String): List<MediaItem> {
        val albums: List<Album>
        try {
            albums = thumpData.getAllAlbums(
                sort = AlbumSort.Newest,
                limit = HOME_SECTION_ITEM_LIMIT,
                offset = 0,
            )
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        return tagWithGroupTitle(albumsToBrowseable(albums), sectionTitle)
    }

    private suspend fun fetchFavoritesSection(sectionTitle: String): List<MediaItem> {
        val starred: StarredCollection
        try {
            starred = thumpData.getStarred()
        } catch (notConfigured: ThumpDataNotConfigured) {
            return emptyList<MediaItem>()
        } catch (transportFailure: IOException) {
            return emptyList<MediaItem>()
        }
        val out: ArrayList<MediaItem> = ArrayList<MediaItem>()
        val albumCount: Int = starred.albums.size
        for (albumIndex in 0 until albumCount) {
            out.add(withGroupTitle(albumToBrowseable(starred.albums[albumIndex]), sectionTitle))
        }
        val artistCount: Int = starred.artists.size
        for (artistIndex in 0 until artistCount) {
            out.add(withGroupTitle(artistToBrowseable(starred.artists[artistIndex]), sectionTitle))
        }
        val trackCount: Int = starred.tracks.size
        for (trackIndex in 0 until trackCount) {
            out.add(withGroupTitle(trackToPlayableStub(starred.tracks[trackIndex]), sectionTitle))
        }
        return out
    }

    /**
     * Recents: mixed recently-touched playlists and artists. Single ThumpData call yields a
     * mixed HomeItem list; we fold tracks into distinct-artist refs (preserving first-seen
     * order) and surface playlists/albums/artists directly. The Pulse-vs-Subsonic branching
     * lives behind the active IProtocol, so the callback no longer reads `isPulseDetected`.
     */
    private suspend fun buildRecentsChildren(
        params: LibraryParams?,
    ): LibraryResult<ImmutableList<MediaItem>> {
        val items: List<HomeItem>
        try {
            items = thumpData.getRecentlyPlayed(RECENTS_TRACK_SCAN_COUNT, ALL_HOME_ITEM_KINDS)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        } catch (transportFailure: IOException) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        }

        val orderedRecentArtists: ArrayList<RecentArtistRef> = ArrayList<RecentArtistRef>()
        val seenArtistIds: HashSet<String> = HashSet<String>()
        val recentPlaylists: ArrayList<Playlist> = ArrayList<Playlist>()
        val recentAlbums: ArrayList<Album> = ArrayList<Album>()
        val recentArtistsFromShelf: ArrayList<Artist> = ArrayList<Artist>()
        val itemCount: Int = items.size
        for (itemIndex in 0 until itemCount) {
            val current: HomeItem = items[itemIndex]
            when (current) {
                is HomeItem.TrackItem -> {
                    val trackArtistId: String? = current.track.artistId
                    val trackArtistName: String? = current.track.artistName
                    if (trackArtistId == null) {
                        continue
                    }
                    if (trackArtistId.isEmpty()) {
                        continue
                    }
                    if (trackArtistName == null) {
                        continue
                    }
                    if (trackArtistName.isEmpty()) {
                        continue
                    }
                    if (seenArtistIds.contains(trackArtistId)) {
                        continue
                    }
                    seenArtistIds.add(trackArtistId)
                    orderedRecentArtists.add(
                        RecentArtistRef(
                            id = trackArtistId,
                            name = trackArtistName,
                            coverArt = current.track.coverArtId,
                        )
                    )
                }
                is HomeItem.ArtistItem -> {
                    recentArtistsFromShelf.add(current.artist)
                }
                is HomeItem.AlbumItem -> {
                    recentAlbums.add(current.album)
                }
                is HomeItem.PlaylistItem -> {
                    recentPlaylists.add(current.playlist)
                }
            }
        }

        val combined: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        var emitted: Int = 0
        val playlistEmitCount: Int = recentPlaylists.size
        for (playlistIndex in 0 until playlistEmitCount) {
            if (emitted >= RECENTS_TOTAL_LIMIT) {
                break
            }
            combined.add(playlistToBrowseable(recentPlaylists[playlistIndex]))
            emitted++
        }
        val artistEmitCount: Int = orderedRecentArtists.size
        for (artistIndex in 0 until artistEmitCount) {
            if (emitted >= RECENTS_TOTAL_LIMIT) {
                break
            }
            val recentArtist: RecentArtistRef = orderedRecentArtists[artistIndex]
            combined.add(
                artistToBrowseable(
                    Artist(
                        artistId = recentArtist.id,
                        name = recentArtist.name,
                        albumCount = 0,
                        coverArtId = recentArtist.coverArt,
                        albums = emptyList<Album>(),
                    )
                )
            )
            emitted++
        }
        val artistFromShelfCount: Int = recentArtistsFromShelf.size
        for (artistFromShelfIndex in 0 until artistFromShelfCount) {
            if (emitted >= RECENTS_TOTAL_LIMIT) {
                break
            }
            combined.add(artistToBrowseable(recentArtistsFromShelf[artistFromShelfIndex]))
            emitted++
        }
        val albumEmitCount: Int = recentAlbums.size
        for (albumIndex in 0 until albumEmitCount) {
            if (emitted >= RECENTS_TOTAL_LIMIT) {
                break
            }
            combined.add(albumToBrowseable(recentAlbums[albumIndex]))
            emitted++
        }
        return LibraryResult.ofItemList(combined.build(), params)
    }

    /**
     * Playlists: every playlist, sorted alphabetically by name (case-insensitive).
     */
    private suspend fun buildPlaylistsChildren(
        params: LibraryParams?,
    ): LibraryResult<ImmutableList<MediaItem>> {
        val playlists: List<Playlist>
        try {
            playlists = thumpData.getAllPlaylists()
        } catch (notConfigured: ThumpDataNotConfigured) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        } catch (transportFailure: IOException) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        }
        val mutableCopy: ArrayList<Playlist> = ArrayList<Playlist>(playlists)
        if (mutableCopy.size > PLAYLISTS_FETCH_LIMIT) {
            mutableCopy.subList(PLAYLISTS_FETCH_LIMIT, mutableCopy.size).clear()
        }
        mutableCopy.sortWith(Comparator { left: Playlist, right: Playlist ->
            left.name.compareTo(right.name, ignoreCase = true)
        })
        val tiles: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        val playlistCount: Int = mutableCopy.size
        for (playlistIndex in 0 until playlistCount) {
            tiles.add(playlistToBrowseable(mutableCopy[playlistIndex]))
        }
        return LibraryResult.ofItemList(tiles.build(), params)
    }

    private suspend fun buildArtistsChildren(
        params: LibraryParams?,
    ): LibraryResult<ImmutableList<MediaItem>> {
        val artists: List<Artist>
        try {
            artists = thumpData.getAllArtists()
        } catch (notConfigured: ThumpDataNotConfigured) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        } catch (transportFailure: IOException) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        }
        val tiles: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        val artistCount: Int = artists.size
        for (artistIndex in 0 until artistCount) {
            tiles.add(artistToBrowseable(artists[artistIndex]))
        }
        return LibraryResult.ofItemList(tiles.build(), params)
    }

    private suspend fun buildPlaylistChildren(
        playlistId: String,
        params: LibraryParams?,
    ): LibraryResult<ImmutableList<MediaItem>> {
        val playlist: Playlist
        try {
            playlist = thumpData.getPlaylist(playlistId)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        } catch (transportFailure: IOException) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        }
        val out: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        out.add(
            buildActionItem(
                mediaId = MEDIA_ID_PREFIX_PLAY_PLAYLIST + playlistId,
                title = "Play",
                iconResourceId = com.therobm.thump.R.drawable.ic_action_play,
            )
        )
        out.add(
            buildActionItem(
                mediaId = MEDIA_ID_PREFIX_SHUFFLE_PLAYLIST + playlistId,
                title = "Shuffle",
                iconResourceId = com.therobm.thump.R.drawable.ic_action_shuffle,
            )
        )
        val trackCount: Int = playlist.tracks.size
        for (trackIndex in 0 until trackCount) {
            out.add(trackToPlayableStub(playlist.tracks[trackIndex]))
        }
        return LibraryResult.ofItemList(out.build(), params)
    }

    private suspend fun buildAlbumChildren(
        albumId: String,
        params: LibraryParams?,
    ): LibraryResult<ImmutableList<MediaItem>> {
        val album: Album
        try {
            album = thumpData.getAlbum(albumId)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        } catch (transportFailure: IOException) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        }
        val out: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        out.add(
            buildActionItem(
                mediaId = MEDIA_ID_PREFIX_PLAY_ALBUM + albumId,
                title = "Play",
                iconResourceId = com.therobm.thump.R.drawable.ic_action_play,
            )
        )
        out.add(
            buildActionItem(
                mediaId = MEDIA_ID_PREFIX_SHUFFLE_ALBUM + albumId,
                title = "Shuffle",
                iconResourceId = com.therobm.thump.R.drawable.ic_action_shuffle,
            )
        )
        val trackCount: Int = album.tracks.size
        for (trackIndex in 0 until trackCount) {
            val albumTrack: Track = album.tracks[trackIndex]
            // Album drill-down rows display the album's own name even when individual tracks
            // carry a null `albumName` (Subsonic's getAlbum song entries sometimes omit it).
            val displayAlbumName: String
            val trackAlbumName: String? = albumTrack.albumName
            if (trackAlbumName == null) {
                displayAlbumName = album.name
            } else {
                displayAlbumName = trackAlbumName
            }
            out.add(trackToPlayableStubWithAlbumOverride(albumTrack, displayAlbumName))
        }
        return LibraryResult.ofItemList(out.build(), params)
    }

    private suspend fun buildArtistChildren(
        artistId: String,
        params: LibraryParams?,
    ): LibraryResult<ImmutableList<MediaItem>> {
        val artist: Artist
        try {
            artist = thumpData.getArtist(artistId)
        } catch (notConfigured: ThumpDataNotConfigured) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        } catch (transportFailure: IOException) {
            return LibraryResult.ofItemList(ImmutableList.of<MediaItem>(), params)
        }
        val out: ImmutableList.Builder<MediaItem> = ImmutableList.builder<MediaItem>()
        out.add(
            buildActionItem(
                mediaId = MEDIA_ID_PREFIX_PLAY_ARTIST + artistId,
                title = "Play",
                iconResourceId = com.therobm.thump.R.drawable.ic_action_play,
            )
        )
        out.add(
            buildActionItem(
                mediaId = MEDIA_ID_PREFIX_SHUFFLE_ARTIST + artistId,
                title = "Shuffle",
                iconResourceId = com.therobm.thump.R.drawable.ic_action_shuffle,
            )
        )
        val albumCount: Int = artist.albums.size
        for (albumIndex in 0 until albumCount) {
            val albumOfArtist: Album = artist.albums[albumIndex]
            // Subtitle is the artist's name (the album rows live inside the artist folder, so
            // the artist context is given; this matches the original Auto behaviour).
            out.add(
                buildBrowseableItem(
                    mediaId = MEDIA_ID_PREFIX_ALBUM + albumOfArtist.albumId,
                    title = albumOfArtist.name,
                    subtitle = artist.name,
                    artUri = buildCoverArtContentUriOrNull(albumOfArtist.coverArtId),
                )
            )
        }
        return LibraryResult.ofItemList(out.build(), params)
    }

    private fun homeItemsToTiles(items: List<HomeItem>): List<MediaItem> {
        val out: ArrayList<MediaItem> = ArrayList<MediaItem>(items.size)
        val itemCount: Int = items.size
        for (itemIndex in 0 until itemCount) {
            val current: HomeItem = items[itemIndex]
            when (current) {
                is HomeItem.TrackItem -> {
                    out.add(trackToPlayableStub(current.track))
                }
                is HomeItem.ArtistItem -> {
                    out.add(artistToBrowseable(current.artist))
                }
                is HomeItem.AlbumItem -> {
                    out.add(albumToBrowseable(current.album))
                }
                is HomeItem.PlaylistItem -> {
                    out.add(playlistToBrowseable(current.playlist))
                }
            }
        }
        return out
    }

    private fun albumsToBrowseable(albums: List<Album>): List<MediaItem> {
        val out: ArrayList<MediaItem> = ArrayList<MediaItem>(albums.size)
        val albumCount: Int = albums.size
        for (albumIndex in 0 until albumCount) {
            out.add(albumToBrowseable(albums[albumIndex]))
        }
        return out
    }

    private fun albumToBrowseable(album: Album): MediaItem {
        return buildBrowseableItem(
            mediaId = MEDIA_ID_PREFIX_ALBUM + album.albumId,
            title = album.name,
            subtitle = album.artistName,
            artUri = buildCoverArtContentUriOrNull(album.coverArtId),
        )
    }

    /**
     * Build the browseable tile for a playlist. Cover art is served through the cover-art
     * ContentProvider; tiles whose underlying playlist has no `coverArtId` come back blank
     * (the active IProtocol is responsible for surfacing server-generated composites where
     * available).
     */
    private fun playlistToBrowseable(playlist: Playlist): MediaItem {
        return buildBrowseableItem(
            mediaId = MEDIA_ID_PREFIX_PLAYLIST + playlist.playlistId,
            title = playlist.name,
            subtitle = null,
            artUri = buildCoverArtContentUriOrNull(playlist.coverArtId),
        )
    }

    private fun artistToBrowseable(artist: Artist): MediaItem {
        return buildBrowseableItem(
            mediaId = MEDIA_ID_PREFIX_ARTIST + artist.artistId,
            title = artist.name,
            subtitle = null,
            artUri = buildCoverArtContentUriOrNull(artist.coverArtId),
        )
    }

    /**
     * Build a playable stub for a track. No `setUri` here — Auto sends the stub back through
     * `onAddMediaItems` when tapped, and `buildPlayableMediaItemWithUri` rewrites it with the
     * `thump://track/<id>` scheme ExoPlayer hands to ThumpData.
     */
    private fun trackToPlayableStub(track: Track): MediaItem {
        return trackToPlayableStubWithAlbumOverride(track, track.albumName)
    }

    private fun trackToPlayableStubWithAlbumOverride(
        track: Track,
        overrideAlbumName: String?,
    ): MediaItem {
        val artistText: String
        val trackArtist: String? = track.artistName
        if (trackArtist == null) {
            artistText = ""
        } else {
            artistText = trackArtist
        }
        val metadataBuilder: MediaMetadata.Builder = MediaMetadata.Builder()
            .setTitle(track.title)
            .setArtist(artistText)
            .setIsBrowsable(false)
            .setIsPlayable(true)
            .setMediaType(MediaMetadata.MEDIA_TYPE_MUSIC)
        if (overrideAlbumName != null) {
            metadataBuilder.setAlbumTitle(overrideAlbumName)
        }
        val artworkUri: android.net.Uri? = buildCoverArtContentUriOrNull(track.coverArtId)
        if (artworkUri != null) {
            metadataBuilder.setArtworkUri(artworkUri)
        }
        return MediaItem.Builder()
            .setMediaId(MEDIA_ID_PREFIX_TRACK + track.trackId)
            .setMediaMetadata(metadataBuilder.build())
            .build()
    }

    private fun buildPlayableMediaItemWithUri(
        trackId: String,
        passedThroughMetadata: MediaMetadata,
    ): MediaItem {
        return MediaItem.Builder()
            .setMediaId(MEDIA_ID_PREFIX_TRACK + trackId)
            .setUri(TRACK_URI_SCHEME_PREFIX + trackId)
            .setMediaMetadata(passedThroughMetadata)
            .build()
    }

    private fun buildBrowseableItem(
        mediaId: String,
        title: String,
    ): MediaItem {
        return buildBrowseableItem(mediaId = mediaId, title = title, subtitle = null, artUri = null)
    }

    private fun buildBrowseableItem(
        mediaId: String,
        title: String,
        subtitle: String?,
        artUri: android.net.Uri?,
    ): MediaItem {
        val contentStyleExtras: android.os.Bundle = android.os.Bundle()
        contentStyleExtras.putInt(CONTENT_STYLE_BROWSABLE_HINT_KEY, CONTENT_STYLE_GRID_ITEM)
        contentStyleExtras.putInt(CONTENT_STYLE_PLAYABLE_HINT_KEY, CONTENT_STYLE_LIST_ITEM)
        val metadataBuilder: MediaMetadata.Builder = MediaMetadata.Builder()
            .setTitle(title)
            .setIsBrowsable(true)
            .setIsPlayable(false)
            .setMediaType(MediaMetadata.MEDIA_TYPE_FOLDER_MIXED)
            .setExtras(contentStyleExtras)
        if (subtitle != null) {
            metadataBuilder.setSubtitle(subtitle)
        }
        if (artUri != null) {
            metadataBuilder.setArtworkUri(artUri)
        }
        return MediaItem.Builder()
            .setMediaId(mediaId)
            .setMediaMetadata(metadataBuilder.build())
            .build()
    }

    private fun stubForMediaId(mediaId: String): MediaItem {
        if (mediaId.startsWith(MEDIA_ID_PREFIX_TRACK)) {
            return MediaItem.Builder()
                .setMediaId(mediaId)
                .setMediaMetadata(
                    MediaMetadata.Builder()
                        .setIsBrowsable(false)
                        .setIsPlayable(true)
                        .build()
                )
                .build()
        }
        return buildBrowseableItem(mediaId = mediaId, title = mediaId)
    }

    private fun trackIdFromMediaId(mediaId: String): String? {
        if (mediaId.startsWith(MEDIA_ID_PREFIX_TRACK)) {
            return mediaId.removePrefix(MEDIA_ID_PREFIX_TRACK)
        }
        return null
    }

    /**
     * Build the cover-art ContentProvider URI for the given art id at our standard size. Auto's
     * image fetcher resolves the URI in its own process; the provider lives in the service
     * process and serves bytes out of ThumpData's blob store (disk-hit-first, network on miss).
     */
    private fun buildCoverArtContentUriOrNull(coverArtId: String?): android.net.Uri? {
        if (coverArtId == null) {
            return null
        }
        if (coverArtId.isEmpty()) {
            return null
        }
        return android.net.Uri.parse(
            "content://" + COVER_ART_CONTENT_AUTHORITY + "/" + coverArtId
                + "?size=" + COVER_ART_REQUEST_SIZE_PX
        )
    }

    /**
     * Returns a new MediaItem identical to [item] except the section's group title is merged
     * into its MediaMetadata extras. Auto reads this and renders a section header above the
     * first item that carries it.
     */
    private fun withGroupTitle(item: MediaItem, sectionTitle: String): MediaItem {
        val newExtras: android.os.Bundle = android.os.Bundle()
        val existingExtras: android.os.Bundle? = item.mediaMetadata.extras
        if (existingExtras != null) {
            newExtras.putAll(existingExtras)
        }
        newExtras.putString(CONTENT_STYLE_GROUP_TITLE_HINT_KEY, sectionTitle)
        val newMetadata: MediaMetadata = item.mediaMetadata.buildUpon().setExtras(newExtras).build()
        return item.buildUpon().setMediaMetadata(newMetadata).build()
    }

    private fun tagWithGroupTitle(items: List<MediaItem>, sectionTitle: String): List<MediaItem> {
        val out: ArrayList<MediaItem> = ArrayList<MediaItem>(items.size)
        val itemCount: Int = items.size
        for (itemIndex in 0 until itemCount) {
            out.add(withGroupTitle(items[itemIndex], sectionTitle))
        }
        return out
    }

    /**
     * Local helper bag for the recents builder. Carries the minimum we need to render an artist
     * row (id, name, optional cover) without making a getArtist call per recent track.
     */
    private data class RecentArtistRef(
        val id: String,
        val name: String,
        val coverArt: String?,
    )
}
