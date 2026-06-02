
using Microsoft.AspNetCore.Http;
using Pulse.Data;
using Pulse.MusicLibrary;
using Pulse.Protocols;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pulse.Protocols.LegacyPulse;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Routing.Matching;

namespace Pulse.Protocols.Subsonic
{
	public class Subsonic
	{
		private static JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		private static List<T> ExtractPulseContentList<T>(IResult result) where T : PulseInfo
		{
			JsonHttpResult<PulseResponse> response = result as JsonHttpResult<PulseResponse>;
			if (response == null || response.Value == null || response.Value.itemList == null)
				return null;
			return response.Value.itemList as List<T>;
		}
		private static T ExtractPulseContent<T>(IResult result) where T : PulseInfo
		{
			JsonHttpResult<PulseResponse> response = result as JsonHttpResult<PulseResponse>;
			if (response == null || response.Value == null || response.Value.item == null)
				return null;
			return response.Value.item as T;
		}
		private static bool IsPulseError(IResult result, out Error error)
		{
			error = null;
			JsonHttpResult<PulseResponse> response = result as JsonHttpResult<PulseResponse>;
			if (response == null || response.Value == null)
				return false;
			error = response.Value.error;
			return error != null;
		}

		global::Pulse.Protocols.LegacyPulse.LegacyPulseAPI m_pulseAPI;

		public Subsonic(global::Pulse.Protocols.LegacyPulse.LegacyPulseAPI pulseAPI)
		{
			m_pulseAPI = pulseAPI;
		}

		// ========================================
		// Helpers
		// ========================================

		private SubsonicResponseBody CreateResponse()
		{
			SubsonicResponseBody body = new SubsonicResponseBody();
			return body;
		}

		private SubsonicResponseBody CreateErrorResponse(int code, string message)
		{
			SubsonicResponseBody body = new SubsonicResponseBody();
			body.status = "failed";
			body.error = new SubsonicError();
			body.error.code = code;
			body.error.message = message;
			return body;
		}

		private IResult Respond(HttpContext context, SubsonicResponseBody body)
		{
			string format = context.Request.Query["f"].FirstOrDefault() ?? "json";

			SubsonicWrapper wrapper = new SubsonicWrapper();
			wrapper.response = body;

			if (format == "json")
			{
				return Results.Json(wrapper, s_jsonOptions);
			}

			// XML — bare minimum for now, expand later if clients need it
			string xml = JsonSerializer.Serialize(wrapper, s_jsonOptions);
			return Results.Text(xml, "application/json");
		}
		// ========================================
		// Endpoints
		// ========================================
		public IResult HandleGetUser(HttpContext context)
		{
			string username = context.Request.Query["username"].FirstOrDefault() ?? "Rob";

			SubsonicResponseBody body = CreateResponse();
			body.user = new UserInfo();
			body.user.username = username;
			body.user.adminRole = true;
			body.user.scrobblingEnabled = true;
			body.user.settingsRole = true;
			body.user.downloadRole = true;
			body.user.playlistRole = true;
			body.user.streamRole = true;

			return Respond(context, body);
		}

		public IResult HandleStream(HttpContext context)
		{
			//shapes are identical
			return m_pulseAPI.GetStream(context);
		}

		public IResult HandlePing(HttpContext context)
		{
			JsonHttpResult<Ping> pResult = m_pulseAPI.Ping(context) as JsonHttpResult<Ping>;

			SubsonicResponseBody body = CreateResponse();
			if (pResult != null && pResult.Value != null)
			{
				Ping response = pResult.Value;
				body.version = response.serverVersion;
			}
			return Respond(context, body);
		}

		public IResult HandleGetSong(HttpContext context)
		{
			//incoming shape is identical
			IResult pResult = m_pulseAPI.GetTrack(context);
			if (IsPulseError(pResult, out Error error))
			{
				return Respond(context, CreateErrorResponse(70, error.message));
			}

			
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			TrackInfo track = ExtractPulseContent<TrackInfo>(pResult);

			if (track == null)
			{
				return Respond(context, CreateErrorResponse(70, "Song not found"));
			}

			SubsonicResponseBody body = CreateResponse();
			body.song = new SongID3(user, track);
			

			return Respond(context, body);
		}

