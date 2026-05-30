
using Pulse.MusicLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Pulse.Protocols.Subsonic
{
	public class SubsonicResponseBody
	{
		public string status { get; set; } = "ok";
		public string version { get; set; } = "1.16.1";
		public string type { get; set; } = "Pulse";
		public string serverVersion { get; set; } = "0.1.0";
		public bool openSubsonic { get; set; } = true;

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public UserInfo user { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public SubsonicError error { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public LicenseInfo license { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public AlbumList2 albumList2 { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public GenresContainer genres { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public List<OpenSubsonicExtension> openSubsonicExtensions { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public PlaylistsContainer playlists { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public PlaylistWithSongs playlist { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public MusicFoldersContainer musicFolders { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public ArtistsContainer artists { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public ArtistWithAlbumsID3 artist { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public AlbumWithSongsID3 album { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public SongID3 song { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public StarredContainer starred { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public TopSongsContainer topSongs { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public ArtistInfo2 artistInfo2 { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public SearchResult3 searchResult3 { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public InternetRadioStationsContainer internetRadioStations { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public IndexesContainer indexes { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public DirectoryContainer directory { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public StarredContainer starred2 { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public SongsByGenreContainer songsByGenre { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public RandomSongsContainer randomSongs { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public NowPlayingContainer nowPlaying { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public AlbumInfoBody albumInfo { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public SimilarSongsContainer similarSongs { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public SimilarSongsContainer similarSongs2 { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public LyricsBody lyrics { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public LyricsListBody lyricsList { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public PlayQueueBody playQueue { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public BookmarksContainer bookmarks { get; set; }

	}

	public class SongsByGenreContainer
	{
		public List<SongID3> song { get; set; } = new List<SongID3>();
	}

	public class RandomSongsContainer
	{
		public List<SongID3> song { get; set; } = new List<SongID3>();
	}

	public class NowPlayingContainer
	{
		public List<NowPlayingEntry> entry { get; set; } = new List<NowPlayingEntry>();
	}

	// Spec NowPlayingEntry extends Child with playback context fields. Composition
	// over inheritance so SongID3's shape stays untouched everywhere else.
	public class NowPlayingEntry
	{
		public string id { get; set; }
		public string title { get; set; }
		public string album { get; set; }
		public string albumId { get; set; }
		public string artist { get; set; }
		public string artistId { get; set; }
		public int track { get; set; }
		public int discNumber { get; set; }
		public int year { get; set; }
		public string genre { get; set; }
		public int duration { get; set; }
		public long size { get; set; }
		public string suffix { get; set; }
		public string contentType { get; set; }
		public string coverArt { get; set; }
		public string username { get; set; }
		public int minutesAgo { get; set; }
		public string playerId { get; set; }
		public string playerName { get; set; }
	}

	public class AlbumInfoBody
	{
		public string notes { get; set; } = "";
		public string musicBrainzId { get; set; } = "";
		public string lastFmUrl { get; set; } = "";
		public string smallImageUrl { get; set; } = "";
		public string mediumImageUrl { get; set; } = "";
		public string largeImageUrl { get; set; } = "";
	}

	public class SimilarSongsContainer
	{
		public List<SongID3> song { get; set; } = new List<SongID3>();
	}

	public class LyricsBody
	{
		public string artist { get; set; } = "";
		public string title { get; set; } = "";
		// Spec calls this "value" -- the raw lyrics text in the response body.
		public string value { get; set; } = "";
	}

	public class LyricsListBody
	{
		public List<StructuredLyrics> structuredLyrics { get; set; } = new List<StructuredLyrics>();
	}

	public class StructuredLyrics
	{
		public string lang { get; set; } = "xxx";
		public bool synced { get; set; } = false;
		public string displayArtist { get; set; } = "";
		public string displayTitle { get; set; } = "";
		public int offset { get; set; } = 0;
		public List<LyricLine> line { get; set; } = new List<LyricLine>();
	}

	public class LyricLine
	{
		public string value { get; set; } = "";
		// Milliseconds offset from track start. Spec omits this for unsynced.
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long start { get; set; }
	}

	public class PlayQueueBody
	{
		public string current { get; set; } = "";
		public long position { get; set; } = 0;
		public string username { get; set; } = "";
		public string changed { get; set; } = "";
		public string changedBy { get; set; } = "";
		public List<SongID3> entry { get; set; } = new List<SongID3>();

		public PlayQueueBody(string user, PlayQueueInfo queue, List<TrackInfo> tracks)
		{
			current = queue.CurrentTrackId;
			position = queue.PositionMs;
			username = user;
			if (queue.Changed != default(DateTime))
			{
				changed = queue.Changed.ToString("o");
			}
			changedBy = queue.ChangedBy;
			for (int index = 0; index < tracks.Count; index++)
			{
				entry.Add(new SongID3(user, tracks[index]));
			}
		}
	}

	public class BookmarksContainer
	{
		public List<BookmarkEntry> bookmark { get; set; } = new List<BookmarkEntry>();
	}

	public class BookmarkEntry
	{
		public string username { get; set; } = "";
		public long position { get; set; } = 0;
		public string comment { get; set; } = "";
		public string created { get; set; } = "";
		public string changed { get; set; } = "";
		public SongID3 entry { get; set; }

		public BookmarkEntry(string user, BookmarkInfo bookmark, TrackInfo track)
		{
			username = user;
			position = bookmark.PositionMs;
			comment = bookmark.Comment;
			if (bookmark.Created != default(DateTime))
			{
				created = bookmark.Created.ToString("o");
			}
			if (bookmark.Changed != default(DateTime))
			{
				changed = bookmark.Changed.ToString("o");
			}
			entry = new SongID3(user, track);
		}
	}

	public class PlaylistWithSongs
	{
		public string id { get; set; }
		public string name { get; set; }
		public string comment { get; set; }
		public int songCount { get; set; }
		public int duration { get; set; }
		// Spec-defined coverArt. Populated by HandleGetPlaylist /
		// HandleCreatePlaylist as "pl-<id>"; the getCoverArt handler
		// turns that into a server-side composite from the playlist's
		// first distinct album covers.
		public string coverArt { get; set; }
		public List<SongID3> entry { get; set; }

		// --- OpenSubsonic spec fields (Flatline #159), all additive ---
		// Pulse doesn't track ownership today (single-user system) so owner
		// defaults to empty and isPublic to true. created / changed need
		// schema support; left empty for now (separate follow-up).
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string owner { get; set; }
		// Wire name is "public" (C# reserved keyword); rename the field
		// instead of using @public escape syntax.
		[JsonPropertyName("public")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool isPublic { get; set; } = true;
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string created { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string changed { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public List<string> allowedUser { get; set; }

		public PlaylistWithSongs()
		{
			entry = new List<SongID3>();
		}

		public PlaylistWithSongs(string user, PlaylistAndTracks playlist)
		{
			id = playlist.Id;
			name = playlist.Name;
			comment = playlist.Comment;
			songCount = playlist.GetSongCount();
			duration = (int)playlist.DurationSeconds;
			coverArt = "pl-" + playlist.Id;
			owner = user;
			entry = new List<SongID3>();
			for (int index = 0; index < playlist.Tracks.Count; index++)
			{
				entry.Add(new SongID3(user, playlist.Tracks[index]));
			}
		}
	}


	public class DirectoryContainer
	{
		public string id { get; set; }
		public string name { get; set; }
		public List<DirectoryChild> child { get; set; } = new List<DirectoryChild>();
	}

	public class DirectoryChild
	{
		public string id { get; set; }
		public string parent { get; set; }
		public string title { get; set; }
		public string artist { get; set; }
		public bool isDir { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string coverArt { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string album { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int duration { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long size { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string suffix { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string contentType { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int track { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int year { get; set; }
	}


	public class IndexesContainer
	{
		// Spec field (Flatline #161). Clients use this to strip leading
		// articles ("The Beatles" → sorted under B) before alpha-sorting.
		// Default matches the Subsonic spec example string.
		public string ignoredArticles { get; set; } = "The El La Los Las Le Les";
		public List<ArtistIndex> index { get; set; }
	}

	public class InternetRadioStationsContainer
	{
		public List<object> internetRadioStation { get; set; }
	}

	public class StarredContainer
	{
		public List<ArtistID3> artist { get; set; } = new List<ArtistID3>();
		public List<AlbumID3> album { get; set; } = new List<AlbumID3>();
		public List<SongID3> song { get; set; } = new List<SongID3>();
	}

	public class TopSongsContainer
	{
		public List<SongID3> song { get; set; } = new List<SongID3>();
	}

	public class ArtistInfo2
	{
		public string biography { get; set; } = "";
		public string musicBrainzId { get; set; }
		public string lastFmUrl { get; set; }
		public string smallImageUrl { get; set; }
		public string mediumImageUrl { get; set; }
		public string largeImageUrl { get; set; }
		public List<ArtistID3> similarArtist { get; set; } = new List<ArtistID3>();
	}

	public class SearchResult3
	{
		public List<ArtistID3> artist { get; set; } = new List<ArtistID3>();
		public List<AlbumID3> album { get; set; } = new List<AlbumID3>();
		public List<SongID3> song { get; set; } = new List<SongID3>();
	}

	public class OpenSubsonicExtension
	{
		public string name { get; set; }
		public List<int> versions { get; set; }
	}

	public class PlaylistsContainer
	{
		public List<PlaylistEntry> playlist { get; set; } = new List<PlaylistEntry>();
	}

	public class PlaylistEntry
	{
		public string id { get; set; }
		public string name { get; set; }
		public string comment { get; set; }
		public int songCount { get; set; }
		public int duration { get; set; }
		// Spec-defined coverArt. See PlaylistWithSongs.coverArt.
		public string coverArt { get; set; }

		// --- OpenSubsonic spec fields (Flatline #159), mirror PlaylistWithSongs. ---
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string owner { get; set; }
		[JsonPropertyName("public")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool isPublic { get; set; } = true;
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string created { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string changed { get; set; }

		public PlaylistEntry(string user, PlaylistInfo playlistInfo)
		{
			id = playlistInfo.Id;
			name = playlistInfo.Name;
			comment = playlistInfo.Comment;
			songCount = playlistInfo.GetSongCount();
			duration = (int)playlistInfo.DurationSeconds;
			coverArt = "pl-" + playlistInfo.Id;
			owner = user;
		}
	}

	public class MusicFoldersContainer
	{
		public List<MusicFolder> musicFolder { get; set; }
	}

	public class MusicFolder
	{
		public string id { get; set; }
		public string name { get; set; }
	}

	public class ArtistsContainer
	{
		// Same spec hint as IndexesContainer (#161) -- /rest/getArtists
		// also returns ArtistsID3 wrapped in indexes by first letter.
		public string ignoredArticles { get; set; } = "The El La Los Las Le Les";
		public List<ArtistIndex> index { get; set; } = new List<ArtistIndex>();
	}

	public class ArtistID3
	{
		public string id { get; set; }
		public string name { get; set; }
		public int albumCount { get; set; }
		public string coverArt { get; set; }

		// --- OpenSubsonic spec fields (Flatline #159), all additive ---
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string starred { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int userRating { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string musicBrainzId { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string sortName { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public List<string> roles { get; set; }
		// Legacy clients sometimes still want artistImageUrl (pre-OpenSubsonic).
		// Empty when we have nothing; same path as coverArt feeds it via
		// getCoverArt if the client requests it.
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string artistImageUrl { get; set; }

		public ArtistID3(ArtistInfo artistInfo)
		{
			id = artistInfo.Id;
			name = artistInfo.Name;
			albumCount = artistInfo.Albums.Count;
			// Always emit a stable id; HandleGetCoverArt resolves "ar-<id>"
			// to a representative album cover (Flatline #224). Keeping the
			// alias here means the response shape doesn't change when an
			// artist gains or loses albums later.
			coverArt = "ar-" + artistInfo.Id;
		}
	}

	public class ArtistIndex
	{
		public string name { get; set; }
		public List<ArtistID3> artist { get; set; } = new List<ArtistID3>();
	}


	public class AlbumWithSongsID3
	{
		public string id { get; set; }
		public string name { get; set; }
		public string artist { get; set; }
		public string artistId { get; set; }
		public int songCount { get; set; }
		public int duration { get; set; }
		public string coverArt { get; set; }
		public int year { get; set; }
		public string genre { get; set; }
		public List<SongID3> song { get; set; } = new List<SongID3>();

		// --- OpenSubsonic spec fields (Flatline #159), mirror AlbumID3. ---
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int playCount { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string played { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string starred { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string displayArtist { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string created { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string musicBrainzId { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string sortName { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool isCompilation { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string releaseDate { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string originalReleaseDate { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int userRating { get; set; }

		public AlbumWithSongsID3(string user, AlbumInfo albumInfo)
		{
			id = albumInfo.Id;
			name = albumInfo.Name;
			artist = albumInfo.ArtistName;
			artistId = albumInfo.ArtistId;
			songCount = albumInfo.Tracks.Count;
			coverArt = albumInfo.CoverArtId;
			year = albumInfo.Year;
			genre = albumInfo.Genre;
			displayArtist = albumInfo.ArtistName;

			long total = 0;
			int playCountTotal = 0;
			DateTime mostRecent = default(DateTime);
			float ratingTotal = 0f;
			int ratedCount = 0;
			for (int trackIndex = 0; trackIndex < albumInfo.Tracks.Count; trackIndex++)
			{
				TrackInfo track = albumInfo.Tracks[trackIndex];
				total = total + track.DurationSeconds;
				playCountTotal = playCountTotal + track.Score.PlayCount;
				if (track.LastPlayed > mostRecent) { mostRecent = track.LastPlayed; }
				if (track.Rating > 0)
				{
					ratingTotal = ratingTotal + track.Rating;
					ratedCount++;
				}
				song.Add(new SongID3(user, track));
			}
			duration = (int)total;
			playCount = playCountTotal;
			if (mostRecent != default(DateTime))
			{
				played = mostRecent.ToString("o");
			}
			if (ratedCount > 0)
			{
				userRating = (int)Math.Round(ratingTotal / ratedCount);
			}
			if (user != null && albumInfo.Starred.ContainsKey(user) && albumInfo.Starred[user])
			{
				starred = DateTime.UtcNow.ToString("o");
			}
		}
	}

	public class SongID3
	{
		public string id { get; set; }
		public string title { get; set; }
		public string album { get; set; }
		public string albumId { get; set; }
		public string artist { get; set; }
		public string artistId { get; set; }
		public int track { get; set; }
		public int discNumber { get; set; }
		public int year { get; set; }
		public string genre { get; set; }
		public int duration { get; set; }
		public long size { get; set; }
		public string suffix { get; set; }
		public string contentType { get; set; }
		public string coverArt { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int userRating { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string starred { get; set; }  // ISO datetime string or null

		// --- OpenSubsonic spec fields (Flatline #159), all additive ---
		// Constants that strict clients expect to always be present:
		public string parent { get; set; }
		public bool isDir { get; set; } = false;
		public bool isVideo { get; set; } = false;
		public string type { get; set; } = "music";
		public string mediaType { get; set; } = "song";
		// Optional metadata. Emitted only when populated so existing clients
		// see exactly the JSON they used to. Pulse has no source for most of
		// these without an external metadata provider; left at default.
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int bitRate { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int playCount { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string played { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string created { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string displayArtist { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string sortName { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string musicBrainzId { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string comment { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int bpm { get; set; }

		public SongID3(string user, TrackInfo trackInfo) : this(trackInfo)
		{
			if (user != null && trackInfo.Starred.ContainsKey(user) && trackInfo.Starred[user])
			{
				starred = DateTime.UtcNow.ToString("o");
			}

			if (user != null && trackInfo.UserScore.ContainsKey(user))
			{
				playCount = trackInfo.UserScore[user].PlayCount;
			}
		}
		public SongID3(TrackInfo trackInfo)
		{
			id = trackInfo.Id;
			title = trackInfo.Title;
			album = trackInfo.Album;
			albumId = trackInfo.AlbumId;
			artist = trackInfo.Artist;
			artistId = trackInfo.ArtistId;
			track = trackInfo.TrackNumber;
			discNumber = trackInfo.DiscNumber;
			year = trackInfo.Year;
			genre = trackInfo.Genre;
			duration = trackInfo.DurationSeconds;
			size = trackInfo.FileSizeBytes;
			suffix = trackInfo.Suffix;
			contentType = trackInfo.ContentType;
			coverArt = trackInfo.CoverArtId;
			userRating = trackInfo.Rating;
			starred = null;
			playCount = trackInfo.Score.PlayCount;

			// --- New populations for OpenSubsonic (#159) ---
			parent = trackInfo.AlbumId;
			displayArtist = trackInfo.Artist;
			// Rough average bitrate from file size + duration. Spec is kbps.
			if (trackInfo.DurationSeconds > 0 && trackInfo.FileSizeBytes > 0)
			{
				bitRate = (int)((trackInfo.FileSizeBytes * 8L) / (trackInfo.DurationSeconds * 1000L));
			}
			
			if (trackInfo.LastPlayed != default(DateTime))
			{
				played = trackInfo.LastPlayed.ToString("o");
			}
		}
	}

	public class ArtistWithAlbumsID3
	{
		public string id { get; set; }
		public string name { get; set; }
		public int albumCount { get; set; }
		public string coverArt { get; set; }
		public List<AlbumID3> album { get; set; } = new List<AlbumID3>();

		// --- OpenSubsonic spec fields (Flatline #159), mirror ArtistID3. ---
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string starred { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int userRating { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string musicBrainzId { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string sortName { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public List<string> roles { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string artistImageUrl { get; set; }

		public ArtistWithAlbumsID3(string user, ArtistInfo artistInfo)
		{
			id = artistInfo.Id;
			name = artistInfo.Name;
			albumCount = artistInfo.Albums.Count;
			// Stable alias id; HandleGetCoverArt resolves "ar-<id>" to a
			// representative album cover.
			coverArt = "ar-" + artistInfo.Id;
			for (int index = 0; index < artistInfo.Albums.Count; index++)
			{
				album.Add(new AlbumID3(artistInfo.Albums[index]));
			}
			if (user != null && artistInfo.Starred.ContainsKey(user) && artistInfo.Starred[user])
			{
				starred = DateTime.UtcNow.ToString("o");
			}
		}
	}

	public class AlbumList2
	{
		public List<AlbumID3> album { get; set; } = new List<AlbumID3>();
	}

	public class AlbumID3
	{
		public string id { get; set; }
		public string name { get; set; }
		public string artist { get; set; }
		public string artistId { get; set; }
		public int songCount { get; set; }
		public int duration { get; set; }
		public string coverArt { get; set; }
		public int year { get; set; }
		public string genre { get; set; }

		// --- OpenSubsonic spec fields (Flatline #159), all additive ---
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int playCount { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string played { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string starred { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string displayArtist { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string created { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string musicBrainzId { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string sortName { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool isCompilation { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string releaseDate { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string originalReleaseDate { get; set; }
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int userRating { get; set; }

		public AlbumID3(AlbumInfo albumInfo)
		{
			id = albumInfo.Id;
			name = albumInfo.Name;
			artist = albumInfo.ArtistName;
			artistId = albumInfo.ArtistId;
			songCount = albumInfo.Tracks.Count;
			coverArt = albumInfo.CoverArtId;
			year = albumInfo.Year;
			genre = albumInfo.Genre;
			// Sum track durations -- previously left at 0 so every album in
			// getAlbumList2 / getArtist / search3 / starred2 reported 0:00.
			long total = 0;
			int playCountTotal = 0;
			DateTime mostRecent = default(DateTime);
			float ratingTotal = 0f;
			int ratedCount = 0;
			for (int trackIndex = 0; trackIndex < albumInfo.Tracks.Count; trackIndex++)
			{
				TrackInfo track = albumInfo.Tracks[trackIndex];
				total = total + track.DurationSeconds;
				playCountTotal = playCountTotal + track.Score.PlayCount;
				if (track.LastPlayed > mostRecent) { mostRecent = track.LastPlayed; }
				if (track.Rating > 0)
				{
					ratingTotal = ratingTotal + track.Rating;
					ratedCount++;
				}
			}
			duration = (int)total;

			// OpenSubsonic aggregates (#159).
			displayArtist = albumInfo.ArtistName;
			playCount = playCountTotal;
			if (mostRecent != default(DateTime))
			{
				played = mostRecent.ToString("o");
			}
			if (ratedCount > 0)
			{
				userRating = (int)Math.Round(ratingTotal / ratedCount);
			}
		}
	}

	public class GenresContainer
	{
		public List<GenreEntry> genre { get; set; } = new List<GenreEntry>();
	}

	public class GenreEntry
	{
		public int songCount { get; set; }
		public int albumCount { get; set; }
		public string value { get; set; }
		public GenreEntry(GenreInfo pulseGenre)
		{
			songCount = pulseGenre.TrackCount;
			albumCount = pulseGenre.AlbumCount;
			value = pulseGenre.Name;
		}
	}

	public class UserInfo
	{
		public string username { get; set; }
		public bool adminRole { get; set; }
		public bool scrobblingEnabled { get; set; }
		public bool settingsRole { get; set; }
		public bool downloadRole { get; set; }
		public bool playlistRole { get; set; }
		public bool streamRole { get; set; }

		// --- Spec fields (Flatline #160). Clients gate UI features on
		// these; missing = treated as false by lenient parsers, rejected
		// by strict ones. Pulse is single-user-ish for now so defaults are
		// generous except for features we don't actually implement.
		public string email { get; set; } = "";
		public int maxBitRate { get; set; } = 0;          // 0 = no limit
		public bool uploadRole { get; set; } = false;     // no upload UI
		public bool coverArtRole { get; set; } = true;
		public bool commentRole { get; set; } = false;
		public bool podcastRole { get; set; } = false;
		public bool jukeboxRole { get; set; } = false;
		public bool shareRole { get; set; } = false;
		public bool videoConversionRole { get; set; } = false;
		// folder ids the user can access. Matches getMusicFolders, which
		// returns a single folder id "1" (see HandleGetMusicFolders).
		public List<string> folder { get; set; } = new List<string>() { "1" };

		public UserInfo() 
		{

		}
		public UserInfo(UserRecord userRecord)
		{
			username = userRecord.Name;
			adminRole = userRecord.IsAdmin;
		}
	}
	

	public class SubsonicError
	{
		public int code { get; set; }
		public string message { get; set; }
	}

	public class LicenseInfo
	{
		public bool valid { get; set; } = true;
	}

	public class SubsonicWrapper
	{
		[JsonPropertyName("subsonic-response")]
		public SubsonicResponseBody response { get; set; }
	}
}
