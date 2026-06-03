using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.ApplicationModel;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Thump.Data;

namespace Thump.Pulse
{
	// The single API surface every media-server client (Subsonic today, Pulse-native
	// in the future) must implement. Consumers (ThumpData, MainView, the playback
	// service, settings) hold an MediaClient, so the concrete implementation can
	// be swapped without touching them.
	public abstract class MediaClient
	{
		private static bool AcceptAnyServerCertificate(HttpRequestMessage request, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
		{
			return true;
		}



		ThumpCache m_cache;

		private Thread m_thread;
		protected string m_baseUrl;
		protected string m_user;
		private string m_apiParams;
		private HttpClient m_httpClient;
		private object m_httpClientLock = new object();

		private bool m_bInitialized = false;
		protected bool m_bIsOnline = true;

		public MediaClient(ThumpCache cache)
		{
			m_cache = cache;

			m_thread = new Thread(ConnectionLoop);
			m_thread.IsBackground = true;
			m_thread.Start();
		}

		private void ConnectionLoop()
		{
			while (true)
			{
				if (m_bInitialized)
				{
					Ping(out JsonElement response);
				}
				Thread.Sleep(5000);
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
		public abstract void Star(string trackId, Action<bool> onComplete);
		public abstract void Unstar(string trackId, Action<bool> onComplete);
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
		protected byte[] HttpGetBinary(string url, bool bCacheAllowed)
		{
			byte[] retVal = null;
			if (bCacheAllowed && GetCachedResults(url, out retVal))
			{
				return retVal;
			}
			HttpResponseMessage response = HttpGet_Internal(url);
			if (!response.IsSuccessStatusCode)
			{
				Log.Error("HTTP request failed: " + url + " status: " + response.StatusCode);
				return null;
			}
			retVal = response.Content.ReadAsByteArrayAsync().Result;
			CacheQueryResults(url, retVal);
			return retVal;
		}
		protected string HttpGet(string url, bool bCacheAllowed)
		{
			string retVal = null;
			if (bCacheAllowed && GetCachedResults(url, out retVal))
			{
				return retVal;
			}
			HttpResponseMessage response = HttpGet_Internal(url);
			if (!response.IsSuccessStatusCode)
			{
				Log.Error("HTTP request failed: " + url + " status: " + response.StatusCode);
				return null;
			}
			retVal = response.Content.ReadAsStringAsync().Result;
			CacheQueryResults(url, retVal);
			return retVal;
		}

		private HttpResponseMessage HttpGet_Internal(string url)
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

			try
			{
				return client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url)).Result;
			}
			catch (Exception ex)
			{
				// A dropped/refused/timed-out connection surfaces here as an
				// AggregateException out of .Result. Left unhandled it unwinds
				// to the thread root and crashes the app to desktop. Mark offline
				// and return an error response so callers see a clean failure.
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
		protected string HttpPostJson(string url, string json)
		{
			HttpResponseMessage response = HttpPostJson_Internal(url, json);
			if (!response.IsSuccessStatusCode)
			{
				Log.Error("HTTP POST failed: " + url + " status: " + response.StatusCode);
				return null;
			}
			return response.Content.ReadAsStringAsync().Result;
		}

		private HttpResponseMessage HttpPostJson_Internal(string url, string json)
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

			try
			{
				HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
				request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
				return client.SendAsync(request).Result;
			}
			catch (Exception ex)
			{
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
