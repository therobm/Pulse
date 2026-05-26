
using Pulse.MusicLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Pulse.SubsonicService
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

		[JsonPropertyName("internetRadioStations")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public InternetRadioStationsContainer InternetRadioStations { get; set; }

		[JsonPropertyName("indexes")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public IndexesContainer Indexes { get; set; }

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

		public PlaylistWithSongs()
		{
			entry = new List<SongID3>();
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
		[JsonPropertyName("index")]
		public List<ArtistIndex> Index { get; set; }
	}

	public class InternetRadioStationsContainer
	{
		[JsonPropertyName("internetRadioStation")]
		public List<object> InternetRadioStation { get; set; }
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
		public List<ArtistIndex> index { get; set; } = new List<ArtistIndex>();
	}

	public class ArtistID3
	{
		public string id { get; set; }
		public string name { get; set; }
		public int albumCount { get; set; }
		public string coverArt { get; set; }
		public ArtistID3(ArtistInfo artistInfo)
		{
			id = artistInfo.Id;
			name = artistInfo.Name;
			albumCount = artistInfo.Albums.Count;
			if (albumCount > 0) 
			{
				coverArt = artistInfo.Albums[0].CoverArtId;
			}
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

		public SongID3(string user, TrackInfo trackInfo)
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
			if (user != null && trackInfo.Starred.ContainsKey(user) && trackInfo.Starred[user])
			{
				starred = DateTime.UtcNow.ToString("o");
			}
			else
			{
				starred = null;
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
			for (int trackIndex = 0; trackIndex < albumInfo.Tracks.Count; trackIndex++)
			{
				total = total + albumInfo.Tracks[trackIndex].DurationSeconds;
			}
			duration = (int)total;
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
