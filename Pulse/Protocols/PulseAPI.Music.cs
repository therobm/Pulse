using Microsoft.AspNetCore.Http;
using Pulse.MusicLibrary;
using System.Collections.Generic;
using System.Linq;

namespace Pulse.Protocols
{
	public partial class PulseAPI
	{
		public IResult Ping(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return Results.Json(new { ok = true, serverVersion = PulseService.GetServerVersion() });
		}

		public IResult GetArtists(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			List<object> result = new List<object>();
			for (int index = 0; index < allArtists.Count; index++)
			{
				ArtistInfo artist = allArtists[index];
				result.Add(new
				{
					id = artist.Id,
					name = artist.Name,
					albumCount = artist.Albums.Count,
					coverArt = "ar-" + artist.Id
				});
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
				return Results.Json(new { error = "artist not found" }, statusCode: 404);
			}

			List<object> albums = new List<object>();
			for (int index = 0; index < source.Albums.Count; index++)
			{
				AlbumInfo album = source.Albums[index];
				albums.Add(new
				{
					id = album.Id,
					name = album.Name,
					year = album.Year,
					coverArt = album.CoverArtId
				});
			}

			return Results.Json(new
			{
				id = source.Id,
				name = source.Name,
				coverArt = "ar-" + source.Id,
				albums = albums
			});
		}

		public IResult GetAlbum(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			AlbumInfo source = m_musicManager.GetAlbum(id);
			if (source == null)
			{
				return Results.Json(new { error = "album not found" }, statusCode: 404);
			}

			List<object> tracks = new List<object>();
			for (int index = 0; index < source.Tracks.Count; index++)
			{
				TrackInfo track = source.Tracks[index];
				tracks.Add(new
				{
					id = track.Id,
					title = track.Title,
					trackNumber = track.TrackNumber,
					discNumber = track.DiscNumber,
					duration = track.DurationSeconds,
					coverArt = track.CoverArtId
				});
			}

			return Results.Json(new
			{
				id = source.Id,
				name = source.Name,
				artistId = source.ArtistId,
				artistName = source.ArtistName,
				year = source.Year,
				coverArt = source.CoverArtId,
				tracks = tracks
			});
		}

