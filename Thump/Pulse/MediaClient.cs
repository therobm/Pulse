
using Microsoft.Maui.ApplicationModel;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Thump.Data;
using Thump.Views.Tiles;

namespace Thump.Pulse
{
	public class HttpReq
	{
		public Action<byte[]> m_onBinaryComplete;
		public Action<string> m_onStringComplete;
		public string m_url;
		public bool m_bCacheAllowed;
		public bool m_bIsBinary;
		public bool m_bLogPerf;
		public HttpReq(string url, Action<byte[]> onComplete, bool cacheAllowed, bool logPerf)
		{
			m_url = url;
			m_onBinaryComplete = onComplete;
			m_bIsBinary = true;
			m_bCacheAllowed = cacheAllowed;
			m_bLogPerf = logPerf;
		}
		public HttpReq(string url, Action<string> onComplete, bool cacheAllowed, bool logPerf)
		{
			m_url = url;
			m_onStringComplete = onComplete;
			m_bIsBinary = false;
			m_bCacheAllowed = cacheAllowed;
			m_bLogPerf = logPerf;
		}
	}

	// The single API surface every media-server client (Subsonic today, Pulse-native
	// in the future) must implement. Consumers (ThumpData, MainView, the playback
	// service, settings) hold an MediaClient, so the concrete implementation can
	// be swapped without touching them.
	public abstract class MediaClient
	{
		private static int s_requestCounter = 0;

