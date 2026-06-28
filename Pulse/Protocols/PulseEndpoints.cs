using Microsoft.AspNetCore.Http;

using Pulse.Data;
using Pulse.DataStorage;
using Pulse.MusicLibrary;
using Pulse.Podcasts;
using Pulse.Series;
using PulseAPI.CSharp;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using TagLib.IFD.Tags;
using System.Text.Json;

namespace Pulse.Protocols
{
	public class PulseEndpoints
	{
		public enum eSortMethod
		{
			MostRecent,
			Score,
		}
		/// <summary>
		/// Intentionally changed from /pulse to add versioning support
		/// </summary>
		string m_apiSpace = "pulse_v1/";
		IPulseRouteHost m_host;
		PulseService m_pulseService;
		MusicManager m_musicManager;
		AnalyticsData m_analyticsData;
		DiagnosticsData m_diagnosticsData;
		PodcastManager m_podcastManager;
		PulseData m_pulseData;
		AudiobookManager m_audiobookManager;
		private byte[] m_defaultCoverArt;
		private ConcurrentDictionary<string, byte[]> m_coverArtCache = new ConcurrentDictionary<string, byte[]>();

		public PulseEndpoints(PulseService pulse, PulseData pulseData, MusicManager musicManager, AnalyticsData analyticsData, DiagnosticsData diagnosticsData, PodcastManager podcastManager, AudiobookManager audiobookManager)
		{
			m_pulseService = pulse;
			m_pulseData = pulseData;
			m_musicManager = musicManager;
			m_analyticsData = analyticsData;
			m_diagnosticsData = diagnosticsData;
			m_podcastManager = podcastManager;
			m_audiobookManager = audiobookManager;
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
			RegisterRoute("albumsLite", GetAlbumsLite);
			RegisterRoute("album", GetAlbum);
			RegisterRoute("track", GetTrack);

			RegisterRoute("genres", GetGenres);
			RegisterRoute("genreTracks", GetGenreTracks);

			RegisterRoute("playlists", GetPlaylists);
			RegisterRoute("playlist", GetPlaylist);
			RegisterRoute("createPlaylist", CreatePlaylist);
			RegisterRoute("updatePlaylist", UpdatePlaylist);
			RegisterRoute("deletePlaylist", DeletePlaylist);
			RegisterRoute("smartQueue", GetSmartQueue);

			RegisterRoute("recentlyPlayed", GetRecentlyPlayed);
			RegisterRoute("search", Search);
			//RegisterRoute("favorites", GetFavorites);
			//RegisterRoute("favorite", Favorite);
			//RegisterRoute("unfavorite", Unfavorite);
			RegisterRoute("reportAnalytics", ReportAnalytics);

			RegisterRoute("ingestAnalytics", IngestAnalytics);
			RegisterRoute("recordAnalytic", RecordAnalytic);
			RegisterRoute("analytics", GetAnalytics);

			RegisterRoute("ingestDiagnostics", PostIngestDiagnostics);
			RegisterRoute("diagnostics", GetDiagnostics);

			RegisterRoute("topItems", GetTop);


			RegisterRoute("podcasts", GetPodcasts);
			RegisterRoute("allPodcasts", GetAllPodcasts);
			RegisterRoute("podcast", GetPodcast);
			RegisterRoute("addPodcast", AddPodcast);
			RegisterRoute("updatePodcast", UpdatePodcast);
			RegisterRoute("subscribePodcast", SubscribePodcast);
			RegisterRoute("unsubscribePodcast", UnsubscribePodcast);
			RegisterRoute("searchPodcasts", SearchPodcasts);
			RegisterRoute("episodeProgress", EpisodeProgress);

			RegisterRoute("audiobooks", GetAudiobooks);
			RegisterRoute("audiobook", GetAudiobook);
			RegisterRoute("chapterProgress", ChapterProgress);



			RegisterRoute("stats", GetStats);
			RegisterRoute("version", GetVersion);


		}

