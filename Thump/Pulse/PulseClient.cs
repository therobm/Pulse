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

		public PulseClient(ThumpCache cache, IMediaClientHost host) : base(cache, host)
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

		private void FetchObject<T>(string url, bool bCacheAllowed, Action<T> onComplete)
		{
			FetchObject(url, bCacheAllowed, (contents)=>
			{
				T value = default(T);
				if(contents != null)
				{
					JsonElement jsonElement = (JsonElement)contents;
					string json = jsonElement.GetRawText();
					if (!string.IsNullOrEmpty(json))
					{
						value = PulseWire.Parse<T>(json);
					}
				}
			
				onComplete(value);
			});
		}

		// Pull the contents element out of a PulseResponse envelope. Returns
		// false when the request failed, the envelope was unparseable, the
		// status wasn't "ok", or there was no payload. On success the boxed
		// contents (System.Text.Json hands back a JsonElement for an object
		// field) is unwrapped so callers can re-parse it into a concrete type.
		private void FetchObject(string url, bool bCacheAllowed, Action<object> onComplete)
		{
			if (onComplete == null)
			{
				return;
			}

			GetHTTP(url, (json)=>
			{
				object contents = null;
				if (string.IsNullOrEmpty(json))
				{
					CompleteOnMain(onComplete, contents);
					return;
				}
				PulseResponse response = PulseWire.Parse<PulseResponse>(json);
				
				if (response == null || response.contents == null)
				{
					Log.Error("Unparseable pulse response: " + url);
				}
				else if (response.status != "ok")
				{
					Log.Error("Pulse endpoint returned status '" + response.status + "': " + url);
				}
				else 
				{ 
					contents = response.contents;
				}
				CompleteOnMain(onComplete, contents);
			}, bCacheAllowed, true);
		
		}


		// Treat a request as a fire-and-forget command: dispatch it and don't
		// wait on the result. FetchObject logs a non-ok envelope on its own.
		private void RunCommand(string url)
		{
			FetchObject(url, false, (contents) => { });
		}

		// Synchronous fetch+parse for the few callers that page in a loop and so
		// can't drive the callback FetchObject. Blocks on the calling thread, so
		// callers must run it inside their own Task.Run.
		private bool FetchObjectSync<T>(string url, bool bCacheAllowed, out T value)
		{
			value = default(T);
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
			JsonElement contents = (JsonElement)response.contents;
			if (contents.ValueKind == JsonValueKind.Undefined || contents.ValueKind == JsonValueKind.Null)
			{
				return false;
			}
			value = PulseWire.Parse<T>(contents.GetRawText());
			return true;
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
					OnPingResult(false);
					return false;
				}
				JsonDocument doc = JsonDocument.Parse(json);
				response = doc.RootElement;
				OnPingResult(true);
				return true;
			}
			catch (Exception ex)
			{
				// Online/offline polling - a failure here just means the server
				// isn't reachable right now, so don't make noise in the log.
				Log.Exception(ex);
			}
			OnPingResult(false);
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
			string url = BuildPulseUrl("track", "id=" + Uri.EscapeDataString(trackId));
			FetchObject<PulseTrack>(url, true, (track) =>
			{
				PulseTrack result = new PulseTrack();
				if (track != null)
				{
					result = track;
				}
				if (onComplete != null)
				{
					onComplete(result);
				}
			});
		}

		public override void GetArtists(Action<List<PulseArtist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseArtist>());
				return;
			}
			string url = BuildPulseUrl("artists", null);
			FetchObject<List<PulseArtist>>(url, true, (data) =>
			{
				List<PulseArtist> results = new List<PulseArtist>();
				if (data != null)
				{
					results = data;
				}
				results.Sort(CompareArtistByName);
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void GetArtist(string artistId, Action<PulseArtistDetails> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulseArtistDetails());
				return;
			}
			string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
			FetchObject<PulseArtistDetails>(url, true, (artistDetails) =>
			{
				if (onComplete != null)
				{
					onComplete(artistDetails);
				}
			});
		}

		public override void GetArtistTracks(string artistId, Action<List<PulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseTrack>());
				return;
			}
			string url = BuildPulseUrl("artistTracks", "id=" + Uri.EscapeDataString(artistId));
			FetchObject<PulseArtistFullDetails>(url, true, (details) =>
			{
				List<PulseTrack> results = new List<PulseTrack>();
				if (details != null && details.AlbumDetails != null)
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
				results.Sort(CompareTrackByTitle);
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void GetPodcasts(Action<List<PulsePodcastChannel>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePodcastChannel>());
				return;
			}
			// Server-side podcasts are still on the roadmap: the route exists
			// but returns status "not_implemented", so the fetch fails and we
			// hand back an empty list. The call is made anyway so the client
			// lights up automatically once the server starts serving them.
			string url = BuildPulseUrl("podcasts", null);
			FetchObject<List<PulsePodcastChannel>>(url, true, (channels) =>
			{
				List<PulsePodcastChannel> results = new List<PulsePodcastChannel>();
				if (channels != null)
				{
					results = channels;
				}
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void Search(string query, Action<PulseSearchData> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulseSearchData());
				return;
			}
			string param = "query=" + Uri.EscapeDataString(query)
				+ "&artistCount=10"
				+ "&albumCount=20"
				+ "&songCount=30";
			string url = BuildPulseUrl("search", param);
			FetchObject<PulseSearchData>(url, true, (data) =>
			{
				PulseSearchData result = new PulseSearchData();
				if (data != null)
				{
					result = data;
				}
				if (onComplete != null)
				{
					onComplete(result);
				}
			});
		}

		public override void GetArtistAlbums(string artistId, Action<List<PulseAlbum>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseAlbum>());
				return;
			}
			string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
			FetchObject<PulseArtistDetails>(url, true, (details) =>
			{
				List<PulseAlbum> results = new List<PulseAlbum>();
				if (details != null && details.Albums != null)
				{
					for (int index = 0; index < details.Albums.Count; index++)
					{
						results.Add(details.Albums[index]);
					}
				}
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void GetAlbum(string albumId, Action<PulseAlbumDetails> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulseAlbumDetails());
				return;
			}
			string url = BuildPulseUrl("album", "id=" + Uri.EscapeDataString(albumId));
			FetchObject<PulseAlbumDetails>(url, true, (details) =>
			{
				PulseAlbumDetails result = new PulseAlbumDetails();
				if (details != null && details.Album != null)
				{
					result = details;
				}
				if (onComplete != null)
				{
					onComplete(result);
				}
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
						if (!FetchObjectSync(url, true, out albums) || albums == null || albums.Count == 0)
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
			string url = BuildPulseUrl("createPlaylist", "name=" + Uri.EscapeDataString(name));
			FetchObject<PulsePlaylistDetails>(url, false, (details) =>
			{
				PulsePlaylist created = null;
				if (details != null && details.Playlist != null)
				{
					created = details.Playlist;
				}
				if (onComplete != null)
				{
					onComplete(created);
				}
			});
		}

		public override void RenamePlaylist(string playlistId, string newName, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			string param = "playlistId=" + Uri.EscapeDataString(playlistId)
				+ "&name=" + Uri.EscapeDataString(newName);
			RunCommand(BuildPulseUrl("updatePlaylist", param));
			CompleteOnMain(onComplete, true);
		}

		public override void Favorite(string trackId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			string param = "id=" + Uri.EscapeDataString(trackId) + "&type=track";
			RunCommand(BuildPulseUrl("favorite", param));
			CompleteOnMain(onComplete, true);
		}

		public override void Unfavorite(string trackId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			string param = "id=" + Uri.EscapeDataString(trackId) + "&type=track";
			RunCommand(BuildPulseUrl("unfavorite", param));
			CompleteOnMain(onComplete, true);
		}

		public override void DeletePlaylist(string playlistId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			string param = "id=" + Uri.EscapeDataString(playlistId);
			RunCommand(BuildPulseUrl("deletePlaylist", param));
			CompleteOnMain(onComplete, true);
		}

		public override void AddTrackToPlaylist(string playlistId, string songId, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			string param = "playlistId=" + Uri.EscapeDataString(playlistId)
				+ "&songIdToAdd=" + Uri.EscapeDataString(songId);
			RunCommand(BuildPulseUrl("updatePlaylist", param));
			CompleteOnMain(onComplete, true);
		}

		public override void RemoveTrackFromPlaylist(string playlistId, int songIndex, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			string param = "playlistId=" + Uri.EscapeDataString(playlistId)
				+ "&songIndexToRemove=" + songIndex;
			RunCommand(BuildPulseUrl("updatePlaylist", param));
			CompleteOnMain(onComplete, true);
		}

		public override void ReorderPlaylist(string playlistId, int fromIndex, int toIndex, List<PulseTrack> newOrder, Action<bool> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, false);
				return;
			}
			// Same strategy as the Subsonic client: remove every entry from the
			// first changed index to the end (high index first), then re-add the
			// new tail in order. updatePlaylist accepts repeated
			// songIndexToRemove and songIdToAdd parameters.
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
			RunCommand(BuildPulseUrl("updatePlaylist", param.ToString()));
			CompleteOnMain(onComplete, true);
		}

		public override void GetPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePlaylist>());
				return;
			}
			string url = BuildPulseUrl("playlists", null);
			FetchObject<List<PulsePlaylist>>(url, true, (playlists) =>
			{
				List<PulsePlaylist> results = new List<PulsePlaylist>();
				if (playlists != null)
				{
					for (int index = 0; index < playlists.Count; index++)
					{
						results.Add(playlists[index]);
					}
				}
				results.Sort(ComparePlaylistByName);
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void GetPlaylist(string playlistId, Action<PulsePlaylistDetails> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new PulsePlaylistDetails());
				return;
			}
			string url = BuildPulseUrl("playlist", "id=" + Uri.EscapeDataString(playlistId));
			FetchObject<PulsePlaylistDetails>(url, true, (details) =>
			{
				PulsePlaylistDetails result = new PulsePlaylistDetails();
				if (details != null)
				{
					result = details;
				}
				if (onComplete != null)
				{
					onComplete(result);
				}
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

			GetHTTPImage(url, (data)=>
			{
				try
				{
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
			}, true);
		}

		public override void GetTrackAudio(string trackId, Action<byte[]> onComplete)
		{
			if (!IsOnline() || string.IsNullOrEmpty(trackId))
			{
				CompleteOnMain(onComplete, null);
				return;
			}
			string url = GetTrackAudioURL(trackId);
			GetHTTPAudio(url, (data)=>
			{
				try
				{
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
			}, true);
		}

		public override void GetRecentlyPlayed(Action<List<PulseObject>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseObject>());
				return;
			}
			string url = BuildPulseUrl("recentlyPlayed", "count=50");
			FetchObject(url, false, (contents) =>
			{
				List<PulseObject> results = new List<PulseObject>();
				if (contents != null) 
				{ 
					JsonElement jsonElement = (JsonElement)contents;
					if (jsonElement.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement element in jsonElement.EnumerateArray())
						{
							PulseObject mapped = MapMixedObject(element);
							if (mapped != null)
							{
								results.Add(mapped);
							}
						}
					}
				}
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void GetPopularArtists(Action<List<PulseArtist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseArtist>());
				return;
			}
			FetchTypedItems<PulseArtist>("topItems", "types=artist&count=20", (items) =>
			{
				if (onComplete != null)
				{
					onComplete(items);
				}
			});
		}

		public override void GetTopPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePlaylist>());
				return;
			}
			FetchTypedItems<PulsePlaylist>("topItems", "types=playlist&count=20", (items) =>
			{
				if (onComplete != null)
				{
					onComplete(items);
				}
			});
		}

		public override void GetRecentPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulsePlaylist>());
				return;
			}
			FetchTypedItems<PulsePlaylist>("recentlyPlayed", "types=playlist&count=20", (items) =>
			{
				if (onComplete != null)
				{
					onComplete(items);
				}
			});
		}

		public override void GetGenres(Action<List<PulseGenre>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseGenre>());
				return;
			}
			string url = BuildPulseUrl("genres", null);
			FetchObject<List<PulseGenre>>(url, true, (genres) =>
			{
				List<PulseGenre> results = new List<PulseGenre>();
				if (genres != null)
				{
					for (int index = 0; index < genres.Count; index++)
					{
						results.Add(genres[index]);
					}
				}
				results.Sort(CompareGenreByName);
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void GetTopItems(Action<List<PulseObject>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseObject>());
				return;
			}
			FetchTypedItems<PulseObject>("topItems", "count=50", (items) =>
			{
				if (onComplete != null)
				{
					onComplete(items);
				}
			});
		}

		public override void GetTracksForGenre(string genre, Action<List<PulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseTrack>());
				return;
			}
			string param = "genre=" + Uri.EscapeDataString(genre) + "&count=500&offset=0";
			string url = BuildPulseUrl("genreTracks", param);
			FetchObject<PulseGenreDetails>(url, true, (details) =>
			{
				List<PulseTrack> results = new List<PulseTrack>();
				if (details != null && details.Tracks != null)
				{
					for (int index = 0; index < details.Tracks.Count; index++)
					{
						results.Add(details.Tracks[index]);
					}
				}
				results.Sort(CompareTrackByTitle);
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
		}

		public override void GetFavorites(Action<List<PulseTrack>> onComplete)
		{
			if (!IsOnline())
			{
				CompleteOnMain(onComplete, new List<PulseTrack>());
				return;
			}
			string url = BuildPulseUrl("favorites", null);
			FetchObject<PulseSearchData>(url, true, (data) =>
			{
				List<PulseTrack> results = new List<PulseTrack>();
				if (data != null && data.Tracks != null)
				{
					for (int index = 0; index < data.Tracks.Count; index++)
					{
						results.Add(data.Tracks[index]);
					}
				}
				if (onComplete != null)
				{
					onComplete(results);
				}
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

		/// <summary>
		/// POST a batch of structured product-analytics events to the server's
		/// pulse_v1/ingestLog route. Swallows its own failures: the analytics
		/// path must never feed errors back into Log.* or it loops.
		/// </summary>
		public override void PostAnalytics(PulseLogBatch batch)
		{
			if (batch == null)
			{
				return;
			}
			if (batch.Events == null || batch.Events.Count == 0)
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
					string url = BuildPulseUrl("ingestLog", null);
					string json = PulseWire.Serialize(batch);
					// logPerf=false: analytics POSTs must not spam the perf log.
					HttpPostJson(url, json, false);
				}
				catch (Exception ex)
				{
					// CRITICAL: the analytics path swallows its OWN failures.
					// Do NOT call Log.Exception / Log.Error / any Log.* here -- that
					// would feed the failure back into the pipeline (recursion / loop).
					System.Diagnostics.Debug.WriteLine("[analytics] PostAnalytics failed: " + ex.Message);
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
		// only the elements whose Kind maps to the requested concrete type.
		// Delivers the filtered list through the callback FetchObject.
		private void FetchTypedItems<T>(string route, string param, Action<List<T>> onComplete) where T : PulseObject
		{
			string url = BuildPulseUrl(route, param);
			FetchObject(url, false, (contents) =>
			{

				List<T> results = new List<T>();
				if (contents != null) 
				{ 
					JsonElement jsonElement = (JsonElement)contents;
					if (jsonElement.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement element in jsonElement.EnumerateArray())
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
				if (onComplete != null)
				{
					onComplete(results);
				}
			});
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