		private static bool AcceptAnyServerCertificate(HttpRequestMessage request, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
		{
			return true;
		}



		ThumpCache m_cache;

		private Thread m_thread;
		private Thread m_metaThread;
		private Thread m_binaryThread;

		protected string m_baseUrl;
		protected string m_user;
		private string m_apiParams;
		private HttpClient m_httpClient;
		private object m_httpClientLock = new object();

		private bool m_bInitialized = false;
		protected bool m_bIsOnline = true;


		object m_metaRequestLock = new object();
		object m_binaryRequestLock = new object();
		Queue<HttpReq> m_metaRequests = new Queue<HttpReq>();
		Queue<HttpReq> m_binaryRequests = new Queue<HttpReq>();


		public MediaClient(ThumpCache cache)
		{
			m_cache = cache;

			m_thread = new Thread(ConnectionLoop);
			m_thread.IsBackground = true;
			m_thread.Start();

			m_metaThread = new Thread(MetaLoop);
			m_metaThread.IsBackground = true;
			m_metaThread.Start();

			m_binaryThread = new Thread(BinaryLoop);
			m_binaryThread.IsBackground = true;
			m_binaryThread.Start();

		}

		private void ConnectionLoop()
		{
			while (true)
			{
				if (m_bInitialized)
				{
					
					Ping(out JsonElement response);
					
					//pump our http queue
				}
				//sleep 1ms
				Thread.Sleep(5000);
			}
		}
		private void MetaLoop()
		{
			while (true)
			{
				ProcessMetaQueue();
				Thread.Sleep(1);
			}
		}
		private void BinaryLoop()
		{
			while (true)
			{
				ProcessBinaryQueue();
				Thread.Sleep(1);
			}
		}

		public virtual void SetServerParams(string ip, string port, string username, string password,  bool enableSSL)
		{
			// Accept an IP/host that may have been entered (or stored) with a
			// scheme and/or trailing slash; strip them so the prefix derived from
			// enableSSL is authoritative. Otherwise a value like "https://host"
			// produced "http://https://host:port".
			ip = ip.Trim().Replace("http://", "").Replace("https://", "").TrimEnd('/');

			string prefix = "http://";
			if (enableSSL)
				prefix = "https://";

			m_baseUrl = prefix + ip + ":" + port;
			m_user = username;
			m_apiParams = "u=" + Uri.EscapeDataString(m_user) + "&p=enc:" + Uri.EscapeDataString(password) + "&v=1.13.0&c=PulseMaui&f=json";
		

			if (m_httpClient != null)
				m_httpClient.Dispose();

			HttpClientHandler handler = new HttpClientHandler();
			handler.ServerCertificateCustomValidationCallback = AcceptAnyServerCertificate;

			HttpClient oldClient;
			lock (m_httpClientLock)
			{
				oldClient = m_httpClient;
				m_httpClient = new HttpClient(handler);
				m_httpClient.Timeout = TimeSpan.FromSeconds(10);
			}
			if (oldClient != null)
			{
				oldClient.Dispose();
			}

			m_bInitialized = true;
			Ping(out JsonElement discard);
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
		public abstract void GetPodcasts(Action<List<PulsePodcastChannel>> onComplete);
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

	
		public virtual void ReportAnalytics(string mediaId, eDataType mediaType, PulseAnalytics.eAction action)
		{
		}
	

		public byte[] ForceFetchTrackAudio(string trackId)
		{
			if (string.IsNullOrEmpty(trackId))
			{
				return null;
			}
			string url = GetTrackAudioURL(trackId);
			return HttpGetBinary(url, true);
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


		public void GetHTTPBinary(string url, Action<byte[]> onComplete, bool bCacheAllowed)
		{
			QueueRequest(new HttpReq(url, onComplete, bCacheAllowed, true));
		}
		public void GetHTTP(string url, Action<string> onComplete, bool bCacheAllowed, bool logPerf)
		{
			QueueRequest(new HttpReq(url, onComplete, bCacheAllowed, logPerf));
		}
		private void QueueRequest(HttpReq req)
		{
			if (req.m_bIsBinary)
			{
				lock (m_binaryRequestLock)
				{
					m_binaryRequests.Enqueue(req);
				}
			}
			else 
			{ 
				lock (m_metaRequestLock)
				{
					m_metaRequests.Enqueue(req);
				}
			}
		}
		private void ProcessMetaQueue()
		{
			int maxRequests = 10;
			for (int i = 0; i < maxRequests; i++)
			{
				HttpReq next = null;
				lock (m_metaRequestLock)
				{
					if (m_metaRequests.Count == 0)
						return;
					next = m_metaRequests.Dequeue();
				}
				string data = HttpGet(next.m_url, next.m_bCacheAllowed, next.m_bLogPerf);
				if (next.m_onStringComplete != null)
					next.m_onStringComplete(data);
			}
		}
		private void ProcessBinaryQueue()
		{
			int maxRequests = 1;
			for (int i = 0; i < maxRequests; i++)
			{
				HttpReq next = null;
				lock (m_binaryRequestLock)
				{
					if (m_binaryRequests.Count == 0)
						return;
					next = m_binaryRequests.Dequeue();
				}
				byte[] data = HttpGetBinary(next.m_url, next.m_bCacheAllowed);
				if (next.m_onBinaryComplete != null)
					next.m_onBinaryComplete(data);
			}
		}
		private byte[] HttpGetBinary(string url, bool bCacheAllowed)
		{
			byte[] retVal = null;
			if (bCacheAllowed && GetCachedResults(url, out retVal))
			{
				return retVal;
			}
			HttpResponseMessage response = HttpGet_Internal(url, true);
			if (!response.IsSuccessStatusCode)
			{
				Log.Error("HTTP request failed: " + url + " status: " + response.StatusCode);
				return null;
			}
			retVal = response.Content.ReadAsByteArrayAsync().Result;
			CacheQueryResults(url, retVal);
			return retVal;
		}
		protected string HttpGet(string url, bool bCacheAllowed, bool logPerf)
		{
			string retVal = null;
			if (bCacheAllowed && GetCachedResults(url, out retVal))
			{
				return retVal;
			}
			HttpResponseMessage response = HttpGet_Internal(url, logPerf);
			if (!response.IsSuccessStatusCode)
			{
				Log.Error("HTTP request failed: " + url + " status: " + response.StatusCode);
				return null;
			}
			retVal = response.Content.ReadAsStringAsync().Result;
			CacheQueryResults(url, retVal);
			return retVal;
		}

		private HttpResponseMessage HttpGet_Internal(string url, bool logPerf)
		{
			HttpClient client;
			lock (m_httpClientLock)
			{
				client = m_httpClient;
			}
			if (client == null)
			{
				return new HttpResponseMessage(System.Net.HttpStatusCode.PreconditionFailed);
			}

			int requestId = Interlocked.Increment(ref s_requestCounter);
			Stopwatch stopwatch = Stopwatch.StartNew();
			Log.Perf("#" + requestId + " start GET " + url);
			try
			{
				HttpResponseMessage response = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url)).Result;
				stopwatch.Stop();
				if (logPerf)
				{ 
					Log.Perf("#" + requestId + " done GET " + stopwatch.ElapsedMilliseconds + "ms status=" + (int)response.StatusCode + " " + url);
				}
				return response;
			}
			catch (Exception ex)
			{
				// A dropped/refused/timed-out connection surfaces here as an
				// AggregateException out of .Result. Left unhandled it unwinds
				// to the thread root and crashes the app to desktop. Mark offline
				// and return an error response so callers see a clean failure.
				stopwatch.Stop();
				if (logPerf)
				{
					Log.Perf("#" + requestId + " fail GET " + stopwatch.ElapsedMilliseconds + "ms " + url);
				}
				Log.Exception(ex);
				m_bIsOnline = false;
				return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
			}
		}

		// POST a JSON body to a command endpoint. The GET helpers above can't
		// carry a payload, so anything that ships a serialized object (e.g.
		// analytics) goes through here. Commands are never cached. Mirrors
		// HttpGet's offline handling: a dropped/refused connection marks the
		// client offline and returns null rather than unwinding to the thread
		// root. Returns the response body on success, null on failure.
		protected string HttpPostJson(string url, string json, bool logPerf)
		{
			HttpResponseMessage response = HttpPostJson_Internal(url, json, logPerf);
			if (!response.IsSuccessStatusCode)
			{
				Log.Error("HTTP POST failed: " + url + " status: " + response.StatusCode);
				return null;
			}
			return response.Content.ReadAsStringAsync().Result;
		}

		private HttpResponseMessage HttpPostJson_Internal(string url, string json, bool logPerf)
		{
			HttpClient client;
			lock (m_httpClientLock)
			{
				client = m_httpClient;
			}
			if (client == null)
			{
				return new HttpResponseMessage(System.Net.HttpStatusCode.PreconditionFailed);
			}

			int requestId = Interlocked.Increment(ref s_requestCounter);
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (logPerf)
			{
				Log.Perf("#" + requestId + " start POST " + url);
			}
			try
			{
				HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
				request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
				HttpResponseMessage response = client.SendAsync(request).Result;
				stopwatch.Stop();
				if (logPerf)
				{
					Log.Perf("#" + requestId + " done POST " + stopwatch.ElapsedMilliseconds + "ms status=" + (int)response.StatusCode + " " + url);
				}
				return response;
			}
			catch (Exception ex)
			{
				stopwatch.Stop();
				if (logPerf)
				{
					Log.Perf("#" + requestId + " fail POST " + stopwatch.ElapsedMilliseconds + "ms " + url);
				}
				Log.Exception(ex);
				m_bIsOnline = false;
				return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
			}
		}

		public bool IsTrackCached(string trackID)
		{
			//todo reconstruct the original query url to check if we have this track or not

			return false;
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
	}
}