		public IResult HandleGetCoverArt(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string sType = "album";

			if (!string.IsNullOrEmpty(id) && id.StartsWith("pl-"))
			{
				id = id.Substring(3);
				sType = "playlist";
			}
			if (!string.IsNullOrEmpty(id) && id.StartsWith("ar-"))
			{
				id = id.Substring(3);
				sType = "artist";
			}

			Dictionary<string, StringValues> dict = new Dictionary<string, StringValues>();
			dict["id"] = id;
			dict["type"] = sType;

			DefaultHttpContext forwarded = new DefaultHttpContext();
			forwarded.Request.Query = new QueryCollection(dict);

			FileContentHttpResult pResult = m_pulseAPI.GetCoverArt(forwarded) as FileContentHttpResult;
			//this is just bytes no conversion needed
			return pResult;
		}

		/// <summary>
		/// Faked route TODO: Delete
		/// Pulse does not support whatever the hell this is for
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		public IResult HandleGetIndexes(HttpContext context)
		{
			SubsonicResponseBody body = new SubsonicResponseBody();
			body.indexes = new IndexesContainer();

			JsonHttpResult<SearchResult> pResult = m_pulseAPI.GetArtists(context) as JsonHttpResult<SearchResult>;
			if (pResult == null || pResult.Value == null)
			{
				return Respond(context, body);
			}

			List<ArtistInfo> allArtists = pResult.Value.Artists;
			Dictionary<string, List<ArtistID3>> grouped = new Dictionary<string, List<ArtistID3>>();

			for (int i = 0; i < allArtists.Count; i++)
			{
				ArtistInfo artist = allArtists[i];
				string firstChar = "#";
				if (artist.Name.Length > 0)
				{
					firstChar = artist.Name.Substring(0, 1).ToUpperInvariant();
					if (!char.IsLetter(firstChar[0]))
					{
						firstChar = "#";
					}
				}

				if (!grouped.ContainsKey(firstChar))
				{
					grouped[firstChar] = new List<ArtistID3>();
				}

				ArtistID3 entry = new ArtistID3(artist);
				grouped[firstChar].Add(entry);
			}

			List<ArtistIndex> indexList = new List<ArtistIndex>();
			List<string> keys = new List<string>(grouped.Keys);
			keys.Sort();

			for (int i = 0; i < keys.Count; i++)
			{
				ArtistIndex idx = new ArtistIndex();
				idx.name = keys[i];
				idx.artist = grouped[keys[i]];
				indexList.Add(idx);
			}


			body.indexes.index = indexList;
			return Respond(context, body);
		}

		/// <summary>
		/// Pulse supports Podcasts so maybe this is ok to keep
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		public IResult HandleGetInternetRadioStations(HttpContext context)
		{
			SubsonicResponseBody body = new SubsonicResponseBody();
			body.internetRadioStations = new InternetRadioStationsContainer();
			body.internetRadioStations.internetRadioStation = new List<object>();

			IResult pResult = m_pulseAPI.GetPodcasts(context);
			if (IsPulseError(pResult, out Error error))
			{
				return Respond(context, CreateErrorResponse(10, error.message));
			}

			//podcast support todo


			return Respond(context, body);
		}

