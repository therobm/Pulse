using Microsoft.Maui.Controls;
using PulseAPI.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Thump.Data;
using System.IO;
using Microsoft.Maui.Storage;



#if ANDROID
using Android.Graphics;
#endif

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
		/// <summary>
		/// Maximum age, in seconds, that a NetworkFirst cache entry can be and
		/// still be served without a network round-trip. Older entries fall through
		/// to the normal NetworkFirst path (network, then stale cache on failure).
		/// </summary>
		public const int s_cacheFreshnessSeconds = 60;

		// In-memory hot cache (L1) for cover art, in front of the on-disk cache
		// (L2, via ThumpCache). Keyed by the resolved cover-art URL so repeated
		// requests for visible tiles return instantly without a queue hop or a
		// SQLite read.
		private ConcurrentDictionary<string, ImageSource> m_imageCache = new ConcurrentDictionary<string, ImageSource>();
		private ConcurrentDictionary<string, byte[]> m_imageBytesCache = new ConcurrentDictionary<string, byte[]>();

		ThumpCache m_cache;

		public PulseClient(ThumpCache cache, IMediaClientHost host) : base(host)
		{
			m_cache = cache;
		}

		private string BuildPulseUrl(string endpoint, string extraParams)
		{
			string url = m_baseUrl + "/pulse_v1/" + endpoint + "?u=" + Uri.EscapeDataString(m_user);
			string token = ThumpSettings.GetToken();
			if (!string.IsNullOrEmpty(token))
			{
				url = url + "&token=" + Uri.EscapeDataString(token);
			}
			if (!string.IsNullOrEmpty(extraParams))
			{
				url = url + "&" + extraParams;
			}
			return url;
		}

		public override byte[] GetTrackAudioFromCache(string trackId)
		{
			if (string.IsNullOrEmpty(trackId))
			{
				return null;
			}

			//todo this needs to be the query url not some random bullshit
			string url = GetTrackAudioURL(trackId);

			//we only stream from disk, whoever wanted this should have cached ahead
			byte[] trackData = m_cache.GetTrackAudioFromCache(url);
			return trackData;
		}
		private void FetchListObject<T>(string url, eMediaCacheStrategy cacheStrategy, Action<List<T>> onComplete) where T : PulseObject
		{
			if (cacheStrategy == eMediaCacheStrategy.CacheFirst && GetCachedPulseData<T>(url, out List<T> data))
			{
				CompleteOnMain(onComplete, data);
				return;
			}
			if (cacheStrategy == eMediaCacheStrategy.NetworkFirst && GetRecentCachedPulseData<T>(url, s_cacheFreshnessSeconds, out List<T> freshData))
			{
				CompleteOnMain(onComplete, freshData);
				return;
			}
			GetHTTP(url, (json) =>
			{
				List<T> contents = null;

				if (!string.IsNullOrEmpty(json))
				{
					PulseResponse response = PulseWire.Parse<PulseResponse>(json);

					if (response != null && response.contents != null)
					{
						JsonElement jsonElement = (JsonElement)response.contents;
						if (jsonElement.ValueKind != JsonValueKind.Undefined && jsonElement.ValueKind != JsonValueKind.Null)
						{
							contents = PulseWire.Parse<List<T>>(jsonElement.GetRawText());
						}
						else
						{
							Log.Error("Unparseable pulse response: " + url);
						}
					}
				}
			
				if(contents != null)
				{
					CacheQueryPulseData(url, contents);
				}
				else if (cacheStrategy != eMediaCacheStrategy.NetworkOnly)
				{
					GetCachedPulseData<T>(url, out contents);
				}

				CompleteOnMain(onComplete, contents);
			});
		}

		

		// Pull the contents element out of a PulseResponse envelope. Returns
		// false when the request failed, the envelope was unparseable, the
		// status wasn't "ok", or there was no payload. On success the boxed
		// contents (System.Text.Json hands back a JsonElement for an object
		// field) is unwrapped so callers can re-parse it into a concrete type.
		private void FetchObject<T>(string url, eMediaCacheStrategy cacheStrategy, Action<T> onComplete) where T : PulseObject
		{
			if (onComplete == null)
			{
				return;
			}
			if (cacheStrategy == eMediaCacheStrategy.CacheFirst && GetCachedPulseData<T>(url, out T data))
			{
				CompleteOnMain(onComplete, data);
				return;
			}
			if (cacheStrategy == eMediaCacheStrategy.NetworkFirst && GetRecentCachedPulseData<T>(url, s_cacheFreshnessSeconds, out T freshData))
			{
				CompleteOnMain(onComplete, freshData);
				return;
			}

			GetHTTP(url, (json) =>
			{
				T contents = null;
				if (!string.IsNullOrEmpty(json))
				{
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
						JsonElement rawContents = (JsonElement)response.contents;
						contents = PulseWire.Parse<T>(rawContents.GetRawText());
					}
				}

				if (contents != null)
				{
					CacheQueryPulseData(url, contents);
				}
				else if (cacheStrategy != eMediaCacheStrategy.NetworkOnly)
				{
					GetCachedPulseData<T>(url, out contents);
				}

				CompleteOnMain(onComplete, contents);
			});

		}


		// Treat a request as a fire-and-forget command: dispatch it and don't
		// wait on the result. FetchObject logs a non-ok envelope on its own.
		private void RunCommand(string url)
		{
			FetchObject<PulseObject>(url, eMediaCacheStrategy.NetworkOnly, (contents) => { });
		}

		protected override bool Ping(out JsonElement response)
		{
			response = default(JsonElement);
			try
			{
				string url = BuildPulseUrl("ping", "");
				string json = HttpGet(url, true, CancellationToken.None);
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
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("track", "id=" + Uri.EscapeDataString(trackId));
				FetchObject<PulseTrack>(url, eMediaCacheStrategy.NetworkFirst, (track) =>
				{
					if (onComplete != null)
					{
						onComplete(track);
					}
				});
			});
		}

		public override void GetArtists(Action<List<PulseArtist>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("artists", null);
				FetchListObject<PulseArtist>(url, eMediaCacheStrategy.NetworkFirst, (data) =>
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
			});
		}

		public override void GetArtist(string artistId, Action<PulseArtistDetails> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
				FetchObject<PulseArtistDetails>(url, eMediaCacheStrategy.NetworkFirst, (artistDetails) =>
				{
					if (onComplete != null)
					{
						onComplete(artistDetails);
					}
				});
			});
		}

		public override void GetArtistTracks(string artistId, Action<List<PulseTrack>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("artistTracks", "id=" + Uri.EscapeDataString(artistId));
				FetchObject<PulseArtistFullDetails>(url, eMediaCacheStrategy.NetworkFirst, (details) =>
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
			});
		}

		public override void GetPodcasts(Action<List<PulsePodcast>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("podcasts", null);
				FetchListObject<PulsePodcast>(url, eMediaCacheStrategy.NetworkFirst, (channels) =>
				{
					List<PulsePodcast> results = new List<PulsePodcast>();
					if (channels != null)
					{
						results = channels;
					}
					if (onComplete != null)
					{
						onComplete(results);
					}
				});
			});
		}

		public override void GetAllPodcasts(Action<List<PulsePodcast>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("allPodcasts", null);
				FetchListObject<PulsePodcast>(url, eMediaCacheStrategy.NetworkFirst, (channels) =>
				{
					List<PulsePodcast> results = new List<PulsePodcast>();
					if (channels != null)
					{
						results = channels;
					}
					if (onComplete != null)
					{
						onComplete(results);
					}
				});
			});
		}

		public override void GetPodcast(string podcastId, Action<PulsePodcastDetails> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("podcast", "id=" + Uri.EscapeDataString(podcastId));
				FetchObject<PulsePodcastDetails>(url, eMediaCacheStrategy.NetworkFirst, (details) =>
				{
					if (onComplete != null)
					{
						onComplete(details);
					}
				});
			});
		}

		public override void GetAudiobooks(Action<List<PulseAudiobook>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("audiobooks", null);
				FetchListObject<PulseAudiobook>(url, eMediaCacheStrategy.NetworkFirst, (books) =>
				{
					List<PulseAudiobook> results = new List<PulseAudiobook>();
					if (books != null)
					{
						results = books;
					}
					if (onComplete != null)
					{
						onComplete(results);
					}
				});
			});
		}

		public override void GetAudiobook(string audiobookId, Action<PulseAudiobookDetails> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("audiobook", "id=" + Uri.EscapeDataString(audiobookId));
				FetchObject<PulseAudiobookDetails>(url, eMediaCacheStrategy.NetworkFirst, (details) =>
				{
					if (onComplete != null)
					{
						onComplete(details);
					}
				});
			});
		}

		public override void SearchPodcasts(string query, Action<List<PulsePodcast>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("searchPodcasts", "query=" + Uri.EscapeDataString(query));
				FetchListObject<PulsePodcast>(url, eMediaCacheStrategy.NetworkFirst, (hits) =>
				{
					List<PulsePodcast> results = new List<PulsePodcast>();
					if (hits != null)
					{
						results = hits;
					}
					if (onComplete != null)
					{
						onComplete(results);
					}
				});
			});
		}

		public override void AddPodcast(string feedUrl, bool subscribe, Action<PulsePodcast> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("addPodcast", "feedUrl=" + Uri.EscapeDataString(feedUrl) + "&subscribe=" + (subscribe ? "1" : "0"));
				FetchObject<PulsePodcast>(url, eMediaCacheStrategy.NetworkOnly, (podcast) =>
				{
					if (onComplete != null)
					{
						onComplete(podcast);
					}
				});
			});
		}

		public override void SubscribePodcast(string podcastId, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "id=" + Uri.EscapeDataString(podcastId);
				RunCommand(BuildPulseUrl("subscribePodcast", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void UnsubscribePodcast(string podcastId, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "id=" + Uri.EscapeDataString(podcastId);
				RunCommand(BuildPulseUrl("unsubscribePodcast", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void UpdatePodcast(string podcastId, string retentionPolicy, int retentionValue,  bool autoDownload, Action<PulsePodcast> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "id=" + Uri.EscapeDataString(podcastId)
					+ "&retentionPolicy=" + Uri.EscapeDataString(retentionPolicy)
					+ "&retentionValue=" + retentionValue
					+ "&autoDownload=" + (autoDownload ? "1" : "0");
				FetchObject<PulsePodcast>(BuildPulseUrl("updatePodcast", param), eMediaCacheStrategy.NetworkOnly, (podcast) =>
				{
					if (onComplete != null)
					{
						onComplete(podcast);
					}
				});
			});
		}

		public override void SaveEpisodeProgress(string episodeId, int positionSeconds)
		{
			string param = "id=" + Uri.EscapeDataString(episodeId) + "&positionSeconds=" + positionSeconds;
			RunCommand(BuildPulseUrl("episodeProgress", param));
		}

		public override void Search(string query, Action<PulseSearchData> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "query=" + Uri.EscapeDataString(query)
					+ "&artistCount=10"
					+ "&albumCount=20"
					+ "&songCount=30";
				string url = BuildPulseUrl("search", param);
				FetchObject<PulseSearchData>(url, eMediaCacheStrategy.NetworkFirst, (data) =>
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
			});
		}

		public override void GetArtistAlbums(string artistId, Action<List<PulseAlbum>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("artist", "id=" + Uri.EscapeDataString(artistId));
				FetchObject<PulseArtistDetails>(url, eMediaCacheStrategy.NetworkFirst, (details) =>
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
			});
		}

		public override void GetAlbum(string albumId, Action<PulseAlbumDetails> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("album", "id=" + Uri.EscapeDataString(albumId));
				FetchObject<PulseAlbumDetails>(url, eMediaCacheStrategy.NetworkFirst, (details) =>
				{
					if (onComplete != null)
					{
						onComplete(details);
					}
				});
			});
		}

		public override void GetAlbums(Action<List<PulseAlbum>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				// One trimmed whole-catalog fetch (albumsLite) rather than paging
				// the full album rows. The grid renders every tile and lazy-loads
				// cover images, so the lean payload (compressed) arrives in a
				// single request instead of several blocking pages.
				string url = BuildPulseUrl("albumsLite", null);
				FetchListObject<PulseAlbum>(url, eMediaCacheStrategy.NetworkFirst, (data) =>
				{
					List<PulseAlbum> results = new List<PulseAlbum>();
					if (data != null)
					{
						results = data;
					}
					results.Sort(CompareAlbumByName);
					if (onComplete != null)
					{
						onComplete(results);
					}
				});
			});
		}

		public override void CreatePlaylist(string name, Action<PulsePlaylist> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("createPlaylist", "name=" + Uri.EscapeDataString(name));
				FetchObject<PulsePlaylistDetails>(url, eMediaCacheStrategy.NetworkOnly, (details) =>
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
			});
		}

		public override void RenamePlaylist(string playlistId, string newName, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "playlistId=" + Uri.EscapeDataString(playlistId)
					+ "&name=" + Uri.EscapeDataString(newName);
				RunCommand(BuildPulseUrl("updatePlaylist", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void Favorite(string trackId, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "id=" + Uri.EscapeDataString(trackId) + "&type=track";
				RunCommand(BuildPulseUrl("favorite", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void Unfavorite(string trackId, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "id=" + Uri.EscapeDataString(trackId) + "&type=track";
				RunCommand(BuildPulseUrl("unfavorite", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void DeletePlaylist(string playlistId, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "id=" + Uri.EscapeDataString(playlistId);
				RunCommand(BuildPulseUrl("deletePlaylist", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void AddTrackToPlaylist(string playlistId, string songId, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "playlistId=" + Uri.EscapeDataString(playlistId)
					+ "&songIdToAdd=" + Uri.EscapeDataString(songId);
				RunCommand(BuildPulseUrl("updatePlaylist", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void RemoveTrackFromPlaylist(string playlistId, int songIndex, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "playlistId=" + Uri.EscapeDataString(playlistId)
					+ "&songIndexToRemove=" + songIndex;
				RunCommand(BuildPulseUrl("updatePlaylist", param));
				CompleteOnMain(onComplete, true);
			});
		}

		public override void ReorderPlaylist(string playlistId, int fromIndex, int toIndex, List<PulseTrack> newOrder, Action<bool> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
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
			});
		}

		public override void GetPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("playlists", null);
				FetchListObject<PulsePlaylist>(url, eMediaCacheStrategy.NetworkFirst, (playlists) =>
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
			});
		}

		public override void GetPlaylist(string playlistId, Action<PulsePlaylistDetails> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("playlist", "id=" + Uri.EscapeDataString(playlistId));
				FetchObject<PulsePlaylistDetails>(url, eMediaCacheStrategy.NetworkFirst, (details) =>
				{

					if (onComplete != null)
					{
						onComplete(details);
					}
				});
			});
		}


		public override void GetCoverArt(string coverArtId, int size, Action<ImageSource> onComplete)
		{
			m_workQueue.Enqueue(()=>
			{ 
				
				if (string.IsNullOrEmpty(coverArtId))
				{
					CompleteOnMain(onComplete, null);
					return;
				}

				string url = BuildCoverArtUrl(coverArtId);

				string cache_url = coverArtId + "_" + size.ToString();

				ImageSource hot;
				if (m_imageCache.TryGetValue(cache_url, out hot))
				{
					CompleteOnMain(onComplete, hot);
					return;
				}

				//maybe we can make the right size
				if (m_imageBytesCache.TryGetValue(url, out byte[] rawImage))
				{
					//make the small size they want now
					ImageSource result = DecodeToSize(cache_url, rawImage, size);
					m_imageCache[cache_url] = result;
					CompleteOnMain(onComplete, result);
					return;
				}

				byte[] cachedData = GetCachedCoverArt(coverArtId);
				if (cachedData != null)
				{
					m_imageBytesCache[url] = cachedData;
					ImageSource result = DecodeToSize(cache_url, cachedData, size);
					m_imageCache[cache_url] = result;
					CompleteOnMain(onComplete, m_imageCache[cache_url]);
					return;
				}
				
				GetHTTPImage(url, (data) =>
				{
					try
					{
						if (data == null || data.Length <= 0)
						{
							Log.Error("Cover art fetch failed: " + url);
							CompleteOnMain(onComplete, null);
							return;
						}
						//cache the full size
						m_imageBytesCache[url] = data;

						//make the requested size
						ImageSource result = DecodeToSize(cache_url, data, size);
						m_imageCache[cache_url] = result;
						CompleteOnMain(onComplete, m_imageCache[cache_url]);
					}
					catch (Exception ex)
					{
						Log.Exception(ex);
						CompleteOnMain(onComplete, null);
					}
				});
			});
		}


		public override byte[] GetCachedCoverArt(string coverArtId)
		{
			if (string.IsNullOrEmpty(coverArtId))
			{
				return null;
			}
			string url = BuildCoverArtUrl(coverArtId);

			if (GetCachedResults(url, out byte[] byteData))
			{
				return byteData;
			}
			return null;
		}
		public override void GetTrackAudio(string trackId, Action<byte[]> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{

				string url = GetTrackAudioURL(trackId);


				if (GetCachedResults(url, out byte[] data))
				{
					CompleteOnMain(onComplete, data);
					return;
				}


				GetHTTPAudio(url, (data) =>
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
						CacheQueryResults(url, data);
						CompleteOnMain(onComplete, captured);
					}
					catch (Exception ex)
					{
						Log.Exception(ex);
						CompleteOnMain(onComplete, null);
					}
				});
			});
		}

		public override void GetRecentlyPlayed(Action<List<PulseObject>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("recentlyPlayed", "count=50");
				FetchMultiTypedItems(url, eMediaCacheStrategy.NetworkFirst, (contents) =>
				{
					List<PulseObject> results = contents;
					if (onComplete != null)
					{
						onComplete(results);
					}
				});
			});
		}

		public override void GetPopularArtists(Action<List<PulseArtist>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("topItems", "types=artist&count=20");
				FetchListObject<PulseArtist>(url, eMediaCacheStrategy.NetworkFirst, (items) =>
				{
					if (onComplete != null)
					{
						onComplete(items);
					}
				});
			});
		}

		public override void GetTopPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("topItems", "types=playlist&count=20");
				FetchListObject<PulsePlaylist>(url, eMediaCacheStrategy.NetworkFirst, (items) =>
				{
					if (onComplete != null)
					{
						onComplete(items);
					}
				});
			});
		}

		public override void GetRecentPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("recentlyPlayed", "types=playlist&count=20");
				FetchListObject<PulsePlaylist>(url , eMediaCacheStrategy.NetworkFirst, (items) =>
				{
					if (onComplete != null)
					{
						onComplete(items);
					}
				});
			});
		}

		public override void GetGenres(Action<List<PulseGenre>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("genres", null);
				FetchListObject<PulseGenre>(url, eMediaCacheStrategy.NetworkFirst, (genres) =>
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
			});
		}

		public override void GetTopItems(Action<List<PulseObject>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("topItems", "count=50");
				FetchMultiTypedItems(url,eMediaCacheStrategy.NetworkFirst, (items) =>
				{
					if (onComplete != null)
					{
						onComplete(items);
					}
				});
			});
		}

		public override void GetTracksForGenre(string genre, Action<List<PulseTrack>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string param = "genre=" + Uri.EscapeDataString(genre) + "&count=500&offset=0";
				string url = BuildPulseUrl("genreTracks", param);
				FetchObject<PulseGenreDetails>(url, eMediaCacheStrategy.NetworkFirst, (details) =>
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
			});
		}

		public override void GetFavorites(Action<List<PulseTrack>> onComplete)
		{
			m_workQueue.Enqueue(() =>
			{
				string url = BuildPulseUrl("favorites", null);
				FetchObject<PulseSearchData>(url, eMediaCacheStrategy.NetworkFirst, (data) =>
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
			});
		}

		public override void ReportAnalytics(string mediaId, PulseAPI.CSharp.ePulseWireType mediaType, PulseAnalytics.eAction action)
		{
			if (string.IsNullOrEmpty(mediaId))
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
					HttpPostJson(url, json);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			});
		}

		/// <summary>
		/// POST a batch of structured product-analytics events to the server's
		/// pulse_v1/ingestAnalytics route. Swallows its own failures: the
		/// analytics path must never feed errors back into Log.* or it loops.
		/// </summary>
		public override void PostAnalytics(PulseAnalyticsBatch batch)
		{
			if (batch == null)
			{
				return;
			}
			if (batch.Events == null || batch.Events.Count == 0)
			{
				return;
			}
			Task.Run(() =>
			{
				try
				{
					string url = BuildPulseUrl("ingestAnalytics", null);
					string json = PulseWire.Serialize(batch);
					// logPerf=false: analytics POSTs must not spam the perf log.
					HttpPostJson(url, json);
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
				case ePulseWireType.Track:
					return PulseWire.Parse<PulseTrack>(raw);
				case ePulseWireType.Album:
					return PulseWire.Parse<PulseAlbum>(raw);
				case ePulseWireType.Playlist:
					return PulseWire.Parse<PulsePlaylist>(raw);
				case ePulseWireType.Artist:
					return PulseWire.Parse<PulseArtist>(raw);
				case ePulseWireType.Genre:
					return PulseWire.Parse<PulseGenre>(raw);
				case ePulseWireType.Podcast:
					return PulseWire.Parse<PulsePodcast>(raw);
				default:
					return probe;
			}
		}

		// Fetches a heterogeneous item feed (topItems / recentlyPlayed) and keeps
		// only the elements whose Kind maps to the requested concrete type.
		// Delivers the filtered list through the callback FetchObject.
		private void FetchMultiTypedItems(string url, eMediaCacheStrategy cacheStrategy, Action<List<PulseObject>> onComplete)
		{
			List<PulseObject> contents = null;
			if (cacheStrategy == eMediaCacheStrategy.CacheFirst && GetCachedResults(url, out byte[] data))
			{
				string json = System.Text.Encoding.UTF8.GetString(data);
				PulseResponse response = PulseWire.Parse<PulseResponse>(json);
				if (response != null && response.contents != null)
				{
					JsonElement jsonElement = (JsonElement)response.contents;
					contents = MapArray(jsonElement);
					CompleteOnMain(onComplete, contents);
					return;
				}
			}

			GetHTTP(url, (json) =>
			{
				if (!string.IsNullOrEmpty(json))
				{
					PulseResponse response = PulseWire.Parse<PulseResponse>(json);
			
					if (response != null && response.contents != null)
					{
						JsonElement jsonElement = (JsonElement)response.contents;
						contents = MapArray(jsonElement);
						if  (contents == null)
						{
							Log.Error("Unparseable pulse response: " + url);
						}
					}
				}

				if (contents != null)
				{
					//special case for mixed object types, we're saving the blob
					byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
					CacheQueryResults(url, bytes);
				}
				else if (cacheStrategy != eMediaCacheStrategy.NetworkOnly)
				{
					if (GetCachedResults(url, out byte[] data))
					{
						json = System.Text.Encoding.UTF8.GetString(data);
						PulseResponse response = PulseWire.Parse<PulseResponse>(json);
						if (response != null && response.contents != null)
						{ 
							JsonElement jsonElement = (JsonElement)response.contents;
							contents = MapArray(jsonElement);
						}
					}
				}

				if (contents == null)
					contents = new List<PulseObject>();
				CompleteOnMain(onComplete, contents);
			});

		}

		private List<PulseObject> MapArray(JsonElement array)
		{
			if (array.ValueKind == JsonValueKind.Array)
			{
				List<PulseObject> list = new List<PulseObject>();
				foreach (JsonElement element in array.EnumerateArray())
				{
					PulseObject mapped = MapMixedObject(element);
					if (mapped != null)
					{
						list.Add(mapped);
					}
				}
				return list;
			}
			else
			{
				return null;
			}
		}

		public override bool IsTrackCached(string trackID)
		{
			if (string.IsNullOrEmpty(trackID))
			{
				return false;
			}
			string url = GetTrackAudioURL(trackID);
			return m_cache.HasCachedResults(url);
		}

		public override void CacheQueryResults(string url, byte[] data)
		{
			if (data == null || data.Length == 0)
				return;

			m_cache.CacheQueryResults(url, data);
		}

		public override bool GetCachedResults(string url, out byte[] data)
		{
			return m_cache.GetCachedResults(url, out data);
		}

		public void CacheQueryPulseData(string url, PulseObject pulseObject)
		{
			if (pulseObject == null)
				return;

			string data = PulseWire.Serialize(pulseObject);
			m_cache.CacheQueryResults(url, data);
		}

		public void CacheQueryPulseData<T>(string url, List<T> pulseObject) where T : PulseObject
		{
			if (pulseObject == null || pulseObject.Count == 0)
				return;

			string data = PulseWire.Serialize(pulseObject);
			m_cache.CacheQueryResults(url, data);
		}

		public bool GetCachedPulseData<T>(string url, out T pulseObject) where T : PulseObject
		{
			if (m_cache.GetCachedResults(url, out string data))
			{
				pulseObject = PulseWire.Parse<T>(data);
				if (pulseObject == null)
				{
					Log.Warn("Warning bad data was cached for " + url);
				}
				return pulseObject != null;
			}
			pulseObject = null;
			return false;
		}

		public bool GetCachedPulseData<T>(string url, out List<T> pulseObject) where T : PulseObject
		{
			if (m_cache.GetCachedResults(url, out string data))
			{
				
				pulseObject = PulseWire.Parse<List<T>>(data);
				if (pulseObject == null) 
				{
					Log.Warn("Warning bad data was cached for " + url);
				}
				return pulseObject != null;
			}
			pulseObject = null;
			return false;
		}

		/// <summary>
		/// Fresh-cache parse for a single PulseObject. Returns true and the parsed
		/// object only when the cache entry for the URL is no older than
		/// maxAgeSeconds. Mirrors GetCachedPulseData but routes through the
		/// age-aware ThumpCache read.
		/// </summary>
		public bool GetRecentCachedPulseData<T>(string url, int maxAgeSeconds, out T pulseObject) where T : PulseObject
		{
			if (m_cache.GetCachedResults(url, maxAgeSeconds, out string data))
			{
				pulseObject = PulseWire.Parse<T>(data);
				if (pulseObject == null)
				{
					Log.Warn("Warning bad data was cached for " + url);
				}
				return pulseObject != null;
			}
			pulseObject = null;
			return false;
		}

		/// <summary>
		/// Fresh-cache parse for a List of PulseObjects. Same semantics as the
		/// single overload, returning a parsed list only when the cached row is
		/// fresh.
		/// </summary>
		public bool GetRecentCachedPulseData<T>(string url, int maxAgeSeconds, out List<T> pulseObject) where T : PulseObject
		{
			if (m_cache.GetCachedResults(url, maxAgeSeconds, out string data))
			{
				pulseObject = PulseWire.Parse<List<T>>(data);
				if (pulseObject == null)
				{
					Log.Warn("Warning bad data was cached for " + url);
				}
				return pulseObject != null && pulseObject.Count > 0;
			}
			pulseObject = null;
			return false;
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

		private ImageSource DecodeToSize(string coverArtId, byte[] data, int size)
		{
			byte[] thumbBytes = null;
#if ANDROID
			Android.Graphics.BitmapFactory.Options bounds = new Android.Graphics.BitmapFactory.Options();
			bounds.InJustDecodeBounds = true;
			Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length, bounds);

			int sampleSize = 1;
			int width = bounds.OutWidth;
			int height = bounds.OutHeight;
			while (width / (sampleSize * 2) >= size && height / (sampleSize * 2) >= size)
			{
				sampleSize = sampleSize * 2;
			}

			Android.Graphics.BitmapFactory.Options decodeOptions = new Android.Graphics.BitmapFactory.Options();
			decodeOptions.InSampleSize = sampleSize;
			Android.Graphics.Bitmap sampled = Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length, decodeOptions);

			Android.Graphics.Bitmap finalBitmap = sampled;
			if (sampled.Width > size || sampled.Height > size)
			{
				float scale = (float)size / Math.Max(sampled.Width, sampled.Height);
				int targetWidth = (int)(sampled.Width * scale);
				int targetHeight = (int)(sampled.Height * scale);
				finalBitmap = Android.Graphics.Bitmap.CreateScaledBitmap(sampled, targetWidth, targetHeight, true);
				if (finalBitmap != sampled)
				{
					sampled.Recycle();
				}
			}

			System.IO.MemoryStream stream = new System.IO.MemoryStream();
			finalBitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Png, 90, stream);
			finalBitmap.Recycle();
			thumbBytes = stream.ToArray();
#else
			thumbBytes = data;
#endif
			//use an on-disk file cache for glide so it can skip the marshal reads
			string filePath = System.IO.Path.Combine(FileSystem.CacheDirectory, coverArtId + ".jpg");
			if (!File.Exists(filePath))
			{
				File.WriteAllBytes(filePath, data);
			
			}

			ImageSource image = ImageSource.FromFile(filePath);
			return image;
			//return ImageSource.FromStream(() => new System.IO.MemoryStream(thumbBytes));
		}
	}
}