		private void RegisterRoute(string route, Func<HttpContext, IResult> handler)
		{
			m_host.RegisterResultRoute(m_apiSpace + route, handler);
		}
		public IResult Respond(HttpStatusCode code)
		{
			PulseResponse body = new PulseResponse();
			body.contentType = PulseResponse.ContentType.PulseObject;
			return Results.Text(PulseWire.Serialize(body), "application/json", Encoding.UTF8, (int)code);
		}
		public IResult Respond(HttpContext context, PulseObject contents)
		{
			PulseResponse body = new PulseResponse();
			body.contentType = PulseResponse.ContentType.PulseObject;
			body.contents = contents;
			return Results.Text(PulseWire.Serialize(body), "application/json", Encoding.UTF8, (int)HttpStatusCode.OK);
		}
		public IResult Respond(HttpContext context, PulseResponse body)
		{
			return Results.Text(PulseWire.Serialize(body), "application/json", Encoding.UTF8, (int)HttpStatusCode.OK);
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

		private PulseAlbum BuildAlbum(AlbumData album)
		{
			PulseAlbum pulseAlbum = new PulseAlbum();
			pulseAlbum.Id = album.Id;
			pulseAlbum.Name = album.Name;
			pulseAlbum.Artist = album.ArtistName;
			pulseAlbum.ArtistId = album.ArtistId;
			pulseAlbum.CoverArt = album.CoverArtId;
			pulseAlbum.Year = album.Year;
			List<TrackData> albumTracks = album.GetTracks();
			pulseAlbum.TrackCount = albumTracks.Count;
			int duration = 0;
			for (int index = 0; index < albumTracks.Count; index++)
			{
				duration = duration + albumTracks[index].DurationSeconds;
			}
			pulseAlbum.Duration = duration;
			return pulseAlbum;
		}

		/// <summary>
		/// Trimmed PulseAlbum projection for the grid/jump-bar view. Sets only
		/// the fields those views need (Id, Name, Artist, CoverArt, Year);
		/// ArtistId, TrackCount, and Duration are left unset because the detail
		/// view fetches the full album separately.
		/// </summary>
		private PulseAlbum BuildAlbumLite(AlbumData album)
		{
			PulseAlbum pulseAlbum = new PulseAlbum();
			pulseAlbum.Id = album.Id;
			pulseAlbum.Name = album.Name;
			pulseAlbum.Artist = album.ArtistName;
			pulseAlbum.CoverArt = album.CoverArtId;
			pulseAlbum.Year = album.Year;
			return pulseAlbum;
		}



		/// <summary>
		/// Builds a PulseTrack envelope from a podcast episode so the /track
		/// endpoint can resolve episode ids the same way /stream does. Cover
		/// art uses the series-art prefix; Artist carries the show title when
		/// the parent series is in the in-memory cache.
		/// </summary>
		private PulseTrack BuildTrackFromEpisode(Episode episode, string user)
		{
			PulseTrack pulseTrack = new PulseTrack();
			pulseTrack.Id = episode.Id;
			pulseTrack.Title = episode.Title;
			pulseTrack.Duration = episode.DurationSeconds;
			pulseTrack.CoverArt = "se-" + episode.PodcastId;
			pulseTrack.Artist = "";
			Podcast podcast = m_podcastManager.GetPodcast(episode.PodcastId);
			if (podcast != null)
			{
				pulseTrack.Artist = podcast.Title;
			}
			pulseTrack.Starred = false;
			pulseTrack.IsSeries = true;
			return pulseTrack;
		}

		/// <summary>
		/// Builds a PulseTrack envelope from an audiobook chapter so the
		/// /track endpoint can resolve chapter ids the same way /stream does.
		/// Cover art uses the series-art prefix; Artist carries the audiobook
		/// title when the parent book is in the in-memory cache.
		/// </summary>
		private PulseTrack BuildTrackFromChapter(Chapter chapter, string user)
		{
			PulseTrack pulseTrack = new PulseTrack();
			pulseTrack.Id = chapter.Id;
			pulseTrack.Title = chapter.Title;
			pulseTrack.Duration = chapter.DurationSeconds;
			pulseTrack.CoverArt = "se-" + chapter.AudiobookId;
			pulseTrack.Artist = "";
			Audiobook book = m_audiobookManager.GetBook(chapter.AudiobookId);
			if (book != null)
			{
				pulseTrack.Artist = book.Title;
			}
			pulseTrack.Starred = false;
			pulseTrack.IsSeries = true;
			return pulseTrack;
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

		private int CompareTrackByDiscThenNumber(TrackData left, TrackData right)
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
			string id = QueryParameters.GetString(context, "id");

			//pass a damn parameter so we don't need this dumb try fail
			string type = QueryParameters.GetString(context, "type", "");

			if (!string.IsNullOrEmpty(id))
			{
				//call direct
			}


			TrackData track = m_musicManager.GetTrack(id);
			if (track != null)
			{
				string trackPath = m_musicManager.GetTrackFilePath(track);
				if (!string.IsNullOrEmpty(trackPath))
				{
					FileStream fileStream = new FileStream(trackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
					return Results.File(fileStream, track.ContentType, enableRangeProcessing: true);
				}
			}

			//try podcast

			string streamPath = "";
			Episode item = m_podcastManager.GetEpisode(id);
			if (item != null)
			{
				streamPath = item.LocalPath;
			}
			else
			{
				//try audiobook
				Chapter chapter = m_audiobookManager.GetChapter(id);
				if (chapter != null)
				{
					streamPath = chapter.LocalPath;
				}
			}

			if (!string.IsNullOrEmpty(streamPath) && File.Exists(streamPath))
			{
				string contentType = "audio/mpeg";
				if (streamPath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
				{
					contentType = "audio/mp4";
				}
				FileStream podcastStream = new FileStream(streamPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
			string id = QueryParameters.GetString(context, "id");

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
				PlaylistData playlist = m_musicManager.GetPlaylist(playlistId);
				if (playlist != null)
				{
					// Collect up to 4 distinct album covers, in playlist order.
					List<byte[]> tileBytes = new List<byte[]>();
					HashSet<string> seenAlbumIds = new HashSet<string>();
					for (int i = 0; i < playlist.TrackIds.Count && tileBytes.Count < 4; i++)
					{
						TrackData track = m_musicManager.GetTrack(playlist.TrackIds[i]);
						if (track == null || string.IsNullOrEmpty(track.AlbumId))
						{
							continue;
						}
						if (!seenAlbumIds.Add(track.AlbumId))
						{
							continue;
						}
						AlbumData tileAlbum = m_musicManager.GetAlbum(track.AlbumId);
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
							Log.Exception(ex);
						}
					}
				}
			}
			else if (id.StartsWith("ar-", StringComparison.Ordinal))
			{
				string artistId = id.Substring(3);
				ArtistData artist = m_musicManager.GetArtist(artistId);
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

				// Podcasts: art lives as folder.jpg in the download directory.
				Podcast podcast = m_podcastManager.GetPodcast(seriesId);
				if (podcast != null)
				{
					string podcastDir = m_podcastManager.GetPodcastMediaDir(podcast);
					string artworkPath = Path.Combine(podcastDir, "folder.jpg");
					if (File.Exists(artworkPath))
					{
						byte[] bytes = File.ReadAllBytes(artworkPath);
						m_coverArtCache[id] = bytes;
						return Results.Bytes(bytes, "image/jpeg");
					}
				}

				// Audiobooks: art cache or folder art next to the audio files.
				string audiobookArt = m_audiobookManager.GetCoverArtPath(seriesId);
				if (!string.IsNullOrEmpty(audiobookArt))
				{
					byte[] bytes = File.ReadAllBytes(audiobookArt);
					m_coverArtCache[id] = bytes;
					string contentType = audiobookArt.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
					return Results.Bytes(bytes, contentType);
				}

				return Results.Bytes(m_defaultCoverArt, "image/png");
			}
			else
			{
				AlbumData album = m_musicManager.GetAlbum(id);
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
			string userId = QueryParameters.GetUserId(context);

			List<PulseArtist> pulseArtists = new List<PulseArtist>();
			List<ArtistData> allArtists = m_musicManager.GetAllArtists();
			for (int index = 0; index < allArtists.Count; index++)
			{
				pulseArtists.Add(allArtists[index].BuildPulse());
			}
			return RespondList(context, pulseArtists);
		}

		public IResult GetArtist(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);

			ArtistData artist = m_musicManager.GetArtist(id);
			if (artist == null)
			{
				return RespondStatus(context, "not_found");
			}

			PulseArtistDetails details = new PulseArtistDetails();
			details.Id = artist.Id;
			details.Artist = artist.BuildPulse();
			List<AlbumData> artistAlbums = artist.GetAlbums();
			for (int index = 0; index < artistAlbums.Count; index++)
			{
				details.Albums.Add(BuildAlbum(artistAlbums[index]));
			}
			return RespondObject(context, details);
		}

		public IResult GetArtistTracks(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);

			ArtistData artist = m_musicManager.GetArtist(id);
			if (artist == null)
			{
				return RespondStatus(context, "not_found");
			}

			PulseArtistFullDetails details = new PulseArtistFullDetails();
			details.Id = artist.Id;
			details.Artist = artist.BuildPulse();
			List<AlbumData> artistAlbums = artist.GetAlbums();
			for (int albumIndex = 0; albumIndex < artistAlbums.Count; albumIndex++)
			{
				AlbumData album = artistAlbums[albumIndex];
				PulseAlbumDetails albumDetails = new PulseAlbumDetails();
				albumDetails.Id = album.Id;
				albumDetails.Album = BuildAlbum(album);

				List<TrackData> ordered = album.GetTracks();
				ordered.Sort(CompareTrackByDiscThenNumber);
				for (int trackIndex = 0; trackIndex < ordered.Count; trackIndex++)
				{
					albumDetails.Tracks.Add(ordered[trackIndex].BuildPulse());
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
			string typeRaw = QueryParameters.GetString(context, "type");
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			if (offset < 0)
			{
				offset = 0;
			}
			string userId = QueryParameters.GetUserId(context);

			string type = "random";
			if (!string.IsNullOrEmpty(typeRaw))
			{
				type = typeRaw.ToLowerInvariant();
			}

			List<AlbumData> allAlbums = m_musicManager.GetAllAlbums();

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
					AlbumData temp = allAlbums[index];
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
			else if (type == "recent" || type == "frequent")
			{
				List<KeyValuePair<AlbumData, DateTime>> scored = new List<KeyValuePair<AlbumData, DateTime>>();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					AlbumData album = allAlbums[index];
					DateTime mostRecent = default;
					List<TrackData> albumTracks = album.GetTracks();
					for (int trackIndex = 0; trackIndex < albumTracks.Count; trackIndex++)
					{
						DateTime trackPlayed = albumTracks[trackIndex].LastPlayed;
						if (trackPlayed > mostRecent)
						{
							mostRecent = trackPlayed;
						}
					}
					if (mostRecent != default)
					{
						scored.Add(new KeyValuePair<AlbumData, DateTime>(album, mostRecent));
					}
				}
				scored.Sort(MusicComparers.CompareAlbumDateDescending);
				allAlbums = new List<AlbumData>();
				for (int index = 0; index < scored.Count; index++)
				{
					allAlbums.Add(scored[index].Key);
				}
			}
			else if (type == "byyear")
			{
				int fromYear = int.MinValue;
				int toYear = int.MaxValue;
				string fromYearRaw = QueryParameters.GetString(context, "fromYear");
				string toYearRaw = QueryParameters.GetString(context, "toYear");
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
				List<AlbumData> filtered = new List<AlbumData>();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					AlbumData album = allAlbums[index];
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
				string genre = QueryParameters.GetString(context, "genre");
				List<AlbumData> filtered = new List<AlbumData>();
				if (!string.IsNullOrEmpty(genre))
				{
					string genreLower = genre.ToLowerInvariant();
					for (int index = 0; index < allAlbums.Count; index++)
					{
						AlbumData album = allAlbums[index];
						if (!string.IsNullOrEmpty(album.Genre) && album.Genre.ToLowerInvariant() == genreLower)
						{
							filtered.Add(album);
						}
					}
				}
				allAlbums = filtered;
			}
			else if (type == "highest")
			{
				Log.Error("Unsupported highest rated albums");
			}

			List<PulseAlbum> pulseAlbums = new List<PulseAlbum>();
			int end = Math.Min(offset + size, allAlbums.Count);
			for (int index = offset; index < end; index++)
			{
				pulseAlbums.Add(BuildAlbum(allAlbums[index]));
			}
			return RespondList(context, pulseAlbums);
		}

		/// <summary>
		/// Returns the entire album catalog (no paging) with a trimmed field set,
		/// sorted alphabetically by name. Serves the Pulse album grid, which
		/// renders every tile at once and lazy-loads cover images. Response
		/// compression makes a single whole-catalog payload cheaper than paging.
		/// </summary>
		public IResult GetAlbumsLite(HttpContext context)
		{
			List<AlbumData> allAlbums = m_musicManager.GetAllAlbums();
			allAlbums.Sort(MusicComparers.CompareAlbumByName);

			List<PulseAlbum> pulseAlbums = new List<PulseAlbum>();
			for (int index = 0; index < allAlbums.Count; index++)
			{
				pulseAlbums.Add(BuildAlbumLite(allAlbums[index]));
			}
			return RespondList(context, pulseAlbums);
		}

		public IResult GetAlbum(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);

			AlbumData album = m_musicManager.GetAlbum(id);
			if (album == null)
			{
				return RespondStatus(context, "not_found");
			}

			List<TrackData> ordered = album.GetTracks();
			ordered.Sort(CompareTrackByDiscThenNumber);

			PulseAlbumDetails details = new PulseAlbumDetails();
			details.Id = album.Id;
			details.Album = BuildAlbum(album);
			for (int index = 0; index < ordered.Count; index++)
			{
				details.Tracks.Add(ordered[index].BuildPulse());
			}
			return RespondObject(context, details);
		}

		/// <summary>
		/// Resolves the requested id to a track envelope. Mirrors the
		/// resolution order of /stream: music track first, then podcast
		/// episode, then audiobook chapter. Returns not_found if none match.
		/// Android Auto calls this endpoint to turn a tapped item into a play
		/// queue, so podcast episodes and audiobook chapters must resolve
		/// here too — otherwise AA cannot play them.
		/// </summary>
		public IResult GetTrack(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);

			TrackData track = m_musicManager.GetTrack(id);
			if (track != null)
			{
				return RespondObject(context, track.BuildPulse());
			}

			Episode episode = m_podcastManager.GetEpisode(id);
			if (episode != null)
			{
				return RespondObject(context, BuildTrackFromEpisode(episode, userId));
			}

			Chapter chapter = m_audiobookManager.GetChapter(id);
			if (chapter != null)
			{
				return RespondObject(context, BuildTrackFromChapter(chapter, userId));
			}

			return RespondStatus(context, "not_found");
		}


		public IResult GetGenres(HttpContext context)
		{
			Dictionary<string, GenreInfo> genreMap = new Dictionary<string, GenreInfo>();

			List<AlbumData> allAlbums = m_musicManager.GetAllAlbums();
			for (int index = 0; index < allAlbums.Count; index++)
			{
				AlbumData album = allAlbums[index];
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
				entry.TrackCount = entry.TrackCount + album.TrackCount;
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
			string genre = QueryParameters.GetString(context, "genre");
			string userId = QueryParameters.GetUserId(context);
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
			List<TrackData> matches = new List<TrackData>();
			int trackCount = 0;
			int albumCount = 0;
			List<TrackData> allTracks = m_musicManager.GetAllTracks();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackData track = allTracks[index];
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
				details.Tracks.Add(matches[index].BuildPulse());
			}
			return RespondObject(context, details);
		}


		public IResult GetPlaylists(HttpContext context)
		{
			string userId = QueryParameters.GetUserId(context);

			List<PulsePlaylist> pulsePlaylists = new List<PulsePlaylist>();
			List<PlaylistData> allPlaylists = m_musicManager.GetAllPlaylists(userId);
			for (int index = 0; index < allPlaylists.Count; index++)
			{
				pulsePlaylists.Add(allPlaylists[index].BuildPulsePlaylist());
			}
			return RespondList(context, pulsePlaylists);
		}

		public IResult GetPlaylist(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);

			if (string.IsNullOrEmpty(id))
			{
				return RespondStatus(context, "missing_id");
			}

			PlaylistAndTracks playlist = m_musicManager.GetPlaylistAndTracks(id);
			if (playlist == null)
			{
				return RespondStatus(context, "not_found");
			}
			return RespondObject(context, playlist.BuildPulsePlaylistDetails());
		}

