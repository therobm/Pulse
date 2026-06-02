using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using PulseAPI.CSharp;
using Thump.Data;
using Thump.Utility;

namespace Thump.Pulse
{
	// Pulse-native media client. Speaks the pulse_v1 API and consumes the
	// shared PulseAPI.CSharp contract types directly, then maps them onto the
	// Legacy* domain objects the MediaClient contract still hands back to the
	// app. Auth is a single u=<username> query parameter on every request; the
	// response is always a PulseResponse envelope whose contents field carries
	// the payload (a single PulseObject or a list of them).
	public class PulseClient : MediaClient
	{
		private ConcurrentDictionary<string, byte[]> m_imageCache = new ConcurrentDictionary<string, byte[]>();

		public PulseClient(ThumpCache cache) : base(cache)
		{
		}

		private string BuildPulseUrl(string endpoint, string extraParams)
		{
			string url = m_baseUrl + "/pulse_v1/" + endpoint + "?u=" + Uri.EscapeDataString(m_user);
			if (!string.IsNullOrEmpty(extraParams))
			{
				url = url + "&" + extraParams;
			}
			return url;
		}

		// Pull the contents element out of a PulseResponse envelope. Returns
		// false when the request failed, the envelope was unparseable, the
		// status wasn't "ok", or there was no payload. On success the boxed
		// contents (System.Text.Json hands back a JsonElement for an object
		// field) is unwrapped so callers can re-parse it into a concrete type.
		private bool TryGetContents(string url, bool bCacheAllowed, out JsonElement contents)
		{
			contents = default(JsonElement);
			string json = HttpGet(url, bCacheAllowed);
			if (string.IsNullOrEmpty(json))
			{
				return false;
			}
			PulseResponse response = PulseWire.Parse<PulseResponse>(json);
			if (response == null)
			{
				Log.Error("Unparseable pulse response: " + url);
				return false;
			}
			if (response.status != "ok")
			{
				Log.Error("Pulse endpoint returned status '" + response.status + "': " + url);
				return false;
			}
			if (response.contents == null)
			{
				return false;
			}
			if (!(response.contents is JsonElement))
			{
				return false;
			}
			contents = (JsonElement)response.contents;
			return true;
		}

		private bool TryGetObject<T>(string url, bool bCacheAllowed, out T value)
		{
			value = default(T);
			JsonElement contents;
			if (!TryGetContents(url, bCacheAllowed, out contents))
			{
				return false;
			}
			value = PulseWire.Parse<T>(contents.GetRawText());
			return true;
		}

		// Treat a request as a fire-and-forget command: success is simply a
		// well-formed envelope with status "ok".
		private bool RunCommand(string url)
		{
			JsonElement discard;
			return TryGetContents(url, false, out discard);
		}

		protected override bool Ping(out JsonElement response)
		{
			response = default(JsonElement);
			try
			{
				string url = m_baseUrl + "/pulse_v1/ping?u=" + Uri.EscapeDataString(m_user);
				string json = HttpGet(url, false);
				if (string.IsNullOrEmpty(json))
				{
					m_bIsOnline = false;
					return false;
				}
				JsonDocument doc = JsonDocument.Parse(json);
				response = doc.RootElement;
				m_bIsOnline = true;
				return true;
			}
			catch (Exception ex)
			{
				// Online/offline polling - a failure here just means the server
				// isn't reachable right now, so don't make noise in the log.
				Log.Exception(ex);
			}
			m_bIsOnline = false;
			return false;
		}

		public override string GetTrackAudioURL(string trackId)
		{
			return BuildPulseUrl("stream", "id=" + Uri.EscapeDataString(trackId));
		}

		protected override string BuildCoverArtUrl(string coverArtId)
		{
			if (string.IsNullOrEmpty(coverArtId))
			{
				return null;
			}
			return BuildPulseUrl("coverArt", "id=" + Uri.EscapeDataString(coverArtId));
		}