		public IResult HandleSearch3(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();

			IResult pResult = m_pulseAPI.Search(context);
			if (IsPulseError(pResult, out Error error))
			{
				return Respond(context, CreateErrorResponse(10, error.message));
			}

			SearchResult searchResult = ExtractPulseContent<SearchResult>(pResult);
			if (searchResult != null)
			{
				body.searchResult3 = new SearchResult3();
				if (searchResult.Albums != null)
				{
					for (int i = 0; i < searchResult.Albums.Count; i++)
						body.searchResult3.album.Add(new AlbumID3(searchResult.Albums[i]));
				}

				if (searchResult.Artists != null)
				{
					for (int i = 0; i < searchResult.Artists.Count; i++)
						body.searchResult3.artist.Add(new ArtistID3(searchResult.Artists[i]));
				}

				if (searchResult.Tracks != null)
				{
					for (int i = 0; i < searchResult.Tracks.Count; i++)
						body.searchResult3.song.Add(new SongID3(searchResult.Tracks[i]));
				}
			}

			return Respond(context, body);
		}


		public IResult HandleGetTopSongs(HttpContext context)
		{
			SearchResult pulseResult = ExtractPulseContent<SearchResult>(m_pulseAPI.GetTopTracks(context));
			
			SubsonicResponseBody body = CreateResponse();
			if (pulseResult == null)
			{
				return Respond(context, body);
			}

			string user = context.Request.Query["u"].FirstOrDefault();

		
			body.topSongs = new TopSongsContainer();
			if (pulseResult.Tracks != null)
			{
				for (int index = 0; index < pulseResult.Tracks.Count; index++)
				{
					body.topSongs.song.Add(new SongID3(user, pulseResult.Tracks[index]));
				}
			}

			return Respond(context, body);
		}

		public IResult HandleGetStarred(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.starred = BuildStarredContainer(context);
			return Respond(context, body);
		}

		public IResult HandleGetStarred2(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.starred2 = BuildStarredContainer(context);
			return Respond(context, body);
		}

		// Shared between getStarred and getStarred2 — both response shapes hold
		// the same container, just under different field names.
		private StarredContainer BuildStarredContainer(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();

			StarredContainer container = new StarredContainer();

			SearchResult content = ExtractPulseContent<SearchResult>(m_pulseAPI.GetFavorites(context));

			if (content == null)
			{
				return container;
			}

			if (content.Artists != null)
			{
				for (int index = 0; index < content.Artists.Count; index++)
				{
					container.artist.Add(new ArtistID3(content.Artists[index]));
				}
			}
			if (content.Albums != null)
			{
				for (int index = 0; index < content.Albums.Count; index++)
				{
					container.album.Add(new AlbumID3(content.Albums[index]));
				}
			}
			if (content.Tracks != null)
			{
				for (int index = 0; index < content.Tracks.Count; index++)
				{
					container.song.Add(new SongID3(user, content.Tracks[index]));
				}
			}
			return container;
		}

		/// <summary>
		/// This is unsupported and should respond with that not mostly empty crap
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		public IResult HandleGetArtistInfo(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 20);
			SubsonicResponseBody body = CreateResponse();
			body.artistInfo2 = new ArtistInfo2();
			return Respond(context, body);
		}

		public IResult HandleSetRating(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int rating = QueryParameters.GetInt(context, "rating", 0);

			return Respond(context, CreateErrorResponse(10, "Rating not supported on Pulse"));
		}

