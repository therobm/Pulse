using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Thump.Pulse
{
	public class HttpReq
	{
		public Action<byte[]> m_onBinaryComplete;
		public Action<string> m_onStringComplete;
		public string m_url;
		public eMediaCacheStrategy m_cacheStrategy;
		public bool m_bIsBinary;
		public bool m_bLogPerf;
		public HttpReq(string url, Action<byte[]> onComplete, eMediaCacheStrategy cacheStrategy, bool logPerf)
		{
			m_url = url;
			m_onBinaryComplete = onComplete;
			m_bIsBinary = true;
			m_cacheStrategy = cacheStrategy;
			m_bLogPerf = logPerf;
		}
		public HttpReq(string url, Action<string> onComplete, eMediaCacheStrategy cacheStrategy, bool logPerf)
		{
			m_url = url;
			m_onStringComplete = onComplete;
			m_bIsBinary = false;
			m_cacheStrategy = cacheStrategy;
			m_bLogPerf = logPerf;
		}
	}

	public class HttpQueue
	{
		private Thread m_thread;
		public int m_maxRequests = 1;
		public object m_queueLock = new object();
		Queue<HttpReq> m_requests = new Queue<HttpReq>();
		MediaClient m_mediaClient;

		private volatile bool m_suspended;
		private CancellationTokenSource m_cancellationToken = new CancellationTokenSource();
		private object m_cancellationLock = new object();
		private SemaphoreSlim m_signal = new SemaphoreSlim(0);
		private HttpReq m_inFlight;

		public HttpQueue(MediaClient mediaClient, int maxRequests)
		{
			m_maxRequests = maxRequests;
			m_mediaClient = mediaClient;
			m_thread = new Thread(Process);
			m_thread.IsBackground = true;
			m_thread.Start();
		}

		public void Suspend()
		{
			lock (m_cancellationLock)
			{
				m_suspended = true;
				m_cancellationToken.Cancel();   // aborts the in-flight body read mid-chunk
			}
		}

		public void Resume()
		{
			lock (m_cancellationLock)
			{
				m_suspended = false;
				m_cancellationToken.Dispose();
				m_cancellationToken = new CancellationTokenSource();
			}
		}

		private CancellationToken CurrentToken()
		{
			lock (m_cancellationLock)
			{
				return m_cancellationToken.Token;
			}
		}

		public void Process()
		{
			while (true)
			{
				m_signal.Wait();
				if (m_suspended)
				{
					m_signal.Release();
					Thread.Sleep(50);
					continue;
				}

				for (int i = 0; i < m_maxRequests; i++)
				{
					HttpReq next = null;
					if (m_inFlight != null)
					{
						next = m_inFlight;
						m_inFlight = null;
					}
					else
					{
						lock (m_queueLock)
						{
							if (m_requests.Count == 0)
							{
								break;
							}
							next = m_requests.Dequeue();
						}
					}

					if (m_suspended)
					{
						m_inFlight = next;   // lost the race before starting; hold it, no callback
						break;
					}
					try
					{
						ProcessRequest(next);
					}
					catch(Exception ex)
					{
						Log.Exception(ex);
					}
				}
			}
		}
		private void ProcessRequest(HttpReq req)
		{
			CancellationToken token = CurrentToken();
			if (req.m_bIsBinary)
			{
				byte[] data = m_mediaClient.HttpGetBinary(req.m_url, req.m_cacheStrategy, token);
				if (token.IsCancellationRequested)
				{
					m_inFlight = req;   // suspended mid-flight: retry on resume, don't fire callback
					return;
				}
				if (req.m_onBinaryComplete != null)
				{
					req.m_onBinaryComplete(data);
				}
			}
			else
			{
				string data = m_mediaClient.HttpGet(req.m_url, req.m_cacheStrategy, req.m_bLogPerf, false);
				if (req.m_onStringComplete != null)
				{
					req.m_onStringComplete(data);
				}
			}
		}

		public void QueueRequest(HttpReq req)
		{
			lock (m_queueLock)
			{
				m_requests.Enqueue(req);
			}
			m_signal.Release();
		}
	}
}