		public IResult GetSmartQueue(HttpContext context)
		{
			string userId = QueryParameters.GetUserId(context);
			string mode = QueryParameters.GetString(context, "mode");

			eQueueMode queueMode;
			if (string.Equals(mode, "personalized", System.StringComparison.OrdinalIgnoreCase))
			{
				queueMode = eQueueMode.Personalized;
			}
			else if (string.Equals(mode, "popular", System.StringComparison.OrdinalIgnoreCase))
			{
				queueMode = eQueueMode.Popular;
			}
			else
			{
				return RespondStatus(context, "missing_mode");
			}

			SmartQueue smartQueue = new SmartQueue(m_pulseData, m_analyticsData);
			List<TrackData> tracks = smartQueue.GetTracks(queueMode, userId);

			PlaylistData info = new PlaylistData();
			info.Id = "smartqueue/" + queueMode.ToString();
			if (queueMode == eQueueMode.Personalized)
			{
				info.Name = "Personalized";
			}
			else
			{
				info.Name = "Popular";
			}
			for (int index = 0; index < tracks.Count; index++)
			{
				info.TrackIds.Add(tracks[index].Id);
			}

			PlaylistAndTracks queuePlaylist = new PlaylistAndTracks(info, tracks);
			return RespondObject(context, queuePlaylist.BuildPulsePlaylistDetails());
		}

