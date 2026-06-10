using Microsoft.Maui.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Thump.Pulse
{
	public class Http
	{
		public const int s_defaultTimeout = 10;

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

		public enum eRequestType
		{
			AudioStream,
			MetaData,
			BinaryData,
		}

		private HttpClient m_httpAudioClient;
		private HttpClient m_httpMetaClient;
		private HttpClient m_httpBinaryClient;
		private object m_httpLock = new object();
		private bool m_bIsOnline = true;
		private int m_bConnectionFailures = 0;
		public Http()
		{

		}
		public void SetOnlineStatus(bool isOnline)
		{
			if (!isOnline)
				m_bConnectionFailures++;
			else
			{
				m_bConnectionFailures = 0;
				m_bIsOnline = true;
			}

			if(m_bConnectionFailures > 5)	
				m_bIsOnline = false;
		}
		private static bool AcceptAnyServerCertificate(HttpRequestMessage request, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
		{
			//most users are only on lan, this is far more convienent than being safe
			return true;
		}

		public void OnServerChanged()
		{
			HttpClientHandler metaHandler = new HttpClientHandler();
			metaHandler.ServerCertificateCustomValidationCallback = AcceptAnyServerCertificate;
			metaHandler.AutomaticDecompression = System.Net.DecompressionMethods.All;

			HttpClientHandler binaryHandler = new HttpClientHandler();
			binaryHandler.ServerCertificateCustomValidationCallback = AcceptAnyServerCertificate;
			binaryHandler.AutomaticDecompression = System.Net.DecompressionMethods.None;

			HttpClientHandler audioHandler = new HttpClientHandler();
			audioHandler.ServerCertificateCustomValidationCallback = AcceptAnyServerCertificate;
			audioHandler.AutomaticDecompression = System.Net.DecompressionMethods.None;

			HttpClient oldAudioClient;
			HttpClient oldMetaClient;
			HttpClient oldBinaryClient;
			lock (m_httpLock)
			{
				oldAudioClient = m_httpAudioClient;
				oldMetaClient = m_httpMetaClient;
				oldBinaryClient = m_httpBinaryClient;

				m_httpAudioClient = new HttpClient(audioHandler);
				m_httpMetaClient = new HttpClient(metaHandler);
				m_httpBinaryClient = new HttpClient(binaryHandler);

				m_httpAudioClient.Timeout = Timeout.InfiniteTimeSpan;
				m_httpMetaClient.Timeout = Timeout.InfiniteTimeSpan;
				m_httpBinaryClient.Timeout = Timeout.InfiniteTimeSpan;
			}

			if (oldAudioClient != null)
				oldAudioClient.Dispose();
			if (oldMetaClient != null)
				oldMetaClient.Dispose();
			if (oldBinaryClient != null)
				oldBinaryClient.Dispose();
		}

		public HttpResponseMessage HttpGetStream(string url, long position, CancellationToken token)
		{
			HttpClient client;
			lock (m_httpLock)
			{
				client = m_httpAudioClient;
			}

			if (client == null) 
			{ 
				return null; 
			}

			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
			if (position > 0)
			{
				request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(position, null);
			}


			try
			{
				return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).Result;
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				return null;
			}
		}

		public static bool IsNetworkAvailable()
		{
			bool isNetworkAvailable = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
			return isNetworkAvailable;
		}

		public HttpResponseMessage HttpGet(string url, eRequestType requestType, CancellationToken token, bool ignoreOnline, float timeoutSeconds)
		{
			bool isNetworkAvailable = IsNetworkAvailable();

			if (!isNetworkAvailable)
				return new HttpResponseMessage(System.Net.HttpStatusCode.GatewayTimeout);

			if (!ignoreOnline && !m_bIsOnline)
				return new HttpResponseMessage(System.Net.HttpStatusCode.GatewayTimeout);

			
			HttpClient client;
			lock (m_httpLock)
			{
				switch (requestType)
				{
					case eRequestType.AudioStream:
						client = m_httpAudioClient;
						break;
					case eRequestType.BinaryData:
						client = m_httpBinaryClient;
						break;
					case eRequestType.MetaData:
					default:
						client = m_httpMetaClient;
						break;
				}
			}

			if (client == null)
			{
				return new HttpResponseMessage(System.Net.HttpStatusCode.PreconditionFailed);
			}


			int requestId = Interlocked.Increment(ref s_requestCounter);
			Stopwatch stopwatch = Stopwatch.StartNew();
			Log.Perf("#" + requestId + " start GET " + url);

			//apply timeout to the header read only
			HttpResponseMessage response = null;
			bool transportFailure = false;
			for (int attempt = 1; attempt <= s_maxHttpAttempts; attempt++)
			{
				response = null;
				transportFailure = false;
				try
				{
					using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
					using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

					Task<HttpResponseMessage> fetch = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, linked.Token);
					response = fetch.Result;
				}
				catch (Exception ex)
				{
					// a deliberate prefetch suspend cancels the token; don't log it as a fault
					if (token.IsCancellationRequested)
					{
						stopwatch.Stop();
						return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
					}
					Log.Exception(ex);
					transportFailure = true;
					response = null;
				}

				// non-retryable: 2xx success or a normal HTTP error like 404/401 — return immediately.
				if (response != null)
				{
					int statusCode = (int)response.StatusCode;
					bool retryableStatus = false;
					if (statusCode == 408 || statusCode == 504)
					{
						retryableStatus = true;
						transportFailure = true;
					}
					else if (statusCode >= 500 && statusCode <= 599)
					{
						retryableStatus = true;
					}
					if (!retryableStatus)
					{
						stopwatch.Stop();
						
						Log.Perf("#" + requestId + " done GET " + stopwatch.ElapsedMilliseconds + "ms status=" + (int)response.StatusCode + " " + url);
						
						return response;
					}
				}

				if (attempt >= s_maxHttpAttempts)
				{
					break;
				}
				if (token.IsCancellationRequested)
				{
					break;
				}
				if (!m_bIsOnline)
				{
					break;
				}
				Thread.Sleep(s_httpRetryBaseMs * attempt);
			}

			stopwatch.Stop();

			// All attempts exhausted: surface the same RequestTimeout that the
			// pre-retry code returned when SendAsync threw.
			if (response == null)
			{
				response = new HttpResponseMessage(System.Net.HttpStatusCode.RequestTimeout);
			}

			
			Log.Perf("#" + requestId + " done GET " + stopwatch.ElapsedMilliseconds + "ms status=" + (int)response.StatusCode + " " + url);
			

			// Transport-level failure with the connection still believed-online:
			// feed the existing ping counter so a couple of dead requests can flip
			// us offline without waiting on the 5-second ping cycle. One vote per
			// exhausted request, not per attempt.
			if (transportFailure && m_bIsOnline)
			{
				SetOnlineStatus(false);
			}

			return response;
		}

		public HttpResponseMessage HttpPostJson_Internal(string url, eRequestType requestType, string json, float timeoutSeconds = s_defaultTimeout)
		{
			if (!m_bIsOnline)
				return new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway);

			HttpClient client;
			lock (m_httpLock)
			{
				switch (requestType)
				{
					case eRequestType.AudioStream:
						client = m_httpAudioClient;
						break;
					case eRequestType.BinaryData:
						client = m_httpBinaryClient;
						break;
					case eRequestType.MetaData:
					default:
						client = m_httpMetaClient;
						break;
				}
			}

			if (client == null)
			{
				return new HttpResponseMessage(System.Net.HttpStatusCode.PreconditionFailed);
			}

			int requestId = Interlocked.Increment(ref s_requestCounter);
			Stopwatch stopwatch = Stopwatch.StartNew();
			
			Log.Perf("#" + requestId + " start POST " + url);
			
			try
			{

				using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

				HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
				request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
				HttpResponseMessage response = client.SendAsync(request).Result;
				stopwatch.Stop();
			
				Log.Perf("#" + requestId + " done POST " + stopwatch.ElapsedMilliseconds + "ms status=" + (int)response.StatusCode + " " + url);
			
				return response;
			}
			catch (Exception ex)
			{
				stopwatch.Stop();
				
				Log.Perf("#" + requestId + " fail POST " + stopwatch.ElapsedMilliseconds + "ms " + url);
				
				Log.Exception(ex);
				return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
			}
		}
	}
}
