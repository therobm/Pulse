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
	// * domain objects the MediaClient contract still hands back to the
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

		private bool FetchObject<T>(string url, bool bCacheAllowed, out T value)
		{
			value = default(T);
			JsonElement contents;
			if (!FetchObject(url, bCacheAllowed, out contents))
			{
				return false;
			}
			value = PulseWire.Parse<T>(contents.GetRawText());
			return true;
		}

		// Pull the contents element out of a PulseResponse envelope. Returns
		// false when the request failed, the envelope was unparseable, the
		// status wasn't "ok", or there was no payload. On success the boxed
		// contents (System.Text.Json hands back a JsonElement for an object
		// field) is unwrapped so callers can re-parse it into a concrete type.
		private bool FetchObject(string url, bool bCacheAllowed, out JsonElement contents)
		{
			contents = default(JsonElement);
			string json = HttpGet(url, bCacheAllowed, true);
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


		// Treat a request as a fire-and-forget command: success is simply a
		// well-formed envelope with status "ok".
		private bool RunCommand(string url)
		{
			JsonElement discard;
			return FetchObject(url, false, out discard);
		}

		protected override bool Ping(out JsonElement response)
		{
			response = default(JsonElement);
			try
			{
				string url = m_baseUrl + "/pulse_v1/ping?u=" + Uri.EscapeDataString(m_user);
				string json = HttpGet(url, false, false);
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

		public override void GetTrack(string trackId, Action<PulseTrack> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulseTrack());
				return;
			}
			Task.Run(() =>
			{
				PulseTrack result = new PulseTrack();
				try
				{
					string url = BuildPulseUrl("track", "id=" + Uri.EscapeDataString(trackId));
					PulseTrack track;
					if (FetchObject(url, true, out track) && track != null)
					{
						result = track;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				PulseTrack captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetArtists(Action<List<PulseArtist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseArtist>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseArtist> results = new List<PulseArtist>();
				try
				{
					string url = BuildPulseUrl("artists", null);
					if (FetchObject(url, true, out List<PulseArtist> data))
					{
						if (data != null)
							results = data;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareArtistByName);
				List<PulseArtist> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetArtist(string artistId, Action<PulseArtistDetails> onComplete)
		{
			PulseArtistDetails artistDetails = null;
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulseArtistDetails());
				return;
			}
			Task.Run(() =>
			{
				try
				{
					string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
					FetchObject(url, true, out artistDetails);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				CompleteOnMain(onComplete, artistDetails);
			});
		}

		public override void GetArtistTracks(string artistId, Action<List<PulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseTrack>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseTrack> results = new List<PulseTrack>();
				try
				{
					string url = BuildPulseUrl("artistTracks", "id=" + Uri.EscapeDataString(artistId));
					PulseArtistFullDetails details;
					if (FetchObject(url, true, out details) && details != null && details.AlbumDetails != null)
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
								results.Add(album.Tracks[trackIndex]);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareTrackByTitle);
				List<PulseTrack> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetPodcasts(Action<List<PulsePodcastChannel>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePodcastChannel>());
				return;
			}
			Task.Run(() =>
			{
				// Server-side podcasts are still on the roadmap: the route exists
				// but returns status "not_implemented", so TryGetObject fails and
				// we hand back an empty list. The call is made anyway so the client
				// lights up automatically once the server starts serving them.
				List<PulsePodcastChannel> results = new List<PulsePodcastChannel>();
				try
				{
					string url = BuildPulseUrl("podcasts", null);
					List<PulsePodcastChannel> channels;
					if (FetchObject(url, true, out channels) && channels != null)
					{
						results = channels;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				List<PulsePodcastChannel> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void Search(string query, Action<PulseSearchData> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulseSearchData());
				return;
			}
			Task.Run(() =>
			{
				PulseSearchData result = new PulseSearchData();
				try
				{
					string param = "query=" + Uri.EscapeDataString(query)
						+ "&artistCount=10"
						+ "&albumCount=20"
						+ "&songCount=30";
					string url = BuildPulseUrl("search", param);
					PulseSearchData data;
					if (FetchObject(url, true, out data) && data != null)
					{
						result = data;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				PulseSearchData captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetArtistAlbums(string artistId, Action<List<PulseAlbum>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseAlbum>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseAlbum> results = new List<PulseAlbum>();
				try
				{
					string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
					PulseArtistDetails details;
					if (FetchObject(url, true, out details) && details != null && details.Albums != null)
					{
						for (int index = 0; index < details.Albums.Count; index++)
						{
							results.Add(details.Albums[index]);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				List<PulseAlbum> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetAlbum(string albumId, Action<PulseAlbumDetails> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulseAlbumDetails());
				return;
			}
			Task.Run(() =>
			{
				PulseAlbumDetails result = new PulseAlbumDetails();
				try
				{
					string url = BuildPulseUrl("album", "id=" + Uri.EscapeDataString(albumId));
					PulseAlbumDetails details;
					if (FetchObject(url, true, out details) && details != null && details.Album != null)
					{
						result = details;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				PulseAlbumDetails captured = result;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetAlbums(Action<List<PulseAlbum>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseAlbum>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseAlbum> results = new List<PulseAlbum>();
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
						if (!FetchObject(url, true, out albums) || albums == null || albums.Count == 0)
						{
							break;
						}
						for (int index = 0; index < albums.Count; index++)
						{
							results.Add(albums[index]);
						}
						offset = offset + albums.Count;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareAlbumByName);
				List<PulseAlbum> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void CreatePlaylist(string name, Action<PulsePlaylist> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, null);
				return;
			}
			Task.Run(() =>
			{
				PulsePlaylist created = null;
				try
				{
					string url = BuildPulseUrl("createPlaylist", "name=" + Uri.EscapeDataString(name));
					PulsePlaylistDetails details;
					if (FetchObject(url, false, out details) && details != null && details.Playlist != null)
					{
						created = details.Playlist;
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				PulsePlaylist captured = created;
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

		public override void ReorderPlaylist(string playlistId, int fromIndex, int toIndex, List<PulseTrack> newOrder, Action<bool> onComplete)
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

		public override void GetPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePlaylist>());
				return;
			}
			Task.Run(() =>
			{
				List<PulsePlaylist> results = new List<PulsePlaylist>();
				try
				{
					string url = BuildPulseUrl("playlists", null);
					List<PulsePlaylist> playlists;
					if (FetchObject(url, true, out playlists) && playlists != null)
					{
						for (int index = 0; index < playlists.Count; index++)
						{
							results.Add(playlists[index]);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(ComparePlaylistByName);
				List<PulsePlaylist> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetPlaylist(string playlistId, Action<PulsePlaylistDetails> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulsePlaylistDetails());
				return;
			}
			Task.Run(() =>
			{
				PulsePlaylistDetails result = new PulsePlaylistDetails();
				try
				{
					string url = BuildPulseUrl("playlist", "id=" + Uri.EscapeDataString(playlistId));
					FetchObject(url, true, out result);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				CompleteOnMain(onComplete, result);
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

		public override void GetRecentlyPlayed(Action<List<PulseObject>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseObject>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseObject> results = new List<PulseObject>();
				try
				{
					string url = BuildPulseUrl("recentlyPlayed", "count=50");
					JsonElement contents;
					if (FetchObject(url, false, out contents) && contents.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement element in contents.EnumerateArray())
						{
							PulseObject mapped = MapMixedObject(element);
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
				List<PulseObject> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetPopularArtists(Action<List<PulseArtist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseArtist>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseArtist> captured = FetchTypedItems<PulseArtist>("topItems", "types=artist&count=20");
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetTopPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePlaylist>());
				return;
			}
			Task.Run(() =>
			{
				List<PulsePlaylist> captured = FetchTypedItems<PulsePlaylist>("topItems", "types=playlist&count=20");
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetRecentPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePlaylist>());
				return;
			}
			Task.Run(() =>
			{
				List<PulsePlaylist> captured = FetchTypedItems<PulsePlaylist>("recentlyPlayed", "types=playlist&count=20");
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetGenres(Action<List<PulseGenre>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseGenre>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseGenre> results = new List<PulseGenre>();
				try
				{
					string url = BuildPulseUrl("genres", null);
					List<PulseGenre> genres;
					if (FetchObject(url, true, out genres) && genres != null)
					{
						for (int index = 0; index < genres.Count; index++)
						{
							results.Add(genres[index]);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareGenreByName);
				List<PulseGenre> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetTopItems(Action<List<PulseObject>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseObject>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseObject> captured = FetchTypedItems<PulseObject>("topItems", "count=50");
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetTracksForGenre(string genre, Action<List<PulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseTrack>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseTrack> results = new List<PulseTrack>();
				try
				{
					string param = "genre=" + Uri.EscapeDataString(genre) + "&count=500&offset=0";
					string url = BuildPulseUrl("genreTracks", param);
					PulseGenreDetails details;
					if (FetchObject(url, true, out details) && details != null && details.Tracks != null)
					{
						for (int index = 0; index < details.Tracks.Count; index++)
						{
							results.Add(details.Tracks[index]);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				results.Sort(CompareTrackByTitle);
				List<PulseTrack> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void GetFavorites(Action<List<PulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseTrack>());
				return;
			}
			Task.Run(() =>
			{
				List<PulseTrack> results = new List<PulseTrack>();
				try
				{
					string url = BuildPulseUrl("favorites", null);
					PulseSearchData data;
					if (FetchObject(url, true, out data) && data != null && data.Tracks != null)
					{
						for (int index = 0; index < data.Tracks.Count; index++)
						{
							results.Add(data.Tracks[index]);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				List<PulseTrack> captured = results;
				CompleteOnMain(onComplete, captured);
			});
		}

		public override void ReportAnalytics(string mediaId, PulseAPI.CSharp.eDataType mediaType, PulseAnalytics.eAction action)
		{
			if (string.IsNullOrEmpty(mediaId))
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
					PulseAnalytics analytics = new PulseAnalytics();
					analytics.MediaId = mediaId;
					analytics.MediaType = mediaType;
					analytics.Action = action;

					string url = BuildPulseUrl("reportAnalytics", null);
					string json = PulseWire.Serialize(analytics);
					HttpPostJson(url, json, true);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			});
		}

		// The recentlyPlayed feed is heterogeneous: each element carries its own
		// Kind discriminator. Probe Kind off the raw element, then re-parse into
		// the matching concrete PulseObject subtype.
		private static PulseObject MapMixedObject(JsonElement element)
		{
			if (element.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			string raw = element.GetRawText();
			PulseObject probe = PulseWire.Parse<PulseObject>(raw);
			if (probe == null)
			{
				return null;
			}
			switch (probe.Kind)
			{
				case eDataType.Track:
					return PulseWire.Parse<PulseTrack>(raw);
				case eDataType.Album:
					return PulseWire.Parse<PulseAlbum>(raw);
				case eDataType.Playlist:
					return PulseWire.Parse<PulsePlaylist>(raw);
				case eDataType.Artist:
					return PulseWire.Parse<PulseArtist>(raw);
				case eDataType.Genre:
					return PulseWire.Parse<PulseGenre>(raw);
				default:
					return probe;
			}
		}

		// Fetches a heterogeneous item feed (topItems / recentlyPlayed) and keeps
		// only the elements whose Kind maps to the requested concrete type. Runs
		// on the caller's thread, so callers wrap it in Task.Run.
		private List<T> FetchTypedItems<T>(string route, string param) where T : PulseObject
		{
			List<T> results = new List<T>();
			try
			{
				string url = BuildPulseUrl(route, param);
				JsonElement contents;
				if (FetchObject(url, false, out contents) && contents.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement element in contents.EnumerateArray())
					{
						PulseObject mapped = MapMixedObject(element);
						T typed = mapped as T;
						if (typed != null)
						{
							results.Add(typed);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			return results;
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

		private static int CompareArtistByName(PulseArtist first, PulseArtist second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareAlbumByName(PulseAlbum first, PulseAlbum second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int ComparePlaylistByName(PulsePlaylist first, PulsePlaylist second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareGenreByName(PulseGenre first, PulseGenre second)
		{
			return string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
		}

		private static int CompareTrackByTitle(PulseTrack first, PulseTrack second)
		{
			return string.Compare(first.Title, second.Title, StringComparison.OrdinalIgnoreCase);
		}
	}
}