		public IResult CreatePlaylist(HttpContext context)
		{
			string playlistId = QueryParameters.GetString(context, "playlistId");
			string name = QueryParameters.GetString(context, "name");
			string userId = QueryParameters.GetUserId(context);
			List<string> trackIds = QueryParameters.GetList(context, "songId");

			PlaylistData playlist = null;
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
				playlist = new PlaylistData();
				playlist.Id = MusicManager.GenerateID("playlist/" + userId + "/" + name + "/" + DateTime.UtcNow.Ticks);
				playlist.Name = name;
			}

			long totalDuration = 0;
			for (int index = 0; index < trackIds.Count; index++)
			{
				TrackData track = m_musicManager.GetTrack(trackIds[index]);
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
			return RespondObject(context, fullPlaylist.BuildPulsePlaylistDetails());
		}

		public IResult UpdatePlaylist(HttpContext context)
		{
			string playlistId = QueryParameters.GetString(context, "playlistId");
			string name = QueryParameters.GetString(context, "name");
			string comment = QueryParameters.GetString(context, "comment");
			string userId = QueryParameters.GetUserId(context);
			List<string> trackIdsToAdd = QueryParameters.GetList(context, "songIdToAdd");
			List<string> indicesToRemove = QueryParameters.GetList(context, "songIndexToRemove");

			if (string.IsNullOrEmpty(playlistId))
			{
				return RespondStatus(context, "missing_id");
			}

			PlaylistData playlist = m_musicManager.GetPlaylist(playlistId);
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

			bool commentProvided = context.Request.Query.ContainsKey("comment");
			if (commentProvided)
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

			for (int index = 0; index < trackIdsToAdd.Count; index++)
			{
				TrackData track = m_musicManager.GetTrack(trackIdsToAdd[index]);
				if (track == null)
				{
					continue;
				}
				playlist.TrackIds.Add(track.Id);
			}

			long totalDuration = 0;
			for (int index = 0; index < playlist.TrackIds.Count; index++)
			{
				TrackData track = m_musicManager.GetTrack(playlist.TrackIds[index]);
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
			return RespondObject(context, fullPlaylist.BuildPulsePlaylistDetails());
		}

		// Case-insensitive duplicate-name check. skipPlaylistId lets the caller
		// exclude the playlist currently being renamed.
		private bool PlaylistNameTaken(string name, string skipPlaylistId)
		{
			string nameLower = name.ToLowerInvariant();
			List<PlaylistData> all = m_musicManager.GetAllPlaylists(null);
			for (int index = 0; index < all.Count; index++)
			{
				PlaylistData existing = all[index];
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
			string playlistId = QueryParameters.GetString(context, "id");
			if (string.IsNullOrEmpty(playlistId))
			{
				return RespondStatus(context, "missing_id");
			}

			PlaylistData playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return RespondStatus(context, "not_found");
			}

			m_musicManager.DeletePlaylist(playlistId);
			return Respond(context, new PulseResponse());
		}

		public IResult Search(HttpContext context)
		{
			string query = QueryParameters.GetString(context, "query");
			query = query.Trim('"');

			if (string.IsNullOrEmpty(query))
			{
				return RespondObject(context, new PulseSearchData());
			}

			string userId = QueryParameters.GetUserId(context);
			int artistCount = QueryParameters.GetInt(context, "artistCount", 20);
			int albumCount = QueryParameters.GetInt(context, "albumCount", 20);
			int trackCount = QueryParameters.GetInt(context, "songCount", 20);

			PulseSearchData result = new PulseSearchData();

			List<ArtistData> allArtists = m_musicManager.GetAllArtists();
			List<AlbumData> allAlbums = m_musicManager.GetAllAlbums();

			string lowerQuery = query.ToLowerInvariant();

			int artistHits = 0;
			for (int i = 0; i < allArtists.Count && artistHits < artistCount; i++)
			{
				if (allArtists[i].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					result.Artists.Add(allArtists[i].BuildPulse());
					artistHits++;
				}
			}

			int albumHits = 0;
			for (int i = 0; i < allAlbums.Count && albumHits < albumCount; i++)
			{
				if (allAlbums[i].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					result.Albums.Add(BuildAlbum(allAlbums[i]));
					albumHits++;
				}
			}

			int trackHits = 0;
			for (int i = 0; i < allAlbums.Count && trackHits < trackCount; i++)
			{
				List<TrackData> tracks = allAlbums[i].GetTracks();
				for (int j = 0; j < tracks.Count && trackHits < trackCount; j++)
				{
					TrackData track = tracks[j];
					if (track == null)
					{
						continue;
					}
					if (track.Title.ToLowerInvariant().Contains(lowerQuery) ||
						track.Artist.ToLowerInvariant().Contains(lowerQuery))
					{
						result.Tracks.Add(track.BuildPulse());
						trackHits++;
					}
				}
			}
			return RespondObject(context, result);
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
			string userId = QueryParameters.GetUserId(context);

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

			m_musicManager.OnPlaybackEvent(userId, analytics);
			return Respond(context, new PulseResponse());
		}

		public IResult GetTop(HttpContext context)
		{
			int count = QueryParameters.GetInt(context, "count", 10);
			string userId = QueryParameters.GetUserId(context);
			string typesParam = QueryParameters.GetString(context, "types");

			List<PulseMusicObject> items = GetItems(count, userId, typesParam, eSortMethod.Score);
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
			string userId = QueryParameters.GetUserId(context);

			string typesParam = QueryParameters.GetString(context, "types");
			List<PulseMusicObject> items = GetItems(count, userId, typesParam, eSortMethod.MostRecent);
			return RespondList(context, items);
		}

		private List<PulseMusicObject> GetItems(int count, string user, string typesParam, eSortMethod sortMethod)
		{
			List<eAnalyticType> types = ParseTypesParam(typesParam);

			List<PulseMusicObject> ranked = GetItems(count, user, types, sortMethod);
			return ranked;
		}
		private List<PulseMusicObject> GetItems(int count, string user, List<eAnalyticType> types, eSortMethod sortMethod)
		{
			List<PulseMusicObject> ranked = m_pulseService.GetRankedItems(user, 100, sortMethod, types);
			return ranked;
		}
		private static List<eAnalyticType> ParseTypesParam(string raw)
		{
			List<eAnalyticType> types = new List<eAnalyticType>();
			if (string.IsNullOrWhiteSpace(raw))
			{
				return types;
			}
			
			string[] parts = raw.Split(',');
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				if (string.Equals(part, "track", StringComparison.OrdinalIgnoreCase)) { types.Add(eAnalyticType.Track); }
				else if (string.Equals(part, "artist", StringComparison.OrdinalIgnoreCase)) { types.Add(eAnalyticType.Artist); }
				else if (string.Equals(part, "album", StringComparison.OrdinalIgnoreCase)) { types.Add(eAnalyticType.Album); }
				else if (string.Equals(part, "playlist", StringComparison.OrdinalIgnoreCase)) { types.Add(eAnalyticType.Playlist); }
			}
			return types;
		}

		

		

