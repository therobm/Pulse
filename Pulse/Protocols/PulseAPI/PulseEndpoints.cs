using Microsoft.AspNetCore.Http;
using Pulse.Database;
using Pulse.MusicLibrary;
using Pulse.Series;
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
		public enum eSortMethod
		{
			MostRecent,
			MostPlays,
		}
		/// <summary>
		/// Intentionally changed from /pulse to add versioning support
		/// </summary>
		string m_apiSpace = "pulse_v1/";
		IPulseRouteHost m_host;
		PulseService m_pulseService;
		MusicManager m_musicManager;
		AnalyticsDB m_analyticsDB;
		PodcastManager m_podcastManager;
		private byte[] m_defaultCoverArt;
		private ConcurrentDictionary<string, byte[]> m_coverArtCache = new ConcurrentDictionary<string, byte[]>();

		public PulseEndpoints(PulseService pulse, MusicManager musicManager, AnalyticsDB analyticsDB, PodcastManager podcastManager)
		{
			m_pulseService = pulse;
			m_musicManager = musicManager;
			m_analyticsDB = analyticsDB;
			m_podcastManager = podcastManager;
			m_defaultCoverArt = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Content", "Media", "pulseLogo.png"));
		}

		public void RegisterRoutes(IPulseRouteHost host)
		{
			m_host = host;

			RegisterRoute("ping", Ping);
			RegisterRoute("stream", GetStream);
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
			RegisterRoute("reportAnalytics", ReportAnalytics);

			RegisterRoute("ingestAnalytics", PostIngestAnalytics);
			RegisterRoute("analytics", GetAnalytics);

			RegisterRoute("topItems", GetTop);


			RegisterRoute("podcasts", GetPodcasts);
			RegisterRoute("allPodcasts", GetAllPodcasts);
			RegisterRoute("podcast", GetPodcast);
			RegisterRoute("addPodcast", AddPodcast);
			RegisterRoute("updatePodcast", UpdatePodcast);
			RegisterRoute("subscribePodcast", SubscribePodcast);
			RegisterRoute("unsubscribePodcast", UnsubscribePodcast);
			RegisterRoute("episodeProgress", EpisodeProgress);
		}

		private void RegisterRoute(string route, Func<HttpContext, IResult> handler)
		{
			m_host.RegisterResultRoute(m_apiSpace + route, handler);
		}

		public IResult Respond(HttpContext context, PulseResponse body)
		{
			return Results.Text(PulseWire.Serialize(body), "application/json");
		}

		private IResult RespondObject<T>(HttpContext context, T contents) where T : PulseObject
		{
			PulseResponse response = new PulseResponse();
			response.contentType = PulseResponse.ContentType.PulseObject;
			response.contents = contents;
			return Respond(context, response);
		}

		private IResult RespondList<T>(HttpContext context, List<T> contents) where T : PulseObject
		{
			PulseResponse response = new PulseResponse();
			response.contentType = PulseResponse.ContentType.PulseObjectList;
			// Box each element as object so System.Text.Json serializes it by its
			// runtime type. The list's element type would otherwise drive the wire
			// shape: a heterogeneous feed (topItems / recentlyPlayed) arrives as
			// List<PulseObject>, and serializing PulseObject-typed elements emits
			// only the base Id/Kind, silently dropping every derived field (Name,
			// CoverArt, Artist, ...). object-typed elements force runtime-type
			// serialization, so derived fields survive. Homogeneous lists are
			// unaffected -- a PulseAlbum still serializes as a PulseAlbum.
			List<object> boxed = new List<object>(contents.Count);
			for (int index = 0; index < contents.Count; index++)
			{
				boxed.Add(contents[index]);
			}
			response.contents = boxed;
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
			if (track != null)
			{
				FileStream fileStream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				return Results.File(fileStream, track.ContentType, enableRangeProcessing: true);
			}

			SeriesItemInfo item = m_podcastManager.GetItem(id);
			if (item != null && !string.IsNullOrEmpty(item.LocalPath) && File.Exists(item.LocalPath))
			{
				string contentType = "audio/mpeg";
				if (item.LocalPath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
				{
					contentType = "audio/mp4";
				}
				FileStream podcastStream = new FileStream(item.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
				return Results.File(podcastStream, contentType, enableRangeProcessing: true);
			}

			return RespondStatus(context, "not_found");
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
					for (int i = 0; i < playlist.TrackIds.Count && tileBytes.Count < 4; i++)
					{
						TrackInfo track = m_musicManager.GetTrack(playlist.TrackIds[i]);
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
			else if (id.StartsWith("se-", StringComparison.Ordinal))
			{
				string seriesId = id.Substring(3);
				SeriesInfo series = m_podcastManager.GetSeries(seriesId);
				if (series != null && !string.IsNullOrEmpty(series.ArtworkPath) && File.Exists(series.ArtworkPath))
				{
					byte[] bytes = File.ReadAllBytes(series.ArtworkPath);
					m_coverArtCache[id] = bytes;
					return Results.Bytes(bytes, "image/jpeg");
				}
				return Results.Bytes(m_defaultCoverArt, "image/png");
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


		// type controls ordering (random / newest / alphabeticalbyname /
		// alphabeticalbyartist / frequent / recent / byyear / bygenre / starred /
		// highest). Default = random.
		public IResult GetAlbums(HttpContext context)
		{
			string typeRaw = context.Request.Query["type"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			if (offset < 0)
			{
				offset = 0;
			}
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
			if (offset < 0)
			{
				offset = 0;
			}

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

		

		/// <summary>
		/// The pulse_v1 analytics feed: clients POST a serialized PulseAnalytics
		/// body describing one playback state change (track started/paused/
		/// skipped/completed, or a collection-level started for an album/artist/
		/// playlist). The body is JSON, so it arrives in the request stream rather
		/// than the query string; HttpServer sets AllowSynchronousIO so the
		/// synchronous read is legal.
		/// </summary>
		public IResult ReportAnalytics(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			string body;
			using (StreamReader reader = new StreamReader(context.Request.Body))
			{
				body = reader.ReadToEnd();
			}

			if (string.IsNullOrEmpty(body))
			{
				return RespondStatus(context, "missing_body");
			}

			PulseAnalytics analytics = PulseWire.Parse<PulseAnalytics>(body);
			if (analytics == null || string.IsNullOrEmpty(analytics.MediaId))
			{
				return RespondStatus(context, "missing_id");
			}

			m_musicManager.OnPlaybackEvent(user, analytics);
			return Respond(context, new PulseResponse());
		}

		public IResult GetTop(HttpContext context)
		{
			int count = QueryParameters.GetInt(context, "count", 10);
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";
			string typesParam = context.Request.Query["types"].FirstOrDefault();

			List<PulseObject> items = GetItems(count, user, typesParam, eSortMethod.MostPlays);
			return RespondList(context, items);
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
			List<PulseObject> items = GetItems(count, user, typesParam, eSortMethod.MostRecent);
			return RespondList(context, items);
		}

		private List<PulseObject> GetItems(int count, string user, string typesParam, eSortMethod sortMethod)
		{
			bool includeTracks;
			bool includeArtists;
			bool includeAlbums;
			bool includePlaylists;
			ParseRecentTypes(typesParam, out includeTracks, out includeArtists, out includeAlbums, out includePlaylists);

			List<RecentCandidate> candidates;
			if (sortMethod == eSortMethod.MostPlays)
			{
				candidates = GatherMostPlayed(user, includeTracks, includeArtists, includeAlbums, includePlaylists);
				candidates.Sort(CompareCandidateByPlaysDescending);
			}
			else
			{
				candidates = GatherMostRecent(user, includeTracks, includeArtists, includeAlbums, includePlaylists);
				candidates.Sort(CompareRecentCandidateDescending);
			}

			List<PulseObject> items = new List<PulseObject>();
			int emit = Math.Min(count, candidates.Count);
			for (int i = 0; i < emit; i++)
			{
				items.Add(candidates[i].Item);
			}
			return items;
		}

		/// <summary>
		/// Builds the candidate set for the MostPlays (topItems) ranking. The
		/// candidate set is driven by the item_stats counter, scoped to the
		/// requesting user (global when user is empty): only items that have
		/// actually been started appear, each carrying its lifetime play count
		/// and most-recent start time for tie-breaking.
		/// </summary>
		private List<RecentCandidate> GatherMostPlayed(string user, bool includeTracks, bool includeArtists, bool includeAlbums, bool includePlaylists)
		{
			List<RecentCandidate> candidates = new List<RecentCandidate>();

			if (includeTracks)
			{
				Dictionary<string, ItemStats> stats = m_musicManager.GetItemStats(user, eDataType.Track);
				foreach (KeyValuePair<string, ItemStats> entry in stats)
				{
					TrackInfo track = m_musicManager.GetTrack(entry.Key);
					if (track == null)
					{
						continue;
					}
					candidates.Add(MakeCandidate(BuildTrack(track, user), entry.Value));
				}
			}

			if (includeArtists)
			{
				Dictionary<string, ItemStats> stats = m_musicManager.GetItemStats(user, eDataType.Artist);
				foreach (KeyValuePair<string, ItemStats> entry in stats)
				{
					ArtistInfo artist = m_musicManager.GetArtist(entry.Key);
					if (artist == null)
					{
						continue;
					}
					candidates.Add(MakeCandidate(BuildArtist(artist, user), entry.Value));
				}
			}

			if (includeAlbums)
			{
				Dictionary<string, ItemStats> stats = m_musicManager.GetItemStats(user, eDataType.Album);
				foreach (KeyValuePair<string, ItemStats> entry in stats)
				{
					AlbumInfo album = m_musicManager.GetAlbum(entry.Key);
					if (album == null)
					{
						continue;
					}
					candidates.Add(MakeCandidate(BuildAlbum(album), entry.Value));
				}
			}

			if (includePlaylists)
			{
				Dictionary<string, ItemStats> stats = m_musicManager.GetItemStats(user, eDataType.Playlist);
				foreach (KeyValuePair<string, ItemStats> entry in stats)
				{
					PlaylistInfo playlist = m_musicManager.GetPlaylist(entry.Key);
					if (playlist == null)
					{
						continue;
					}
					candidates.Add(MakeCandidate(BuildPlaylist(playlist, user), entry.Value));
				}
			}

			return candidates;
		}

		/// <summary>
		/// Builds the candidate set for the MostRecent (recentlyPlayed) ranking.
		/// Tracks come from the in-memory FIFO recents list; artists and playlists
		/// from their in-memory last-played (skipping never-played items); albums,
		/// which hold no in-memory last-played, have their recency read back from
		/// the item_stats counter.
		/// </summary>
		private List<RecentCandidate> GatherMostRecent(string user, bool includeTracks, bool includeArtists, bool includeAlbums, bool includePlaylists)
		{
			List<RecentCandidate> candidates = new List<RecentCandidate>();

			if (includeTracks)
			{
				PulseAnalyticsInfo analytics = m_musicManager.GetAnalytics();
				List<string> recentIds = new List<string>(analytics.RecentlyPlayed);
				for (int i = 0; i < recentIds.Count; i++)
				{
					TrackInfo track = m_musicManager.GetTrack(recentIds[i]);
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
						candidate.LastPlayed = track.LastPlayed;
					}
					else
					{
						candidate.LastPlayed = DateTime.UtcNow.AddSeconds(-i);
					}
					candidates.Add(candidate);
				}
			}

			if (includeArtists)
			{
				List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
				for (int i = 0; i < allArtists.Count; i++)
				{
					ArtistInfo artist = allArtists[i];
					if (artist.LastPlayed == default(DateTime))
					{
						continue;
					}
					RecentCandidate candidate = new RecentCandidate();
					candidate.Item = BuildArtist(artist, user);
					candidate.LastPlayed = artist.LastPlayed;
					candidates.Add(candidate);
				}
			}

			if (includeAlbums)
			{
				Dictionary<string, ItemStats> stats = m_musicManager.GetItemStats(user, eDataType.Album);
				foreach (KeyValuePair<string, ItemStats> entry in stats)
				{
					AlbumInfo album = m_musicManager.GetAlbum(entry.Key);
					if (album == null)
					{
						continue;
					}
					RecentCandidate candidate = new RecentCandidate();
					candidate.Item = BuildAlbum(album);
					candidate.LastPlayed = entry.Value.LastPlayed;
					candidates.Add(candidate);
				}
			}

			if (includePlaylists)
			{
				List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(user);
				for (int i = 0; i < allPlaylists.Count; i++)
				{
					PlaylistInfo playlist = allPlaylists[i];
					DateTime playlistLastPlayed = playlist.GetLastPlayed(user);
					if (playlistLastPlayed == default(DateTime))
					{
						continue;
					}
					RecentCandidate candidate = new RecentCandidate();
					candidate.Item = BuildPlaylist(playlist, user);
					candidate.LastPlayed = playlistLastPlayed;
					candidates.Add(candidate);
				}
			}

			return candidates;
		}

		private static RecentCandidate MakeCandidate(PulseObject item, ItemStats stats)
		{
			RecentCandidate candidate = new RecentCandidate();
			candidate.Item = item;
			candidate.PlayCount = stats.PlayCount;
			candidate.LastPlayed = stats.LastPlayed;
			return candidate;
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
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				if (string.Equals(part, "track", StringComparison.OrdinalIgnoreCase)) { track = true; }
				else if (string.Equals(part, "artist", StringComparison.OrdinalIgnoreCase)) { artist = true; }
				else if (string.Equals(part, "album", StringComparison.OrdinalIgnoreCase)) { album = true; }
				else if (string.Equals(part, "playlist", StringComparison.OrdinalIgnoreCase)) { playlist = true; }
			}
		}

		private static int CompareRecentCandidateDescending(RecentCandidate left, RecentCandidate right)
		{
			return right.LastPlayed.CompareTo(left.LastPlayed);
		}

		// Most-played first; ties broken by most-recently-started so equal-play
		// items still land in a stable, sensible order.
		private static int CompareCandidateByPlaysDescending(RecentCandidate left, RecentCandidate right)
		{
			int byPlays = right.PlayCount.CompareTo(left.PlayCount);
			if (byPlays != 0)
			{
				return byPlays;
			}
			return right.LastPlayed.CompareTo(left.LastPlayed);
		}

		private class RecentCandidate
		{
			public int PlayCount;
			public DateTime LastPlayed;
			public PulseObject Item;
		}

		private PulsePodcast BuildPulsePodcast(SeriesInfo series, string user)
		{
			PulsePodcast pulsePodcast = new PulsePodcast();
			pulsePodcast.Id = series.Id;
			pulsePodcast.Title = series.Title;
			pulsePodcast.Author = series.Author;
			pulsePodcast.Narrator = series.Narrator;
			pulsePodcast.Description = series.Description;
			pulsePodcast.Collection = series.Collection;
			pulsePodcast.CollectionIndex = series.CollectionIndex;
			pulsePodcast.CoverArt = "se-" + series.Id;

			List<SeriesItemInfo> downloaded = m_podcastManager.GetDownloadedItems(series.Id);
			pulsePodcast.EpisodeCount = downloaded.Count;
			pulsePodcast.ItemCount = downloaded.Count;
			pulsePodcast.UnplayedCount = m_podcastManager.GetUnplayedCount(series.Id, user);

			SeriesUserDataInfo userSeries = m_podcastManager.GetUserSeries(series.Id, user);
			if (userSeries != null)
			{
				pulsePodcast.Subscribed = userSeries.Subscribed;
				pulsePodcast.LastItemId = userSeries.LastItemId;
				pulsePodcast.LastPlayed = userSeries.LastPlayed;
			}

			pulsePodcast.FeedUrl = series.FeedUrl;
			pulsePodcast.AutoDownload = series.AutoDownload;
			pulsePodcast.RetentionPolicy = series.Retention.ToString();
			pulsePodcast.RetentionValue = series.RetentionValue;
			pulsePodcast.PollIntervalMinutes = series.PollIntervalMinutes;

			return pulsePodcast;
		}

		private PulsePodcastEpisode BuildPulsePodcastEpisode(SeriesItemInfo item, string user)
		{
			PulsePodcastEpisode episode = new PulsePodcastEpisode();
			episode.Id = item.Id;
			episode.SeriesId = item.SeriesId;
			episode.Title = item.Title;
			episode.Description = item.Description;
			episode.OrderIndex = item.OrderIndex;
			episode.PublishedDate = item.PublishedDate;
			episode.Duration = item.DurationSeconds;
			episode.CoverArt = "se-" + item.SeriesId;

			SeriesItemUserDataInfo progress = m_podcastManager.GetProgress(item.Id, user);
			if (progress != null)
			{
				episode.PositionSeconds = progress.PositionSeconds;
				episode.Completed = progress.Completed;
			}
			return episode;
		}

		public IResult GetPodcasts(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}

			List<SeriesInfo> subscribed = m_podcastManager.GetSubscribedPodcasts(user);
			List<PulsePodcast> pulsePodcasts = new List<PulsePodcast>();
			for (int index = 0; index < subscribed.Count; index++)
			{
				pulsePodcasts.Add(BuildPulsePodcast(subscribed[index], user));
			}
			return RespondList(context, pulsePodcasts);
		}

		public IResult GetAllPodcasts(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}

			List<SeriesInfo> allSeries = m_podcastManager.GetAllPodcasts();
			List<PulsePodcast> pulsePodcasts = new List<PulsePodcast>();
			for (int index = 0; index < allSeries.Count; index++)
			{
				pulsePodcasts.Add(BuildPulsePodcast(allSeries[index], user));
			}
			return RespondList(context, pulsePodcasts);
		}

		public IResult GetPodcast(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}

			SeriesInfo series = m_podcastManager.GetSeries(id);
			if (series == null)
			{
				return RespondStatus(context, "not_found");
			}

			PulsePodcastDetails details = new PulsePodcastDetails();
			details.Series = BuildPulsePodcast(series, user);

			List<SeriesItemInfo> downloaded = m_podcastManager.GetDownloadedItems(id);
			for (int index = 0; index < downloaded.Count; index++)
			{
				details.Episodes.Add(BuildPulsePodcastEpisode(downloaded[index], user));
			}
			return RespondObject(context, details);
		}

		public IResult AddPodcast(HttpContext context)
		{
			string feedUrl = context.Request.Query["feedUrl"].FirstOrDefault();
			if (string.IsNullOrEmpty(feedUrl))
			{
				return RespondStatus(context, "missing_feedUrl");
			}
			string subscribeRaw = context.Request.Query["subscribe"].FirstOrDefault();
			bool subscribe = subscribeRaw == "1";
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}

			SeriesInfo series = m_podcastManager.AddPodcast(feedUrl, user, subscribe);
			if (series == null)
			{
				return RespondStatus(context, "add_failed");
			}
			return RespondObject(context, BuildPulsePodcast(series, user));
		}

		/// <summary>
		/// Updates a podcast's backlog settings (poll interval, retention
		/// policy + value, auto-download). Reads query params, defaults
		/// missing/invalid values (KeepAll / 0 / 60 / false), writes
		/// through PodcastManager.UpdatePodcastSettings (which also kicks
		/// a background apply so the new settings take effect immediately
		/// rather than waiting for the next poll cycle), then returns the
		/// updated PulsePodcast for the caller to reflect in their UI.
		/// </summary>
		public IResult UpdatePodcast(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null) { user = ""; }
			SeriesInfo series = m_podcastManager.GetSeries(id);
			if (series == null) { return RespondStatus(context, "not_found"); }

			string retentionRaw = context.Request.Query["retentionPolicy"].FirstOrDefault();
			eRetentionPolicy retention = eRetentionPolicy.KeepAll;
			bool retentionParsed = Enum.TryParse<eRetentionPolicy>(retentionRaw, out retention);
			if (!retentionParsed) { retention = eRetentionPolicy.KeepAll; }

			string retentionValueRaw = context.Request.Query["retentionValue"].FirstOrDefault();
			int retentionValue = 0;
			int.TryParse(retentionValueRaw, out retentionValue);

			string pollRaw = context.Request.Query["pollIntervalMinutes"].FirstOrDefault();
			int pollInterval = 60;
			bool pollParsed = int.TryParse(pollRaw, out pollInterval);
			if (!pollParsed || pollInterval <= 0) { pollInterval = 60; }

			string autoRaw = context.Request.Query["autoDownload"].FirstOrDefault();
			bool autoDownload = autoRaw == "1";

			m_podcastManager.UpdatePodcastSettings(id, pollInterval, retention, retentionValue, autoDownload);
			SeriesInfo updated = m_podcastManager.GetSeries(id);
			return RespondObject(context, BuildPulsePodcast(updated, user));
		}

		public IResult SubscribePodcast(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}
			m_podcastManager.SetSubscribed(id, user, true);
			return RespondStatus(context, "ok");
		}

		public IResult UnsubscribePodcast(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}
			m_podcastManager.SetSubscribed(id, user, false);
			return RespondStatus(context, "ok");
		}

		public IResult EpisodeProgress(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			if (user == null)
			{
				user = "";
			}
			string positionRaw = context.Request.Query["positionSeconds"].FirstOrDefault();
			int positionSeconds = 0;
			int.TryParse(positionRaw, out positionSeconds);
			m_podcastManager.SaveProgress(id, user, positionSeconds);
			return RespondStatus(context, "ok");
		}

		/// <summary>
		/// Analytics intake. Clients POST a PulseAnalyticsBatch describing a
		/// window of client-side usage events (navigation, playback, failures).
		/// The body is JSON; HttpServer sets AllowSynchronousIO so the
		/// synchronous read is legal. The handler stamps received_at on the
		/// server clock and hands the item to AnalyticsDB -- the request thread
		/// never touches the database.
		/// </summary>
		public IResult PostIngestAnalytics(HttpContext context)
		{
			string body;
			using (StreamReader reader = new StreamReader(context.Request.Body))
			{
				body = reader.ReadToEnd();
			}

			if (string.IsNullOrEmpty(body))
			{
				return RespondStatus(context, "missing_body");
			}

			PulseAnalyticsBatch batch = PulseWire.Parse<PulseAnalyticsBatch>(body);
			if (batch == null)
			{
				return RespondStatus(context, "missing_body");
			}
			if (string.IsNullOrEmpty(batch.SessionId))
			{
				return RespondStatus(context, "missing_id");
			}
			if (batch.Events == null || batch.Events.Count == 0)
			{
				return RespondStatus(context, "missing_events");
			}

			string receivedAt = DateTime.UtcNow.ToString("o");
			m_analyticsDB.Enqueue(batch, receivedAt);
			return Respond(context, new PulseResponse());
		}

		/// <summary>
		/// Analytics read endpoint. With ?session_id=... returns every event for
		/// that session (optionally filtered by category, action, and result),
		/// ordered by client timestamp. With ?device_id=... returns every
		/// session for that device, most recent first.
		/// </summary>
		public IResult GetAnalytics(HttpContext context)
		{
			string sessionId = context.Request.Query["session_id"].FirstOrDefault();
			string deviceId = context.Request.Query["device_id"].FirstOrDefault();

			if (!string.IsNullOrEmpty(sessionId))
			{
				string categoryFilter = context.Request.Query["category"].FirstOrDefault();
				string actionFilter = context.Request.Query["action"].FirstOrDefault();
				string resultFilter = context.Request.Query["result"].FirstOrDefault();
				List<PulseAnalyticsEvent> events = m_analyticsDB.GetEventsForSession(sessionId, categoryFilter, actionFilter, resultFilter);
				AnalyticsEventsResponse response = new AnalyticsEventsResponse();
				response.SessionId = sessionId;
				response.Events = events;
				return Results.Text(PulseWire.Serialize(response), "application/json");
			}

			if (!string.IsNullOrEmpty(deviceId))
			{
				List<PulseAnalyticsSession> sessions = m_analyticsDB.GetSessionsForDevice(deviceId);
				AnalyticsSessionsResponse response = new AnalyticsSessionsResponse();
				response.DeviceId = deviceId;
				response.Sessions = sessions;
				return Results.Text(PulseWire.Serialize(response), "application/json");
			}

			return RespondStatus(context, "missing_id");
		}
	}

	/// <summary>
	/// Response envelope for the /analytics events query. Plain public
	/// fields; serialized through PulseWire which emits field names verbatim.
	/// </summary>
	public class AnalyticsEventsResponse
	{
		public string SessionId;
		public List<PulseAnalyticsEvent> Events;
	}

	/// <summary>
	/// Response envelope for the /analytics sessions query.
	/// </summary>
	public class AnalyticsSessionsResponse
	{
		public string DeviceId;
		public List<PulseAnalyticsSession> Sessions;
	}
}
