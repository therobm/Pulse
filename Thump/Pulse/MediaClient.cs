
using Microsoft.Maui.ApplicationModel;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Thump.Data;
using Thump.Views.Tiles;

namespace Thump.Pulse
{
	
	public enum eMediaCacheStrategy
	{
		NetworkFirst,	// always use for metadata, cache is for offline only
		CacheFirst,		// prefer cached data (useful for tracks, art, etc..)
		NetworkOnly,	// no caching ever
	}

	public interface IMediaClientHost
	{
		void OnOnlineStateChanged(bool online);
	}

	
	// The single API surface every media-server client (Subsonic today, Pulse-native
	// in the future) must implement. Consumers (ThumpData, MainView, the playback
	// service, settings) hold an MediaClient, so the concrete implementation can
	// be swapped without touching them.
	public abstract class MediaClient
	{
		private static int s_requestCounter = 0;

		/// <summary>
		/// Total number of attempts (initial + retries) the HTTP GET path will make
		/// before giving up. A single hiccup must not surface as a hard failure.
		/// </summary>
		private const int s_maxHttpAttempts = 3;

		/// <summary>
		/// Base backoff between HTTP attempts, multiplied by the attempt number
		/// (i.e. 250ms after attempt 1, then 500ms after attempt 2).
		/// </summary>
		private const int s_httpRetryBaseMs = 250;