		public IResult GetPodcasts(HttpContext context)
		{
			string userId = QueryParameters.GetUserId(context);

			List<Podcast> subscribed = m_podcastManager.GetSubscribedPodcasts(userId);
			List<PulsePodcast> pulsePodcasts = new List<PulsePodcast>();
			for (int index = 0; index < subscribed.Count; index++)
			{
				pulsePodcasts.Add(subscribed[index].BuildPulsePodcast(m_podcastManager, userId));
			}
			return RespondList(context, pulsePodcasts);
		}

		public IResult GetAllPodcasts(HttpContext context)
		{
			string userId = QueryParameters.GetUserId(context);

			List<Podcast> allSeries = m_podcastManager.GetAllPodcasts();
			List<PulsePodcast> pulsePodcasts = new List<PulsePodcast>();
			for (int index = 0; index < allSeries.Count; index++)
			{
				pulsePodcasts.Add(allSeries[index].BuildPulsePodcast(m_podcastManager, userId));
			}
			return RespondList(context, pulsePodcasts);
		}

		public IResult GetPodcast(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);

			Podcast podcast = m_podcastManager.GetPodcast(id);
			if (podcast == null)
			{
				return RespondStatus(context, "not_found");
			}

			PulsePodcastDetails details = new PulsePodcastDetails();
			details.Series = podcast.BuildPulsePodcast(m_podcastManager, userId);