		public IResult GetAlbums(HttpContext context)
		{
			string sort = context.Request.Query["sort"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult GetGenres(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();

			Dictionary<string, (int songs, int albums)> data = GetGenresData();
			List<object> result = new List<object>();
			foreach (KeyValuePair<string, (int songs, int albums)> entry in data)
			{
				result.Add(new
				{
					name = entry.Key,
					songCount = entry.Value.songs,
					albumCount = entry.Value.albums
				});
			}
			return Results.Json(result);
		}

		// Genre aggregation shared with Subsonic.HandleGetGenres -- the loop over
		// every album to count songs and albums per genre lives here so the two
		// protocol surfaces stay consistent and we only walk the library once
		// worth of code.
		internal Dictionary<string, (int songs, int albums)> GetGenresData()
		{
			Dictionary<string, (int songs, int albums)> map = new Dictionary<string, (int songs, int albums)>();
			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			for (int index = 0; index < allAlbums.Count; index++)
			{
				AlbumInfo album = allAlbums[index];
				if (string.IsNullOrEmpty(album.Genre))
				{
					continue;
				}
				(int songs, int albums) entry;
				if (!map.TryGetValue(album.Genre, out entry))
				{
					entry = (0, 0);
				}
				map[album.Genre] = (entry.songs + album.Tracks.Count, entry.albums + 1);
			}
			return map;
		}

		public IResult GetGenre(HttpContext context)
		{
			string name = context.Request.Query["name"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 50);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult GetTrack(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			TrackInfo track = m_musicManager.GetTrack(id);
			if (track == null)
			{
				return Results.Json(new { error = "track not found" }, statusCode: 404);
			}

			return Results.Json(new
			{
				id = track.Id,
				title = track.Title,
				artist = track.Artist,
				artistId = track.ArtistId,
				album = track.Album,
				albumId = track.AlbumId,
				duration = track.DurationSeconds,
				coverArt = track.CoverArtId,
				trackNumber = track.TrackNumber,
				discNumber = track.DiscNumber,
				year = track.Year
			});
		}

		public IResult GetTracks(HttpContext context)
		{
			string[] trackIDs = context.Request.Query["trackIDs"].ToArray();
			string u = context.Request.Query["u"].FirstOrDefault();

			List<object> result = new List<object>();
			for (int index = 0; index < trackIDs.Length; index++)
			{
				TrackInfo track = m_musicManager.GetTrack(trackIDs[index]);
				if (track == null)
				{
					continue;
				}
				result.Add(new
				{
					id = track.Id,
					title = track.Title,
					artist = track.Artist,
					artistId = track.ArtistId,
					album = track.Album,
					albumId = track.AlbumId,
					duration = track.DurationSeconds,
					coverArt = track.CoverArtId
				});
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
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult GetPlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			PlaylistInfo source = m_musicManager.GetPlaylist(id);
			if (source == null)
			{
				return Results.Json(new { error = "playlist not found" }, statusCode: 404);
			}

			List<object> tracks = new List<object>();
			List<TrackInfo> entries = m_musicManager.GetPlaylistTracks(id);
			for (int index = 0; index < entries.Count; index++)
			{
				TrackInfo track = entries[index];
				tracks.Add(new
				{
					id = track.Id,
					title = track.Title,
					artist = track.Artist,
					album = track.Album,
					duration = track.DurationSeconds,
					coverArt = track.CoverArtId
				});
			}

			return Results.Json(new
			{
				id = source.Id,
				name = source.Name,
				comment = source.Comment,
				songCount = source.GetSongCount(),
				duration = source.DurationSeconds,
				coverArt = "pl-" + source.Id,
				lastPlayed = source.GetLastPlayed(u),
				tracks = tracks
			});
		}

		public IResult GetPlaylists(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(u);
			List<object> result = new List<object>();
			for (int index = 0; index < allPlaylists.Count; index++)
			{
				PlaylistInfo playlist = allPlaylists[index];
				result.Add(new
				{
					id = playlist.Id,
					name = playlist.Name,
					comment = playlist.Comment,
					songCount = playlist.GetSongCount(),
					duration = playlist.DurationSeconds,
					coverArt = "pl-" + playlist.Id,
					lastPlayed = playlist.GetLastPlayed(u)
				});
			}
			return Results.Json(result);
		}

		public IResult CreatePlaylist(HttpContext context)
		{
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> songIds = context.Request.Query["songIds"].ToList();
			string u = context.Request.Query["u"].FirstOrDefault();
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult UpdatePlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> tracks = context.Request.Query["tracks"].ToList();
			string u = context.Request.Query["u"].FirstOrDefault();
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult DeletePlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(id))
			{
				return Results.Json(new { error = "missing id" }, statusCode: 400);
			}
			PlaylistInfo existing = m_musicManager.GetPlaylist(id);
			if (existing == null)
			{
				return Results.Json(new { error = "playlist not found" }, statusCode: 404);
			}
			m_musicManager.DeletePlaylist(id);
			return Results.Json(new { ok = true });
		}

		public IResult GetFavorites(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult Favorite(HttpContext context)
		{
			string kind = context.Request.Query["kind"].FirstOrDefault();
			string id = context.Request.Query["id"].FirstOrDefault();
			bool starred = QueryParameters.GetBool(context, "starred", true);
			string u = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(kind))
			{
				return Results.Json(new { error = "missing u, id, or kind" }, statusCode: 400);
			}

			string trackId = null;
			string albumId = null;
			string artistId = null;
			if (kind == "track") { trackId = id; }
			else if (kind == "album") { albumId = id; }
			else if (kind == "artist") { artistId = id; }
			else
			{
				return Results.Json(new { error = "unknown kind (track|album|artist)" }, statusCode: 400);
			}

			m_musicManager.UpdateStar(u, trackId, albumId, artistId, starred);
			return Results.Json(new { ok = true });
		}

		public IResult Stream(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult GetCoverArt(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 0);
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult TrackAnalytics(HttpContext context)
		{
			string trackId = context.Request.Query["trackId"].FirstOrDefault();
			string phase = context.Request.Query["event"].FirstOrDefault();
			int elapsedSeconds = QueryParameters.GetInt(context, "elapsedSeconds", 0);
			string u = context.Request.Query["u"].FirstOrDefault();

			if (string.IsNullOrEmpty(trackId) || string.IsNullOrEmpty(u))
			{
				return Results.Json(new { error = "missing trackId or u" }, statusCode: 400);
			}

			TrackInfo track = m_musicManager.GetTrack(trackId);
			if (track == null)
			{
				return Results.Json(new { error = "track not found" }, statusCode: 404);
			}

			// OnTrackStreamed currently only flips the now-playing pointer and
			// derives complete-vs-skip from the elapsed time on the *previous*
			// track. `phase` and `elapsedSeconds` from the client are accepted on
			// the wire for the future explicit-phase model but aren't used yet.
			m_musicManager.OnTrackStreamed(u, trackId);
			return Results.Json(new { ok = true, phase = phase });
		}
	}
}
