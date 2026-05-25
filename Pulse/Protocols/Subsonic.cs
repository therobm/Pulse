
using Microsoft.AspNetCore.Http;
using Pulse.Data;
using Pulse.MusicLibrary;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulse.SubsonicService
{
	public class Subsonic
	{
		private static JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		private ConcurrentDictionary<string, byte[]> m_coverArtCache = new ConcurrentDictionary<string, byte[]>();
		private byte[] m_placeholder = new byte[] { };
		private byte[] m_defaultCoverArt;
		PulseService m_pulseService;
		MusicManager m_musicManager;
		public Subsonic(PulseService pulse, MusicManager musicManager)
		{
			m_pulseService = pulse;
			m_musicManager = musicManager;
			string pulseLogoPath = Path.Combine(AppContext.BaseDirectory, "Content", "Media", "pulseLogo.png");
			m_defaultCoverArt = File.ReadAllBytes(pulseLogoPath);
			m_placeholder = File.ReadAllBytes(pulseLogoPath);
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

		/// <summary>
		/// This is not a subsonic api route
		/// TODO port 100% of this stuff to PulseAPI and leave Subsonic as a dumb wrapper
		/// </summary>
		/// <param name="context"></param>
		/// <returns></returns>
		public IResult HandlePlayRandom(HttpContext context)
		{
			string artistId = context.Request.Query["artistId"].FirstOrDefault();
			string albumId = context.Request.Query["albumId"].FirstOrDefault();
			string genre = context.Request.Query["genre"].FirstOrDefault(); 
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

			List<TrackInfo> candidates = new List<TrackInfo>();

			if (!string.IsNullOrEmpty(albumId))
			{
				AlbumInfo album = m_musicManager.GetAlbum(albumId);
				if (album != null)
				{
					candidates.AddRange(album.Tracks);
				}
			}
			else if (!string.IsNullOrEmpty(artistId))
			{
				ArtistInfo artist = m_musicManager.GetArtist(artistId);
				if (artist != null)
				{
					for (int index = 0; index < artist.Albums.Count; index++)
					{
						candidates.AddRange(artist.Albums[index].Tracks);
					}
				}
			}
			else if (!string.IsNullOrEmpty(genre))
			{
				string lowerGenre = genre.ToLowerInvariant();
				List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					if (allAlbums[index].Genre.ToLowerInvariant().Contains(lowerGenre))
					{
						candidates.AddRange(allAlbums[index].Tracks);
					}
				}
			}
			else
			{
				List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
				for (int index = 0; index < allAlbums.Count; index++)
				{
					candidates.AddRange(allAlbums[index].Tracks);
				}
			}

			if (candidates.Count == 0)
			{
				return Respond(context, CreateErrorResponse(70, "No matching tracks found"));
			}

			Random rng = new Random();
			TrackInfo track = candidates[rng.Next(candidates.Count)];

			SubsonicResponseBody body = CreateResponse();
			body.song = new SongID3(user, track);
			return Respond(context, body);
		}

		public IResult HandleStream(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			TrackInfo track = m_musicManager.GetTrack(id);

			if (track == null)
			{
				return Respond(context, CreateErrorResponse(70, "Song not found"));
			}

			FileStream fileStream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			return Results.File(fileStream, track.ContentType, enableRangeProcessing: true);
		}

		public IResult HandlePing(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			return Respond(context, body);
		}

		public IResult HandleGetSong(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			TrackInfo track = m_musicManager.GetTrack(id);

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

			byte[] cached;
			if (m_coverArtCache.TryGetValue(id, out cached))
			{
				if (cached.Length == 0)
				{
					return Results.Bytes(m_defaultCoverArt, "image/png");
				}
				return Results.Bytes(cached, "image/jpeg");
			}

			// Playlist composite art -- generated on demand from the first few
			// distinct album covers in the playlist, served (and cached) like
			// any other cover. Stable id format is "pl-<playlistId>".
			if (!string.IsNullOrEmpty(id) && id.StartsWith("pl-"))
			{
				return HandlePlaylistCompositeCover(id);
			}

			AlbumInfo album = m_musicManager.GetAlbum(id);
			byte[] albumBytes;
			string albumContentType;
			if (album != null && TryGetAlbumCoverBytes(album, out albumBytes, out albumContentType))
			{
				m_coverArtCache[id] = albumBytes;
				return Results.Bytes(albumBytes, albumContentType);
			}

			if (album == null)
			{
				m_coverArtCache[id] = m_defaultCoverArt;
				return Results.Bytes(m_defaultCoverArt, "image/png");
			}

			m_coverArtCache[id] = m_placeholder;
			return Results.Bytes(m_placeholder, "image/png");
		}

		/// <summary>
		/// Resolves cover bytes for an album: embedded ID3 art first, then
		/// folder art (cover.jpg / folder.png / etc). Used by HandleGetCoverArt
		/// for direct album requests and by the playlist composite generator.
		/// </summary>
		private bool TryGetAlbumCoverBytes(AlbumInfo album, out byte[] bytes, out string contentType)
		{
			bytes = null;
			contentType = "image/jpeg";
			if (album == null || album.Tracks.Count == 0)
			{
				return false;
			}

			for (int index = 0; index < album.Tracks.Count; index++)
			{
				try
				{
					TagLib.File tagFile = TagLib.File.Create(album.Tracks[index].FilePath);
					if (tagFile.Tag.Pictures.Length > 0)
					{
						bytes = tagFile.Tag.Pictures[0].Data.Data;
						tagFile.Dispose();
						return true;
					}
					tagFile.Dispose();
				}
				catch (Exception ex)
				{
					Log.Error(-1, "TryGetAlbumCoverBytes: failed to read embedded art - " + ex.Message);
				}
			}

			string albumDir = Path.GetDirectoryName(album.Tracks[0].FilePath);
			string[] artFileNames = new string[] { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png", "album.jpg", "album.png" };
			for (int artIndex = 0; artIndex < artFileNames.Length; artIndex++)
			{
				string artPath = Path.Combine(albumDir, artFileNames[artIndex]);
				if (File.Exists(artPath))
				{
					bytes = File.ReadAllBytes(artPath);
					if (artPath.EndsWith(".png"))
					{
						contentType = "image/png";
					}
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Build (or return cached) composite cover art for a playlist. Picks
		/// the first 4 distinct album covers from the playlist's tracks and
		/// tiles them into a single 600x600 JPEG. Falls back to a single tile
		/// if fewer covers are available; falls back to the default placeholder
		/// if none are. Cached under the "pl-<id>" key so subsequent hits are
		/// the same fast path as album covers.
		/// </summary>
		private IResult HandlePlaylistCompositeCover(string coverId)
		{
			string playlistId = coverId.Substring(3);
			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null || playlist.TrackIds.Count == 0)
			{
				m_coverArtCache[coverId] = m_defaultCoverArt;
				return Results.Bytes(m_defaultCoverArt, "image/png");
			}

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
				byte[] albumBytes;
				string albumContentType;
				if (TryGetAlbumCoverBytes(album, out albumBytes, out albumContentType))
				{
					tileBytes.Add(albumBytes);
				}
			}

			if (tileBytes.Count == 0)
			{
				m_coverArtCache[coverId] = m_defaultCoverArt;
				return Results.Bytes(m_defaultCoverArt, "image/png");
			}

			try
			{
				byte[] composed = ComposeTiledImage(tileBytes, 600);
				m_coverArtCache[coverId] = composed;
				return Results.Bytes(composed, "image/jpeg");
			}
			catch (Exception ex)
			{
				Log.Error(-1, "HandlePlaylistCompositeCover: failed to compose - " + ex.Message);
				// Cache the first tile so we don't keep retrying composition.
				m_coverArtCache[coverId] = tileBytes[0];
				return Results.Bytes(tileBytes[0], "image/jpeg");
			}
		}

		/// <summary>
		/// Compose 1, 2, or 4 source images into a single square JPEG of the
		/// requested size. 1 image = full size; 2 = side-by-side halves;
		/// 3 or 4 = 2x2 grid (with last cell blank if only 3). Source images
		/// are stretched to fill their cell.
		/// </summary>
		private static byte[] ComposeTiledImage(List<byte[]> tiles, int size)
		{
			int tileCount = tiles.Count;
			using (System.Drawing.Bitmap canvas = new System.Drawing.Bitmap(size, size))
			{
				using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(canvas))
				{
					graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
					graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
					graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
					graphics.Clear(System.Drawing.Color.Black);

					if (tileCount == 1)
					{
						DrawTile(graphics, tiles[0], 0, 0, size, size);
					}
					else if (tileCount == 2)
					{
						int half = size / 2;
						DrawTile(graphics, tiles[0], 0, 0, half, size);
						DrawTile(graphics, tiles[1], half, 0, size - half, size);
					}
					else
					{
						int half = size / 2;
						DrawTile(graphics, tiles[0], 0, 0, half, half);
						DrawTile(graphics, tiles[1], half, 0, size - half, half);
						DrawTile(graphics, tiles[2], 0, half, half, size - half);
						if (tileCount >= 4)
						{
							DrawTile(graphics, tiles[3], half, half, size - half, size - half);
						}
					}
				}

				using (MemoryStream output = new MemoryStream())
				{
					System.Drawing.Imaging.ImageCodecInfo jpegCodec = GetJpegCodec();
					System.Drawing.Imaging.EncoderParameters encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
					encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
					canvas.Save(output, jpegCodec, encoderParams);
					return output.ToArray();
				}
			}
		}

		private static void DrawTile(System.Drawing.Graphics graphics, byte[] imageBytes, int x, int y, int width, int height)
		{
			using (MemoryStream source = new MemoryStream(imageBytes))
			{
				using (System.Drawing.Image tile = System.Drawing.Image.FromStream(source))
				{
					graphics.DrawImage(tile, new System.Drawing.Rectangle(x, y, width, height));
				}
			}
		}

		private static System.Drawing.Imaging.ImageCodecInfo GetJpegCodec()
		{
			System.Drawing.Imaging.ImageCodecInfo[] codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
			for (int idx = 0; idx < codecs.Length; idx++)
			{
				if (codecs[idx].FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid)
				{
					return codecs[idx];
				}
			}
			return null;
		}

		public IResult HandleGetIndexes(HttpContext context)
		{
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			Dictionary<string, List<ArtistID3>> grouped = new Dictionary<string, List<ArtistID3>>();

			for (int i = 0; i < allArtists.Count; i++)
			{
				ArtistInfo artist = allArtists[i];
				string firstChar = artist.Name.Substring(0, 1).ToUpperInvariant();
				if (!char.IsLetter(firstChar[0]))
				{
					firstChar = "#";
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

			SubsonicResponseBody body = new SubsonicResponseBody();
			body.Indexes = new IndexesContainer();
			body.Indexes.Index = indexList;

			return Results.Json(new SubsonicWrapper { response = body });
		}

		public IResult HandleGetInternetRadioStations(HttpContext context)
		{
			SubsonicResponseBody body = new SubsonicResponseBody();
			body.InternetRadioStations = new InternetRadioStationsContainer();
			body.InternetRadioStations.InternetRadioStation = new List<object>();
			return Results.Json(new SubsonicWrapper { response = body });
		}

		public IResult HandleSearch3(HttpContext context)
		{
			string query = context.Request.Query["query"].FirstOrDefault() ?? "";
			query = query.Trim('"');

			string user = context.Request.Query["u"].FirstOrDefault();

			int artistCount = int.Parse(context.Request.Query["artistCount"].FirstOrDefault() ?? "20");
			int albumCount = int.Parse(context.Request.Query["albumCount"].FirstOrDefault() ?? "20");
			int songCount = int.Parse(context.Request.Query["songCount"].FirstOrDefault() ?? "20");

			int artistOffset = int.Parse(context.Request.Query["artistOffset"].FirstOrDefault() ?? "0");
			int albumOffset = int.Parse(context.Request.Query["albumOffset"].FirstOrDefault() ?? "0");
			int songOffset = int.Parse(context.Request.Query["songOffset"].FirstOrDefault() ?? "0");

			SubsonicResponseBody body = CreateResponse();
			body.searchResult3 = new SearchResult3();

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();

			if (string.IsNullOrEmpty(query))
			{
				int artistEnd = Math.Min(artistOffset + artistCount, allArtists.Count);
				for (int index = artistOffset; index < artistEnd; index++)
				{
					ArtistID3 entry = new ArtistID3(allArtists[index]);
					body.searchResult3.artist.Add(entry);
				}

				int albumEnd = Math.Min(albumOffset + albumCount, allAlbums.Count);
				for (int index = albumOffset; index < albumEnd; index++)
				{
					AlbumID3 entry = new AlbumID3(allAlbums[index]);
					body.searchResult3.album.Add(entry);
				}

				int songsSeen = 0;
				for (int albumIndex = 0; albumIndex < allAlbums.Count && body.searchResult3.song.Count < songCount; albumIndex++)
				{
					List<TrackInfo> tracks = allAlbums[albumIndex].Tracks;
					for (int trackIndex = 0; trackIndex < tracks.Count && body.searchResult3.song.Count < songCount; trackIndex++)
					{
						if (songsSeen < songOffset)
						{
							songsSeen++;
							continue;
						}

						TrackInfo track = tracks[trackIndex];
						SongID3 entry = new SongID3(user, track);

						body.searchResult3.song.Add(entry);
					}
				}

				return Respond(context, body);
			}

			string lowerQuery = query.ToLowerInvariant();

			
			int artistHits = 0;
			for (int index = 0; index < allArtists.Count && artistHits < artistCount; index++)
			{
				if (allArtists[index].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					ArtistID3 entry = new ArtistID3(allArtists[index]);
					body.searchResult3.artist.Add(entry);
					artistHits++;
				}
			}

			
			int albumHits = 0;
			for (int index = 0; index < allAlbums.Count && albumHits < albumCount; index++)
			{
				if (allAlbums[index].Name.ToLowerInvariant().Contains(lowerQuery))
				{
					AlbumID3 entry = new AlbumID3(allAlbums[index]);
					body.searchResult3.album.Add(entry);
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
						SongID3 entry = new SongID3(user, track);
						body.searchResult3.song.Add(entry);
						songHits++;
					}
				}
			}

			return Respond(context, body);
		}

		public IResult HandleGetStarred(HttpContext context)
		{
			string userName = context.Request.Query["u"].FirstOrDefault() ?? "";

			SubsonicResponseBody body = CreateResponse();
			body.starred = new StarredContainer();

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			for (int albumIndex = 0; albumIndex < allAlbums.Count; albumIndex++)
			{
				AlbumInfo album = allAlbums[albumIndex];
				List<TrackInfo> tracks = album.Tracks;
				for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
				{
					TrackInfo track = tracks[trackIndex];
					if (track.Starred.ContainsKey(userName) && track.Starred[userName])
					{
						body.starred.song.Add(new SongID3(userName, track));
					}
				}
			}

			return Respond(context, body);
		}

		public IResult HandleGetStarred2(HttpContext context)
		{
			string userName = context.Request.Query["u"].FirstOrDefault() ?? "";

			SubsonicResponseBody body = CreateResponse();
			body.starred2 = new StarredContainer();

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int index = 0; index < allArtists.Count; index++)
			{
				ArtistInfo artist = allArtists[index];
				if (artist.Starred.ContainsKey(userName) && artist.Starred[userName])
				{
					ArtistID3 entry = new ArtistID3(artist);
					body.starred2.artist.Add(entry);
				}
			}

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			for (int albumIndex = 0; albumIndex < allAlbums.Count; albumIndex++)
			{
				AlbumInfo album = allAlbums[albumIndex];
				if (album.Starred.ContainsKey(userName) && album.Starred[userName])
				{
					AlbumID3 entry = new AlbumID3(album);
					body.starred2.album.Add(entry);
				}

				for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
				{
					TrackInfo track = album.Tracks[trackIndex];
					if (track.Starred.ContainsKey(userName) && track.Starred[userName])
					{
						body.starred2.song.Add(new SongID3(userName, track));
					}
				}
			}

			return Respond(context, body);
		}


		public IResult HandleGetTopSongs(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.topSongs = new TopSongsContainer();
			return Respond(context, body);
		}

		public IResult HandleGetArtistInfo(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.artistInfo2 = new ArtistInfo2();
			return Respond(context, body);
		}

		public IResult HandleSetRating(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int rating = int.Parse(context.Request.Query["rating"].FirstOrDefault() ?? "0");

			if (string.IsNullOrEmpty(id))
			{
				return Respond(context, CreateErrorResponse(10, "Missing id parameter"));
			}

			if (rating < 0 || rating > 5)
			{
				return Respond(context, CreateErrorResponse(10, "Rating must be between 0 and 5"));
			}

			m_musicManager.SetRating(id, rating);

			SubsonicResponseBody body = CreateResponse();
			return Respond(context, body);
		}

		public IResult HandleStar(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string albumId = context.Request.Query["albumId"].FirstOrDefault();
			string artistId = context.Request.Query["artistId"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			m_musicManager.UpdateStar(user, id, albumId, artistId, true);

			SubsonicResponseBody body = CreateResponse();
			return Respond(context, body);
		}

	
		public IResult HandleUnstar(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string albumId = context.Request.Query["albumId"].FirstOrDefault();
			string artistId = context.Request.Query["artistId"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			m_musicManager.UpdateStar(user, id, albumId, artistId, false);

			SubsonicResponseBody body = CreateResponse();
			return Respond(context, body);
		}

		public IResult HandleScrobble(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();
			string submissionParam = context.Request.Query["submission"].FirstOrDefault();
			string timeParam = context.Request.Query["time"].FirstOrDefault();
			string clientParam = context.Request.Query["c"].FirstOrDefault();

			bool submission = (submissionParam ?? "true") != "false";

			// The Subsonic API's "submission=true" intent (after-play scrobble) doesn't align with Pulse's scoring model.
			// We intentionally REJECT the explicit submit and use the !submission (track-start) call instead, because that
			// is the signal 3rd-party clients give us that maps cleanly to Pulse's "served to user" play metric. Do not
			// flip this condition.
			if (!submission)//string.IsNullOrEmpty(id) && submission)
			{
				Log.Info(-1, "Scrobble: id=" + id + " user=" + user + " submission=" + submission + " time=" + timeParam + " client=" + clientParam + " raw_submission=" + submissionParam);

				m_musicManager.OnTrackStreamed(user, id);
			}
			SubsonicResponseBody body = CreateResponse();
			return Respond(context, body);
		}
		public IResult HandleGetLicense(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.license = new LicenseInfo();
			return Respond(context, body);
		}
		public IResult HandleGetArtists(HttpContext context)
		{
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();

			SubsonicResponseBody body = CreateResponse();
			body.artists = new ArtistsContainer();

			Dictionary<string, ArtistIndex> indexMap = new Dictionary<string, ArtistIndex>();

			for (int index = 0; index < allArtists.Count; index++)
			{
				ArtistInfo source = allArtists[index];
				string firstChar = source.Name.Substring(0, 1).ToUpperInvariant();
				if (!char.IsLetter(firstChar[0]))
				{
					firstChar = "#";
				}

				ArtistIndex artistIndex;
				if (!indexMap.TryGetValue(firstChar, out artistIndex))
				{
					artistIndex = new ArtistIndex();
					artistIndex.name = firstChar;
					indexMap[firstChar] = artistIndex;
					body.artists.index.Add(artistIndex);
				}

				ArtistID3 artistEntry = new ArtistID3(source);
				artistIndex.artist.Add(artistEntry);
			}

			return Respond(context, body);
		}

		public IResult HandleGetArtist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			ArtistInfo source = m_musicManager.GetArtist(id);

			if (source == null)
			{
				return Respond(context, CreateErrorResponse(70, "Artist not found"));
			}

			SubsonicResponseBody body = CreateResponse();
			body.artist = new ArtistWithAlbumsID3();
			body.artist.id = source.Id;
			body.artist.name = source.Name;
			body.artist.albumCount = source.Albums.Count;

			for (int index = 0; index < source.Albums.Count; index++)
			{
				AlbumInfo albumSource = source.Albums[index];

				AlbumID3 album = new AlbumID3(albumSource);
				body.artist.album.Add(album);
			}

			return Respond(context, body);
		}

		public IResult HandleGetAlbum(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			AlbumInfo source = m_musicManager.GetAlbum(id);

			if (source == null)
			{
				return Respond(context, CreateErrorResponse(70, "Album not found"));
			}

			string user = context.Request.Query["u"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			body.album = new AlbumWithSongsID3();
			body.album.id = source.Id;
			body.album.name = source.Name;
			body.album.artist = source.ArtistName;
			body.album.artistId = source.ArtistId;
			body.album.songCount = source.Tracks.Count;
			body.album.coverArt = source.CoverArtId;
			body.album.year = source.Year;
			body.album.genre = source.Genre;

			List<TrackInfo> orderedTracks = new List<TrackInfo>(source.Tracks);
			orderedTracks.Sort(CompareTrackByDiscThenNumber);

			long albumDuration = 0;
			for (int index = 0; index < orderedTracks.Count; index++)
			{
				TrackInfo trackSource = orderedTracks[index];

				SongID3 song = new SongID3(user, trackSource);

				body.album.song.Add(song);
				albumDuration = albumDuration + trackSource.DurationSeconds;
			}
			body.album.duration = (int)albumDuration;

			return Respond(context, body);
		}
		public IResult HandleGetAlbumList2(HttpContext context)
		{
			string type = context.Request.Query["type"].FirstOrDefault() ?? "random";
			int size = int.Parse(context.Request.Query["size"].FirstOrDefault() ?? "20");
			int offset = int.Parse(context.Request.Query["offset"].FirstOrDefault() ?? "0");

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();

			if (type == "newest")
			{
				allAlbums.Sort(CompareAlbumYearDescending);
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
			// "frequent", "recent", "byYear" all just return whatever we have for now

			SubsonicResponseBody body = CreateResponse();
			body.albumList2 = new AlbumList2();

			int end = Math.Min(offset + size, allAlbums.Count);
			for (int index = offset; index < end; index++)
			{
				AlbumInfo source = allAlbums[index];

				AlbumID3 album = new AlbumID3(source);
				body.albumList2.album.Add(album);
			}

			return Respond(context, body);
		}

		public IResult HandleGetGenres(HttpContext context)
		{
			Dictionary<string, GenreEntry> genreMap = new Dictionary<string, GenreEntry>();

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			for (int index = 0; index < allAlbums.Count; index++)
			{
				AlbumInfo album = allAlbums[index];
				if (string.IsNullOrEmpty(album.Genre))
				{
					continue;
				}

				GenreEntry entry;
				if (!genreMap.TryGetValue(album.Genre, out entry))
				{
					entry = new GenreEntry();
					entry.value = album.Genre;
					genreMap[album.Genre] = entry;
				}
				entry.albumCount = entry.albumCount + 1;
				entry.songCount = entry.songCount + album.Tracks.Count;
			}

			SubsonicResponseBody body = CreateResponse();
			body.genres = new GenresContainer();
			body.genres.genre = new List<GenreEntry>(genreMap.Values);

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

			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");
			int offset = int.Parse(context.Request.Query["offset"].FirstOrDefault() ?? "0");

			List<TrackInfo> matches = new List<TrackInfo>();
			List<TrackInfo> allTracks = m_musicManager.GetAllTracks();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackInfo track = allTracks[index];
				if (!string.IsNullOrEmpty(track.Genre) && string.Equals(track.Genre, genre, StringComparison.OrdinalIgnoreCase))
				{
					matches.Add(track);
				}
			}

			SubsonicResponseBody body = CreateResponse();
			body.songsByGenre = new SongsByGenreContainer();

			int end = Math.Min(matches.Count, offset + count);
			for (int index = offset; index < end; index++)
			{
				body.songsByGenre.song.Add(new SongID3(user, matches[index]));
			}

			return Respond(context, body);
		}

		public IResult HandleGetOpenSubsonicExtensions(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.openSubsonicExtensions = new List<OpenSubsonicExtension>();
			return Respond(context, body);
		}

		public IResult HandleGetPlaylists(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.playlists = new PlaylistsContainer();

			string user = context.Request.Query["u"].FirstOrDefault();

			List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(user);
			for (int index = 0; index < allPlaylists.Count; index++)
			{
				PlaylistInfo playlist = allPlaylists[index];
				PlaylistEntry entry = new PlaylistEntry();
				entry.id = playlist.Id;
				entry.name = playlist.Name;
				entry.comment = playlist.Comment;
				entry.songCount = playlist.GetSongCount();
				entry.duration = (int)playlist.DurationSeconds;
				entry.coverArt = "pl-" + playlist.Id;
				body.playlists.playlist.Add(entry);
			}

			return Respond(context, body);
		}

		public IResult HandleGetPlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			if (string.IsNullOrEmpty(id))
			{
				return Results.BadRequest();
			}

			PlaylistInfo playlist = m_musicManager.GetPlaylist(id);
			if (playlist == null)
			{
				return Results.NotFound();
			}

			string user = context.Request.Query["u"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			body.playlist = new PlaylistWithSongs();
			body.playlist.id = playlist.Id;
			body.playlist.name = playlist.Name;
			body.playlist.comment = playlist.Comment;
			body.playlist.songCount = playlist.GetSongCount();
			body.playlist.duration = (int)playlist.DurationSeconds;
			body.playlist.coverArt = "pl-" + playlist.Id;

			List<TrackInfo> tracks = m_musicManager.GetPlaylistTracks(playlist.Id);
			for (int index = 0; index < tracks.Count; index++)
			{
				TrackInfo track = tracks[index];
				SongID3 entry = new SongID3(user, track);
				body.playlist.entry.Add(entry);
			}

			return Respond(context, body);
		}


		public IResult HandleCreatePlaylist(HttpContext context)
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
					return Respond(context, CreateErrorResponse(10, "Missing required parameter: name"));
				}
				if (PlaylistNameTaken(name, ""))
				{
					return Respond(context, CreateErrorResponse(50, "A playlist named '" + name + "' already exists."));
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

			SubsonicResponseBody body = CreateResponse();
			body.playlist = new PlaylistWithSongs();
			body.playlist.id = playlist.Id;
			body.playlist.name = playlist.Name;
			body.playlist.songCount = playlist.TrackIds.Count;
			body.playlist.duration = (int)playlist.DurationSeconds;
			body.playlist.coverArt = "pl-" + playlist.Id;

			List<TrackInfo> tracks = m_musicManager.GetPlaylistTracks(playlist.Id);
			for (int index = 0; index < tracks.Count; index++)
			{
				body.playlist.entry.Add(new SongID3(user, tracks[index]));
			}

			return Respond(context, body);
		}

		public IResult HandleUpdatePlaylist(HttpContext context)
		{
			string playlistId = context.Request.Query["playlistId"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> songIdsToAdd = context.Request.Query["songIdToAdd"].ToList();
			List<string> indicesToRemove = context.Request.Query["songIndexToRemove"].ToList();

			if (string.IsNullOrEmpty(playlistId))
			{
				return Respond(context, CreateErrorResponse(10, "Missing playlistId"));
			}

			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return Respond(context, CreateErrorResponse(70, "Playlist not found"));
			}

			if (!string.IsNullOrEmpty(name))
			{
				if (PlaylistNameTaken(name, playlist.Id))
				{
					return Respond(context, CreateErrorResponse(50, "A playlist named '" + name + "' already exists."));
				}
				playlist.Name = name;
			}

			if (!string.IsNullOrEmpty(comment))
			{
				playlist.Comment = comment;
			}

			// Remove by index descending so indices don't shift
			List<int> parsedIndices = new List<int>();
			for (int index = 0; index < indicesToRemove.Count; index++)
			{
				int parsed;
				if (int.TryParse(indicesToRemove[index], out parsed))
				{
					parsedIndices.Add(parsed);
				}
			}
			parsedIndices.Sort(CompareIntDescending);
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

			// Recalculate duration
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

			IResult result = Respond(context, CreateResponse());
			return result;
		}

		// Case-insensitive duplicate-name check. skipPlaylistId lets the caller
		// exclude the playlist currently being renamed.
		private bool PlaylistNameTaken(string name, string skipPlaylistId)
		{
			List<PlaylistInfo> all = m_musicManager.GetAllPlaylists(null);
			for (int index = 0; index < all.Count; index++)
			{
				PlaylistInfo existing = all[index];
				if (!string.IsNullOrEmpty(skipPlaylistId) && existing.Id == skipPlaylistId)
				{
					continue;
				}
				if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		public IResult HandleDeletePlaylist(HttpContext context)
		{
			string playlistId = context.Request.Query["id"].FirstOrDefault();

			if (string.IsNullOrEmpty(playlistId))
			{
				return Respond(context, CreateErrorResponse(10, "Missing id"));
			}

			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return Respond(context, CreateErrorResponse(70, "Playlist not found"));
			}

			m_musicManager.DeletePlaylist(playlistId);

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
			string id = context.Request.Query["id"].FirstOrDefault();
			if (string.IsNullOrEmpty(id))
			{
				return Respond(context, CreateErrorResponse(70, "Missing id"));
			}

			ArtistInfo artist = m_musicManager.GetArtist(id);
			if (artist != null)
			{
				SubsonicResponseBody body = CreateResponse();
				body.directory = new DirectoryContainer();
				body.directory.id = artist.Id;
				body.directory.name = artist.Name;

				for (int i = 0; i < artist.Albums.Count; i++)
				{
					AlbumInfo album = artist.Albums[i];
					DirectoryChild child = new DirectoryChild();
					child.id = album.Id;
					child.parent = artist.Id;
					child.title = album.Name;
					child.artist = artist.Name;
					child.isDir = true;
					child.coverArt = album.CoverArtId;
					body.directory.child.Add(child);
				}

				return Respond(context, body);
			}

			AlbumInfo albumMatch = m_musicManager.GetAlbum(id);
			if (albumMatch != null)
			{
				SubsonicResponseBody body = CreateResponse();
				body.directory = new DirectoryContainer();
				body.directory.id = albumMatch.Id;
				body.directory.name = albumMatch.Name;

				List<TrackInfo> orderedTracks = new List<TrackInfo>(albumMatch.Tracks);
				orderedTracks.Sort(CompareTrackByDiscThenNumber);

				for (int i = 0; i < orderedTracks.Count; i++)
				{
					TrackInfo track = orderedTracks[i];
					DirectoryChild child = new DirectoryChild();
					child.id = track.Id;
					child.parent = albumMatch.Id;
					child.title = track.Title;
					child.artist = track.Artist;
					child.isDir = false;
					child.coverArt = track.CoverArtId;
					child.duration = track.DurationSeconds;
					child.size = track.FileSizeBytes;
					child.suffix = track.Suffix;
					child.contentType = track.ContentType;
					child.track = track.TrackNumber;
					child.year = track.Year;
					child.album = track.Album;
					body.directory.child.Add(child);
				}

				return Respond(context, body);
			}

			return Respond(context, CreateErrorResponse(70, "Directory not found"));
		}

		private static int CompareAlbumYearDescending(AlbumInfo left, AlbumInfo right)
		{
			return right.Year.CompareTo(left.Year);
		}

		private static int CompareTrackByDiscThenNumber(TrackInfo left, TrackInfo right)
		{
			int discCompare = left.DiscNumber.CompareTo(right.DiscNumber);
			if (discCompare != 0)
			{
				return discCompare;
			}
			return left.TrackNumber.CompareTo(right.TrackNumber);
		}

		private static int CompareIntDescending(int left, int right)
		{
			return right.CompareTo(left);
		}
	}
}
