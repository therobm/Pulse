package com.therobm.thump.data

import android.content.ContentValues
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import kotlinx.serialization.Serializable
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json

/**
 * SQLite-side translation for ThumpData's metadata mirror. Mirrors the IProtocol
 * implementations in reverse: domain types written into the cache here can be read back
 * into the same domain types a fresh network read would have produced.
 *
 * No policy lives here. ThumpData picks NetworkFirst / offline / etc. and calls these
 * helpers for the storage half. Each public method opens its own SQLite transaction so a
 * partial response is never observable through the index.
 */
internal class ThumpMetadataCache(
    private val database: ThumpDatabase,
    private val jsonCodec: Json,
) {

    // -- Writes ------------------------------------------------------------------------------

    fun writeArtistList(artists: List<Artist>): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val artistCount: Int = artists.size
            for (artistIndex in 0 until artistCount) {
                writeArtistRow(writableDatabase, artists[artistIndex])
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeArtistWithAlbums(artist: Artist): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            writeArtistRow(writableDatabase, artist)
            val albumCount: Int = artist.albums.size
            for (albumIndex in 0 until albumCount) {
                val rawAlbum: Album = artist.albums[albumIndex]
                val albumWithParentArtist: Album
                if (rawAlbum.artistId == null) {
                    albumWithParentArtist = rawAlbum.copy(artistId = artist.artistId)
                } else {
                    albumWithParentArtist = rawAlbum
                }
                writeAlbumRow(writableDatabase, albumWithParentArtist)
                val trackCount: Int = albumWithParentArtist.tracks.size
                for (trackIndex in 0 until trackCount) {
                    writeTrackRow(writableDatabase, albumWithParentArtist.tracks[trackIndex])
                }
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeArtistTracks(artistId: String, tracks: List<Track>): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val trackCount: Int = tracks.size
            for (trackIndex in 0 until trackCount) {
                val rawTrack: Track = tracks[trackIndex]
                val trackWithParentArtist: Track
                if (rawTrack.artistId == null) {
                    trackWithParentArtist = rawTrack.copy(artistId = artistId)
                } else {
                    trackWithParentArtist = rawTrack
                }
                writeTrackRow(writableDatabase, trackWithParentArtist)
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeAlbumWithTracks(album: Album): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            writeAlbumRow(writableDatabase, album)
            val trackCount: Int = album.tracks.size
            for (trackIndex in 0 until trackCount) {
                val rawTrack: Track = album.tracks[trackIndex]
                val trackWithParentAlbum: Track
                if (rawTrack.albumId == null) {
                    trackWithParentAlbum = rawTrack.copy(albumId = album.albumId)
                } else {
                    trackWithParentAlbum = rawTrack
                }
                writeTrackRow(writableDatabase, trackWithParentAlbum)
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeAlbumList(albums: List<Album>): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val albumCount: Int = albums.size
            for (albumIndex in 0 until albumCount) {
                writeAlbumRow(writableDatabase, albums[albumIndex])
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeTrackList(tracks: List<Track>): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val trackCount: Int = tracks.size
            for (trackIndex in 0 until trackCount) {
                writeTrackRow(writableDatabase, tracks[trackIndex])
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeGenreList(genres: List<Genre>): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val genreCount: Int = genres.size
            for (genreIndex in 0 until genreCount) {
                writeGenreRow(writableDatabase, genres[genreIndex])
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writePlaylistList(playlists: List<Playlist>): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val playlistCount: Int = playlists.size
            for (playlistIndex in 0 until playlistCount) {
                writePlaylistRow(writableDatabase, playlists[playlistIndex])
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writePlaylistWithTracks(playlist: Playlist): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            writePlaylistRow(writableDatabase, playlist)
            // Replace, don't merge: the track list on a playlist response is authoritative.
            writableDatabase.delete(
                "playlist_tracks",
                "playlist_id = ?",
                arrayOf<String>(playlist.playlistId),
            )
            val trackCount: Int = playlist.tracks.size
            for (trackIndex in 0 until trackCount) {
                val rawTrack: Track = playlist.tracks[trackIndex]
                writeTrackRow(writableDatabase, rawTrack)
                writePlaylistTrackBinding(
                    writableDatabase = writableDatabase,
                    playlistId = playlist.playlistId,
                    sortOrder = trackIndex,
                    trackId = rawTrack.trackId,
                )
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeStarred(starred: StarredCollection): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val orderedEntries: ArrayList<HomeSectionEntry> = ArrayList<HomeSectionEntry>()
            val trackCount: Int = starred.tracks.size
            for (trackIndex in 0 until trackCount) {
                val starredTrack: Track = starred.tracks[trackIndex]
                writeTrackRow(writableDatabase, starredTrack)
                orderedEntries.add(HomeSectionEntry(kind = KIND_TRACK, id = starredTrack.trackId))
            }
            val albumCount: Int = starred.albums.size
            for (albumIndex in 0 until albumCount) {
                val starredAlbum: Album = starred.albums[albumIndex]
                writeAlbumRow(writableDatabase, starredAlbum)
                orderedEntries.add(HomeSectionEntry(kind = KIND_ALBUM, id = starredAlbum.albumId))
            }
            val artistCount: Int = starred.artists.size
            for (artistIndex in 0 until artistCount) {
                val starredArtist: Artist = starred.artists[artistIndex]
                writeArtistRow(writableDatabase, starredArtist)
                orderedEntries.add(HomeSectionEntry(kind = KIND_ARTIST, id = starredArtist.artistId))
            }
            writeHomeSectionRow(writableDatabase, SECTION_KEY_STARRED, orderedEntries)
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun writeHomeShelf(sectionKey: String, items: List<HomeItem>): Unit {
        val writableDatabase: SQLiteDatabase = database.writableDatabase
        writableDatabase.beginTransaction()
        try {
            val orderedEntries: ArrayList<HomeSectionEntry> = ArrayList<HomeSectionEntry>(items.size)
            val itemCount: Int = items.size
            for (itemIndex in 0 until itemCount) {
                val singleItem: HomeItem = items[itemIndex]
                if (singleItem is HomeItem.TrackItem) {
                    writeTrackRow(writableDatabase, singleItem.track)
                    orderedEntries.add(HomeSectionEntry(kind = KIND_TRACK, id = singleItem.track.trackId))
                } else if (singleItem is HomeItem.ArtistItem) {
                    writeArtistRow(writableDatabase, singleItem.artist)
                    orderedEntries.add(HomeSectionEntry(kind = KIND_ARTIST, id = singleItem.artist.artistId))
                } else if (singleItem is HomeItem.AlbumItem) {
                    writeAlbumRow(writableDatabase, singleItem.album)
                    orderedEntries.add(HomeSectionEntry(kind = KIND_ALBUM, id = singleItem.album.albumId))
                } else if (singleItem is HomeItem.PlaylistItem) {
                    writePlaylistRow(writableDatabase, singleItem.playlist)
                    orderedEntries.add(HomeSectionEntry(kind = KIND_PLAYLIST, id = singleItem.playlist.playlistId))
                }
            }
            writeHomeSectionRow(writableDatabase, sectionKey, orderedEntries)
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    // -- Reads -------------------------------------------------------------------------------

    fun loadArtistList(): List<Artist> {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT artist_id, name, album_count, cover_art_id FROM artists "
                + "ORDER BY name COLLATE NOCASE ASC",
            arrayOf<String>(),
        )
        val collected: ArrayList<Artist> = ArrayList<Artist>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(
                    Artist(
                        artistId = cursor.getString(0),
                        name = cursor.getString(1),
                        albumCount = cursor.getInt(2),
                        coverArtId = readNullableString(cursor, 3),
                        albums = emptyList<Album>(),
                    )
                )
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    fun loadArtist(artistId: String): Artist? {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val artistCursor: Cursor = readableDatabase.rawQuery(
            "SELECT artist_id, name, album_count, cover_art_id FROM artists WHERE artist_id = ?",
            arrayOf<String>(artistId),
        )
        val artistId2: String
        val artistName: String
        val albumCountValue: Int
        val coverArtId: String?
        try {
            if (!artistCursor.moveToFirst()) {
                return null
            }
            artistId2 = artistCursor.getString(0)
            artistName = artistCursor.getString(1)
            albumCountValue = artistCursor.getInt(2)
            coverArtId = readNullableString(artistCursor, 3)
        } finally {
            artistCursor.close()
        }
        val albumList: List<Album> = loadAlbumsForArtist(readableDatabase, artistId2)
        return Artist(
            artistId = artistId2,
            name = artistName,
            albumCount = albumCountValue,
            coverArtId = coverArtId,
            albums = albumList,
        )
    }

    fun loadArtistTracks(artistId: String): List<Track> {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT track_id, title, artist_name, artist_id, album_name, album_id, "
                + "track_number, disc_number, year, genre, duration_seconds, size_bytes, "
                + "suffix, content_type, cover_art_id FROM tracks WHERE artist_id = ? "
                + "ORDER BY album_id, disc_number, track_number, title COLLATE NOCASE",
            arrayOf<String>(artistId),
        )
        val collected: ArrayList<Track> = ArrayList<Track>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(readTrackFromCursor(cursor))
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    fun loadAlbum(albumId: String): Album? {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val albumCursor: Cursor = readableDatabase.rawQuery(
            "SELECT album_id, name, artist_name, artist_id, year, genre, duration_seconds, "
                + "song_count, cover_art_id FROM albums WHERE album_id = ?",
            arrayOf<String>(albumId),
        )
        val loadedAlbum: Album?
        try {
            if (!albumCursor.moveToFirst()) {
                return null
            }
            loadedAlbum = Album(
                albumId = albumCursor.getString(0),
                name = albumCursor.getString(1),
                artistName = readNullableString(albumCursor, 2),
                artistId = readNullableString(albumCursor, 3),
                year = readNullableInt(albumCursor, 4),
                genre = readNullableString(albumCursor, 5),
                durationSeconds = readNullableInt(albumCursor, 6),
                songCount = readNullableInt(albumCursor, 7),
                coverArtId = readNullableString(albumCursor, 8),
                tracks = emptyList<Track>(),
            )
        } finally {
            albumCursor.close()
        }
        val albumTracks: List<Track> = loadTracksForAlbum(readableDatabase, albumId)
        return loadedAlbum.copy(tracks = albumTracks)
    }

    fun loadAlbumList(sort: AlbumSort, limit: Int, offset: Int): List<Album> {
        val orderByClause: String = albumSortToOrderBy(sort)
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT album_id, name, artist_name, artist_id, year, genre, duration_seconds, "
                + "song_count, cover_art_id FROM albums ORDER BY " + orderByClause
                + " LIMIT ? OFFSET ?",
            arrayOf<String>(limit.toString(), offset.toString()),
        )
        val collected: ArrayList<Album> = ArrayList<Album>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(
                    Album(
                        albumId = cursor.getString(0),
                        name = cursor.getString(1),
                        artistName = readNullableString(cursor, 2),
                        artistId = readNullableString(cursor, 3),
                        year = readNullableInt(cursor, 4),
                        genre = readNullableString(cursor, 5),
                        durationSeconds = readNullableInt(cursor, 6),
                        songCount = readNullableInt(cursor, 7),
                        coverArtId = readNullableString(cursor, 8),
                        tracks = emptyList<Track>(),
                    )
                )
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    fun loadGenreList(): List<Genre> {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT name, song_count, album_count FROM genres ORDER BY name COLLATE NOCASE ASC",
            arrayOf<String>(),
        )
        val collected: ArrayList<Genre> = ArrayList<Genre>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(
                    Genre(
                        name = cursor.getString(0),
                        songCount = readNullableInt(cursor, 1),
                        albumCount = readNullableInt(cursor, 2),
                    )
                )
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    fun loadTracksByGenre(genre: String, limit: Int, offset: Int): List<Track> {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT track_id, title, artist_name, artist_id, album_name, album_id, "
                + "track_number, disc_number, year, genre, duration_seconds, size_bytes, "
                + "suffix, content_type, cover_art_id FROM tracks WHERE genre = ? "
                + "ORDER BY ROWID ASC LIMIT ? OFFSET ?",
            arrayOf<String>(genre, limit.toString(), offset.toString()),
        )
        val collected: ArrayList<Track> = ArrayList<Track>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(readTrackFromCursor(cursor))
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    fun loadPlaylistList(): List<Playlist> {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT playlist_id, name, owner_username, comment, song_count, duration_seconds, "
                + "cover_art_id FROM playlists ORDER BY name COLLATE NOCASE ASC",
            arrayOf<String>(),
        )
        val collected: ArrayList<Playlist> = ArrayList<Playlist>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(
                    Playlist(
                        playlistId = cursor.getString(0),
                        name = cursor.getString(1),
                        ownerUsername = readNullableString(cursor, 2),
                        comment = readNullableString(cursor, 3),
                        songCount = readNullableInt(cursor, 4),
                        durationSeconds = readNullableInt(cursor, 5),
                        coverArtId = readNullableString(cursor, 6),
                        tracks = emptyList<Track>(),
                    )
                )
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    fun loadPlaylist(playlistId: String): Playlist? {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val playlistCursor: Cursor = readableDatabase.rawQuery(
            "SELECT playlist_id, name, owner_username, comment, song_count, duration_seconds, "
                + "cover_art_id FROM playlists WHERE playlist_id = ?",
            arrayOf<String>(playlistId),
        )
        val basePlaylist: Playlist?
        try {
            if (!playlistCursor.moveToFirst()) {
                return null
            }
            basePlaylist = Playlist(
                playlistId = playlistCursor.getString(0),
                name = playlistCursor.getString(1),
                ownerUsername = readNullableString(playlistCursor, 2),
                comment = readNullableString(playlistCursor, 3),
                songCount = readNullableInt(playlistCursor, 4),
                durationSeconds = readNullableInt(playlistCursor, 5),
                coverArtId = readNullableString(playlistCursor, 6),
                tracks = emptyList<Track>(),
            )
        } finally {
            playlistCursor.close()
        }
        val orderedTrackIds: List<String> = loadPlaylistTrackIdsInOrder(readableDatabase, playlistId)
        val hydratedTracks: ArrayList<Track> = ArrayList<Track>(orderedTrackIds.size)
        val trackIdCount: Int = orderedTrackIds.size
        for (orderedIdIndex in 0 until trackIdCount) {
            val hydrated: Track? = loadTrack(readableDatabase, orderedTrackIds[orderedIdIndex])
            if (hydrated != null) {
                hydratedTracks.add(hydrated)
            }
        }
        return basePlaylist.copy(tracks = hydratedTracks)
    }

    fun loadStarred(): StarredCollection? {
        val sectionEntries: List<HomeSectionEntry>? = loadHomeSectionEntries(SECTION_KEY_STARRED)
        if (sectionEntries == null) {
            return null
        }
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val tracksOut: ArrayList<Track> = ArrayList<Track>()
        val albumsOut: ArrayList<Album> = ArrayList<Album>()
        val artistsOut: ArrayList<Artist> = ArrayList<Artist>()
        val entryCount: Int = sectionEntries.size
        for (entryIndex in 0 until entryCount) {
            val entry: HomeSectionEntry = sectionEntries[entryIndex]
            if (entry.kind == KIND_TRACK) {
                val track: Track? = loadTrack(readableDatabase, entry.id)
                if (track != null) {
                    tracksOut.add(track)
                }
            } else if (entry.kind == KIND_ALBUM) {
                val album: Album? = loadAlbumSummary(readableDatabase, entry.id)
                if (album != null) {
                    albumsOut.add(album)
                }
            } else if (entry.kind == KIND_ARTIST) {
                val artist: Artist? = loadArtistSummary(readableDatabase, entry.id)
                if (artist != null) {
                    artistsOut.add(artist)
                }
            }
        }
        return StarredCollection(
            tracks = tracksOut,
            albums = albumsOut,
            artists = artistsOut,
        )
    }

    fun loadHomeShelf(sectionKey: String): List<HomeItem>? {
        val sectionEntries: List<HomeSectionEntry>? = loadHomeSectionEntries(sectionKey)
        if (sectionEntries == null) {
            return null
        }
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val collected: ArrayList<HomeItem> = ArrayList<HomeItem>(sectionEntries.size)
        val entryCount: Int = sectionEntries.size
        for (entryIndex in 0 until entryCount) {
            val entry: HomeSectionEntry = sectionEntries[entryIndex]
            if (entry.kind == KIND_TRACK) {
                val track: Track? = loadTrack(readableDatabase, entry.id)
                if (track != null) {
                    collected.add(HomeItem.TrackItem(track))
                }
            } else if (entry.kind == KIND_ALBUM) {
                val album: Album? = loadAlbumSummary(readableDatabase, entry.id)
                if (album != null) {
                    collected.add(HomeItem.AlbumItem(album))
                }
            } else if (entry.kind == KIND_ARTIST) {
                val artist: Artist? = loadArtistSummary(readableDatabase, entry.id)
                if (artist != null) {
                    collected.add(HomeItem.ArtistItem(artist))
                }
            } else if (entry.kind == KIND_PLAYLIST) {
                val playlist: Playlist? = loadPlaylistSummary(readableDatabase, entry.id)
                if (playlist != null) {
                    collected.add(HomeItem.PlaylistItem(playlist))
                }
            }
        }
        return collected
    }

    // -- internals (writes) ------------------------------------------------------------------

    private fun writeArtistRow(writableDatabase: SQLiteDatabase, artist: Artist): Unit {
        val row: ContentValues = ContentValues()
        row.put("artist_id", artist.artistId)
        row.put("name", artist.name)
        row.put("album_count", artist.albumCount)
        if (artist.coverArtId == null) {
            row.putNull("cover_art_id")
        } else {
            row.put("cover_art_id", artist.coverArtId)
        }
        row.put("fetched_at_epoch_millis", System.currentTimeMillis())
        writableDatabase.insertWithOnConflict(
            "artists",
            null,
            row,
            SQLiteDatabase.CONFLICT_REPLACE,
        )
    }

    private fun writeAlbumRow(writableDatabase: SQLiteDatabase, album: Album): Unit {
        val row: ContentValues = ContentValues()
        row.put("album_id", album.albumId)
        row.put("name", album.name)
        if (album.artistName == null) {
            row.putNull("artist_name")
        } else {
            row.put("artist_name", album.artistName)
        }
        if (album.artistId == null) {
            row.putNull("artist_id")
        } else {
            row.put("artist_id", album.artistId)
        }
        if (album.year == null) {
            row.putNull("year")
        } else {
            row.put("year", album.year)
        }
        if (album.genre == null) {
            row.putNull("genre")
        } else {
            row.put("genre", album.genre)
        }
        if (album.durationSeconds == null) {
            row.putNull("duration_seconds")
        } else {
            row.put("duration_seconds", album.durationSeconds)
        }
        if (album.songCount == null) {
            row.putNull("song_count")
        } else {
            row.put("song_count", album.songCount)
        }
        if (album.coverArtId == null) {
            row.putNull("cover_art_id")
        } else {
            row.put("cover_art_id", album.coverArtId)
        }
        row.put("fetched_at_epoch_millis", System.currentTimeMillis())
        writableDatabase.insertWithOnConflict(
            "albums",
            null,
            row,
            SQLiteDatabase.CONFLICT_REPLACE,
        )
    }

    private fun writeTrackRow(writableDatabase: SQLiteDatabase, track: Track): Unit {
        val row: ContentValues = ContentValues()
        row.put("track_id", track.trackId)
        row.put("title", track.title)
        if (track.artistName == null) {
            row.putNull("artist_name")
        } else {
            row.put("artist_name", track.artistName)
        }
        if (track.artistId == null) {
            row.putNull("artist_id")
        } else {
            row.put("artist_id", track.artistId)
        }
        if (track.albumName == null) {
            row.putNull("album_name")
        } else {
            row.put("album_name", track.albumName)
        }
        if (track.albumId == null) {
            row.putNull("album_id")
        } else {
            row.put("album_id", track.albumId)
        }
        if (track.trackNumber == null) {
            row.putNull("track_number")
        } else {
            row.put("track_number", track.trackNumber)
        }
        if (track.discNumber == null) {
            row.putNull("disc_number")
        } else {
            row.put("disc_number", track.discNumber)
        }
        if (track.year == null) {
            row.putNull("year")
        } else {
            row.put("year", track.year)
        }
        if (track.genre == null) {
            row.putNull("genre")
        } else {
            row.put("genre", track.genre)
        }
        if (track.durationSeconds == null) {
            row.putNull("duration_seconds")
        } else {
            row.put("duration_seconds", track.durationSeconds)
        }
        if (track.sizeBytes == null) {
            row.putNull("size_bytes")
        } else {
            row.put("size_bytes", track.sizeBytes)
        }
        if (track.suffix == null) {
            row.putNull("suffix")
        } else {
            row.put("suffix", track.suffix)
        }
        if (track.contentType == null) {
            row.putNull("content_type")
        } else {
            row.put("content_type", track.contentType)
        }
        if (track.coverArtId == null) {
            row.putNull("cover_art_id")
        } else {
            row.put("cover_art_id", track.coverArtId)
        }
        row.put("fetched_at_epoch_millis", System.currentTimeMillis())
        writableDatabase.insertWithOnConflict(
            "tracks",
            null,
            row,
            SQLiteDatabase.CONFLICT_REPLACE,
        )
    }

    private fun writePlaylistRow(writableDatabase: SQLiteDatabase, playlist: Playlist): Unit {
        val row: ContentValues = ContentValues()
        row.put("playlist_id", playlist.playlistId)
        row.put("name", playlist.name)
        if (playlist.ownerUsername == null) {
            row.putNull("owner_username")
        } else {
            row.put("owner_username", playlist.ownerUsername)
        }
        if (playlist.comment == null) {
            row.putNull("comment")
        } else {
            row.put("comment", playlist.comment)
        }
        if (playlist.songCount == null) {
            row.putNull("song_count")
        } else {
            row.put("song_count", playlist.songCount)
        }
        if (playlist.durationSeconds == null) {
            row.putNull("duration_seconds")
        } else {
            row.put("duration_seconds", playlist.durationSeconds)
        }
        if (playlist.coverArtId == null) {
            row.putNull("cover_art_id")
        } else {
            row.put("cover_art_id", playlist.coverArtId)
        }
        row.put("fetched_at_epoch_millis", System.currentTimeMillis())
        writableDatabase.insertWithOnConflict(
            "playlists",
            null,
            row,
            SQLiteDatabase.CONFLICT_REPLACE,
        )
    }

    private fun writePlaylistTrackBinding(
        writableDatabase: SQLiteDatabase,
        playlistId: String,
        sortOrder: Int,
        trackId: String,
    ): Unit {
        val row: ContentValues = ContentValues()
        row.put("playlist_id", playlistId)
        row.put("sort_order", sortOrder)
        row.put("track_id", trackId)
        writableDatabase.insertWithOnConflict(
            "playlist_tracks",
            null,
            row,
            SQLiteDatabase.CONFLICT_REPLACE,
        )
    }

    private fun writeGenreRow(writableDatabase: SQLiteDatabase, genre: Genre): Unit {
        val row: ContentValues = ContentValues()
        row.put("name", genre.name)
        if (genre.songCount == null) {
            row.putNull("song_count")
        } else {
            row.put("song_count", genre.songCount)
        }
        if (genre.albumCount == null) {
            row.putNull("album_count")
        } else {
            row.put("album_count", genre.albumCount)
        }
        row.put("fetched_at_epoch_millis", System.currentTimeMillis())
        writableDatabase.insertWithOnConflict(
            "genres",
            null,
            row,
            SQLiteDatabase.CONFLICT_REPLACE,
        )
    }

    private fun writeHomeSectionRow(
        writableDatabase: SQLiteDatabase,
        sectionKey: String,
        entries: List<HomeSectionEntry>,
    ): Unit {
        val serialisedEntries: String = jsonCodec.encodeToString(
            ListSerializer(HomeSectionEntry.serializer()),
            entries,
        )
        val row: ContentValues = ContentValues()
        row.put("section_key", sectionKey)
        row.put("item_ids_json", serialisedEntries)
        row.put("fetched_at_epoch_millis", System.currentTimeMillis())
        writableDatabase.insertWithOnConflict(
            "home_sections",
            null,
            row,
            SQLiteDatabase.CONFLICT_REPLACE,
        )
    }

    // -- internals (reads) -------------------------------------------------------------------

    private fun loadAlbumsForArtist(readableDatabase: SQLiteDatabase, artistId: String): List<Album> {
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT album_id, name, artist_name, artist_id, year, genre, duration_seconds, "
                + "song_count, cover_art_id FROM albums WHERE artist_id = ? "
                + "ORDER BY (year IS NULL), year ASC, name COLLATE NOCASE ASC",
            arrayOf<String>(artistId),
        )
        val collected: ArrayList<Album> = ArrayList<Album>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(
                    Album(
                        albumId = cursor.getString(0),
                        name = cursor.getString(1),
                        artistName = readNullableString(cursor, 2),
                        artistId = readNullableString(cursor, 3),
                        year = readNullableInt(cursor, 4),
                        genre = readNullableString(cursor, 5),
                        durationSeconds = readNullableInt(cursor, 6),
                        songCount = readNullableInt(cursor, 7),
                        coverArtId = readNullableString(cursor, 8),
                        tracks = emptyList<Track>(),
                    )
                )
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    private fun loadTracksForAlbum(readableDatabase: SQLiteDatabase, albumId: String): List<Track> {
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT track_id, title, artist_name, artist_id, album_name, album_id, "
                + "track_number, disc_number, year, genre, duration_seconds, size_bytes, "
                + "suffix, content_type, cover_art_id FROM tracks WHERE album_id = ? "
                + "ORDER BY disc_number, track_number, title COLLATE NOCASE",
            arrayOf<String>(albumId),
        )
        val collected: ArrayList<Track> = ArrayList<Track>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(readTrackFromCursor(cursor))
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    private fun loadTrack(readableDatabase: SQLiteDatabase, trackId: String): Track? {
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT track_id, title, artist_name, artist_id, album_name, album_id, "
                + "track_number, disc_number, year, genre, duration_seconds, size_bytes, "
                + "suffix, content_type, cover_art_id FROM tracks WHERE track_id = ?",
            arrayOf<String>(trackId),
        )
        try {
            if (!cursor.moveToFirst()) {
                return null
            }
            return readTrackFromCursor(cursor)
        } finally {
            cursor.close()
        }
    }

    private fun loadAlbumSummary(readableDatabase: SQLiteDatabase, albumId: String): Album? {
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT album_id, name, artist_name, artist_id, year, genre, duration_seconds, "
                + "song_count, cover_art_id FROM albums WHERE album_id = ?",
            arrayOf<String>(albumId),
        )
        try {
            if (!cursor.moveToFirst()) {
                return null
            }
            return Album(
                albumId = cursor.getString(0),
                name = cursor.getString(1),
                artistName = readNullableString(cursor, 2),
                artistId = readNullableString(cursor, 3),
                year = readNullableInt(cursor, 4),
                genre = readNullableString(cursor, 5),
                durationSeconds = readNullableInt(cursor, 6),
                songCount = readNullableInt(cursor, 7),
                coverArtId = readNullableString(cursor, 8),
                tracks = emptyList<Track>(),
            )
        } finally {
            cursor.close()
        }
    }

    private fun loadArtistSummary(readableDatabase: SQLiteDatabase, artistId: String): Artist? {
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT artist_id, name, album_count, cover_art_id FROM artists WHERE artist_id = ?",
            arrayOf<String>(artistId),
        )
        try {
            if (!cursor.moveToFirst()) {
                return null
            }
            return Artist(
                artistId = cursor.getString(0),
                name = cursor.getString(1),
                albumCount = cursor.getInt(2),
                coverArtId = readNullableString(cursor, 3),
                albums = emptyList<Album>(),
            )
        } finally {
            cursor.close()
        }
    }

    private fun loadPlaylistSummary(readableDatabase: SQLiteDatabase, playlistId: String): Playlist? {
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT playlist_id, name, owner_username, comment, song_count, duration_seconds, "
                + "cover_art_id FROM playlists WHERE playlist_id = ?",
            arrayOf<String>(playlistId),
        )
        try {
            if (!cursor.moveToFirst()) {
                return null
            }
            return Playlist(
                playlistId = cursor.getString(0),
                name = cursor.getString(1),
                ownerUsername = readNullableString(cursor, 2),
                comment = readNullableString(cursor, 3),
                songCount = readNullableInt(cursor, 4),
                durationSeconds = readNullableInt(cursor, 5),
                coverArtId = readNullableString(cursor, 6),
                tracks = emptyList<Track>(),
            )
        } finally {
            cursor.close()
        }
    }

    private fun loadPlaylistTrackIdsInOrder(
        readableDatabase: SQLiteDatabase,
        playlistId: String,
    ): List<String> {
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT track_id FROM playlist_tracks WHERE playlist_id = ? ORDER BY sort_order ASC",
            arrayOf<String>(playlistId),
        )
        val collected: ArrayList<String> = ArrayList<String>()
        try {
            val rowCount: Int = cursor.count
            for (rowIndex in 0 until rowCount) {
                if (!cursor.moveToNext()) {
                    break
                }
                collected.add(cursor.getString(0))
            }
        } finally {
            cursor.close()
        }
        return collected
    }

    private fun loadHomeSectionEntries(sectionKey: String): List<HomeSectionEntry>? {
        val readableDatabase: SQLiteDatabase = database.readableDatabase
        val cursor: Cursor = readableDatabase.rawQuery(
            "SELECT item_ids_json FROM home_sections WHERE section_key = ?",
            arrayOf<String>(sectionKey),
        )
        val rawJson: String
        try {
            if (!cursor.moveToFirst()) {
                return null
            }
            rawJson = cursor.getString(0)
        } finally {
            cursor.close()
        }
        return jsonCodec.decodeFromString(
            ListSerializer(HomeSectionEntry.serializer()),
            rawJson,
        )
    }

    private fun readTrackFromCursor(cursor: Cursor): Track {
        return Track(
            trackId = cursor.getString(0),
            title = cursor.getString(1),
            artistName = readNullableString(cursor, 2),
            artistId = readNullableString(cursor, 3),
            albumName = readNullableString(cursor, 4),
            albumId = readNullableString(cursor, 5),
            trackNumber = readNullableInt(cursor, 6),
            discNumber = readNullableInt(cursor, 7),
            year = readNullableInt(cursor, 8),
            genre = readNullableString(cursor, 9),
            durationSeconds = readNullableInt(cursor, 10),
            sizeBytes = readNullableLong(cursor, 11),
            suffix = readNullableString(cursor, 12),
            contentType = readNullableString(cursor, 13),
            coverArtId = readNullableString(cursor, 14),
        )
    }

    private fun readNullableString(cursor: Cursor, columnIndex: Int): String? {
        if (cursor.isNull(columnIndex)) {
            return null
        }
        return cursor.getString(columnIndex)
    }

    private fun readNullableInt(cursor: Cursor, columnIndex: Int): Int? {
        if (cursor.isNull(columnIndex)) {
            return null
        }
        return cursor.getInt(columnIndex)
    }

    private fun readNullableLong(cursor: Cursor, columnIndex: Int): Long? {
        if (cursor.isNull(columnIndex)) {
            return null
        }
        return cursor.getLong(columnIndex)
    }

    private fun albumSortToOrderBy(sort: AlbumSort): String {
        when (sort) {
            AlbumSort.AlphabeticalByName -> {
                return "name COLLATE NOCASE ASC"
            }
            AlbumSort.AlphabeticalByArtist -> {
                return "(artist_name IS NULL), artist_name COLLATE NOCASE ASC, name COLLATE NOCASE ASC"
            }
            AlbumSort.Newest -> {
                return "(year IS NULL), year DESC, name COLLATE NOCASE ASC"
            }
            AlbumSort.Recent -> {
                return "fetched_at_epoch_millis DESC"
            }
            AlbumSort.Frequent -> {
                // No local signal for play frequency; insertion order is a stable substitute.
                return "ROWID ASC"
            }
            AlbumSort.Random -> {
                // Cache fallback for a random listing — insertion order rather than re-randomising
                // every call, so paged offsets stay coherent within a single offline session.
                return "ROWID ASC"
            }
        }
    }

    companion object {
        const val SECTION_KEY_STARRED: String = "starred"
        const val SECTION_KEY_POPULAR_ARTISTS: String = "popular_artists"
        const val SECTION_KEY_TOP_PLAYLISTS: String = "top_playlists"
        const val SECTION_KEY_RECENTLY_PLAYED_PREFIX: String = "recents:"

        private const val KIND_TRACK: String = "track"
        private const val KIND_ARTIST: String = "artist"
        private const val KIND_ALBUM: String = "album"
        private const val KIND_PLAYLIST: String = "playlist"

        /**
         * Build a stable home_sections key for a recently-played query so cache reads find the
         * exact rows the matching write produced. The `types` set is sorted alphabetically
         * before joining so two callers passing the same logical set hash to the same key.
         */
        fun recentlyPlayedSectionKey(types: Set<HomeItemKind>): String {
            val canonicalNames: ArrayList<String> = ArrayList<String>(types.size)
            if (types.contains(HomeItemKind.Album)) {
                canonicalNames.add("Album")
            }
            if (types.contains(HomeItemKind.Artist)) {
                canonicalNames.add("Artist")
            }
            if (types.contains(HomeItemKind.Playlist)) {
                canonicalNames.add("Playlist")
            }
            if (types.contains(HomeItemKind.Track)) {
                canonicalNames.add("Track")
            }
            val builder: StringBuilder = StringBuilder()
            builder.append(SECTION_KEY_RECENTLY_PLAYED_PREFIX)
            val canonicalCount: Int = canonicalNames.size
            for (nameIndex in 0 until canonicalCount) {
                if (nameIndex > 0) {
                    builder.append(",")
                }
                builder.append(canonicalNames[nameIndex])
            }
            return builder.toString()
        }
    }
}

/**
 * One entry in a `home_sections` JSON row. Kept here rather than in a shared file because
 * it is the wire format between [ThumpMetadataCache]'s writes and reads — nothing else
 * needs to know its shape.
 */
@Serializable
private data class HomeSectionEntry(
    val kind: String,
    val id: String,
)
