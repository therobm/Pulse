

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

		protected IResult CreateResponse(PulseInfo content)
		{
			PulseResponse response = new PulseResponse();
			response.content = content;
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
			return Results.Json(track);
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
					result.Artists.Add(allArtists[index]);
					artistHits++;
				}
			}


			int albumHits = 0;
			for (int index = 0; index < allAlbums.Count && albumHits < albumCount; index++)
			{
				if (allAlbums[index].Name.ToLowerInvariant().Contains(lowerQuery))
				{
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
						result.Albums.Add(track);
						songHits++;
					}
				}
			}
			return CreateResponse(result);
		}

		public IResult GetFavorites(HttpContext context) { }
		public IResult GetTopTracks(HttpContext context) { }
		public IResult Favorite(HttpContext context) { }
		public IResult Unfavorite(HttpContext context) { }
		public IResult ReportTrackAnalytics(HttpContext context) { }

		public IResult GetArtists(HttpContext context) { }
		public IResult GetArtist(HttpContext context) { }

		public IResult GetAlbums(HttpContext context) { }
		public IResult GetAlbum(HttpContext context) { }

		public IResult GetGenres(HttpContext context) { }
		public IResult GetGenreTracks(HttpContext context) { }

		public IResult GetPlaylists(HttpContext context) { }
		public IResult GetPlaylist(HttpContext context) { }
		public IResult CreatePlaylist(HttpContext context) { }
		public IResult UpdatePlaylist(HttpContext context) { }
		public IResult DeletePlaylist(HttpContext context) { }


	}
}