		private static bool AcceptAnyServerCertificate(HttpRequestMessage request, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
		{
			return true;
		}

		ThumpCache m_cache;

		private Thread m_thread;

		protected string m_baseUrl;
		protected string m_user;
		private string m_apiParams;
	
		private Http m_httpClient;
		private object m_httpClientLock = new object();

		private bool m_bInitialized = false;
		private volatile bool m_bIsOnline = false;
		private int m_pingFailureCount = 0;

		/// <summary>
		/// True when we're streaming music directly off the wire
		/// when this is true all other binary requests should be sacrificed
		/// to prioritize the stream.
		/// </summary>
		protected volatile bool m_bIsStreaming = false;
		protected object m_streamingLock = new object();

		// Fires when the online/offline state actually flips (not on every poll).
		// Raised from the background connection/request threads, so subscribers
		// that touch UI must marshal to the main thread themselves.
		IMediaClientHost m_host;

		HttpQueue m_metaData;
		HttpQueue m_imageData;
		HttpQueue m_audioData;

		public MediaClient(ThumpCache cache, IMediaClientHost host)
		{
			m_cache = cache;
			m_host = host;
			m_thread = new Thread(ConnectionLoop);
			m_thread.IsBackground = true;
			m_thread.Start();

			m_metaData = new HttpQueue(this, 2);
			m_imageData = new HttpQueue(this, 2);
			m_audioData = new HttpQueue(this, 1);

			m_httpClient = new Http();
		}

		public void SetStreamingStatus(bool isStreaming)
		{
			lock(m_streamingLock)
			{ 
				if (m_bIsStreaming == isStreaming)
					return;

				m_bIsStreaming = isStreaming;
				if (isStreaming)
				{ 
					m_audioData.Suspend();
					m_imageData.Suspend();
				}
				else
				{
					m_audioData.Resume();
					m_imageData.Resume();
				}
			}
		}
	
		private void ConnectionLoop()
		{
			while (true)
			{
				if (m_bInitialized)
				{
					Ping(out JsonElement response);
				}
				//sleep 1ms
				Thread.Sleep(5000);
			}
		}
		public void OnPingResult(bool success)
		{
			if (success)
			{
				m_pingFailureCount = 0;
				SetOnline(true);
			}
			else
			{
				m_pingFailureCount++;
				if (m_pingFailureCount > 3)
					SetOnline(false);
			}
		}

		public virtual void SetServerParams(string ip, string port, string username, string password,  bool enableSSL)
		{
			// Accept an IP/host that may have been entered (or stored) with a
			// scheme and/or trailing slash; strip them so the prefix derived from
			// enableSSL is authoritative. Otherwise a value like "https://host"
			// produced "http://https://host:port".
			ip = ip.Trim().Replace("http://", "").Replace("https://", "").TrimEnd('/');

			//save the real ip in case this is a domain name to skip future dns lookups
			System.Net.IPAddress[] addresses = System.Net.Dns.GetHostAddresses(ip);
			if (addresses.Length > 0)
			{
				ip = addresses[0].ToString();
			}



			string prefix = "http://";
			if (enableSSL)
				prefix = "https://";

			m_baseUrl = prefix + ip + ":" + port;
			m_user = username;
			m_apiParams = "u=" + Uri.EscapeDataString(m_user) + "&p=enc:" + Uri.EscapeDataString(password) + "&v=1.13.0&c=PulseMaui&f=json";
	
			m_httpClient.OnServerChanged();
			m_bInitialized = true;
			//Ping(out JsonElement discard);
		}

		

		protected virtual bool Ping(out JsonElement response)
		{
			response = default;
			return false;
		}

		public bool TestConnection(out JsonElement response)
		{
			return Ping(out response);
		}

		public bool IsOnline()
		{
			return m_bIsOnline;
		}

		// Single choke point for online-state changes. Subclasses (Ping) and the
		// HTTP failure paths route through here so the OnlineStateChanged event
		// fires once per transition rather than on every poll.
		protected void SetOnline(bool online)
		{
			if (m_host != null)
				m_host.OnOnlineStateChanged(online);
			if (m_bIsOnline == online)
			{
				return;
			}
			m_bIsOnline = online;
		
		}

		public string BuildStreamUrl(string trackId)
		{
			return BuildRestUrl("stream", "id=" + Uri.EscapeDataString(trackId));
		}

		public string BuildRestUrl(string endpoint, string extraParams = null)
		{
			string url = m_baseUrl + "/rest/" + endpoint + "?" + m_apiParams;
			if (!string.IsNullOrEmpty(extraParams))
			{
				url = url + "&" + extraParams;
			}
			return url;
		}

		protected virtual string BuildCoverArtUrl(string coverArtId)
		{
			if (string.IsNullOrEmpty(coverArtId))
			{
				return null;
			}

			return BuildRestUrl("getCoverArt", "id=" + Uri.EscapeDataString(coverArtId));
		}
		public virtual string GetTrackAudioURL(string trackId)
		{
			return BuildStreamUrl(trackId);
		}

		public abstract void GetTrack(string trackId, Action<PulseTrack> onComplete);
		public abstract void GetArtists(Action<List<PulseArtist>> onComplete);
		public abstract void GetArtist(string artistId, Action<PulseArtistDetails> onComplete);
		public abstract void GetPodcasts(Action<List<PulsePodcast>> onComplete);
		public abstract void GetAllPodcasts(Action<List<PulsePodcast>> onComplete);
		public abstract void SearchPodcasts(string query, Action<List<PulsePodcast>> onComplete);
		public abstract void GetPodcast(string podcastId, Action<PulsePodcastDetails> onComplete);
		public abstract void GetAudiobooks(Action<List<PulseAudiobook>> onComplete);
		public abstract void GetAudiobook(string audiobookId, Action<PulseAudiobookDetails> onComplete);
		public abstract void AddPodcast(string feedUrl, bool subscribe, Action<PulsePodcast> onComplete);
		public abstract void SubscribePodcast(string podcastId, Action<bool> onComplete);
		public abstract void UnsubscribePodcast(string podcastId, Action<bool> onComplete);
		public abstract void UpdatePodcast(string podcastId, string retentionPolicy, int retentionValue, bool autoDownload, Action<PulsePodcast> onComplete);
		public abstract void SaveEpisodeProgress(string episodeId, int positionSeconds);
		public abstract void Search(string query, Action<PulseSearchData> onComplete);
		public abstract void GetArtistAlbums(string artistId, Action<List<PulseAlbum>> onComplete);
		public abstract void GetArtistTracks(string artistId, Action<List<PulseTrack>> onComplete);
		public abstract void GetAlbum(string albumId, Action<PulseAlbumDetails> onComplete);
		public abstract void GetAlbums(Action<List<PulseAlbum>> onComplete);
		public abstract void CreatePlaylist(string name, Action<PulsePlaylist> onComplete);
		public abstract void RenamePlaylist(string playlistId, string newName, Action<bool> onComplete);
		public abstract void Favorite(string trackId, Action<bool> onComplete);
		public abstract void Unfavorite(string trackId, Action<bool> onComplete);
		public abstract void DeletePlaylist(string playlistId, Action<bool> onComplete);
		public abstract void AddTrackToPlaylist(string playlistId, string songId, Action<bool> onComplete);
		public abstract void RemoveTrackFromPlaylist(string playlistId, int songIndex, Action<bool> onComplete);
		public abstract void ReorderPlaylist(string playlistId, int fromIndex, int toIndex, List<PulseTrack> newOrder, Action<bool> onComplete);
		public abstract void GetPlaylists(Action<List<PulsePlaylist>> onComplete);
		public abstract void GetPlaylist(string playlistId, Action<PulsePlaylistDetails> onComplete);
		public abstract void GetCoverArt(string coverArtId, Action<byte[]> onComplete);
		public abstract byte[] GetCachedCoverArt(string coverArtId);
		public abstract void GetTrackAudio(string trackId, Action<byte[]> onComplete);
		public void CacheTrackAudio(string trackId, Action<bool> onComplete)
		{
			GetTrackAudio(trackId, (bytes)=>
			{
				if (onComplete != null)
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						if (bytes != null && bytes.Length > 0)
							onComplete(true);
						else
							onComplete(false);
					});
					
				}
			});
		}
		public abstract void GetRecentlyPlayed(Action<List<PulseObject>> onComplete);
		public abstract void GetPopularArtists(Action<List<PulseArtist>> onComplete);
		public abstract void GetTopPlaylists(Action<List<PulsePlaylist>> onComplete);
		public abstract void GetRecentPlaylists(Action<List<PulsePlaylist>> onComplete);
		public abstract void GetGenres(Action<List<PulseGenre>> onComplete);
		public abstract void GetTopItems(Action<List<PulseObject>> onComplete);
		public abstract void GetTracksForGenre(string genre, Action<List<PulseTrack>> onComplete);
		public abstract void GetFavorites(Action<List<PulseTrack>> onComplete);

	
		public virtual void ReportAnalytics(string mediaId, ePulseWireType mediaType, PulseAnalytics.eAction action)
		{
		}

		/// <summary>
		/// Ship a batch of structured product-analytics events to the server.
		/// Base implementation is a no-op; the Pulse client overrides this to
		/// POST the batch. Mirrors ReportAnalytics' fire-and-forget shape.
		/// </summary>
		public virtual void PostAnalytics(PulseAnalyticsBatch batch)
		{
		}
	
		public byte[] GetTrackAudioFromCache(string trackId)
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

		public void GetHTTPAudio(string url, Action<byte[]> onComplete, eMediaCacheStrategy cacheStrategy)
		{
			m_audioData.QueueRequest(new HttpReq(url, onComplete, cacheStrategy, true));
		}
		public void GetHTTPImage(string url, Action<byte[]> onComplete, eMediaCacheStrategy cacheStrategy)
		{
			m_imageData.QueueRequest(new HttpReq(url, onComplete, cacheStrategy, true));
		}
		public void GetHTTP(string url, Action<string> onComplete, eMediaCacheStrategy cacheStrategy, bool logPerf)
		{
			m_metaData.QueueRequest(new HttpReq(url, onComplete, cacheStrategy, logPerf));
		}

		public byte[] HttpGetBinary(string url, eMediaCacheStrategy cacheStrategy, CancellationToken token)
		{
			bool tryCacheFirst = !IsOnline() || cacheStrategy == eMediaCacheStrategy.CacheFirst;

			byte[] retVal = null;
			if (tryCacheFirst && GetCachedResults(url, out retVal))
			{
				return retVal;
			}

			HttpResponseMessage response = null;
			try
			{
				response = m_httpClient.HttpGet(url, Http.eRequestType.BinaryData, token, false, 120);

				if (cacheStrategy != eMediaCacheStrategy.NetworkOnly && !response.IsSuccessStatusCode)
				{
					//try our offline cache
					if (!GetCachedResults(url, out retVal))
					{ 
						Log.Error("HTTP request failed: " + url + " status: " + response.StatusCode);
						return null;
					}
					else
					{
						return retVal;
					}
				}
			
				retVal = ReadBinaryBodyWithStallTimeout(response, 30, token);
			}
			catch (Exception ex)
			{
				//todo this should log exceptions that are unexpected
				// suspend cancel arrives here as AggregateException(OperationCanceled); not a real error
				return null;
			}
			finally
			{
				if (response != null)
					response.Dispose();
			}
			if (retVal != null)
				CacheQueryResults(url, retVal);
			return retVal;
		}

		public string HttpGet(string url, eMediaCacheStrategy cacheStrategy, bool logPerf, bool ignoreOnline, CancellationToken token, float timeoutSeconds = 8)
		{
			bool tryCacheFirst = !IsOnline() || cacheStrategy == eMediaCacheStrategy.CacheFirst;
			
			//never permit cache reads for network only calls (ping is impacted)
			if (cacheStrategy == eMediaCacheStrategy.NetworkOnly)
				tryCacheFirst = false;

			string retVal = null;
			if (tryCacheFirst && GetCachedResults(url, out retVal))
			{
				return retVal;
			}
			HttpResponseMessage response = null;
			
			try
			{
				response = m_httpClient.HttpGet(url, Http.eRequestType.MetaData, token, ignoreOnline, timeoutSeconds);
				if (!response.IsSuccessStatusCode)
				{
					//try our offline cache
					if (cacheStrategy != eMediaCacheStrategy.NetworkOnly && !GetCachedResults(url, out retVal))
					{
						Log.Error("HTTP request failed: " + url + " status: " + response.StatusCode);
						return null;
					}
					else
					{
						return retVal;
					}
				}
				else
				{
					retVal = ReadStringBodyWithStallTimeout(response, 30);
				}
			}
			catch(Exception ex)
			{
				Log.Exception(ex);
				return null;
			}
			finally
			{
				if (response != null)
					response.Dispose();
			}

			if (retVal != null)
				CacheQueryResults(url, retVal);
			return retVal;
		}

		

		private byte[] ReadBinaryBodyWithStallTimeout(HttpResponseMessage response, int stallSeconds, CancellationToken external)
		{
			TimeSpan stallWindow = TimeSpan.FromSeconds(stallSeconds);
			using CancellationTokenSource stallCts = new CancellationTokenSource(stallWindow);
			using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(external, stallCts.Token);
			using Stream stream = response.Content.ReadAsStreamAsync().Result;
			
			int capacity = 64 * 1024;
			long? contentLength = response.Content.Headers.ContentLength;
			if (contentLength.HasValue && contentLength.Value > 0)
			{
				capacity = (int)contentLength.Value;
			}

			using MemoryStream buffer = new MemoryStream(capacity);

			byte[] chunk = new byte[64 * 1024];
			int safeLoop = 99999;
			while (safeLoop > 0)
			{
				safeLoop--;

				int read = stream.ReadAsync(chunk, 0, chunk.Length, linked.Token).Result;
				if (read <= 0)
				{
					break;
				}
				buffer.Write(chunk, 0, read);
				stallCts.CancelAfter(stallWindow);
			}
			if (buffer.Length == buffer.Capacity)
			{
				return buffer.GetBuffer();
			}
			return buffer.ToArray();
		}

		private string ReadStringBodyWithStallTimeout(HttpResponseMessage response, int stallSeconds)
		{
			TimeSpan stallWindow = TimeSpan.FromSeconds(stallSeconds);
			using CancellationTokenSource cts = new CancellationTokenSource(stallWindow);
			using Stream stream = response.Content.ReadAsStreamAsync().Result;
			using MemoryStream buffer = new MemoryStream();

			byte[] chunk = new byte[64 * 1024];
			while (true)
			{
				int read = stream.ReadAsync(chunk, 0, chunk.Length, cts.Token).Result;
				if (read <= 0)
				{
					break;
				}
				buffer.Write(chunk, 0, read);
				cts.CancelAfter(stallWindow);
			}

			Encoding encoding = ResolveEncoding(response);
			return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
		}

		private Encoding ResolveEncoding(HttpResponseMessage response)
		{
			string charset = null;
			if (response.Content != null && response.Content.Headers.ContentType != null)
			{
				charset = response.Content.Headers.ContentType.CharSet;
			}
			if (string.IsNullOrEmpty(charset))
			{
				return Encoding.UTF8;
			}

			// Some servers quote the charset value: charset="utf-8"
			charset = charset.Trim('"', '\'', ' ');
			try
			{
				return Encoding.GetEncoding(charset);
			}
			catch (ArgumentException)
			{
				return Encoding.UTF8;
			}
		}

		/// <summary>
		/// used for direct streaming audio
		/// always tries to fetch even if we're "offline"
		/// caller must dispose of response
		/// </summary>
		/// <param name="url"></param>
		/// <param name="position"></param>
		/// <returns></returns>
		public HttpResponseMessage HttpGetStream(string url, long position, CancellationToken token)
		{
			return m_httpClient.HttpGetStream(url, position, token);
		}

		// POST a JSON body to a command endpoint. The GET helpers above can't
		// carry a payload, so anything that ships a serialized object (e.g.
		// analytics) goes through here. Commands are never cached. Mirrors
		// HttpGet's offline handling: a dropped/refused connection marks the
		// client offline and returns null rather than unwinding to the thread
		// root. Returns the response body on success, null on failure.
		protected string HttpPostJson(string url, string json, float timeoutSeconds = 8)
		{ 
			using HttpResponseMessage response = m_httpClient.HttpPostJson_Internal(url, Http.eRequestType.MetaData, json, timeoutSeconds = 8); 
			if (response == null )
			{
				Log.Error("HTTP POST failed: " + url);
				return null;
			}
			if (!response.IsSuccessStatusCode)
			{
				Log.Error("HTTP POST failed: " + url + " status: " + response.StatusCode);
				return null;
			}
			string result = response.Content.ReadAsStringAsync().Result;
			return result;
		}

		public bool IsTrackCached(string trackID)
		{
			if (string.IsNullOrEmpty(trackID))
			{
				return false;
			}
			string url = GetTrackAudioURL(trackID);
			return m_cache.HasCachedResults(url);
		}
		public void CacheQueryResults(string url, byte[] data)
		{
			m_cache.CacheQueryResults(url, data);
		}
		public bool GetCachedResults(string url, out byte[] data)
		{
			return m_cache.GetCachedResults(url, out data);
		}
		public void CacheQueryResults(string url, string data)
		{
			m_cache.CacheQueryResults(url, data);
		}

		public bool GetCachedResults(string url, out string data)
		{
			return m_cache.GetCachedResults(url, out data);
		}

		protected void CompleteOnMain<T>(Action<T> onComplete, T value)
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
	}
}