		public IResult HandleStar(HttpContext context)
		{

			string id = context.Request.Query["id"].FirstOrDefault();
			string albumId = context.Request.Query["albumId"].FirstOrDefault();
			string artistId = context.Request.Query["artistId"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();


			string itemID = id;
			string type = "track";
			if (!string.IsNullOrEmpty(albumId))
			{
				itemID = albumId;
				type = "album";
			}
			else if (!string.IsNullOrEmpty(artistId))
			{
				itemID = artistId;
				type = "artist";
			}

			if (string.IsNullOrEmpty(itemID))
			{
				return Respond(context, CreateResponse());
			}

			Dictionary<string, StringValues> dict = new Dictionary<string, StringValues>();
			dict["id"] = itemID;
			dict["type"] = type;
			dict["u"] = user;

			DefaultHttpContext pulseContext = new DefaultHttpContext();
			pulseContext.Request.Query = new QueryCollection(dict);

			IResult response = m_pulseAPI.Favorite(pulseContext);
			if (IsPulseError(response, out Error error))
			{
				return Respond(context, CreateErrorResponse(10, error.message));
			}

			return Respond(context, CreateResponse());

		}

		public IResult HandleUnstar(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string albumId = context.Request.Query["albumId"].FirstOrDefault();
			string artistId = context.Request.Query["artistId"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();


			string itemID = id;
			string type = "track";
			if (!string.IsNullOrEmpty(albumId))
			{
				itemID = albumId;
				type = "album";
			}
			else if (!string.IsNullOrEmpty(artistId))
			{
				itemID = artistId;
				type = "artist";
			}

			if (string.IsNullOrEmpty(itemID))
			{
				return Respond(context, CreateResponse());
			}

			Dictionary<string, StringValues> dict = new Dictionary<string, StringValues>();
			dict["id"] = itemID;
			dict["type"] = type;
			dict["u"] = user;

			DefaultHttpContext forwarded = new DefaultHttpContext();
			forwarded.Request.Query = new QueryCollection(dict);

			IResult response = m_pulseAPI.Unfavorite(forwarded);
			if (IsPulseError(response, out Error error))
			{
				return Respond(context, CreateErrorResponse(10, error.message));
			}

			return Respond(context, CreateResponse());
		}


		public IResult HandleScrobble(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			string submissionParam = context.Request.Query["submission"].FirstOrDefault();
			string timeParam = context.Request.Query["time"].FirstOrDefault();
			string clientParam = context.Request.Query["c"].FirstOrDefault();

			string submissionResolved = submissionParam;
			if (submissionResolved == null)
			{
				submissionResolved = "true";
			}
			bool submission = submissionResolved != "false";

			// The Subsonic API's "submission=true" intent (after-play scrobble) doesn't align with Pulse's scoring model.
			// We intentionally REJECT the explicit submit and use the !submission (track-start) call instead, because that
			// is the signal 3rd-party clients give us that maps cleanly to Pulse's "served to user" play metric. Do not
			// flip this condition.
			if (!submission)
			{
				Log.Info(-1, "Scrobble: id=" + id + " user=" + user + " submission=" + submission + " time=" + timeParam + " client=" + clientParam + " raw_submission=" + submissionParam);


				Dictionary<string, StringValues> values = new Dictionary<string, StringValues>();
				values["u"] = user;
				values["id"] = id;
				DefaultHttpContext forwarded = new DefaultHttpContext();
				forwarded.Request.Query = new QueryCollection(values);

				m_pulseAPI.ReportTrackAnalytics(forwarded);
			}
			return Respond(context, CreateResponse());
		}


		public IResult HandleGetLicense(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.license = new LicenseInfo();
			return Respond(context, body);
		}
		public IResult HandleGetArtists(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.artists = new ArtistsContainer();

			SearchResult content = ExtractPulseContent<SearchResult>(m_pulseAPI.GetArtists(context));
			if (content == null || content.Artists == null)
			{
				return Respond(context, body);
			}

			Dictionary<string, ArtistIndex> indexMap = new Dictionary<string, ArtistIndex>();
			for (int index = 0; index < content.Artists.Count; index++)
			{
				ArtistInfo source = content.Artists[index];
				string firstChar = "#";
				if (source.Name.Length > 0)
				{
					firstChar = source.Name.Substring(0, 1).ToUpperInvariant();
					if (!char.IsLetter(firstChar[0]))
					{
						firstChar = "#";
					}
				}

				ArtistIndex artistIndex = null;
				bool hasIndex = indexMap.TryGetValue(firstChar, out artistIndex);
				if (!hasIndex)
				{
					artistIndex = new ArtistIndex();
					artistIndex.name = firstChar;
					indexMap[firstChar] = artistIndex;
					body.artists.index.Add(artistIndex);
				}

				artistIndex.artist.Add(new ArtistID3(source));
			}

			return Respond(context, body);
		}

		public IResult HandleGetArtist(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();

			ArtistInfo source = ExtractPulseContent<ArtistInfo>(m_pulseAPI.GetArtist(context));
			if (source == null)
			{
				return Respond(context, CreateErrorResponse(70, "Artist not found"));
			}

			SubsonicResponseBody body = CreateResponse();
			body.artist = new ArtistWithAlbumsID3(user, source);
			return Respond(context, body);
		}

		public IResult HandleGetAlbum(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();

			AlbumInfo source = ExtractPulseContent<AlbumInfo>(m_pulseAPI.GetAlbum(context));
			if (source == null)
			{
				return Respond(context, CreateErrorResponse(70, "Album not found"));
			}

			SubsonicResponseBody body = CreateResponse();
			body.album = new AlbumWithSongsID3(user, source);
			return Respond(context, body);
		}

		public IResult HandleGetAlbumList2(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.albumList2 = new AlbumList2();

			SearchResult content = ExtractPulseContent<SearchResult>(m_pulseAPI.GetAlbums(context));
			if (content != null && content.Albums != null)
			{
				for (int index = 0; index < content.Albums.Count; index++)
				{
					body.albumList2.album.Add(new AlbumID3(content.Albums[index]));
				}
			}

			return Respond(context, body);
		}

		public IResult HandleGetGenres(HttpContext context)
		{
			List<GenreInfo> genres = ExtractPulseContentList<GenreInfo>(m_pulseAPI.GetGenres(context));

			SubsonicResponseBody body = CreateResponse();
			body.genres = new GenresContainer();
			body.genres.genre = new List<GenreEntry>();
			if (genres != null)
			{
				for (int i = 0; i < genres.Count; i++)
				{
					if (genres[i] != null)
					{
						body.genres.genre.Add(new GenreEntry(genres[i]));
					}
				}
			}

			return Respond(context, body);
		}

		/// <summary>
		/// OpenSubsonic /rest/getSongsByGenre (Flatline #149). Returns tracks
		/// matching the requested genre, paginated via count/offset (defaults
		/// match the spec: count=10, offset=0). Used by Thump's Library tab
		/// genre rows and the Genre detail screen.
		/// </summary>
		public IResult HandleGetSongsByGenre(HttpContext context)
		{
			string genre = context.Request.Query["genre"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(genre))
			{
				return Respond(context, CreateErrorResponse(10, "Missing required parameter: genre"));
			}

			SubsonicResponseBody body = CreateResponse();
			body.songsByGenre = new SongsByGenreContainer();

			SearchResult content = ExtractPulseContent<SearchResult>(m_pulseAPI.GetGenreTracks(context));
			if (content != null && content.Tracks != null)
			{
				for (int index = 0; index < content.Tracks.Count; index++)
				{
					body.songsByGenre.song.Add(new SongID3(user, content.Tracks[index]));
				}
			}

			return Respond(context, body);
		}

		public IResult HandleGetRandomSongs(HttpContext context)
		{
			
			SubsonicResponseBody body = CreateResponse();
			body.randomSongs = new RandomSongsContainer();
			return Respond(context, body);
		}

		// Standard /rest/download (Flatline #163). Same semantics as stream
		// but with Content-Disposition: attachment so clients save rather than
		// play inline. No transcoding -- original file.
		public IResult HandleDownload(HttpContext context)
		{
			//pulse does "downloads" via streams
			return Respond(context, CreateErrorResponse(70, "Download not supported"));
		}



		public IResult HandleGetNowPlaying(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "Server side playback not supported"));
		}

