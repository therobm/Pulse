using Microsoft.AspNetCore.Http;
using Pulse.MusicLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulse.Protocols
{
	public partial class PulseAPI
	{
		public IResult Ping(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			PulseAPI_Ping response = new PulseAPI_Ping();
			response.serverVersion = PulseService.GetServerVersion();
			return Results.Json(response);
		}

		public IResult GetArtists(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			List<PulseAPI_ArtistSummary> result = new List<PulseAPI_ArtistSummary>();
			for (int index = 0; index < allArtists.Count; index++)
			{
				ArtistInfo artist = allArtists[index];
				PulseAPI_ArtistSummary entry = new PulseAPI_ArtistSummary();
				entry.id = artist.Id;
				entry.name = artist.Name;
				entry.albumCount = artist.Albums.Count;
				entry.coverArt = "ar-" + artist.Id;
				result.Add(entry);
			}
			return Results.Json(result);
		}

		public IResult GetArtist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			ArtistInfo source = m_musicManager.GetArtist(id);
			if (source == null)
			{
				return Error("artist not found", 404);
			}

			PulseAPI_Artist response = new PulseAPI_Artist();
			response.id = source.Id;
			response.name = source.Name;
			response.coverArt = "ar-" + source.Id;
			response.albumCount = source.Albums.Count;
			for (int index = 0; index < source.Albums.Count; index++)
			{
				AlbumInfo album = source.Albums[index];
				PulseAPI_AlbumSummary entry = new PulseAPI_AlbumSummary();
				entry.id = album.Id;
				entry.name = album.Name;
				entry.year = album.Year;
				entry.coverArt = album.CoverArtId;
				response.albums.Add(entry);
			}
			return Results.Json(response);
		}

		public IResult GetAlbum(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			AlbumInfo source = m_musicManager.GetAlbum(id);
			if (source == null)
			{
				return Error("album not found", 404);
			}

			PulseAPI_Album response = new PulseAPI_Album();
			response.id = source.Id;
			response.name = source.Name;
			response.artistId = source.ArtistId;
			response.artistName = source.ArtistName;
			response.year = source.Year;
			response.coverArt = source.CoverArtId;
			for (int index = 0; index < source.Tracks.Count; index++)
			{
				response.tracks.Add(new PulseAPI_Track(source.Tracks[index]));
			}
			return Results.Json(response);
		}

		public IResult GetAlbums(HttpContext context)
		{
			string sort = context.Request.Query["sort"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return Error("not implemented", 501);
		}

		public IResult GetGenres(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			List<GenreInfo> genres = m_musicManager.GetAllGenres();
			List<PulseAPI_Genre> result = new List<PulseAPI_Genre>();
			for (int index = 0; index < genres.Count; index++)
			{
				GenreInfo g = genres[index];
				PulseAPI_Genre entry = new PulseAPI_Genre();
				entry.name = g.Name;
				entry.songCount = g.SongCount;
				entry.albumCount = g.AlbumCount;
				result.Add(entry);
			}
			return Results.Json(result);
		}

		public IResult GetGenre(HttpContext context)
		{
			string name = context.Request.Query["name"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 50);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return Error("not implemented", 501);
		}

		public IResult GetTrack(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			TrackInfo track = m_musicManager.GetTrack(id);
			if (track == null)
			{
				return Error("track not found", 404);
			}
			return Results.Json(new PulseAPI_Track(track));
		}

		public IResult GetTracks(HttpContext context)
		{
			string[] trackIDs = context.Request.Query["trackIDs"].ToArray();
			string u = context.Request.Query["u"].FirstOrDefault();

			List<PulseAPI_Track> result = new List<PulseAPI_Track>();
			for (int index = 0; index < trackIDs.Length; index++)
			{
				TrackInfo track = m_musicManager.GetTrack(trackIDs[index]);
				if (track == null)
				{
					continue;
				}
				result.Add(new PulseAPI_Track(track));
			}
			return Results.Json(result);
		}

		public IResult Search(HttpContext context)
		{
			string q = context.Request.Query["q"].FirstOrDefault();
			int artistCount = QueryParameters.GetInt(context, "artistCount", 20);
			int albumCount = QueryParameters.GetInt(context, "albumCount", 20);
			int songCount = QueryParameters.GetInt(context, "songCount", 20);
			int playlistCount = QueryParameters.GetInt(context, "playlistCount", 20);
			string u = context.Request.Query["u"].FirstOrDefault();
			return Error("not implemented", 501);
		}

		public IResult GetPlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			PlaylistInfo source = m_musicManager.GetPlaylist(id);
			if (source == null)
			{
				return Error("playlist not found", 404);
			}

			PulseAPI_Playlist response = new PulseAPI_Playlist();
			response.id = source.Id;
			response.name = source.Name;
			response.comment = source.Comment;
			response.songCount = source.GetSongCount();
			response.duration = source.DurationSeconds;
			response.coverArt = "pl-" + source.Id;
			response.lastPlayed = source.GetLastPlayed(u);

			List<TrackInfo> entries = m_musicManager.GetPlaylistTracks(id);
			for (int index = 0; index < entries.Count; index++)
			{
				response.tracks.Add(new PulseAPI_Track(entries[index]));
			}
			return Results.Json(response);
		}

		public IResult GetPlaylists(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(u);
			List<PulseAPI_PlaylistSummary> result = new List<PulseAPI_PlaylistSummary>();
			for (int index = 0; index < allPlaylists.Count; index++)
			{
				PlaylistInfo playlist = allPlaylists[index];
				PulseAPI_PlaylistSummary entry = new PulseAPI_PlaylistSummary();
				entry.id = playlist.Id;
				entry.name = playlist.Name;
				entry.comment = playlist.Comment;
				entry.songCount = playlist.GetSongCount();
				entry.duration = playlist.DurationSeconds;
				entry.coverArt = "pl-" + playlist.Id;
				entry.lastPlayed = playlist.GetLastPlayed(u);
				result.Add(entry);
			}
			return Results.Json(result);
		}

		public IResult CreatePlaylist(HttpContext context)
		{
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> songIds = context.Request.Query["songIds"].ToList();
			string u = context.Request.Query["u"].FirstOrDefault();
			return Error("not implemented", 501);
		}

		public IResult UpdatePlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> tracks = context.Request.Query["tracks"].ToList();
			string u = context.Request.Query["u"].FirstOrDefault();
			return Error("not implemented", 501);
		}

		public IResult DeletePlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(id))
			{
				return Error("missing id", 400);
			}
			PlaylistInfo existing = m_musicManager.GetPlaylist(id);
			if (existing == null)
			{
				return Error("playlist not found", 404);
			}
			m_musicManager.DeletePlaylist(id);
			return Ok();
		}

		public IResult GetFavorites(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return Error("not implemented", 501);
		}

		public IResult Favorite(HttpContext context)
		{
			string kind = context.Request.Query["kind"].FirstOrDefault();
			string id = context.Request.Query["id"].FirstOrDefault();
			bool starred = QueryParameters.GetBool(context, "starred", true);
			string u = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(kind))
			{
				return Error("missing u, id, or kind", 400);
			}

			string trackId = null;
			string albumId = null;
			string artistId = null;
			if (kind == "track") { trackId = id; }
			else if (kind == "album") { albumId = id; }
			else if (kind == "artist") { artistId = id; }
			else
			{
				return Error("unknown kind (track|album|artist)", 400);
			}

			m_musicManager.UpdateStar(u, trackId, albumId, artistId, starred);
			return Ok();
		}

		public IResult Stream(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return Error("not implemented", 501);
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
			/*
			if (m_coverArtCache.TryGetValue(id, out byte[] cached))
			{
				if (cached.Length == 0)
				{
					return Results.Bytes(m_defaultCoverArt, "image/png");
				}
				return Results.Bytes(cached, "image/jpeg");
			}*/

			if (sType == "playlist")
			{
				Log.Info(0, "Cover fro playlist");
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

		public IResult TrackAnalytics(HttpContext context)
		{
			string trackId = context.Request.Query["trackId"].FirstOrDefault();
			string phase = context.Request.Query["event"].FirstOrDefault();
			int elapsedSeconds = QueryParameters.GetInt(context, "elapsedSeconds", 0);
			string u = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(trackId) || string.IsNullOrEmpty(u))
			{
				return Error("missing trackId or u", 400);
			}

			TrackInfo track = m_musicManager.GetTrack(trackId);
			if (track == null)
			{
				return Error("track not found", 404);
			}

			// OnTrackStreamed currently only flips the now-playing pointer and
			// derives complete-vs-skip from the elapsed time on the *previous*
			// track. `phase` and `elapsedSeconds` from the client are accepted on
			// the wire for the future explicit-phase model but aren't used yet.
			m_musicManager.OnTrackStreamed(u, trackId);
			return Ok();
		}

	
		private static IResult Ok()
		{
			return Results.Json(new PulseAPI_OkResult());
		}

		private static IResult Error(string message, int statusCode)
		{
			PulseAPI_Error body = new PulseAPI_Error();
			body.error = message;
			return Results.Json(body, statusCode: statusCode);
		}
	}
}