			List<Episode> downloaded = m_podcastManager.GetDownloadedItems(id);
			for (int index = 0; index < downloaded.Count; index++)
			{
				details.Episodes.Add(downloaded[index].BuildPulsePodcastEpisode(m_podcastManager, userId));
			}
			return RespondObject(context, details);
		}

		

		private PulseChapter BuildPulseChapter(Chapter item, string user, string streamId)
		{
			PulseChapter chapter = new PulseChapter();
			chapter.Id = item.Id;
			chapter.SeriesId = item.AudiobookId;
			chapter.Title = item.Title;
			chapter.OrderIndex = item.OrderIndex;
			chapter.Duration = item.DurationSeconds;
			chapter.StartMs = item.StartMs;
			chapter.EndMs = item.EndMs;
			chapter.StreamId = streamId;
			chapter.CoverArt = "se-" + item.AudiobookId;

			Chapter.UserData progress;
			bool hasUser = item.Users.TryGetValue(user, out progress);
			if (hasUser)
			{
				chapter.PositionSeconds = progress.PositionSeconds;
				chapter.Completed = progress.Completed;
			}
			return chapter;
		}

		public IResult GetAudiobooks(HttpContext context)
		{
			string userId = QueryParameters.GetUserId(context);

			List<Audiobook> allBooks = m_audiobookManager.GetAllAudiobooks();
			List<PulseAudiobook> pulseBooks = new List<PulseAudiobook>();
			for (int i = 0; i < allBooks.Count; i++)
			{
				pulseBooks.Add(allBooks[i].BuildPulseAudiobook(m_audiobookManager, userId));
			}
			return RespondList(context, pulseBooks);
		}

		public IResult GetAudiobook(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);

			Audiobook series = m_audiobookManager.GetBook(id);
			if (series == null)
			{
				return RespondStatus(context, "not_found");
			}

			PulseAudiobookDetails details = new PulseAudiobookDetails();
			details.Book = series.BuildPulseAudiobook(m_audiobookManager, userId);

			List<Chapter> chapters = m_audiobookManager.GetChapters(id);

			// Group chapters by their underlying LocalPath and pick a canonical
			// stream id per group so chapters sharing a single file all resolve
			// to one StreamId for client caching.
			Dictionary<string, string> canonicalStreamIdByPath = new Dictionary<string, string>();
			Dictionary<string, int> lowestOrderIndexByPath = new Dictionary<string, int>();
			for (int i = 0; i < chapters.Count; i++)
			{
				Chapter chapter = chapters[i];
				string localPath = chapter.LocalPath;
				if (string.IsNullOrEmpty(localPath))
				{
					continue;
				}
				bool alreadySeen = canonicalStreamIdByPath.ContainsKey(localPath);
				if (!alreadySeen)
				{
					canonicalStreamIdByPath[localPath] = chapter.Id;
					lowestOrderIndexByPath[localPath] = chapter.OrderIndex;
					continue;
				}
				if (chapter.OrderIndex < lowestOrderIndexByPath[localPath])
				{
					canonicalStreamIdByPath[localPath] = chapter.Id;
					lowestOrderIndexByPath[localPath] = chapter.OrderIndex;
				}
			}