		public IResult HandleGetAlbumInfo(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "Album Info not supported"));
		}

		// Standard /rest/getSimilarSongs and /rest/getSimilarSongs2 (Flatline
		// #166). Derives similarity from same-artist tracks first (by score
		// desc) then same-genre tracks from other artists. No external lookup.
		public IResult HandleGetSimilarSongs2(HttpContext context)
		{

			return Respond(context, CreateErrorResponse(70, "SimilarSong not supported"));
		}

		public IResult HandleGetLyrics(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "Lyrics not supported"));
		}

		public IResult HandleGetLyricsBySongId(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "Lyrics not supported"));
		}

		public IResult HandleGetPlayQueue(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "not supported"));
		}

		public IResult HandleSavePlayQueue(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, " not supported"));
		}

		public IResult HandleGetBookmarks(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "not supported"));
		}

		public IResult HandleCreateBookmark(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "not supported"));
		}

		public IResult HandleDeleteBookmark(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "not supported"));
		}

		public IResult HandleGetOpenSubsonicExtensions(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.openSubsonicExtensions = new List<OpenSubsonicExtension>();
			return Respond(context, body);
		}

		public IResult HandleGetPlaylists(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			body.playlists = new PlaylistsContainer();

			JsonHttpResult<PulseResponse> pulseResult = m_pulseAPI.GetPlaylists(context) as JsonHttpResult<PulseResponse>;
			SearchResult content = ExtractPulseContent<SearchResult>(pulseResult);
			if (content != null && content.Playlists != null)
			{
				for (int index = 0; index < content.Playlists.Count; index++)
				{
					body.playlists.playlist.Add(new PlaylistEntry(user, content.Playlists[index]));
				}
			}

			return Respond(context, body);
		}

		public IResult HandleGetPlaylist(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();

			PlaylistAndTracks playlist = ExtractPulseContent<PlaylistAndTracks>(m_pulseAPI.GetPlaylist(context));
			if (playlist == null)
			{
				return Results.NotFound();
			}

			SubsonicResponseBody body = CreateResponse();
			body.playlist = new PlaylistWithSongs(user, playlist);
			return Respond(context, body);
		}

		public IResult HandleCreatePlaylist(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();


			PlaylistAndTracks playlist = ExtractPulseContent<PlaylistAndTracks>(m_pulseAPI.CreatePlaylist(context));

			if (playlist == null)
			{
				return Respond(context, CreateErrorResponse(0, "Playlist creation failed"));
			}

			SubsonicResponseBody body = CreateResponse();
			body.playlist = new PlaylistWithSongs(user, playlist);
			return Respond(context, body);
		}

		public IResult HandleUpdatePlaylist(HttpContext context)
		{
			JsonHttpResult<PulseResponse> pulseResult = m_pulseAPI.UpdatePlaylist(context) as JsonHttpResult<PulseResponse>;
			if (pulseResult != null && pulseResult.Value != null && pulseResult.Value.error != null)
			{
				return Respond(context, CreateErrorResponse(pulseResult.Value.error.code, pulseResult.Value.error.message));
			}
			// PulseAPI.UpdatePlaylist owns the cover-art cache eviction. If
			// Subsonic ever holds its own copy under a "pl-"-prefixed key we'll
			// need to evict it here as well.
			return Respond(context, CreateResponse());
		}

		public IResult HandleDeletePlaylist(HttpContext context)
		{
			JsonHttpResult<PulseResponse> pulseResult = m_pulseAPI.DeletePlaylist(context) as JsonHttpResult<PulseResponse>;
			if (pulseResult != null && pulseResult.Value != null && pulseResult.Value.error != null)
			{
				return Respond(context, CreateErrorResponse(pulseResult.Value.error.code, pulseResult.Value.error.message));
			}
			return Respond(context, CreateResponse());
		}




		public IResult HandleGetMusicFolders(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.musicFolders = new MusicFoldersContainer();
			body.musicFolders.musicFolder = new List<MusicFolder>();

			MusicFolder folder = new MusicFolder();
			folder.id = "1";
			folder.name = "Music";
			body.musicFolders.musicFolder.Add(folder);

			return Respond(context, body);
		}
		public IResult HandleGetMusicDirectory(HttpContext context)
		{
			return Respond(context, CreateErrorResponse(70, "not supported"));
		}


	}
}
