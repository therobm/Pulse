
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
		PulseAPI m_pulseAPI;

		public Subsonic(PulseAPI pulseAPI, PulseService pulse, MusicManager musicManager)
		{
			m_pulseAPI = pulseAPI;
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
			// New spec fields default through the UserInfo type itself
			// (Flatline #160). coverArtRole = true, other unsupported roles
			// stay false, folder pre-populated with "1" to match
			// HandleGetMusicFolders. Nothing to do here beyond the existing
			// assignments above.
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
			if (string.IsNullOrEmpty(id))
			{
				return Results.Bytes(m_defaultCoverArt, "image/png");
			}

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

			// Artist alias (Flatline #224): "ar-<artistId>" resolves to a
			// representative album's cover so every artist response has a
			// usable coverArt id even when no portrait is uploaded.
			if (!string.IsNullOrEmpty(id) && id.StartsWith("ar-"))
			{
				return HandleArtistAliasCover(id);
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
		/// Resolve a stable "ar-<id>" cover-art alias to a representative
		/// album cover so every artist response has usable bytes behind it
		/// (Flatline #224). Picks the album with the most plays, falling back
		/// to the first album with any embedded/folder art. Cached under the
		/// alias so repeat fetches don't re-scan the artist's discography.
		/// </summary>
		private IResult HandleArtistAliasCover(string coverId)
		{
			string artistId = coverId.Substring(3);
			ArtistInfo artist = m_musicManager.GetArtist(artistId);
			if (artist == null || artist.Albums.Count == 0)
			{
				m_coverArtCache[coverId] = m_defaultCoverArt;
				return Results.Bytes(m_defaultCoverArt, "image/png");
			}

			// Score each album by its total play count across tracks and pick the
			// busiest. Ties go to the order the artist has albums in. New albums
			// (zero plays) still get a chance through the fallback loop below.
			AlbumInfo bestAlbum = null;
			int bestPlays = -1;
			for (int index = 0; index < artist.Albums.Count; index++)
			{
				AlbumInfo album = artist.Albums[index];
				int plays = 0;
				for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
				{
					plays = plays + album.Tracks[trackIndex].Score.PlayCount;
				}
				if (plays > bestPlays)
				{
					bestPlays = plays;
					bestAlbum = album;
				}
			}

			byte[] bytes;
			string contentType;
			if (bestAlbum != null && TryGetAlbumCoverBytes(bestAlbum, out bytes, out contentType))
			{
				m_coverArtCache[coverId] = bytes;
				return Results.Bytes(bytes, contentType);
			}

			// Fallback: walk every album until we find one with art.
			for (int index = 0; index < artist.Albums.Count; index++)
			{
				if (artist.Albums[index] == bestAlbum) { continue; }
				if (TryGetAlbumCoverBytes(artist.Albums[index], out bytes, out contentType))
				{
					m_coverArtCache[coverId] = bytes;
					return Results.Bytes(bytes, contentType);
				}
			}

			m_coverArtCache[coverId] = m_defaultCoverArt;
			return Results.Bytes(m_defaultCoverArt, "image/png");
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

			SubsonicResponseBody body = new SubsonicResponseBody();
			body.indexes = new IndexesContainer();
			body.indexes.index = indexList;

			return Respond(context, body);
		}

		public IResult HandleGetInternetRadioStations(HttpContext context)
		{
			SubsonicResponseBody body = new SubsonicResponseBody();
			body.internetRadioStations = new InternetRadioStationsContainer();
			body.internetRadioStations.internetRadioStation = new List<object>();
			return Respond(context, body);
		}

		public IResult HandleSearch3(HttpContext context)
		{
			string query = context.Request.Query["query"].FirstOrDefault() ?? "";
			query = query.Trim('"');

			string user = context.Request.Query["u"].FirstOrDefault();

			int artistCount = QueryParameters.GetInt(context, "artistCount", 20);
			int albumCount = QueryParameters.GetInt(context, "albumCount", 20);
			int songCount = QueryParameters.GetInt(context, "songCount", 20);

			int artistOffset = QueryParameters.GetInt(context, "artistOffset", 0);
			int albumOffset = QueryParameters.GetInt(context, "albumOffset", 0);
			int songOffset = QueryParameters.GetInt(context, "songOffset", 0);

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
			// Spec returns starred artists + albums + songs (same as getStarred2).
			// Previously only emitted songs (Flatline #155). Mirror the
			// getStarred2 population so legacy clients see what they expect.
			string userName = context.Request.Query["u"].FirstOrDefault() ?? "";

			SubsonicResponseBody body = CreateResponse();
			body.starred = new StarredContainer();

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int index = 0; index < allArtists.Count; index++)
			{
				ArtistInfo artist = allArtists[index];
				if (artist.Starred.ContainsKey(userName) && artist.Starred[userName])
				{
					body.starred.artist.Add(new ArtistID3(artist));
				}
			}

			List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
			for (int albumIndex = 0; albumIndex < allAlbums.Count; albumIndex++)
			{
				AlbumInfo album = allAlbums[albumIndex];
				if (album.Starred.ContainsKey(userName) && album.Starred[userName])
				{
					body.starred.album.Add(new AlbumID3(album));
				}

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
			// Spec params: artist (name, required), count (default 50).
			// Pulse derives "top" from per-track WeightedScore desc, then
			// PlayCount desc as the tiebreaker. Previously returned an empty
			// container regardless of input (Flatline #157).
			string artistName = context.Request.Query["artist"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 50);
			if (count < 1) { count = 1; }
			if (count > 500) { count = 500; }
			string user = context.Request.Query["u"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			body.topSongs = new TopSongsContainer();

			if (string.IsNullOrEmpty(artistName))
			{
				return Respond(context, body);
			}

			// Case-insensitive artist-name match against ArtistInfo.Name.
			ArtistInfo artist = null;
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int idx = 0; idx < allArtists.Count; idx++)
			{
				if (string.Equals(allArtists[idx].Name, artistName, StringComparison.OrdinalIgnoreCase))
				{
					artist = allArtists[idx];
					break;
				}
			}
			if (artist == null)
			{
				return Respond(context, body);
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
			tracks.Sort(CompareTrackByTopSongsRank);

			for (int idx = 0; idx < tracks.Count && body.topSongs.song.Count < count; idx++)
			{
				TrackInfo track = tracks[idx];
				// Drop never-played tracks so callers don't get filler in
				// "top songs" lists for artists with no listening history.
				if (track.Score.PlayCount == 0 && track.Score.WeightedScore <= 0f)
				{
					break;
				}
				body.topSongs.song.Add(new SongID3(user, track));
			}

			return Respond(context, body);
		}

		private static int CompareTrackByTopSongsRank(TrackInfo left, TrackInfo right)
		{
			int byScore = right.Score.WeightedScore.CompareTo(left.Score.WeightedScore);
			if (byScore != 0) { return byScore; }
			return right.Score.PlayCount.CompareTo(left.Score.PlayCount);
		}

		public IResult HandleGetArtistInfo(HttpContext context)
		{
			// Pulse has no external metadata provider, so biography /
			// musicBrainzId / lastFm / image URLs stay empty. similarArtist
			// is derivable from in-memory state though (Flatline #158):
			// other artists whose albums overlap on genre with this one,
			// sorted by WeightedScore desc.
			string id = context.Request.Query["id"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 20);
			if (count < 1) { count = 1; }
			if (count > 100) { count = 100; }

			SubsonicResponseBody body = CreateResponse();
			body.artistInfo2 = new ArtistInfo2();

			if (string.IsNullOrEmpty(id))
			{
				return Respond(context, body);
			}

			ArtistInfo subject = m_musicManager.GetArtist(id);
			if (subject == null)
			{
				return Respond(context, body);
			}

			HashSet<string> subjectGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int albumIndex = 0; albumIndex < subject.Albums.Count; albumIndex++)
			{
				string g = subject.Albums[albumIndex].Genre;
				if (!string.IsNullOrEmpty(g)) { subjectGenres.Add(g); }
			}
			if (subjectGenres.Count == 0)
			{
				return Respond(context, body);
			}

			List<ArtistInfo> candidates = new List<ArtistInfo>();
			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
			for (int idx = 0; idx < allArtists.Count; idx++)
			{
				ArtistInfo other = allArtists[idx];
				if (other.Id == subject.Id) { continue; }
				bool genreOverlap = false;
				for (int otherAlbumIndex = 0; otherAlbumIndex < other.Albums.Count; otherAlbumIndex++)
				{
					string g = other.Albums[otherAlbumIndex].Genre;
					if (!string.IsNullOrEmpty(g) && subjectGenres.Contains(g))
					{
						genreOverlap = true;
						break;
					}
				}
				if (genreOverlap)
				{
					candidates.Add(other);
				}
			}
			candidates.Sort(CompareArtistByWeightedScoreDescending);

			int take = Math.Min(count, candidates.Count);
			for (int idx = 0; idx < take; idx++)
			{
				body.artistInfo2.similarArtist.Add(new ArtistID3(candidates[idx]));
			}

			return Respond(context, body);
		}

		private static int CompareArtistByWeightedScoreDescending(ArtistInfo left, ArtistInfo right)
		{
			return right.WeightedScore.CompareTo(left.WeightedScore);
		}

		public IResult HandleSetRating(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int rating = QueryParameters.GetInt(context, "rating", 0);

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
				string firstChar = "#";
				if (source.Name.Length > 0)
				{
					firstChar = source.Name.Substring(0, 1).ToUpperInvariant();
					if (!char.IsLetter(firstChar[0]))
					{
						firstChar = "#";
					}
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

			string artistUser = context.Request.Query["u"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			body.artist = new ArtistWithAlbumsID3();
			body.artist.id = source.Id;
			body.artist.name = source.Name;
			body.artist.albumCount = source.Albums.Count;
			// Spec coverArt for the artist (Flatline #154 / #224). Stable alias
			// id; HandleGetCoverArt resolves "ar-<id>" to a representative
			// album cover so the bytes are always available.
			body.artist.coverArt = "ar-" + source.Id;
			// OpenSubsonic per-user starred (#159).
			if (!string.IsNullOrEmpty(artistUser))
			{
				bool artistStarredFlag = false;
				bool artistHasStar = source.Starred.TryGetValue(artistUser, out artistStarredFlag);
				if (artistHasStar && artistStarredFlag)
				{
					body.artist.starred = DateTime.UtcNow.ToString("o");
				}
			}

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
			// OpenSubsonic aggregates + per-user starred (#159).
			body.album.displayArtist = source.ArtistName;
			if (!string.IsNullOrEmpty(user))
			{
				bool albumStarredFlag = false;
				bool albumHasStar = source.Starred.TryGetValue(user, out albumStarredFlag);
				if (albumHasStar && albumStarredFlag)
				{
					body.album.starred = DateTime.UtcNow.ToString("o");
				}
			}

			int albumPlayCount = 0;
			DateTime albumMostRecent = default(DateTime);
			float albumRatingTotal = 0f;
			int albumRatedCount = 0;
			for (int statIndex = 0; statIndex < source.Tracks.Count; statIndex++)
			{
				TrackInfo statTrack = source.Tracks[statIndex];
				albumPlayCount = albumPlayCount + statTrack.Score.PlayCount;
				if (statTrack.LastPlayed > albumMostRecent)
				{
					albumMostRecent = statTrack.LastPlayed;
				}
				if (statTrack.Rating > 0)
				{
					albumRatingTotal = albumRatingTotal + statTrack.Rating;
					albumRatedCount++;
				}
			}
			body.album.playCount = albumPlayCount;
			if (albumMostRecent != default(DateTime))
			{
				body.album.played = albumMostRecent.ToString("o");
			}
			if (albumRatedCount > 0)
			{
				body.album.userRating = (int)Math.Round(albumRatingTotal / albumRatedCount);
			}

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
			// Honors every spec type value (Flatline #156). Previously only
			// newest / random did anything; everything else silently fell
			// through to "whatever order we had". Each branch derives its
			// ranking on the fly from in-memory state -- no schema changes.
			string type = context.Request.Query["type"].FirstOrDefault() ?? "random";
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string user = context.Request.Query["u"].FirstOrDefault() ?? "";

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
			else if (type == "alphabeticalByName")
			{
				allAlbums.Sort(CompareAlbumByName);
			}
			else if (type == "alphabeticalByArtist")
			{
				allAlbums.Sort(CompareAlbumByArtistThenName);
			}
			else if (type == "frequent")
			{
				// Sum play counts across each album's tracks, sort desc, drop zeros.
				List<KeyValuePair<AlbumInfo, int>> scored = new List<KeyValuePair<AlbumInfo, int>>();
				for (int idx = 0; idx < allAlbums.Count; idx++)
				{
					AlbumInfo album = allAlbums[idx];
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
				scored.Sort(CompareAlbumPlayCountDescending);
				allAlbums = new List<AlbumInfo>();
				for (int idx = 0; idx < scored.Count; idx++)
				{
					allAlbums.Add(scored[idx].Key);
				}
			}
			else if (type == "recent")
			{
				// Most-recent track LastPlayed across the album. Albums no tracks
				// have been played in get dropped (no recency signal at all).
				List<KeyValuePair<AlbumInfo, DateTime>> scored = new List<KeyValuePair<AlbumInfo, DateTime>>();
				for (int idx = 0; idx < allAlbums.Count; idx++)
				{
					AlbumInfo album = allAlbums[idx];
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
				scored.Sort(CompareAlbumDateDescending);
				allAlbums = new List<AlbumInfo>();
				for (int idx = 0; idx < scored.Count; idx++)
				{
					allAlbums.Add(scored[idx].Key);
				}
			}
			else if (type == "byYear")
			{
				int fromYear = int.MinValue;
				int toYear = int.MaxValue;
				int parsed;
				if (int.TryParse(context.Request.Query["fromYear"].FirstOrDefault(), out parsed)) { fromYear = parsed; }
				if (int.TryParse(context.Request.Query["toYear"].FirstOrDefault(), out parsed)) { toYear = parsed; }
				List<AlbumInfo> filtered = new List<AlbumInfo>();
				for (int idx = 0; idx < allAlbums.Count; idx++)
				{
					AlbumInfo album = allAlbums[idx];
					if (album.Year >= fromYear && album.Year <= toYear)
					{
						filtered.Add(album);
					}
				}
				// Spec: ascending if fromYear <= toYear, descending if reversed.
				if (fromYear <= toYear)
				{
					filtered.Sort(CompareAlbumYearAscending);
				}
				else
				{
					filtered.Sort(CompareAlbumYearDescending);
				}
				allAlbums = filtered;
			}
			else if (type == "byGenre")
			{
				string genre = context.Request.Query["genre"].FirstOrDefault();
				List<AlbumInfo> filtered = new List<AlbumInfo>();
				if (!string.IsNullOrEmpty(genre))
				{
					for (int idx = 0; idx < allAlbums.Count; idx++)
					{
						AlbumInfo album = allAlbums[idx];
						if (!string.IsNullOrEmpty(album.Genre) && string.Equals(album.Genre, genre, StringComparison.OrdinalIgnoreCase))
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
				for (int idx = 0; idx < allAlbums.Count; idx++)
				{
					AlbumInfo album = allAlbums[idx];
					bool isStarred;
					if (album.Starred.TryGetValue(user, out isStarred) && isStarred)
					{
						filtered.Add(album);
					}
				}
				allAlbums = filtered;
			}
			else if (type == "highest")
			{
				// Spec: highest user-rated albums. Pulse only has per-track
				// Rating, so average it across each album. Albums with no
				// rated tracks fall off the end.
				List<KeyValuePair<AlbumInfo, float>> scored = new List<KeyValuePair<AlbumInfo, float>>();
				for (int idx = 0; idx < allAlbums.Count; idx++)
				{
					AlbumInfo album = allAlbums[idx];
					float total = 0f;
					int rated = 0;
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						int r = album.Tracks[trackIndex].Rating;
						if (r > 0)
						{
							total = total + r;
							rated++;
						}
					}
					if (rated > 0)
					{
						scored.Add(new KeyValuePair<AlbumInfo, float>(album, total / rated));
					}
				}
				scored.Sort(CompareAlbumFloatDescending);
				allAlbums = new List<AlbumInfo>();
				for (int idx = 0; idx < scored.Count; idx++)
				{
					allAlbums.Add(scored[idx].Key);
				}
			}

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

		// ---- helpers for HandleGetAlbumList2 (#156) ----
		private static int CompareAlbumByName(AlbumInfo left, AlbumInfo right)
		{
			return string.Compare(left.Name ?? "", right.Name ?? "", StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareAlbumByArtistThenName(AlbumInfo left, AlbumInfo right)
		{
			int byArtist = string.Compare(left.ArtistName ?? "", right.ArtistName ?? "", StringComparison.OrdinalIgnoreCase);
			if (byArtist != 0) { return byArtist; }
			return string.Compare(left.Name ?? "", right.Name ?? "", StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareAlbumYearAscending(AlbumInfo left, AlbumInfo right)
		{
			return left.Year.CompareTo(right.Year);
		}

		private static int CompareAlbumPlayCountDescending(KeyValuePair<AlbumInfo, int> left, KeyValuePair<AlbumInfo, int> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int CompareAlbumDateDescending(KeyValuePair<AlbumInfo, DateTime> left, KeyValuePair<AlbumInfo, DateTime> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int CompareAlbumFloatDescending(KeyValuePair<AlbumInfo, float> left, KeyValuePair<AlbumInfo, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		public IResult HandleGetGenres(HttpContext context)
		{
			// Genre aggregation is shared with PulseAPI -- this just maps the
			// shared (songs, albums) counts into Subsonic's GenreEntry shape.
			Dictionary<string, (int songs, int albums)> data = m_pulseAPI.GetGenresData();
			SubsonicResponseBody body = CreateResponse();
			body.genres = new GenresContainer();
			body.genres.genre = new List<GenreEntry>();
			foreach (KeyValuePair<string, (int songs, int albums)> entry in data)
			{
				GenreEntry shaped = new GenreEntry();
				shaped.value = entry.Key;
				shaped.songCount = entry.Value.songs;
				shaped.albumCount = entry.Value.albums;
				body.genres.genre.Add(shaped);
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

			int count = QueryParameters.GetInt(context, "count", 10);
			int offset = QueryParameters.GetInt(context, "offset", 0);

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

		// Standard /rest/getRandomSongs (Flatline #162). Filter by optional
		// genre / fromYear / toYear, return up to `size` random tracks.
		// musicFolderId is accepted but ignored -- Pulse exposes a single
		// folder. Brand new endpoint; doesn't touch any existing read path.
		public IResult HandleGetRandomSongs(HttpContext context)
		{
			int size = QueryParameters.GetInt(context, "size", 10);
			if (size < 1) { size = 1; }
			if (size > 500) { size = 500; }
			string genre = context.Request.Query["genre"].FirstOrDefault();
			string fromYearStr = context.Request.Query["fromYear"].FirstOrDefault();
			string toYearStr = context.Request.Query["toYear"].FirstOrDefault();
			string user = context.Request.Query["u"].FirstOrDefault();

			int fromYear = int.MinValue;
			int toYear = int.MaxValue;
			int parsed;
			if (int.TryParse(fromYearStr, out parsed)) { fromYear = parsed; }
			if (int.TryParse(toYearStr, out parsed)) { toYear = parsed; }

			List<TrackInfo> candidates = new List<TrackInfo>();
			List<TrackInfo> allTracks = m_musicManager.GetAllTracks();
			for (int index = 0; index < allTracks.Count; index++)
			{
				TrackInfo track = allTracks[index];
				if (!string.IsNullOrEmpty(genre) && !string.Equals(track.Genre, genre, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (track.Year < fromYear || track.Year > toYear)
				{
					continue;
				}
				candidates.Add(track);
			}

			Random rng = new Random();
			SubsonicResponseBody body = CreateResponse();
			body.randomSongs = new RandomSongsContainer();
			int take = Math.Min(size, candidates.Count);
			for (int idx = 0; idx < take; idx++)
			{
				int pick = rng.Next(candidates.Count);
				body.randomSongs.song.Add(new SongID3(user, candidates[pick]));
				candidates[pick] = candidates[candidates.Count - 1];
				candidates.RemoveAt(candidates.Count - 1);
			}

			return Respond(context, body);
		}

		// Standard /rest/download (Flatline #163). Same semantics as stream
		// but with Content-Disposition: attachment so clients save rather than
		// play inline. No transcoding -- original file.
		public IResult HandleDownload(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			TrackInfo track = m_musicManager.GetTrack(id);
			if (track == null)
			{
				return Respond(context, CreateErrorResponse(70, "Song not found"));
			}

			FileStream fileStream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			string downloadName = Path.GetFileName(track.FilePath);
			return Results.File(fileStream, track.ContentType, downloadName, enableRangeProcessing: true);
		}

		// Standard /rest/getNowPlaying (Flatline #164). Single-slot today --
		// Pulse only tracks one currently-playing track in MusicManager. When
		// multi-user concurrent play is added this will return one entry per
		// active user. Returns an empty list when nothing is playing.
		public IResult HandleGetNowPlaying(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			body.nowPlaying = new NowPlayingContainer();

			string trackId = m_musicManager.GetNowPlayingTrackId();
			if (string.IsNullOrEmpty(trackId))
			{
				return Respond(context, body);
			}

			TrackInfo track = m_musicManager.GetTrack(trackId);
			if (track == null)
			{
				return Respond(context, body);
			}

			DateTime startedAt = m_musicManager.GetNowPlayingStartTime();
			int minutesAgo = 0;
			if (startedAt != default(DateTime))
			{
				double minutes = (DateTime.UtcNow - startedAt).TotalMinutes;
				if (minutes > 0) { minutesAgo = (int)minutes; }
			}

			NowPlayingEntry entry = new NowPlayingEntry();
			entry.id = track.Id;
			entry.title = track.Title;
			entry.album = track.Album;
			entry.albumId = track.AlbumId;
			entry.artist = track.Artist;
			entry.artistId = track.ArtistId;
			entry.track = track.TrackNumber;
			entry.discNumber = track.DiscNumber;
			entry.year = track.Year;
			entry.genre = track.Genre;
			entry.duration = track.DurationSeconds;
			entry.size = track.FileSizeBytes;
			entry.suffix = track.Suffix;
			entry.contentType = track.ContentType;
			entry.coverArt = track.CoverArtId;
			entry.username = user ?? "";
			entry.minutesAgo = minutesAgo;
			entry.playerId = "1";
			entry.playerName = "Pulse";
			body.nowPlaying.entry.Add(entry);

			return Respond(context, body);
		}

		// Standard /rest/getAlbumInfo and /rest/getAlbumInfo2 (Flatline #165).
		// Pulse has no external metadata provider, so notes / musicBrainzId /
		// lastFmUrl are empty. Image URLs are intentionally blank too -- clients
		// already use the album's coverArt id directly via getCoverArt, and
		// embedding pre-built URLs here would need absolute auth-bearing URLs
		// that go stale per session. Endpoint exists, response is spec-shaped.
		public IResult HandleGetAlbumInfo(HttpContext context)
		{
			SubsonicResponseBody body = CreateResponse();
			body.albumInfo = new AlbumInfoBody();
			return Respond(context, body);
		}

		// Standard /rest/getSimilarSongs and /rest/getSimilarSongs2 (Flatline
		// #166). Derives similarity from same-artist tracks first (by score
		// desc) then same-genre tracks from other artists. No external lookup.
		public IResult HandleGetSimilarSongs2(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 50);
			if (count < 1) { count = 1; }
			if (count > 500) { count = 500; }
			string user = context.Request.Query["u"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			SimilarSongsContainer container = new SimilarSongsContainer();
			body.similarSongs = container;
			body.similarSongs2 = container;

			ArtistInfo artist = m_musicManager.GetArtist(id);
			if (artist == null)
			{
				return Respond(context, body);
			}

			HashSet<string> seenTrackIds = new HashSet<string>();
			List<TrackInfo> bucket = new List<TrackInfo>();

			for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
			{
				AlbumInfo album = artist.Albums[albumIndex];
				for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
				{
					bucket.Add(album.Tracks[trackIndex]);
				}
			}
			bucket.Sort(CompareTrackByScoreDescending);
			for (int idx = 0; idx < bucket.Count && container.song.Count < count; idx++)
			{
				TrackInfo track = bucket[idx];
				if (seenTrackIds.Add(track.Id))
				{
					container.song.Add(new SongID3(user, track));
				}
			}

			if (container.song.Count < count)
			{
				HashSet<string> targetGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
				{
					string g = artist.Albums[albumIndex].Genre;
					if (!string.IsNullOrEmpty(g)) { targetGenres.Add(g); }
				}

				List<TrackInfo> crossGenre = new List<TrackInfo>();
				List<TrackInfo> allTracks = m_musicManager.GetAllTracks();
				for (int idx = 0; idx < allTracks.Count; idx++)
				{
					TrackInfo track = allTracks[idx];
					if (track.ArtistId == artist.Id) { continue; }
					if (string.IsNullOrEmpty(track.Genre) || !targetGenres.Contains(track.Genre)) { continue; }
					crossGenre.Add(track);
				}
				crossGenre.Sort(CompareTrackByScoreDescending);
				for (int idx = 0; idx < crossGenre.Count && container.song.Count < count; idx++)
				{
					TrackInfo track = crossGenre[idx];
					if (seenTrackIds.Add(track.Id))
					{
						container.song.Add(new SongID3(user, track));
					}
				}
			}

			return Respond(context, body);
		}

		private static int CompareTrackByScoreDescending(TrackInfo left, TrackInfo right)
		{
			return right.Score.WeightedScore.CompareTo(left.Score.WeightedScore);
		}

		// Standard /rest/getLyrics (Flatline #167) -- legacy lookup by
		// artist + title. Reads the track's USLT frame via TagLib at request
		// time so we always reflect whatever is currently embedded.
		public IResult HandleGetLyrics(HttpContext context)
		{
			string artist = context.Request.Query["artist"].FirstOrDefault();
			string title = context.Request.Query["title"].FirstOrDefault();

			SubsonicResponseBody body = CreateResponse();
			body.lyrics = new LyricsBody();
			body.lyrics.artist = artist ?? "";
			body.lyrics.title = title ?? "";

			if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
			{
				return Respond(context, body);
			}

			TrackInfo match = FindTrackByArtistTitle(artist, title);
			if (match != null)
			{
				body.lyrics.value = ReadLyricsFromFile(match.FilePath);
			}
			return Respond(context, body);
		}

		// OpenSubsonic /rest/getLyricsBySongId (Flatline #167). Same read path
		// as getLyrics but addressed by track id and wrapped in the structured
		// lyrics container (still emits unsynced -- synced SYLT lookup is a
		// follow-up).
		public IResult HandleGetLyricsBySongId(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			SubsonicResponseBody body = CreateResponse();
			body.lyricsList = new LyricsListBody();

			if (string.IsNullOrEmpty(id))
			{
				return Respond(context, body);
			}

			TrackInfo track = m_musicManager.GetTrack(id);
			if (track == null)
			{
				return Respond(context, body);
			}

			string lyricsText = ReadLyricsFromFile(track.FilePath);
			if (string.IsNullOrEmpty(lyricsText))
			{
				return Respond(context, body);
			}

			StructuredLyrics structured = new StructuredLyrics();
			structured.synced = false;
			structured.displayArtist = track.Artist ?? "";
			structured.displayTitle = track.Title ?? "";
			string[] lines = lyricsText.Replace("\r\n", "\n").Split('\n');
			for (int idx = 0; idx < lines.Length; idx++)
			{
				LyricLine line = new LyricLine();
				line.value = lines[idx];
				structured.line.Add(line);
			}
			body.lyricsList.structuredLyrics.Add(structured);
			return Respond(context, body);
		}

		private TrackInfo FindTrackByArtistTitle(string artist, string title)
		{
			List<TrackInfo> all = m_musicManager.GetAllTracks();
			for (int idx = 0; idx < all.Count; idx++)
			{
				TrackInfo track = all[idx];
				if (string.Equals(track.Artist, artist, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(track.Title, title, StringComparison.OrdinalIgnoreCase))
				{
					return track;
				}
			}
			return null;
		}

		private static string ReadLyricsFromFile(string filePath)
		{
			try
			{
				TagLib.File tagFile = TagLib.File.Create(filePath);
				string lyrics = tagFile.Tag.Lyrics;
				tagFile.Dispose();
				if (lyrics == null) { return ""; }
				return lyrics;
			}
			catch (Exception ex)
			{
				Log.Error(-1, "ReadLyricsFromFile: " + filePath + " - " + ex.Message);
				return "";
			}
		}

		// Standard /rest/getPlayQueue (Flatline #168). Returns the persisted
		// queue + position state for this user.
		public IResult HandleGetPlayQueue(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			SubsonicResponseBody body = CreateResponse();
			body.playQueue = new PlayQueueBody();
			body.playQueue.username = user ?? "";

			if (string.IsNullOrEmpty(user))
			{
				return Respond(context, body);
			}

			PlayQueueInfo info = m_musicManager.GetPlayQueue(user);
			body.playQueue.current = info.CurrentTrackId ?? "";
			body.playQueue.position = info.PositionMs;
			if (info.Changed != default(DateTime))
			{
				body.playQueue.changed = info.Changed.ToString("o");
			}
			body.playQueue.changedBy = info.ChangedBy ?? "";

			for (int idx = 0; idx < info.TrackIds.Count; idx++)
			{
				TrackInfo track = m_musicManager.GetTrack(info.TrackIds[idx]);
				if (track != null)
				{
					body.playQueue.entry.Add(new SongID3(user, track));
				}
			}

			return Respond(context, body);
		}

		// Standard /rest/savePlayQueue (Flatline #168). Writes through to
		// SQLite. Empty `id` list clears the queue.
		public IResult HandleSavePlayQueue(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			if (string.IsNullOrEmpty(user))
			{
				return Respond(context, CreateErrorResponse(10, "Missing required parameter: u"));
			}

			List<string> trackIds = new List<string>(context.Request.Query["id"].ToList());
			string current = context.Request.Query["current"].FirstOrDefault() ?? "";
			long position = 0;
			long.TryParse(context.Request.Query["position"].FirstOrDefault() ?? "0", out position);
			string changedBy = context.Request.Query["c"].FirstOrDefault() ?? "";

			m_musicManager.SavePlayQueue(user, trackIds, current, position, changedBy);
			return Respond(context, CreateResponse());
		}

		// Standard /rest/getBookmarks (Flatline #168). One per (user, track).
		public IResult HandleGetBookmarks(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			SubsonicResponseBody body = CreateResponse();
			body.bookmarks = new BookmarksContainer();

			if (string.IsNullOrEmpty(user))
			{
				return Respond(context, body);
			}

			List<BookmarkInfo> bookmarks = m_musicManager.GetBookmarks(user);
			for (int idx = 0; idx < bookmarks.Count; idx++)
			{
				BookmarkInfo bookmark = bookmarks[idx];
				TrackInfo track = m_musicManager.GetTrack(bookmark.TrackId);
				if (track == null) { continue; }

				BookmarkEntry entry = new BookmarkEntry();
				entry.username = user;
				entry.position = bookmark.PositionMs;
				entry.comment = bookmark.Comment ?? "";
				if (bookmark.Created != default(DateTime))
				{
					entry.created = bookmark.Created.ToString("o");
				}
				if (bookmark.Changed != default(DateTime))
				{
					entry.changed = bookmark.Changed.ToString("o");
				}
				entry.entry = new SongID3(user, track);
				body.bookmarks.bookmark.Add(entry);
			}

			return Respond(context, body);
		}

		// Standard /rest/createBookmark (Flatline #168). Upserts.
		public IResult HandleCreateBookmark(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			string id = context.Request.Query["id"].FirstOrDefault();
			long position = 0;
			long.TryParse(context.Request.Query["position"].FirstOrDefault() ?? "0", out position);
			string comment = context.Request.Query["comment"].FirstOrDefault() ?? "";

			if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(id))
			{
				return Respond(context, CreateErrorResponse(10, "Missing required parameters"));
			}

			m_musicManager.SaveBookmark(user, id, position, comment);
			return Respond(context, CreateResponse());
		}

		// Standard /rest/deleteBookmark (Flatline #168).
		public IResult HandleDeleteBookmark(HttpContext context)
		{
			string user = context.Request.Query["u"].FirstOrDefault();
			string id = context.Request.Query["id"].FirstOrDefault();

			if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(id))
			{
				return Respond(context, CreateErrorResponse(10, "Missing required parameters"));
			}

			m_musicManager.DeleteBookmark(user, id);
			return Respond(context, CreateResponse());
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
				// OpenSubsonic ownership fields (#159). Pulse is single-user,
				// so owner = the requesting user and everything is treated as
				// public. created / changed need schema support; left unset.
				string ownerName = user;
				if (ownerName == null)
				{
					ownerName = "";
				}
				entry.owner = ownerName;
				entry.isPublic = true;
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
			// OpenSubsonic ownership fields (#159).
			string playlistOwner = user;
			if (playlistOwner == null)
			{
				playlistOwner = "";
			}
			body.playlist.owner = playlistOwner;
			body.playlist.isPublic = true;

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
			string createdOwner = user;
			if (createdOwner == null)
			{
				createdOwner = "";
			}
			body.playlist.owner = createdOwner;
			body.playlist.isPublic = true;

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

			// Drop the cached composite so the next /rest/getCoverArt regenerates
			// from the new track set (Flatline #224).
			byte[] discard;
			m_coverArtCache.TryRemove("pl-" + playlist.Id, out discard);

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