			for (int i = 0; i < chapters.Count; i++)
			{
				Chapter chapter = chapters[i];
				string streamId = chapter.Id;
				if (!string.IsNullOrEmpty(chapter.LocalPath))
				{
					string resolved;
					bool found = canonicalStreamIdByPath.TryGetValue(chapter.LocalPath, out resolved);
					if (found)
					{
						streamId = resolved;
					}
				}
				details.Chapters.Add(BuildPulseChapter(chapter, userId, streamId));
			}
			return RespondObject(context, details);
		}

		public IResult AddPodcast(HttpContext context)
		{
			string feedUrl = QueryParameters.GetString(context, "feedUrl");
			if (string.IsNullOrEmpty(feedUrl))
			{
				return RespondStatus(context, "missing_feedUrl");
			}
			bool subscribe = QueryParameters.GetBool(context, "subscribe");
			string userId = QueryParameters.GetUserId(context);

			Podcast podcast = m_podcastManager.AddPodcast(feedUrl, userId, subscribe);
			if (podcast == null)
			{
				return RespondStatus(context, "add_failed");
			}
			return RespondObject(context, podcast.BuildPulsePodcast(m_podcastManager, userId));
		}

		/// <summary>
		/// Podcast discovery: searches the configured provider by name and
		/// returns the hits as PulsePodcasts. These are NOT catalogued series -
		/// they have no Id; the client adds one via AddPodcast using its FeedUrl.
		/// CoverArt carries the provider's remote artwork URL directly (clients
		/// load it as-is rather than through the coverArt endpoint).
		/// </summary>
		public IResult SearchPodcasts(HttpContext context)
		{
			string query = QueryParameters.GetString(context, "query");
			if (string.IsNullOrEmpty(query))
			{
				query = QueryParameters.GetString(context, "q");
			}

			List<PodcastSearchResult> hits = m_podcastManager.SearchPodcasts(query);
			List<PulsePodcast> pulsePodcasts = new List<PulsePodcast>();
			for (int index = 0; index < hits.Count; index++)
			{
				pulsePodcasts.Add(BuildSearchResultPodcast(hits[index]));
			}
			return RespondList(context, pulsePodcasts);
		}

		/// <summary>
		/// Maps a remote discovery hit to the PulsePodcast wire shape. Id is left
		/// empty (not in the catalogue yet) and CoverArt holds the remote artwork
		/// URL so the search UI can render it without a server-side cover.
		/// </summary>
		private PulsePodcast BuildSearchResultPodcast(PodcastSearchResult hit)
		{
			PulsePodcast pulsePodcast = new PulsePodcast();
			pulsePodcast.Id = "";
			pulsePodcast.Title = hit.Title;
			pulsePodcast.Author = hit.Author;
			pulsePodcast.Description = hit.Description;
			pulsePodcast.CoverArt = hit.ArtworkUrl;
			pulsePodcast.FeedUrl = hit.FeedUrl;
			pulsePodcast.Subscribed = false;
			pulsePodcast.EpisodeCount = 0;
			pulsePodcast.ItemCount = 0;
			return pulsePodcast;
		}

		/// <summary>
		/// <summary>
		/// Updates a podcast's backlog settings (retention policy + value,
		/// auto-download). Writes through PodcastManager.UpdatePodcastSettings
		/// which also kicks a background apply so changes take effect immediately.
		/// </summary>
		public IResult UpdatePodcast(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);
			Podcast series = m_podcastManager.GetPodcast(id);
			if (series == null)
			{
				return RespondStatus(context, "not_found");
			}

			string retentionRaw = QueryParameters.GetString(context, "retentionPolicy");
			eRetentionPolicy retention = eRetentionPolicy.KeepAll;
			bool retentionParsed = Enum.TryParse(retentionRaw, out retention);
			if (!retentionParsed)
			{
				retention = eRetentionPolicy.KeepAll;
			}

			string retentionValueRaw = QueryParameters.GetString(context, "retentionValue");
			int retentionValue = 0;
			int.TryParse(retentionValueRaw, out retentionValue);

			bool autoDownload = QueryParameters.GetBool(context, "autoDownload");

			m_podcastManager.UpdatePodcastSettings(id, retention, retentionValue, autoDownload);
			Podcast updated = m_podcastManager.GetPodcast(id);
			return RespondObject(context, updated.BuildPulsePodcast(m_podcastManager, userId));
		}

		public IResult SubscribePodcast(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);
			m_podcastManager.SetSubscribed(id, userId, true);
			return RespondStatus(context, "ok");
		}

		public IResult UnsubscribePodcast(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);
			m_podcastManager.SetSubscribed(id, userId, false);
			return RespondStatus(context, "ok");
		}

		public IResult EpisodeProgress(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);
			string positionRaw = QueryParameters.GetString(context, "positionSeconds");
			int positionSeconds = 0;
			int.TryParse(positionRaw, out positionSeconds);
			m_podcastManager.SaveProgress(id, userId, positionSeconds);
			return RespondStatus(context, "ok");
		}

		public IResult ChapterProgress(HttpContext context)
		{
			string id = QueryParameters.GetString(context, "id");
			string userId = QueryParameters.GetUserId(context);
			string positionRaw = QueryParameters.GetString(context, "positionSeconds");
			int positionSeconds = 0;
			int.TryParse(positionRaw, out positionSeconds);
			m_audiobookManager.SaveProgress(id, userId, positionSeconds);
			return RespondStatus(context, "ok");
		}


		public IResult IngestAnalytics(HttpContext context)
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


			//process our events into useful data
			string userID = batch.UserID;

			foreach (PulseAnalyticsEvent evt in batch.Events)
			{
				float secondsPlayed = evt.DurationMs / 1000.0f;
				string objectId = evt.ObjectId;
				ePulseWireType objectType = evt.ObjectType;
				switch (evt.Action)
				{
					case eAction.Play:
					{
						m_analyticsData.OnItemPlayed(userID, objectType, objectId, m_musicManager);
						break;
					}
					case eAction.Stop:
					{
						m_analyticsData.OnItemStopped(userID, objectType, objectId, secondsPlayed);
						break;
					}
				}
				
			}
			return Respond(context, new PulseResponse());
		}

	
		public IResult RecordAnalytic(HttpContext context)
		{
			string userId = QueryParameters.GetString(context, "uid");
			string objectId = QueryParameters.GetString(context, "id");
			string actionRaw = QueryParameters.GetString(context, "action");
			string typeRaw = QueryParameters.GetString(context, "type");
			string msRaw = QueryParameters.GetString(context, "ms");

			if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(objectId))
			{
				return RespondStatus(context, "missing_id");
			}

			eAction action = eAction.Invalid;
			Enum.TryParse<eAction>(actionRaw, out action);

			ePulseWireType objectType = ePulseWireType.Invalid;
			Enum.TryParse<ePulseWireType>(typeRaw, out objectType);

			long ms = 0;
			long.TryParse(msRaw, out ms);
			float secondsPlayed = ms / 1000.0f;

			switch (action)
			{
				case eAction.Play:
				{
					m_analyticsData.OnItemPlayed(userId, objectType, objectId, m_musicManager);
					break;
				}
				case eAction.Stop:
				{
					m_analyticsData.OnItemStopped(userId, objectType, objectId, secondsPlayed);
					break;
				}
			}

			return RespondStatus(context, "ok");
		}

		[Obsolete("Analytics path is gone, this is just so clients don't get upset")]
		public IResult GetAnalytics(HttpContext context)
		{
			string sessionId = QueryParameters.GetString(context, "session_id");
			string deviceId = QueryParameters.GetString(context, "device_id");

			if (!string.IsNullOrEmpty(sessionId))
			{
				string categoryFilter = QueryParameters.GetString(context, "category");
				string actionFilter = QueryParameters.GetString(context, "action");
				string resultFilter = QueryParameters.GetString(context, "result");
				List<PulseAnalyticsEvent> events = new List<PulseAnalyticsEvent>();
				AnalyticsEventsResponse response = new AnalyticsEventsResponse();
				response.SessionId = sessionId;
				response.Events = events;
				return Results.Text(PulseWire.Serialize(response), "application/json");
			}

			if (!string.IsNullOrEmpty(deviceId))
			{
				List<PulseAnalyticsSession> sessions = new List<PulseAnalyticsSession>();
				AnalyticsSessionsResponse response = new AnalyticsSessionsResponse();
				response.DeviceId = deviceId;
				response.Sessions = sessions;
				return Results.Text(PulseWire.Serialize(response), "application/json");
			}

			return RespondStatus(context, "missing_id");
		}

		/// <summary>
		/// Diagnostics intake. Clients POST a single PulseDiagnosticsEvent -- errors
		/// are not batched, each ships on its own so the one before a crash still
		/// gets out. The handler stamps received_at on the server clock and hands the
		/// record to DiagnosticsData (in-memory, persisted through PulseDataStore).
		/// </summary>
		public IResult PostIngestDiagnostics(HttpContext context)
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

			PulseDiagnosticsEvent diagnosticsEvent = PulseWire.Parse<PulseDiagnosticsEvent>(body);
			if (diagnosticsEvent == null)
			{
				return RespondStatus(context, "missing_body");
			}

			DiagnosticRecord record = new DiagnosticRecord();
			record.Id = Guid.NewGuid().ToString();
			record.DeviceId = diagnosticsEvent.DeviceId;
			record.SessionId = diagnosticsEvent.SessionId;
			record.AppVersion = diagnosticsEvent.AppVersion;
			record.BuildNumber = diagnosticsEvent.BuildNumber;
			record.User = diagnosticsEvent.User;
			record.Platform = diagnosticsEvent.Platform;
			record.OsVersion = diagnosticsEvent.OsVersion;
			record.DeviceModel = diagnosticsEvent.DeviceModel;
			record.NetworkType = diagnosticsEvent.NetworkType;
			record.Caller = diagnosticsEvent.Caller;
			record.MemberName = diagnosticsEvent.MemberName;
			record.ErrorMessage = diagnosticsEvent.ErrorMessage;
			record.Detail = diagnosticsEvent.Detail;
			record.Timestamp = diagnosticsEvent.Timestamp;

			m_diagnosticsData.Add(record);
			return Respond(context, new PulseResponse());
		}

		/// <summary>
		/// Diagnostics read endpoint. Returns recent diagnostic events newest-first
		/// by server received_at. Optional ?device_id=... filters to one device;
		/// ?limit=... caps the row count (default 200).
		/// </summary>
		public IResult GetDiagnostics(HttpContext context)
		{
			string deviceId = QueryParameters.GetString(context, "device_id");
			int limit = 200;
			string limitText = QueryParameters.GetString(context, "limit");
			bool indented = QueryParameters.GetBool(context, "indented", true);

			if (!string.IsNullOrEmpty(limitText))
			{
				int parsedLimit = 0;
				if (int.TryParse(limitText, out parsedLimit) && parsedLimit > 0)
				{
					limit = parsedLimit;
				}
			}

			List<DiagnosticRecord> records = m_diagnosticsData.GetRecent(deviceId, limit);
			DiagnosticsEventsResponse response = new DiagnosticsEventsResponse();
			response.Events = new List<PulseDiagnosticsEvent>();
			for (int index = 0; index < records.Count; index++)
			{
				DiagnosticRecord record = records[index];
				PulseDiagnosticsEvent evt = new PulseDiagnosticsEvent();
				evt.DeviceId = record.DeviceId;
				evt.SessionId = record.SessionId;
				evt.AppVersion = record.AppVersion;
				evt.BuildNumber = record.BuildNumber;
				evt.User = record.User;
				evt.Platform = record.Platform;
				evt.OsVersion = record.OsVersion;
				evt.DeviceModel = record.DeviceModel;
				evt.NetworkType = record.NetworkType;
				evt.Caller = record.Caller;
				evt.MemberName = record.MemberName;
				evt.ErrorMessage = record.ErrorMessage;
				evt.Detail = record.Detail;
				evt.Timestamp = record.Timestamp;
				response.Events.Add(evt);
			}

			string json = PulseWire.Serialize(response, indented);
			return Results.Text(json, "application/json");
		}

		private IResult GetStats(HttpContext context)
		{
			string userId = QueryParameters.GetUserId(context);

			List<TrackData> allTracks = m_musicManager.GetAllTracks();
			List<AlbumData> allAlbums = m_musicManager.GetAllAlbums();
			List<ArtistData> allArtists = m_musicManager.GetAllArtists();
			List<PlaylistData> allPlaylists = m_musicManager.GetAllPlaylists(userId);

			PulseStats stats = Data.PulseStatsBuilder.Build(allTracks, allAlbums, allArtists, allPlaylists, userId);

			return Respond(context, stats);
		}

		private IResult GetVersion(HttpContext context)
		{
			return Respond(context, new PulseVersion(PulseService.GetServerVersion()));
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

		/// <summary>
		/// Response envelope for the /diagnostics read query.
		/// </summary>
		public class DiagnosticsEventsResponse
		{
			public List<PulseDiagnosticsEvent> Events;
		}
	}
}
