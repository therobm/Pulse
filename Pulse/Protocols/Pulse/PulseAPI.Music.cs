

using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using Pulse.MusicLibrary;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Pulse.Protocols.Pulse
{
	public partial class PulseAPI
	{
		static JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions()
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		protected IResult CreateResponse()
		{
			PulseResponse response = new PulseResponse();
			return Results.Json(response);
		}
		
		protected IResult CreateResponse(PulseInfo content)
		{
			PulseResponse response = new PulseResponse();
			response.item = content;
			return Results.Json(response);
		}
	
		protected IResult CreateResponse(byte[] data)
		{
			PulseResponse response = new PulseResponse();
			response.data = data;
			return Results.Json(response);
		}
		protected IResult CreateResponse(Error error)
		{
			PulseResponse response = new PulseResponse();
			response.error = error;
			return Results.Json(response);
		}


		public IResult GetUser(HttpContext context)
		{
			// I doubt Pulse should support server side user permissions, messy and nearly useless
			List<UserRecord> users = m_musicManager.GetAllUsers();
			if (users.Count > 0)
			{
				return CreateResponse(users[0]);
			}
			return CreateResponse(new Error(ePulseCode.NotFound, "User not found"));
		}

		public IResult GetStream(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			TrackInfo track = m_musicManager.GetTrack(id);

			if (track == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Track not found"));
			}
			
			FileStream fileStream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			return Results.File(fileStream, track.ContentType, enableRangeProcessing: true);
		}

		public IResult GetDownload(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			TrackInfo track = m_musicManager.GetTrack(id);

			if (track == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Track not found"));
			}

			FileStream fileStream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

			//specify a file name to add Content-Disposition: attachment
			string downloadName = Path.GetFileName(track.FilePath);
			return Results.File(fileStream, track.ContentType, enableRangeProcessing: true);
		}

		public IResult Ping(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			Ping response = new Ping();
			response.serverVersion = PulseService.GetServerVersion();
			return Results.Json(response);
		}
		public IResult GetTrack(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			TrackInfo track = m_musicManager.GetTrack(id);

			if (track == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Track not found"));
			}
			return CreateResponse(track);
		}
		public IResult GetCoverArt(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string sType = context.Request.Query["type"].FirstOrDefault();

			int size = QueryParameters.GetInt(context, "size", 0);

			if (string.IsNullOrEmpty(id))
			{
				return Results.Bytes(m_defaultCoverArt, "image/png");
			}
			
			if (m_coverArtCache.TryGetValue(id, out byte[] cached))
			{
				if (cached.Length == 0)
				{
					return Results.Bytes(m_defaultCoverArt, "image/png");
				}
				return Results.Bytes(cached, "image/jpeg");
			}

			if (sType == "playlist")
			{
				PlaylistInfo playlist = m_musicManager.GetPlaylist(id);
				if (playlist != null)
				{
					// Collect up to 4 distinct album covers, in playlist order.
					List<byte[]> tileBytes = new List<byte[]>();
					HashSet<string> seenAlbumIds = new HashSet<string>();
					for (int idx = 0; idx < playlist.TrackIds.Count && tileBytes.Count < 4; idx++)
					{
						TrackInfo track = m_musicManager.GetTrack(playlist.TrackIds[idx]);
						if (track == null || string.IsNullOrEmpty(track.AlbumId))
						{
							continue;
						}

						if (!seenAlbumIds.Add(track.AlbumId))
						{
							continue;
						}

						AlbumInfo album = m_musicManager.GetAlbum(track.AlbumId);
						if (album == null)
						{
							continue;
						}

						if (m_musicManager.GetAlbumCover(album, out byte[] imageBytes, out string contentType))
						{
							tileBytes.Add(imageBytes);
						}
					}
					if (tileBytes.Count > 0)
					{

						try
						{
							byte[] composed = ImageComposer.ComposeTiledImage(tileBytes, 600);
							m_coverArtCache[id] = composed;
							return Results.Bytes(composed, "image/jpeg");
						}
						catch (Exception ex)
						{
							Log.Error(-1, "HandlePlaylistCompositeCover: failed to compose - " + ex.Message);
						}
					}
				}
			}
			else if (sType == "artist")
			{
				ArtistInfo artist = m_musicManager.GetArtist(id);
				if (artist != null)
				{
					if (m_musicManager.GetArtistImage(artist, out byte[] imageBytes, out string contentType))
					{
						m_coverArtCache[id] = imageBytes;
						return Results.Bytes(imageBytes, contentType);
					}
				}

			}
			else
			{
				//albums

				AlbumInfo album = m_musicManager.GetAlbum(id);
				if (album != null)
				{
					if (m_musicManager.GetAlbumCover(album, out byte[] imageBytes, out string contentType))
					{
						m_coverArtCache[id] = imageBytes;
						return Results.Bytes(imageBytes, contentType);
					}
				}

			}

			m_coverArtCache[id] = m_defaultCoverArt;
			return Results.Bytes(m_defaultCoverArt, "image/png");
		}



		public IResult GetPodcasts(HttpContext context)
		{
			return CreateResponse(new Error(ePulseCode.NotImplemented, "Not implemented yet"));
		}

		public IResult Search(HttpContext context)
		{
			string query = context.Request.Query["query"].FirstOrDefault() ?? "";
			query = query.Trim('"');

			if (string.IsNullOrEmpty(query))
			{
				return CreateResponse(new SearchResult());
			}

			string user = context.Request.Query["u"].FirstOrDefault();

			int artistCount = QueryParameters.GetInt(context, "artistCount", 20);
			int albumCount = QueryParameters.GetInt(context, "albumCount", 20);
			int songCount = QueryParameters.GetInt(context, "songCount", 20);

			int artistOffset = QueryParameters.GetInt(context, "artistOffset", 0);
			int albumOffset = QueryParameters.GetInt(context, "albumOffset", 0);
			int songOffset = QueryParameters.GetInt(context, "songOffset", 0);


			SearchResult result = new SearchResult();


			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();

			string lowerQuery = query.ToLowerInvariant();

			int artistHits = 0;
			for (int index = 0; index < allArtists.Count && artistHits < artistCount; index++)
			{
				if (allArtists[index].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					if (result.Artists == null)
						result.Artists = new List<ArtistInfo>();
					result.Artists.Add(allArtists[index]);
					artistHits++;
				}
			}


			int albumHits = 0;
			for (int index = 0; index < allAlbums.Count && albumHits < albumCount; index++)
			{
				if (allAlbums[index].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					if (result.Albums == null)
						result.Albums = new List<AlbumInfo>();
					result.Albums.Add(allAlbums[index]);
					albumHits++;
				}
			}

			// Song search — Feishin sends songCount=500, so cap it
			int songHits = 0;
			for (int albumIndex = 0; albumIndex < allAlbums.Count && songHits < songCount; albumIndex++)
			{
				List<TrackInfo> tracks = allAlbums[albumIndex].Tracks;
				for (int trackIndex = 0; trackIndex < tracks.Count && songHits < songCount; trackIndex++)
				{
					TrackInfo track = tracks[trackIndex];
					if (track == null)
					{
						continue;
					}

					if (track.Title.ToLowerInvariant().Contains(lowerQuery) ||
						track.Artist.ToLowerInvariant().Contains(lowerQuery))
					{
						if (result.Tracks == null)
							result.Tracks = new List<TrackInfo>();
						result.Tracks.Add(track);
						songHits++;
					}
				}
			}
			return CreateResponse(result);
		}


		public IResult GetTopTracks(HttpContext context)
		{
			string artistName = context.Request.Query["artist"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 50);
			if (count < 1)
			{
				count = 1;
			}
			if (count > 500)
			{
				count = 500;
			}

			SearchResult result = new SearchResult();
			result.Tracks = new List<TrackInfo>();

			if (string.IsNullOrEmpty(artistName))
			{
				return CreateResponse(result);
			}

			string artistNameLower = artistName.ToLowerInvariant();
			ArtistInfo artist = null;
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int index = 0; index < allArtists.Count; index++)
			{
				if (allArtists[index].Name.ToLowerInvariant() == artistNameLower)
				{
					artist = allArtists[index];
					break;
				}
			}
			if (artist == null)
			{
				return CreateResponse(result);
			}

			List<TrackInfo> tracks = new List<TrackInfo>();
			for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
			{
				AlbumInfo album = artist.Albums[albumIndex];
				for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
				{
					tracks.Add(album.Tracks[trackIndex]);
				}
			}
			tracks.Sort(MusicComparers.CompareTrackByTopRank);

			for (int index = 0; index < tracks.Count && result.Tracks.Count < count; index++)
			{
				TrackInfo track = tracks[index];
				// Drop never-played tracks so the list isn't filler for artists with no listening history.
				if (track.Score.PlayCount == 0 && track.Score.WeightedScore <= 0f)
				{
					break;
				}
				result.Tracks.Add(track);
			}

			return CreateResponse(result);
		}

		public IResult GetFavorites(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}

			SearchResult result = new SearchResult();
			result.Artists = new List<ArtistInfo>();
			result.Albums = new List<AlbumInfo>();
			result.Tracks = new List<TrackInfo>();

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int index = 0; index < allArtists.Count; index++)
			{
				ArtistInfo artist = allArtists[index];
				bool artistStarred = false;
				artist.Starred.TryGetValue(user, out artistStarred);
				if (artistStarred)
				{
					result.Artists.Add(artist);
				}
			}

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			for (int albumIndex = 0; albumIndex < allAlbums.Count; albumIndex++)
			{
				AlbumInfo album = allAlbums[albumIndex];
				bool albumStarred = false;
				album.Starred.TryGetValue(user, out albumStarred);
				if (albumStarred)
				{
					result.Albums.Add(album);
				}

				for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
				{
					TrackInfo track = album.Tracks[trackIndex];
					bool trackStarred = false;
					track.Starred.TryGetValue(user, out trackStarred);
					if (trackStarred)
					{
						result.Tracks.Add(track);
					}
				}
			}

			return CreateResponse(result);
		}

		public IResult Favorite(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string type = context.Request.Query["type"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(id))
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Missing id"));
			}

			string typeLower = "track";
			if (!string.IsNullOrEmpty(type))
			{
				typeLower = type.ToLowerInvariant();
			}

			string trackId = null;
			string albumId = null;
			string artistId = null;
			if (typeLower == "album")
			{
				albumId = id;
			}
			else if (typeLower == "artist")
			{
				artistId = id;
			}
			else
			{
				trackId = id;
			}

			m_musicManager.UpdateStar(user, trackId, albumId, artistId, true);
			return CreateResponse();

		}

		public IResult Unfavorite(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string type = context.Request.Query["type"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(id))
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Missing id"));
			}

			string typeLower = "track";
			if (!string.IsNullOrEmpty(type))
			{
				typeLower = type.ToLowerInvariant();
			}

			string trackId = null;
			string albumId = null;
			string artistId = null;
			if (typeLower == "album")
			{
				albumId = id;
			}
			else if (typeLower == "artist")
			{
				artistId = id;
			}
			else
			{
				trackId = id;
			}

			m_musicManager.UpdateStar(user, trackId, albumId, artistId, false);

			return CreateResponse();
		}

		
		public IResult ReportTrackAnalytics(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(id))
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Missing id"));
			}

			m_musicManager.OnTrackStreamed(user, id);
			return CreateResponse();
		}

		public IResult GetArtists(HttpContext context)
		{
			SearchResult result = new SearchResult();
			result.Artists = m_musicManager.GetAllArtists();
			return CreateResponse(result);
		}

		public IResult GetArtist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			ArtistInfo artist = m_musicManager.GetArtist(id);
			if (artist == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Artist not found"));
			}
			return CreateResponse(artist);
		}

		// type controls ordering (random / newest / alphabeticalbyname / alphabeticalbyartist
		// / frequent / recent / byyear / bygenre / starred / highest). Default = random.
		public IResult GetAlbums(HttpContext context)
		{
			string typeRaw = context.Request.Query["type"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}

			string type = "random";
			if (!string.IsNullOrEmpty(typeRaw))
			{
				type = typeRaw.ToLowerInvariant();
			}

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();

			if (type == "newest")
			{
				allAlbums.Sort(MusicComparers.CompareAlbumYearDescending);
			}
			else if (type == "random")
			{
				Random rng = new Random();
				for (int index = allAlbums.Count - 1; index > 0; index--)
				{
					int swapIndex = rng.Next(index + 1);
					AlbumInfo temp = allAlbums[index];
					allAlbums[index] = allAlbums[swapIndex];
					allAlbums[swapIndex] = temp;
				}
			}
			else if (type == "alphabeticalbyname")
			{
				allAlbums.Sort(MusicComparers.CompareAlbumByName);
			}
			else if (type == "alphabeticalbyartist")
			{
				allAlbums.Sort(MusicComparers.CompareAlbumByArtistThenName);
			}
			else if (type == "frequent")
			{
				List<KeyValuePair<AlbumInfo, int>> scored = new List<KeyValuePair<AlbumInfo, int>>();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					AlbumInfo album = allAlbums[index];
					int total = 0;
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						total = total + album.Tracks[trackIndex].Score.PlayCount;
					}
					if (total > 0)
					{
						scored.Add(new KeyValuePair<AlbumInfo, int>(album, total));
					}
				}
				scored.Sort(MusicComparers.CompareAlbumPlayCountDescending);
				allAlbums = new List<AlbumInfo>();
				for (int index = 0; index < scored.Count; index++)
				{
					allAlbums.Add(scored[index].Key);
				}
			}
			else if (type == "recent")
			{
				List<KeyValuePair<AlbumInfo, DateTime>> scored = new List<KeyValuePair<AlbumInfo, DateTime>>();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					AlbumInfo album = allAlbums[index];
					DateTime mostRecent = default(DateTime);
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						DateTime trackPlayed = album.Tracks[trackIndex].LastPlayed;
						if (trackPlayed > mostRecent)
						{
							mostRecent = trackPlayed;
						}
					}
					if (mostRecent != default(DateTime))
					{
						scored.Add(new KeyValuePair<AlbumInfo, DateTime>(album, mostRecent));
					}
				}
				scored.Sort(MusicComparers.CompareAlbumDateDescending);
				allAlbums = new List<AlbumInfo>();
				for (int index = 0; index < scored.Count; index++)
				{
					allAlbums.Add(scored[index].Key);
				}
			}
			else if (type == "byyear")
			{
				int fromYear = int.MinValue;
				int toYear = int.MaxValue;
				string fromYearRaw = context.Request.Query["fromYear"].FirstOrDefault();
				string toYearRaw = context.Request.Query["toYear"].FirstOrDefault();
				int parsedFromYear = 0;
				bool fromYearParsed = int.TryParse(fromYearRaw, out parsedFromYear);
				if (fromYearParsed)
				{
					fromYear = parsedFromYear;
				}
				int parsedToYear = 0;
				bool toYearParsed = int.TryParse(toYearRaw, out parsedToYear);
				if (toYearParsed)
				{
					toYear = parsedToYear;
				}
				List<AlbumInfo> filtered = new List<AlbumInfo>();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					AlbumInfo album = allAlbums[index];
					if (album.Year >= fromYear && album.Year <= toYear)
					{
						filtered.Add(album);
					}
				}
				if (fromYear <= toYear)
				{
					filtered.Sort(MusicComparers.CompareAlbumYearAscending);
				}
				else
				{
					filtered.Sort(MusicComparers.CompareAlbumYearDescending);
				}
				allAlbums = filtered;
			}
			else if (type == "bygenre")
			{
				string genre = context.Request.Query["genre"].FirstOrDefault();
				List<AlbumInfo> filtered = new List<AlbumInfo>();
				if (!string.IsNullOrEmpty(genre))
				{
					string genreLower = genre.ToLowerInvariant();
					for (int index = 0; index < allAlbums.Count; index++)
					{
						AlbumInfo album = allAlbums[index];
						if (!string.IsNullOrEmpty(album.Genre) && album.Genre.ToLowerInvariant() == genreLower)
						{
							filtered.Add(album);
						}
					}
				}
				allAlbums = filtered;
			}
			else if (type == "starred")
			{
				List<AlbumInfo> filtered = new List<AlbumInfo>();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					AlbumInfo album = allAlbums[index];
					bool isStarred = false;
					album.Starred.TryGetValue(user, out isStarred);
					if (isStarred)
					{
						filtered.Add(album);
					}
				}
				allAlbums = filtered;
			}
			else if (type == "highest")
			{
				// Pulse has per-track Rating only; average over the album. Unrated albums drop off.
				List<KeyValuePair<AlbumInfo, float>> scored = new List<KeyValuePair<AlbumInfo, float>>();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					AlbumInfo album = allAlbums[index];
					float total = 0f;
					int rated = 0;
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						int trackRating = album.Tracks[trackIndex].Rating;
						if (trackRating > 0)
						{
							total = total + trackRating;
							rated++;
						}
					}
					if (rated > 0)
					{
						scored.Add(new KeyValuePair<AlbumInfo, float>(album, total / rated));
					}
				}
				scored.Sort(MusicComparers.CompareAlbumFloatDescending);
				allAlbums = new List<AlbumInfo>();
				for (int index = 0; index < scored.Count; index++)
				{
					allAlbums.Add(scored[index].Key);
				}
			}

			SearchResult result = new SearchResult();
			result.Albums = new List<AlbumInfo>();
			int end = Math.Min(offset + size, allAlbums.Count);
			for (int index = offset; index < end; index++)
			{
				result.Albums.Add(allAlbums[index]);
			}
			return CreateResponse(result);
		}

		public IResult GetAlbum(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			AlbumInfo album = m_musicManager.GetAlbum(id);
			if (album == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Album not found"));
			}

			//todo this should be done once at load time only
			//sort tracks by actual album order
			album.Tracks.Sort(CompareTrackByDiscThenNumber);

			return CreateResponse(album);
		}

		public IResult GetGenres(HttpContext context)
		{
			Dictionary<string, GenreInfo> genreMap = new Dictionary<string, GenreInfo>();

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			for (int index = 0; index < allAlbums.Count; index++)
			{
				AlbumInfo album = allAlbums[index];
				if (string.IsNullOrEmpty(album.Genre))
				{
					continue;
				}

				GenreInfo entry;
				if (!genreMap.TryGetValue(album.Genre, out entry))
				{
					entry = new GenreInfo();
					entry.Name = album.Genre;
					genreMap[album.Genre] = entry;
				}
				entry.AlbumCount = entry.AlbumCount + 1;
				entry.TrackCount = entry.TrackCount + album.Tracks.Count;
			}

			SearchResult searchResult = new SearchResult();
			searchResult.Genres = new List<GenreInfo>(genreMap.Values);
			searchResult.Genres.Sort();

			return CreateResponse(searchResult);
		}

		public IResult GetGenreTracks(HttpContext context)
		{
			string genre = context.Request.Query["genre"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 10);
			int offset = QueryParameters.GetInt(context, "offset", 0);

			if (string.IsNullOrEmpty(genre))
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Missing genre"));
			}

			string genreLower = genre.ToLowerInvariant();
			List<TrackInfo> matches = new List<TrackInfo>();
			List<TrackInfo> allTracks = m_musicManager.GetAllTracks();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackInfo track = allTracks[index];
				if (!string.IsNullOrEmpty(track.Genre) && track.Genre.ToLowerInvariant() == genreLower)
				{
					matches.Add(track);
				}
			}

			SearchResult result = new SearchResult();
			result.Tracks = new List<TrackInfo>();
			int end = Math.Min(matches.Count, offset + count);
			for (int index = offset; index < end; index++)
			{
				result.Tracks.Add(matches[index]);
			}
			return CreateResponse(result);
		}

		public IResult GetPlaylists(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();

			SearchResult result = new SearchResult();
			result.Playlists = m_musicManager.GetAllPlaylists(user);
			return CreateResponse(result);
		}

		public IResult GetPlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			if (string.IsNullOrEmpty(id))
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Missing id"));
			}

			PlaylistAndTracks playlist = m_musicManager.GetPlaylistAndTracks(id);
			if (playlist == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Playlist not found"));
			}
			return CreateResponse(playlist);
		}

		public IResult CreatePlaylist(HttpContext context)
		{
			string playlistId = context.Request.Query["playlistId"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			List<string> songIds = context.Request.Query["songId"].ToList();

			PlaylistInfo playlist = null;
			if (!string.IsNullOrEmpty(playlistId))
			{
				playlist = m_musicManager.GetPlaylist(playlistId);
				if (playlist != null)
				{
					playlist.TrackIds.Clear();
				}
			}

			if (playlist == null)
			{
				if (string.IsNullOrEmpty(name))
				{
					return CreateResponse(new Error(ePulseCode.NotFound, "Missing name"));
				}
				if (PlaylistNameTaken(name, ""))
				{
					return CreateResponse(new Error(ePulseCode.NotFound, "A playlist named '" + name + "' already exists."));
				}
				playlist = new PlaylistInfo();
				playlist.Id = MusicManager.GenerateID("playlist/" + user + "/" + name + "/" + DateTime.UtcNow.Ticks);
				playlist.Name = name;
			}

			long totalDuration = 0;
			for (int index = 0; index < songIds.Count; index++)
			{
				TrackInfo track = m_musicManager.GetTrack(songIds[index]);
				if (track == null)
				{
					continue;
				}
				playlist.TrackIds.Add(track.Id);
				totalDuration = totalDuration + track.DurationSeconds;
			}
			playlist.DurationSeconds = totalDuration;
			m_musicManager.CreateOrUpdatePlaylist(playlist);


			PlaylistAndTracks fullPlaylist = m_musicManager.GetPlaylistAndTracks(playlist.Id);
			return CreateResponse(fullPlaylist);
		}

		public IResult UpdatePlaylist(HttpContext context)
		{
			string playlistId = context.Request.Query["playlistId"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> songIdsToAdd = context.Request.Query["songIdToAdd"].ToList();
			List<string> indicesToRemove = context.Request.Query["songIndexToRemove"].ToList();

			if (string.IsNullOrEmpty(playlistId))
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Missing playlistId"));
			}

			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Playlist not found"));
			}

			if (!string.IsNullOrEmpty(name))
			{
				if (PlaylistNameTaken(name, playlist.Id))
				{
					return CreateResponse(new Error(ePulseCode.NotFound, "A playlist named '" + name + "' already exists."));
				}
				playlist.Name = name;
			}

			if (!string.IsNullOrEmpty(comment))
			{
				playlist.Comment = comment;
			}

			// Remove by descending index so the indices don't shift under us.
			List<int> parsedIndices = new List<int>();
			for (int index = 0; index < indicesToRemove.Count; index++)
			{
				int parsed = 0;
				bool didParse = int.TryParse(indicesToRemove[index], out parsed);
				if (didParse)
				{
					parsedIndices.Add(parsed);
				}
			}
			parsedIndices.Sort(MusicComparers.CompareIntDescending);
			for (int index = 0; index < parsedIndices.Count; index++)
			{
				int removeIndex = parsedIndices[index];
				if (removeIndex >= 0 && removeIndex < playlist.TrackIds.Count)
				{
					playlist.TrackIds.RemoveAt(removeIndex);
				}
			}

			for (int index = 0; index < songIdsToAdd.Count; index++)
			{
				TrackInfo track = m_musicManager.GetTrack(songIdsToAdd[index]);
				if (track == null)
				{
					continue;
				}
				playlist.TrackIds.Add(track.Id);
			}

			long totalDuration = 0;
			for (int index = 0; index < playlist.TrackIds.Count; index++)
			{
				TrackInfo track = m_musicManager.GetTrack(playlist.TrackIds[index]);
				if (track != null)
				{
					totalDuration = totalDuration + track.DurationSeconds;
				}
			}
			playlist.DurationSeconds = totalDuration;

			m_musicManager.CreateOrUpdatePlaylist(playlist);

			// Composite playlist cover (#224) regenerates from the new track set.
			byte[] discard;
			m_coverArtCache.TryRemove(playlist.Id, out discard);


			PlaylistAndTracks fullPlaylist = m_musicManager.GetPlaylistAndTracks(playlist.Id);
			return CreateResponse(fullPlaylist);
		}

		// Case-insensitive duplicate-name check. skipPlaylistId lets the caller
		// exclude the playlist currently being renamed.
		private bool PlaylistNameTaken(string name, string skipPlaylistId)
		{
			string nameLower = name.ToLowerInvariant();
			List<PlaylistInfo> all = m_musicManager.GetAllPlaylists(null);
			for (int index = 0; index < all.Count; index++)
			{
				PlaylistInfo existing = all[index];
				if (!string.IsNullOrEmpty(skipPlaylistId) && existing.Id == skipPlaylistId)
				{
					continue;
				}
				if (existing.Name.ToLowerInvariant() == nameLower)
				{
					return true;
				}
			}
			return false;
		}

		public IResult DeletePlaylist(HttpContext context)
		{
			string playlistId = context.Request.Query["id"].FirstOrDefault();
			if (string.IsNullOrEmpty(playlistId))
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Missing id"));
			}

			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return CreateResponse(new Error(ePulseCode.NotFound, "Playlist not found"));
			}

			m_musicManager.DeletePlaylist(playlistId);
			return CreateResponse();
		}


	}
}