		public override void GetTrack(string trackId, Action<LegacyPulseTrack> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new LegacyPulseTrack());
				return;
			}
			Task.Run(() =>
			{
				LegacyPulseTrack result = new LegacyPulseTrack();
				try
				{
					string url = BuildPulseUrl("track", "id=" + Uri.EscapeDataString(trackId));
					PulseTrack track;
					if (TryGetObject(url, true, out track) && track != null)
					{
						result = MapTrack(track);
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				LegacyPulseTrack captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetArtists(Action<List<LegacyPulseArtist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseArtist>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseArtist> results = new List<LegacyPulseArtist>();
				try
				{
					string url = BuildPulseUrl("artists", null);
					List<PulseArtist> artists;
					if (TryGetObject(url, true, out artists) && artists != null)
					{
						for (int index = 0; index < artists.Count; index++)
						{
							results.Add(MapArtist(artists[index]));
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareArtistByName);
				List<LegacyPulseArtist> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetArtist(string artistId, Action<LegacyPulseArtist> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new LegacyPulseArtist());
				return;
			}
			Task.Run(() =>
			{
				LegacyPulseArtist result = new LegacyPulseArtist();
				try
				{
					string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
					PulseArtistDetails details;
					if (TryGetObject(url, true, out details) && details != null && details.Artist != null)
					{
						LegacyPulseArtist artist = MapArtist(details.Artist);
						List<string> albumIds = new List<string>();
						if (details.Albums != null)
						{
							for (int index = 0; index < details.Albums.Count; index++)
							{
								albumIds.Add(details.Albums[index].Id);
							}
						}
						artist.AlbumIDs = albumIds;
						result = artist;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				LegacyPulseArtist captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetArtistTracks(string artistId, Action<List<LegacyPulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseTrack>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseTrack> results = new List<LegacyPulseTrack>();
				try
				{
					string url = BuildPulseUrl("artistTracks", "id=" + Uri.EscapeDataString(artistId));
					PulseArtistFullDetails details;
					if (TryGetObject(url, true, out details) && details != null && details.AlbumDetails != null)
					{
						for (int albumIndex = 0; albumIndex < details.AlbumDetails.Count; albumIndex++)
						{
							PulseAlbumDetails album = details.AlbumDetails[albumIndex];
							if (album == null || album.Tracks == null)
							{
								continue;
							}
							for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
							{
								results.Add(MapTrack(album.Tracks[trackIndex]));
							}
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareTrackByTitle);
				List<LegacyPulseTrack> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetPodcasts(Action<List<LegacyPulsePodcastChannel>> onComplete)
		{
			// The pulse_v1 server returns status "not_implemented" for podcasts.
			// Complete with an empty list rather than calling the endpoint.
			CompleteOnMain(onComplete, new List<LegacyPulsePodcastChannel>());
		}

		public override void Search(string query, Action<LegacyPulseSearchData> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new LegacyPulseSearchData());
				return;
			}
			Task.Run(() =>
			{
				LegacyPulseSearchData result = new LegacyPulseSearchData();
				try
				{
					string param = "query=" + Uri.EscapeDataString(query)
						+ "&artistCount=10"
						+ "&albumCount=20"
						+ "&songCount=30";
					string url = BuildPulseUrl("search", param);
					PulseSearchData data;
					if (TryGetObject(url, true, out data) && data != null)
					{
						result = MapSearchData(data);
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				LegacyPulseSearchData captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetArtistAlbums(string artistId, Action<List<LegacyPulseAlbum>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseAlbum>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseAlbum> results = new List<LegacyPulseAlbum>();
				try
				{
					string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
					PulseArtistDetails details;
					if (TryGetObject(url, true, out details) && details != null && details.Albums != null)
					{
						for (int index = 0; index < details.Albums.Count; index++)
						{
							results.Add(MapAlbum(details.Albums[index]));
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				List<LegacyPulseAlbum> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetAlbum(string albumId, Action<LegacyPulseAlbum> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new LegacyPulseAlbum());
				return;
			}
			Task.Run(() =>
			{
				LegacyPulseAlbum result = new LegacyPulseAlbum();
				try
				{
					string url = BuildPulseUrl("album", "id=" + Uri.EscapeDataString(albumId));
					PulseAlbumDetails details;
					if (TryGetObject(url, true, out details) && details != null && details.Album != null)
					{
						result = MapAlbumDetails(details);
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				LegacyPulseAlbum captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetAlbums(Action<List<LegacyPulseAlbum>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseAlbum>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseAlbum> results = new List<LegacyPulseAlbum>();
				try
				{
					// Page until the server stops handing back rows. Advance the
					// offset by the count actually returned so a server-side size
					// cap (returning fewer than requested) doesn't truncate us.
					int pageSize = 500;
					int offset = 0;
					for (int page = 0; page < 1000; page++)
					{
						string param = "type=alphabeticalbyname&size=" + pageSize + "&offset=" + offset;
						string url = BuildPulseUrl("albums", param);
						List<PulseAlbum> albums;
						if (!TryGetObject(url, true, out albums) || albums == null || albums.Count == 0)
						{
							break;
						}
						for (int index = 0; index < albums.Count; index++)
						{
							results.Add(MapAlbum(albums[index]));
						}
						offset = offset + albums.Count;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareAlbumByName);
				List<LegacyPulseAlbum> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void CreatePlaylist(string name, Action<LegacyPulsePlaylist> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, null);
				return;
			}
			Task.Run(() =>
			{
				LegacyPulsePlaylist created = null;
				try
				{
					string url = BuildPulseUrl("createPlaylist", "name=" + Uri.EscapeDataString(name));
					PulsePlaylistDetails details;
					if (TryGetObject(url, false, out details) && details != null && details.Playlist != null)
					{
						created = MapPlaylistDetails(details);
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				LegacyPulsePlaylist captured = created;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void RenamePlaylist(string playlistId, string newName, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			Task.Run(() =>
			{
				bool ok = false;
				try
				{
					string param = "playlistId=" + Uri.EscapeDataString(playlistId)
						+ "&name=" + Uri.EscapeDataString(newName);
					ok = RunCommand(BuildPulseUrl("updatePlaylist", param));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				bool captured = ok;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void Star(string trackId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			Task.Run(() =>
			{
				bool ok = false;
				try
				{
					string param = "id=" + Uri.EscapeDataString(trackId) + "&type=track";
					ok = RunCommand(BuildPulseUrl("favorite", param));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				bool captured = ok;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void Unstar(string trackId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			Task.Run(() =>
			{
				bool ok = false;
				try
				{
					string param = "id=" + Uri.EscapeDataString(trackId) + "&type=track";
					ok = RunCommand(BuildPulseUrl("unfavorite", param));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				bool captured = ok;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void DeletePlaylist(string playlistId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			Task.Run(() =>
			{
				bool ok = false;
				try
				{
					string param = "id=" + Uri.EscapeDataString(playlistId);
					ok = RunCommand(BuildPulseUrl("deletePlaylist", param));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				bool captured = ok;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void AddTrackToPlaylist(string playlistId, string songId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			Task.Run(() =>
			{
				bool ok = false;
				try
				{
					string param = "playlistId=" + Uri.EscapeDataString(playlistId)
						+ "&songIdToAdd=" + Uri.EscapeDataString(songId);
					ok = RunCommand(BuildPulseUrl("updatePlaylist", param));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				bool captured = ok;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void RemoveTrackFromPlaylist(string playlistId, int songIndex, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			Task.Run(() =>
			{
				bool ok = false;
				try
				{
					string param = "playlistId=" + Uri.EscapeDataString(playlistId)
						+ "&songIndexToRemove=" + songIndex;
					ok = RunCommand(BuildPulseUrl("updatePlaylist", param));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				bool captured = ok;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void ReorderPlaylist(string playlistId, int fromIndex, int toIndex, List<LegacyPulseTrack> newOrder, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			Task.Run(() =>
			{
				bool ok = false;
				try
				{
					// Same strategy as the Subsonic client: remove every entry
					// from the first changed index to the end (high index first),
					// then re-add the new tail in order. updatePlaylist accepts
					// repeated songIndexToRemove and songIdToAdd parameters.
					int divergence = fromIndex;
					if (toIndex < divergence)
					{
						divergence = toIndex;
					}
					StringBuilder param = new StringBuilder();
					param.Append("playlistId=").Append(Uri.EscapeDataString(playlistId));
					for (int index = newOrder.Count - 1; index >= divergence; index--)
					{
						param.Append("&songIndexToRemove=").Append(index);
					}
					for (int index = divergence; index < newOrder.Count; index++)
					{
						param.Append("&songIdToAdd=").Append(Uri.EscapeDataString(newOrder[index].Id));
					}
					ok = RunCommand(BuildPulseUrl("updatePlaylist", param.ToString()));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				bool captured = ok;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void MarkPlaylistPlayed(string playlistId, Action<bool> onComplete)
		{
			// No pulse_v1 endpoint records a playlist play. This is a best-effort
			// analytics signal, so treat the absence as a benign no-op rather than
			// surfacing a failure.
			CompleteOnMain(onComplete, true);
		}

		public override void GetPlaylists(Action<List<LegacyPulsePlaylist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulsePlaylist>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulsePlaylist> results = new List<LegacyPulsePlaylist>();
				try
				{
					string url = BuildPulseUrl("playlists", null);
					List<PulsePlaylist> playlists;
					if (TryGetObject(url, true, out playlists) && playlists != null)
					{
						for (int index = 0; index < playlists.Count; index++)
						{
							results.Add(MapPlaylist(playlists[index]));
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(ComparePlaylistByName);
				List<LegacyPulsePlaylist> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetPlaylist(string playlistId, Action<LegacyPulsePlaylist> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new LegacyPulsePlaylist());
				return;
			}
			Task.Run(() =>
			{
				LegacyPulsePlaylist result = new LegacyPulsePlaylist();
				try
				{
					string url = BuildPulseUrl("playlist", "id=" + Uri.EscapeDataString(playlistId));
					PulsePlaylistDetails details;
					if (TryGetObject(url, true, out details) && details != null && details.Playlist != null)
					{
						result = MapPlaylistDetails(details);
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				LegacyPulsePlaylist captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetCoverArt(string coverArtId, Action<byte[]> onComplete)
		{
			if (string.IsNullOrEmpty(coverArtId))
			{
				CompleteOnMain(onComplete, null);
				return;
			}
			string url = BuildCoverArtUrl(coverArtId);
			byte[] cached;
			if (m_imageCache.TryGetValue(url, out cached))
			{
				CompleteOnMain(onComplete, cached);
				return;
			}
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, null);
				return;
			}
			Task.Run(() =>
			{
				try
				{
					byte[] data = HttpGetBinary(url, true);
					if (data == null || data.Length <= 0)
					{
						Log.Error("Cover art fetch failed: " + url);
						CompleteOnMain(onComplete, null);
						return;
					}
					m_imageCache[url] = data;
					CompleteOnMain(onComplete, data);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					CompleteOnMain(onComplete, null);
				}
			});
		}

		public override void GetTrackAudio(string trackId, Action<byte[]> onComplete)
		{
			if (!IsOnline() || string.IsNullOrEmpty(trackId))
			{
				CompleteOnMain(onComplete, null);
				return;
			}
			string url = GetTrackAudioURL(trackId);
			Task.Run(() =>
			{
				try
				{
					byte[] data = HttpGetBinary(url, true);
					if (data == null || data.Length <= 0)
					{
						Log.Error("Audio fetch failed: " + url);
						CompleteOnMain(onComplete, null);
						return;
					}
					byte[] captured = data;
					CompleteOnMain(onComplete, captured);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					CompleteOnMain(onComplete, null);
				}
			});
		}

		public override void GetRecentlyPlayed(Action<List<LegacyPulseObject>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseObject>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseObject> results = new List<LegacyPulseObject>();
				try
				{
					string url = BuildPulseUrl("recentlyPlayed", "count=50");
					JsonElement contents;
					if (TryGetContents(url, false, out contents) && contents.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement element in contents.EnumerateArray())
						{
							LegacyPulseObject mapped = MapMixedObject(element);
							if (mapped != null)
							{
								results.Add(mapped);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				List<LegacyPulseObject> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetPopularArtists(Action<List<LegacyPulseArtist>> onComplete)
		{
			// No pulse_v1 endpoint exposes popularity-ranked artists.
			CompleteOnMain(onComplete, new List<LegacyPulseArtist>());
		}

		public override void GetTopPlaylists(Action<List<LegacyPulsePlaylist>> onComplete)
		{
			// No pulse_v1 endpoint exposes score-ranked playlists.
			CompleteOnMain(onComplete, new List<LegacyPulsePlaylist>());
		}

		public override void GetRecentPlaylists(Action<List<LegacyPulsePlaylist>> onComplete)
		{
			// No pulse_v1 endpoint exposes recently-played playlists.
			CompleteOnMain(onComplete, new List<LegacyPulsePlaylist>());
		}

		public override void GetRecentlyAdded(Action<List<LegacyPulseObject>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseObject>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseObject> results = new List<LegacyPulseObject>();
				try
				{
					string url = BuildPulseUrl("albums", "type=newest&size=50&offset=0");
					List<PulseAlbum> albums;
					if (TryGetObject(url, true, out albums) && albums != null)
					{
						for (int index = 0; index < albums.Count; index++)
						{
							results.Add(MapAlbum(albums[index]));
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				List<LegacyPulseObject> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetGenres(Action<List<LegacyPulseGenre>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseGenre>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseGenre> results = new List<LegacyPulseGenre>();
				try
				{
					string url = BuildPulseUrl("genres", null);
					List<PulseGenre> genres;
					if (TryGetObject(url, true, out genres) && genres != null)
					{
						for (int index = 0; index < genres.Count; index++)
						{
							results.Add(MapGenre(genres[index]));
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareGenreByName);
				List<LegacyPulseGenre> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetTopItems(Action<List<LegacyPulseObject>> onComplete)
		{
			// No pulse_v1 endpoint exposes a ranked "top items" feed.
			CompleteOnMain(onComplete, new List<LegacyPulseObject>());
		}

		public override void GetTracksForGenre(string genre, Action<List<LegacyPulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseTrack>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseTrack> results = new List<LegacyPulseTrack>();
				try
				{
					string param = "genre=" + Uri.EscapeDataString(genre) + "&count=500&offset=0";
					string url = BuildPulseUrl("genreTracks", param);
					PulseGenreDetails details;
					if (TryGetObject(url, true, out details) && details != null && details.Tracks != null)
					{
						for (int index = 0; index < details.Tracks.Count; index++)
						{
							results.Add(MapTrack(details.Tracks[index]));
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareTrackByTitle);
				List<LegacyPulseTrack> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetFavorites(Action<List<LegacyPulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<LegacyPulseTrack>());
				return;
			}
			Task.Run(() =>
			{
				List<LegacyPulseTrack> results = new List<LegacyPulseTrack>();
				try
				{
					string url = BuildPulseUrl("favorites", null);
					PulseSearchData data;
					if (TryGetObject(url, true, out data) && data != null && data.Tracks != null)
					{
						for (int index = 0; index < data.Tracks.Count; index++)
						{
							results.Add(MapTrack(data.Tracks[index]));
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				List<LegacyPulseTrack> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void ReportTrackAnalytics(string trackId)
		{
			if (string.IsNullOrEmpty(trackId))
			{
				return;
			}
			if (!IsOnline())
			{
				return;
			}
			Task.Run(() =>
			{
				try
				{
					string url = BuildPulseUrl("reportTrackAnalytics", "id=" + Uri.EscapeDataString(trackId));
					HttpGet(url, false);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			});
		}

		private LegacyPulseObject MapMixedObject(JsonElement element)
		{
			JsonElement kindElement;
			if (!element.TryGetProperty("Kind", out kindElement))
			{
				return null;
			}
			if (kindElement.ValueKind != JsonValueKind.String)
			{
				return null;
			}
			string kind = kindElement.GetString();
			string raw = element.GetRawText();
			if (kind == "Track")
			{
				return MapTrack(PulseWire.Parse<PulseTrack>(raw));
			}
			if (kind == "Album")
			{
				return MapAlbum(PulseWire.Parse<PulseAlbum>(raw));
			}
			if (kind == "Artist")
			{
				return MapArtist(PulseWire.Parse<PulseArtist>(raw));
			}
			if (kind == "Playlist")
			{
				return MapPlaylist(PulseWire.Parse<PulsePlaylist>(raw));
			}
			return null;
		}

		private LegacyPulseTrack MapTrack(PulseTrack source)
		{
			LegacyPulseTrack track = new LegacyPulseTrack();
			if (source == null)
			{
				return track;
			}
			track.Id = source.Id;
			track.Title = source.Title;
			track.Artist = source.Artist;
			track.ArtistId = source.ArtistId;
			track.Album = source.Album;
			track.AlbumId = source.AlbumId;
			track.CoverArt = source.CoverArt;
			track.Duration = source.Duration;
			track.Starred = source.Starred;
			return track;
		}

		private LegacyPulseAlbum MapAlbum(PulseAlbum source)
		{
			LegacyPulseAlbum album = new LegacyPulseAlbum();
			if (source == null)
			{
				return album;
			}
			album.Id = source.Id;
			album.Name = source.Name;
			album.Artist = source.Artist;
			album.ArtistId = source.ArtistId;
			album.CoverArt = source.CoverArt;
			album.Year = source.Year;
			album.TrackCount = source.TrackCount;
			album.Duration = source.Duration;
			return album;
		}

		private LegacyPulseAlbum MapAlbumDetails(PulseAlbumDetails source)
		{
			LegacyPulseAlbum album = MapAlbum(source.Album);
			album.Id = source.Id;
			List<LegacyPulseTrack> tracks = new List<LegacyPulseTrack>();
			if (source.Tracks != null)
			{
				for (int index = 0; index < source.Tracks.Count; index++)
				{
					tracks.Add(MapTrack(source.Tracks[index]));
				}
			}
			album.Tracks = tracks;
			return album;
		}

		private LegacyPulseArtist MapArtist(PulseArtist source)
		{
			LegacyPulseArtist artist = new LegacyPulseArtist();
			if (source == null)
			{
				return artist;
			}
			artist.Id = source.Id;
			artist.Name = source.Name;
			artist.CoverArt = source.CoverArt;
			artist.AlbumCount = source.AlbumCount;
			// PlayCount has no PulseArtist counterpart; the contract carries
			// TrackCount instead. Leave PlayCount at zero.
			artist.PlayCount = 0;
			artist.Score = source.Score;
			artist.LastPlayed = source.LastPlayed;
			return artist;
		}

		private LegacyPulsePlaylist MapPlaylist(PulsePlaylist source)
		{
			LegacyPulsePlaylist playlist = new LegacyPulsePlaylist();
			if (source == null)
			{
				return playlist;
			}
			playlist.Id = source.Id;
			playlist.Name = source.Name;
			playlist.CoverArt = source.CoverArt;
			playlist.TrackCount = source.TrackCount;
			playlist.Duration = source.Duration;
			playlist.Score = source.Score;
			playlist.LastPlayed = source.LastPlayed;
			return playlist;
		}

		private LegacyPulsePlaylist MapPlaylistDetails(PulsePlaylistDetails source)
		{
			LegacyPulsePlaylist playlist = MapPlaylist(source.Playlist);
			playlist.Id = source.Id;
			List<LegacyPulseTrack> tracks = new List<LegacyPulseTrack>();
			if (source.Tracks != null)
			{
				for (int index = 0; index < source.Tracks.Count; index++)
				{
					tracks.Add(MapTrack(source.Tracks[index]));
				}
			}
			playlist.Tracks = tracks;
			return playlist;
		}

		private LegacyPulseGenre MapGenre(PulseGenre source)
		{
			LegacyPulseGenre genre = new LegacyPulseGenre();
			if (source == null)
			{
				return genre;
			}
			genre.Id = source.Id;
			if (string.IsNullOrEmpty(genre.Id))
			{
				genre.Id = source.Name;
			}
			genre.Name = source.Name;
			genre.TrackCount = source.TrackCount;
			genre.AlbumCount = source.AlbumCount;
			return genre;
		}

		private LegacyPulseSearchData MapSearchData(PulseSearchData source)
		{
			LegacyPulseSearchData data = new LegacyPulseSearchData();
			if (source.Artists != null)
			{
				for (int index = 0; index < source.Artists.Count; index++)
				{
					data.Artists.Add(MapArtist(source.Artists[index]));
				}
			}
			if (source.Albums != null)
			{
				for (int index = 0; index < source.Albums.Count; index++)
				{
					data.Albums.Add(MapAlbum(source.Albums[index]));
				}
			}
			if (source.Tracks != null)
			{
				for (int index = 0; index < source.Tracks.Count; index++)
				{
					data.Tracks.Add(MapTrack(source.Tracks[index]));
				}
			}
			if (source.Playlists != null)
			{
				for (int index = 0; index < source.Playlists.Count; index++)
				{
					data.Playlists.Add(MapPlaylist(source.Playlists[index]));
				}
			}
			return data;
		}

		private static void CompleteOnMain<T>(Action<T> onComplete, T value)
		{
			if (onComplete == null)
			{
				return;
			}
			MainThread.BeginInvokeOnMainThread(() =>
			{
				onComplete(value);
			});
		}

		private static int CompareArtistByName(LegacyPulseArtist first, LegacyPulseArtist second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareAlbumByName(LegacyPulseAlbum first, LegacyPulseAlbum second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int ComparePlaylistByName(LegacyPulsePlaylist first, LegacyPulsePlaylist second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareGenreByName(LegacyPulseGenre first, LegacyPulseGenre second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareTrackByTitle(LegacyPulseTrack first, LegacyPulseTrack second)
		{
			return string.Compare(first.Title, second.Title, StringComparison.OrdinalIgnoreCase);
		}
	}
}
