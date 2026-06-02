using Microsoft.AspNetCore.Http;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pulse.Protocols.PulseAPI
{
	public class PulseEndpoints
	{
		/// <summary>
		/// Intentionally changed from /pulse to add versioning support
		/// </summary>
		string m_apiSpace = "pulse_v1/";
		IPulseRouteHost m_host;
		PulseService m_pulseService;
		MusicManager m_musicManager;
		private byte[] m_defaultCoverArt;
		private ConcurrentDictionary<string, byte[]> m_coverArtCache = new ConcurrentDictionary<string, byte[]>();

		public PulseEndpoints(PulseService pulse, MusicManager musicManager)
		{
			m_pulseService = pulse;
			m_musicManager = musicManager;
			m_defaultCoverArt = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Content", "Media", "pulseLogo.png"));
		}

		public void RegisterRoutes(IPulseRouteHost host)
		{
			m_host = host;

			RegisterRoute("ping", Ping);
			RegisterRoute("stream", GetStream);
			RegisterRoute("download", GetDownload);
			RegisterRoute("coverArt", GetCoverArt);

			RegisterRoute("artists", GetArtists);
			RegisterRoute("artist", GetArtist);
			RegisterRoute("artistTracks", GetArtistTracks);
			RegisterRoute("albums", GetAlbums);
			RegisterRoute("album", GetAlbum);
			RegisterRoute("track", GetTrack);

			RegisterRoute("genres", GetGenres);
			RegisterRoute("genreTracks", GetGenreTracks);

			RegisterRoute("playlists", GetPlaylists);
			RegisterRoute("playlist", GetPlaylist);
			RegisterRoute("createPlaylist", CreatePlaylist);
			RegisterRoute("updatePlaylist", UpdatePlaylist);
			RegisterRoute("deletePlaylist", DeletePlaylist);

			RegisterRoute("recentlyPlayed", GetRecentlyPlayed);
			RegisterRoute("search", Search);
			RegisterRoute("favorites", GetFavorites);
			RegisterRoute("favorite", Favorite);
			RegisterRoute("unfavorite", Unfavorite);
			RegisterRoute("reportTrackAnalytics", ReportTrackAnalytics);

			RegisterRoute("podcasts", GetPodcasts);
		}

		private void RegisterRoute(string route, Func<HttpContext, IResult> handler)
		{
			m_host.RegisterResultRoute(m_apiSpace + route, handler);
		}

		public IResult Respond(HttpContext context, PulseResponse body)
		{
			return Results.Text(PulseWire.Serialize(body), "application/json");
		}

		private IResult RespondObject(HttpContext context, object contents)
		{
			PulseResponse response = new PulseResponse();
			response.contentType = PulseResponse.ContentType.PulseObject;
			response.contents = contents;
			return Respond(context, response);
		}

		private IResult RespondList(HttpContext context, object contents)
		{
			PulseResponse response = new PulseResponse();
			response.contentType = PulseResponse.ContentType.PulseObjectList;
			response.contents = contents;
			return Respond(context, response);
		}

		// The envelope has no message field; the status string carries the
		// failure code so the client can branch without parsing contents.
		private IResult RespondStatus(HttpContext context, string status)
		{
			PulseResponse response = new PulseResponse();
			response.status = status;
			return Respond(context, response);
		}

		// -- builders: runtime *Info -> wire Pulse* ----------------------------

		private PulseArtist BuildArtist(ArtistInfo artist, string user)
		{
			PulseArtist pulseArtist = new PulseArtist();
			pulseArtist.Id = artist.Id;
			pulseArtist.Name = artist.Name;
			pulseArtist.AlbumCount = artist.Albums.Count;
			int trackCount = 0;
			for (int index = 0; index < artist.Albums.Count; index++)
			{
				trackCount = trackCount + artist.Albums[index].Tracks.Count;
			}
			pulseArtist.TrackCount = trackCount;
			pulseArtist.CoverArt = "ar-" + artist.Id;
			pulseArtist.Score = artist.GetScore(user);
			pulseArtist.LastPlayed = artist.LastPlayed;
			return pulseArtist;
		}

		private PulseAlbum BuildAlbum(AlbumInfo album)
		{
			PulseAlbum pulseAlbum = new PulseAlbum();
			pulseAlbum.Id = album.Id;
			pulseAlbum.Name = album.Name;
			pulseAlbum.Artist = album.ArtistName;
			pulseAlbum.ArtistId = album.ArtistId;
			pulseAlbum.CoverArt = album.CoverArtId;
			pulseAlbum.Year = album.Year;
			pulseAlbum.TrackCount = album.Tracks.Count;
			int duration = 0;
			for (int index = 0; index < album.Tracks.Count; index++)
			{
				duration = duration + album.Tracks[index].DurationSeconds;
			}
			pulseAlbum.Duration = duration;
			return pulseAlbum;
		}

		private PulseTrack BuildTrack(TrackInfo track, string user)
		{
			PulseTrack pulseTrack = new PulseTrack();
			pulseTrack.Id = track.Id;
			pulseTrack.Title = track.Title;
			pulseTrack.Artist = track.Artist;
			pulseTrack.ArtistId = track.ArtistId;
			pulseTrack.Album = track.Album;
			pulseTrack.AlbumId = track.AlbumId;
			pulseTrack.CoverArt = track.CoverArtId;
			pulseTrack.Duration = track.DurationSeconds;
			pulseTrack.Starred = track.IsStarredBy(user);
			return pulseTrack;
		}

		private PulsePlaylist BuildPlaylist(PlaylistInfo playlist, string user)
		{
			PulsePlaylist pulsePlaylist = new PulsePlaylist();
			pulsePlaylist.Id = playlist.Id;
			pulsePlaylist.Name = playlist.Name;
			pulsePlaylist.Comment = playlist.Comment;
			pulsePlaylist.CoverArt = "pl-" + playlist.Id;
			pulsePlaylist.TrackCount = playlist.GetSongCount();
			pulsePlaylist.Duration = (int)playlist.DurationSeconds;
			pulsePlaylist.Score = 0f;
			pulsePlaylist.LastPlayed = playlist.GetLastPlayed(user);
			return pulsePlaylist;
		}

		private PulseGenre BuildGenre(GenreInfo genre)
		{
			PulseGenre pulseGenre = new PulseGenre();
			pulseGenre.Id = genre.Name;
			pulseGenre.Name = genre.Name;
			pulseGenre.TrackCount = genre.TrackCount;
			pulseGenre.AlbumCount = genre.AlbumCount;
			return pulseGenre;
		}

		private int CompareTrackByDiscThenNumber(TrackInfo left, TrackInfo right)
		{
			int discCompare = left.DiscNumber.CompareTo(right.DiscNumber);
			if (discCompare != 0)
			{
				return discCompare;
			}
			return left.TrackNumber.CompareTo(right.TrackNumber);
		}

		// -- service / binary endpoints ----------------------------------------

		public IResult Ping(HttpContext context)
		{
			PulseResponse response = new PulseResponse();
			response.contentType = PulseResponse.ContentType.PulseObject;
			return Respond(context, response);
		}

		public IResult GetStream(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			TrackInfo track = m_musicManager.GetTrack(id);
			if (track == null)
			{
				return RespondStatus(context, "not_found");
			}

			FileStream fileStream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			return Results.File(fileStream, track.ContentType, enableRangeProcessing: true);
		}

		public IResult GetDownload(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			TrackInfo track = m_musicManager.GetTrack(id);
			if (track == null)
			{
				return RespondStatus(context, "not_found");
			}

			FileStream fileStream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			return Results.File(fileStream, track.ContentType, enableRangeProcessing: true);
		}

		// Cover art ids carry a one-char source hint: "ar-" for artist images and
		// "pl-" for composite playlist tiles; everything else is treated as an
		// album id. Mirrors the legacy /pulse/coverArt behavior so wire ids stay
		// interchangeable between the two API surfaces.
		public IResult GetCoverArt(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();

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

			if (id.StartsWith("pl-", StringComparison.Ordinal))
			{
				string playlistId = id.Substring(3);
				PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
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
						AlbumInfo tileAlbum = m_musicManager.GetAlbum(track.AlbumId);
						if (tileAlbum == null)
						{
							continue;
						}
						if (m_musicManager.GetAlbumCover(tileAlbum, out byte[] tileImage, out string tileType))
						{
							tileBytes.Add(tileImage);
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
							Log.Error(-1, "PulseEndpoints.GetCoverArt: failed to compose - " + ex.Message);
						}
					}
				}
			}
			else if (id.StartsWith("ar-", StringComparison.Ordinal))
			{
				string artistId = id.Substring(3);
				ArtistInfo artist = m_musicManager.GetArtist(artistId);
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

		// -- artists -----------------------------------------------------------

		public IResult GetArtists(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			List<PulseArtist> pulseArtists = new List<PulseArtist>();
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int index = 0; index < allArtists.Count; index++)
			{
				pulseArtists.Add(BuildArtist(allArtists[index], user));
			}
			return RespondList(context, pulseArtists);
		}

		public IResult GetArtist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			ArtistInfo artist = m_musicManager.GetArtist(id);
			if (artist == null)
			{
				return RespondStatus(context, "not_found");
			}

			PulseArtistDetails details = new PulseArtistDetails();
			details.Id = artist.Id;
			details.Artist = BuildArtist(artist, user);
			for (int index = 0; index < artist.Albums.Count; index++)
			{
				details.Albums.Add(BuildAlbum(artist.Albums[index]));
			}
			return RespondObject(context, details);
		}

		public IResult GetArtistTracks(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			ArtistInfo artist = m_musicManager.GetArtist(id);
			if (artist == null)
			{
				return RespondStatus(context, "not_found");
			}

			PulseArtistFullDetails details = new PulseArtistFullDetails();
			details.Id = artist.Id;
			details.Artist = BuildArtist(artist, user);
			for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
			{
				AlbumInfo album = artist.Albums[albumIndex];
				PulseAlbumDetails albumDetails = new PulseAlbumDetails();
				albumDetails.Id = album.Id;
				albumDetails.Album = BuildAlbum(album);

				List<TrackInfo> ordered = new List<TrackInfo>(album.Tracks);
				ordered.Sort(CompareTrackByDiscThenNumber);
				for (int trackIndex = 0; trackIndex < ordered.Count; trackIndex++)
				{
					albumDetails.Tracks.Add(BuildTrack(ordered[trackIndex], user));
				}
				details.AlbumDetails.Add(albumDetails);
			}
			return RespondObject(context, details);
		}

		// -- albums ------------------------------------------------------------

		// type controls ordering (random / newest / alphabeticalbyname /
		// alphabeticalbyartist / frequent / recent / byyear / bygenre / starred /
		// highest). Default = random.
		public IResult GetAlbums(HttpContext context)
		{
			string typeRaw = context.Request.Query["type"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

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
				if (int.TryParse(fromYearRaw, out parsedFromYear))
				{
					fromYear = parsedFromYear;
				}
				int parsedToYear = 0;
				if (int.TryParse(toYearRaw, out parsedToYear))
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
				// Pulse has per-track Rating only; average over the album. Unrated
				// albums drop off.
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

			List<PulseAlbum> pulseAlbums = new List<PulseAlbum>();
			int end = Math.Min(offset + size, allAlbums.Count);
			for (int index = offset; index < end; index++)
			{
				pulseAlbums.Add(BuildAlbum(allAlbums[index]));
			}
			return RespondList(context, pulseAlbums);
		}

		public IResult GetAlbum(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			AlbumInfo album = m_musicManager.GetAlbum(id);
			if (album == null)
			{
				return RespondStatus(context, "not_found");
			}

			List<TrackInfo> ordered = new List<TrackInfo>(album.Tracks);
			ordered.Sort(CompareTrackByDiscThenNumber);

			PulseAlbumDetails details = new PulseAlbumDetails();
			details.Id = album.Id;
			details.Album = BuildAlbum(album);
			for (int index = 0; index < ordered.Count; index++)
			{
				details.Tracks.Add(BuildTrack(ordered[index], user));
			}
			return RespondObject(context, details);
		}

		public IResult GetTrack(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			TrackInfo track = m_musicManager.GetTrack(id);
			if (track == null)
			{
				return RespondStatus(context, "not_found");
			}
			return RespondObject(context, BuildTrack(track, user));
		}

		// -- genres ------------------------------------------------------------

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

			List<GenreInfo> genres = new List<GenreInfo>(genreMap.Values);
			genres.Sort();

			List<PulseGenre> pulseGenres = new List<PulseGenre>();
			for (int index = 0; index < genres.Count; index++)
			{
				pulseGenres.Add(BuildGenre(genres[index]));
			}
			return RespondList(context, pulseGenres);
		}

		public IResult GetGenreTracks(HttpContext context)
		{
			string genre = context.Request.Query["genre"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";
			int count = QueryParameters.GetInt(context, "count", 10);
			int offset = QueryParameters.GetInt(context, "offset", 0);

			if (string.IsNullOrEmpty(genre))
			{
				return RespondStatus(context, "missing_genre");
			}

			string genreLower = genre.ToLowerInvariant();
			List<TrackInfo> matches = new List<TrackInfo>();
			int trackCount = 0;
			int albumCount = 0;
			List<TrackInfo> allTracks = m_musicManager.GetAllTracks();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackInfo track = allTracks[index];
				if (!string.IsNullOrEmpty(track.Genre) && track.Genre.ToLowerInvariant() == genreLower)
				{
					matches.Add(track);
				}
			}

			HashSet<string> albumIds = new HashSet<string>();
			for (int index = 0; index < matches.Count; index++)
			{
				trackCount++;
				if (!string.IsNullOrEmpty(matches[index].AlbumId))
				{
					albumIds.Add(matches[index].AlbumId);
				}
			}
			albumCount = albumIds.Count;

			PulseGenreDetails details = new PulseGenreDetails();
			details.Id = genre;
			PulseGenre pulseGenre = new PulseGenre();
			pulseGenre.Id = genre;
			pulseGenre.Name = genre;
			pulseGenre.TrackCount = trackCount;
			pulseGenre.AlbumCount = albumCount;
			details.Genre = pulseGenre;

			int end = Math.Min(matches.Count, offset + count);
			for (int index = offset; index < end; index++)
			{
				details.Tracks.Add(BuildTrack(matches[index], user));
			}
			return RespondObject(context, details);
		}

		// -- playlists ---------------------------------------------------------

		public IResult GetPlaylists(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			List<PulsePlaylist> pulsePlaylists = new List<PulsePlaylist>();
			List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(user);
			for (int index = 0; index < allPlaylists.Count; index++)
			{
				pulsePlaylists.Add(BuildPlaylist(allPlaylists[index], user));
			}
			return RespondList(context, pulsePlaylists);
		}

		public IResult GetPlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			if (string.IsNullOrEmpty(id))
			{
				return RespondStatus(context, "missing_id");
			}

			PlaylistAndTracks playlist = m_musicManager.GetPlaylistAndTracks(id);
			if (playlist == null)
			{
				return RespondStatus(context, "not_found");
			}
			return RespondObject(context, BuildPlaylistDetails(playlist, user));
		}

		private PulsePlaylistDetails BuildPlaylistDetails(PlaylistAndTracks playlist, string user)
		{
			PulsePlaylistDetails details = new PulsePlaylistDetails();
			details.Id = playlist.Id;
			details.Playlist = BuildPlaylist(playlist, user);
			for (int index = 0; index < playlist.Tracks.Count; index++)
			{
				details.Tracks.Add(BuildTrack(playlist.Tracks[index], user));
			}
			return details;
		}

		public IResult CreatePlaylist(HttpContext context)
		{
			string playlistId = context.Request.Query["playlistId"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";
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
					return RespondStatus(context, "missing_name");
				}
				if (PlaylistNameTaken(name, ""))
				{
					return RespondStatus(context, "name_taken");
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
			return RespondObject(context, BuildPlaylistDetails(fullPlaylist, user));
		}

		public IResult UpdatePlaylist(HttpContext context)
		{
			string playlistId = context.Request.Query["playlistId"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";
			List<string> songIdsToAdd = context.Request.Query["songIdToAdd"].ToList();
			List<string> indicesToRemove = context.Request.Query["songIndexToRemove"].ToList();

			if (string.IsNullOrEmpty(playlistId))
			{
				return RespondStatus(context, "missing_id");
			}

			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return RespondStatus(context, "not_found");
			}

			if (!string.IsNullOrEmpty(name))
			{
				if (PlaylistNameTaken(name, playlist.Id))
				{
					return RespondStatus(context, "name_taken");
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
				if (int.TryParse(indicesToRemove[index], out parsed))
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

			// Composite playlist cover regenerates from the new track set.
			byte[] discard;
			m_coverArtCache.TryRemove("pl-" + playlist.Id, out discard);

			PlaylistAndTracks fullPlaylist = m_musicManager.GetPlaylistAndTracks(playlist.Id);
			return RespondObject(context, BuildPlaylistDetails(fullPlaylist, user));
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
				return RespondStatus(context, "missing_id");
			}

			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return RespondStatus(context, "not_found");
			}

			m_musicManager.DeletePlaylist(playlistId);
			return Respond(context, new PulseResponse());
		}

		// -- search / favorites ------------------------------------------------

		public IResult Search(HttpContext context)
		{
			string query = context.Request.Query["query"].FirstOrDefault() ?? "";
			query = query.Trim('"');

			if (string.IsNullOrEmpty(query))
			{
				return RespondObject(context, new PulseSearchData());
			}

			string user = context.Request.Query["u"].FirstOrDefault() ?? "";
			int artistCount = QueryParameters.GetInt(context, "artistCount", 20);
			int albumCount = QueryParameters.GetInt(context, "albumCount", 20);
			int songCount = QueryParameters.GetInt(context, "songCount", 20);

			PulseSearchData result = new PulseSearchData();

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();

			string lowerQuery = query.ToLowerInvariant();

			int artistHits = 0;
			for (int index = 0; index < allArtists.Count && artistHits < artistCount; index++)
			{
				if (allArtists[index].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					result.Artists.Add(BuildArtist(allArtists[index], user));
					artistHits++;
				}
			}

			int albumHits = 0;
			for (int index = 0; index < allAlbums.Count && albumHits < albumCount; index++)
			{
				if (allAlbums[index].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					result.Albums.Add(BuildAlbum(allAlbums[index]));
					albumHits++;
				}
			}

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
						result.Tracks.Add(BuildTrack(track, user));
						songHits++;
					}
				}
			}
			return RespondObject(context, result);
		}

		public IResult GetFavorites(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			PulseSearchData result = new PulseSearchData();

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int index = 0; index < allArtists.Count; index++)
			{
				ArtistInfo artist = allArtists[index];
				bool artistStarred = false;
				artist.Starred.TryGetValue(user, out artistStarred);
				if (artistStarred)
				{
					result.Artists.Add(BuildArtist(artist, user));
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
					result.Albums.Add(BuildAlbum(album));
				}

				for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
				{
					TrackInfo track = album.Tracks[trackIndex];
					bool trackStarred = false;
					track.Starred.TryGetValue(user, out trackStarred);
					if (trackStarred)
					{
						result.Tracks.Add(BuildTrack(track, user));
					}
				}
			}

			return RespondObject(context, result);
		}

		public IResult Favorite(HttpContext context)
		{
			return SetStar(context, true);
		}

		public IResult Unfavorite(HttpContext context)
		{
			return SetStar(context, false);
		}

		private IResult SetStar(HttpContext context, bool starred)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string type = context.Request.Query["type"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			if (string.IsNullOrEmpty(id))
			{
				return RespondStatus(context, "missing_id");
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

			m_musicManager.UpdateStar(user, trackId, albumId, artistId, starred);
			return Respond(context, new PulseResponse());
		}

		public IResult ReportTrackAnalytics(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			if (string.IsNullOrEmpty(id))
			{
				return RespondStatus(context, "missing_id");
			}

			m_musicManager.OnTrackStreamed(user, id);
			return Respond(context, new PulseResponse());
		}

		// Mixed-kind recents shelf: tracks, artists, albums, and playlists ranked
		// together by recency, newest first. The `types` query param is a CSV of
		// any combination of "track", "artist", "album", "playlist"; when omitted
		// all four are included (the pulse_v1 surface has no pre-#223 callers to
		// stay tracks-only for). Each item serializes as its concrete Pulse* type
		// with a Kind discriminator, so the client branches on Kind.
		public IResult GetRecentlyPlayed(HttpContext context)
		{
			int count = QueryParameters.GetInt(context, "count", 10);
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";
			string typesParam = context.Request.Query["types"].FirstOrDefault();

			bool includeTracks;
			bool includeArtists;
			bool includeAlbums;
			bool includePlaylists;
			ParseRecentTypes(typesParam, out includeTracks, out includeArtists, out includeAlbums, out includePlaylists);

			List<RecentCandidate> candidates = new List<RecentCandidate>();

			if (includeTracks)
			{
				PulseAnalyticsInfo analytics = m_musicManager.GetAnalytics();
				List<string> recentIds = new List<string>(analytics.RecentlyPlayed);
				for (int idx = 0; idx < recentIds.Count; idx++)
				{
					TrackInfo track = m_musicManager.GetTrack(recentIds[idx]);
					if (track == null)
					{
						continue;
					}
					RecentCandidate candidate = new RecentCandidate();
					candidate.Item = BuildTrack(track, user);
					// RecentlyPlayed is FIFO-ordered; if a track has no LastPlayed,
					// fall back to its position so it still slots in roughly right.
					if (track.LastPlayed != default(DateTime))
					{
						candidate.RankTime = track.LastPlayed;
					}
					else
					{
						candidate.RankTime = DateTime.UtcNow.AddSeconds(-idx);
					}
					candidates.Add(candidate);
				}
			}

			if (includeArtists)
			{
				List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
				for (int idx = 0; idx < allArtists.Count; idx++)
				{
					ArtistInfo artist = allArtists[idx];
					if (artist.LastPlayed == default(DateTime))
					{
						continue;
					}
					RecentCandidate candidate = new RecentCandidate();
					candidate.Item = BuildArtist(artist, user);
					candidate.RankTime = artist.LastPlayed;
					candidates.Add(candidate);
				}
			}

			if (includeAlbums)
			{
				List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
				for (int idx = 0; idx < allAlbums.Count; idx++)
				{
					AlbumInfo album = allAlbums[idx];
					DateTime albumLastPlayed = default(DateTime);
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						DateTime trackLastPlayed = album.Tracks[trackIndex].LastPlayed;
						if (trackLastPlayed > albumLastPlayed)
						{
							albumLastPlayed = trackLastPlayed;
						}
					}
					if (albumLastPlayed == default(DateTime))
					{
						continue;
					}
					RecentCandidate candidate = new RecentCandidate();
					candidate.Item = BuildAlbum(album);
					candidate.RankTime = albumLastPlayed;
					candidates.Add(candidate);
				}
			}

			if (includePlaylists)
			{
				List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(user);
				for (int idx = 0; idx < allPlaylists.Count; idx++)
				{
					PlaylistInfo playlist = allPlaylists[idx];
					DateTime playlistLastPlayed = playlist.GetLastPlayed(user);
					if (playlistLastPlayed == default(DateTime))
					{
						continue;
					}
					RecentCandidate candidate = new RecentCandidate();
					candidate.Item = BuildPlaylist(playlist, user);
					candidate.RankTime = playlistLastPlayed;
					candidates.Add(candidate);
				}
			}

			candidates.Sort(CompareRecentCandidateDescending);

			List<object> items = new List<object>();
			int emit = Math.Min(count, candidates.Count);
			for (int idx = 0; idx < emit; idx++)
			{
				items.Add(candidates[idx].Item);
			}
			return RespondList(context, items);
		}

		private static void ParseRecentTypes(string raw, out bool track, out bool artist, out bool album, out bool playlist)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				track = true; artist = true; album = true; playlist = true;
				return;
			}
			track = false; artist = false; album = false; playlist = false;
			string[] parts = raw.Split(',');
			for (int idx = 0; idx < parts.Length; idx++)
			{
				string part = parts[idx].Trim();
				if (string.Equals(part, "track", StringComparison.OrdinalIgnoreCase)) { track = true; }
				else if (string.Equals(part, "artist", StringComparison.OrdinalIgnoreCase)) { artist = true; }
				else if (string.Equals(part, "album", StringComparison.OrdinalIgnoreCase)) { album = true; }
				else if (string.Equals(part, "playlist", StringComparison.OrdinalIgnoreCase)) { playlist = true; }
			}
		}

		private static int CompareRecentCandidateDescending(RecentCandidate left, RecentCandidate right)
		{
			return right.RankTime.CompareTo(left.RankTime);
		}

		private class RecentCandidate
		{
			public DateTime RankTime;
			public object Item;
		}

		public IResult GetPodcasts(HttpContext context)
		{
			return RespondStatus(context, "not_implemented");
		}
	}
}
