using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using Thump.Pulse;


namespace Thump.Playback.AndroidOS
{
	/// <summary>
	/// Pair to <see cref="AndroidMediaDataSource"/>. Media3 holds a single
	/// factory and asks it for a fresh <see cref="IDataSource"/> per request.
	/// The factory propagates the host's <see cref="m_onResolveBytes"/> to
	/// every data source it creates, so the host wires up the resolver once on
	/// the factory and every created data source inherits it.
	/// </summary>
	public class AndroidMediaDataSourceFactory : Java.Lang.Object, IDataSourceFactory
	{
		private MediaClient m_data;
		private string m_cacheDir;

		public AndroidMediaDataSourceFactory(MediaClient data, string cacheDir)
		{
			m_data = data;
			m_cacheDir = cacheDir;
		}

		public IDataSource CreateDataSource()
		{
			var inner = new DefaultDataSource.Factory(Android.App.Application.Context);// new FileDataSource.Factory();
			var resolver = new TrackResolver(m_data, m_cacheDir);
			return new ResolvingDataSource(inner.CreateDataSource(), resolver);
		}
	}

	/// <summary>
	/// The minimum boundary between Thump code and Media3's
	/// <see cref="IDataSource"/> contract. Media3 opens a fresh data source for
	/// every URI it wants to stream; this boundary handles the position /
	/// length / EOF bookkeeping that the contract requires and delegates the
	/// actual byte fetch to a pluggable <see cref="m_onResolveBytes"/>
	/// callback. Nothing on this class's surface knows about Thump types;
	/// the host turns a URI into a byte array however it likes.
	///
	/// The current shape assumes the host returns the full asset bytes up
	/// front and streaming is just a memory slice. That matches Thump's
	/// existing usage. If the host needs true streaming, a different shape
	/// (open-returns-stream, read-pulls-from-stream) would be required.
	/// </summary>
	public class AndroidMediaDataSource : BaseDataSource
	{
		/// <summary>
		/// A skipahead function to find where audio data actually starts
		/// </summary>
		/// <param name="bytes"></param>
		/// <returns></returns>
		private static int GetAudioStartOffset(byte[] bytes)
		{
			int offset = 0;

			// ID3v2 tag: skip it (synchsafe size covers APIC art + everything)
			if (bytes.Length >= 10 && bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33)
			{
				int flags = bytes[5];
				int size = ((bytes[6] & 0x7F) << 21)
						 | ((bytes[7] & 0x7F) << 14)
						 | ((bytes[8] & 0x7F) << 7)
						 | (bytes[9] & 0x7F);
				offset = 10 + size;
				if ((flags & 0x10) != 0) { offset += 10; }   // footer
			}

			return offset;   // tag-end
		}

		/// <summary>
		/// Host-supplied resolver. Given the URI Media3 is asking for, the
		/// host returns the full bytes of the asset, or null if the asset
		/// can't be served. A null result causes Open to throw an IOException,
		/// which Media3 surfaces as a playback error.
		/// </summary>
		public Func<Android.Net.Uri, byte[]> m_onResolveBytes;

		private MediaClient m_data;
		private byte[] m_bytes;
		private Android.Net.Uri m_uri;
		private int m_readPosition;
		private int m_bytesRemaining;
		private string m_trackId;
		private string m_url;
		private Stream m_readStream;
		private HttpResponseMessage m_response;
		/// <summary>
		/// um.. starting queue?
		/// </summary>
		private bool m_pendingCache = false;
		private MemoryStream m_memorySource;
		private MemoryStream m_cacheBuffer;
		private int m_expectedCacheLength;
		private TimeSpan m_stallWindow;
		private bool m_streamFinalized;

		public AndroidMediaDataSource(MediaClient data) : base(false)
		{
			m_data = data;
		}

		/// <inheritdoc/>
		public override long Open(DataSpec dataSpec)
		{
			Log.Perf("AndroidMediaDataSource::Open-Start");
			m_trackId = ExtractTrackId(dataSpec.Uri);
			m_uri = dataSpec.Uri;
			m_url = m_data.GetTrackAudioURL(m_trackId);
			int position = (int)dataSpec.Position;
			TransferInitializing(dataSpec);

			// 1. full blob already cached -> serve from memory, no network
			byte[] cached;
			if (m_data.GetCachedResults(m_url, out cached) && cached != null)
			{
				m_memorySource = new MemoryStream(cached, false);
				m_readStream = m_memorySource;
				m_bytesRemaining = cached.Length - position;
				
				int audioStart = GetAudioStartOffset(cached);
				int skipTo = audioStart + position;
				FastForwardStream(skipTo);
				m_bytesRemaining -= skipTo;

				m_pendingCache = false;
				TransferStarted(dataSpec);
				return m_bytesRemaining;
			}

			m_data.SetStreamingStatus(true);
			m_streamFinalized = false;

			// 2. network stream
			m_response = m_data.HttpGetStream(m_url, position);
			if (m_response == null || !m_response.IsSuccessStatusCode)
			{
				throw new IOException("stream open failed: " + m_trackId);
			}
			if (position > 0 && m_response.StatusCode != System.Net.HttpStatusCode.PartialContent)
			{
				// server ignored Range -> bytes start at 0, seeking would corrupt
				throw new IOException("server lacks range support: " + m_trackId);
			}
			m_readStream = m_response.Content.ReadAsStreamAsync().Result;

			long contentLength = -1;
			if (m_response.Content.Headers.ContentLength.HasValue)
			{
				contentLength = m_response.Content.Headers.ContentLength.Value;
			}
			m_bytesRemaining = (int)contentLength;

			// cache only on a clean sequential start with known length
			if (position == 0 && contentLength > 0)
			{
				m_cacheBuffer = new MemoryStream((int)contentLength);
				m_expectedCacheLength = (int)contentLength;
				m_pendingCache = true;
			}
			else
			{
				m_pendingCache = false;
			}

			if (position == 0)
			{
				byte[] header = new byte[10];
				ReadFull(m_readStream, header, 0, 10);
				if (m_pendingCache) 
				{ 
					m_cacheBuffer.Write(header, 0, 10); 
				}

				int audioStart = GetAudioStartOffset(header);   // header-only, drop the length guard
				{
					FastForwardStream(audioStart - 10);
					m_bytesRemaining -= audioStart;
				}
			}

			m_stallWindow = TimeSpan.FromSeconds(30);
			TransferStarted(dataSpec);

			Log.Perf("AndroidMediaDataSource::Open-Complete");
			return m_bytesRemaining;
		}


		private static int ReadFull(Stream stream, byte[] buf, int off, int count)
		{
			int total = 0;
			while (total < count)
			{
				int n = stream.Read(buf, off + total, count - total);
				if (n <= 0) { break; }
				total += n;
			}
			return total;
		}

		private void FastForwardStream(int count)
		{
			if (count <= 0)
			{
				return;
			}
			byte[] tmp = new byte[Math.Min(count, 8192)];
			int remaining = count;
			while (remaining > 0)
			{
				int n;
				try
				{
					n = m_readStream.Read(tmp, 0, Math.Min(remaining, tmp.Length));
				}
				catch (IOException)
				{
					// loader thread interrupted (load cancelled mid-open) — surface as
					// an IOException from Open so ExoPlayer treats it as a cancelled load
					throw;
				}
				if (n <= 0) { break; }
				if (m_pendingCache) { m_cacheBuffer.Write(tmp, 0, n); }
				remaining -= n;
			}
		}

		private string ExtractTrackId(Android.Net.Uri uri)
		{
			if (uri == null)
			{
				return null;
			}
			// MediaItemBuilder.GetURI builds "thump://<trackId>", so the id is the authority.
			string id = uri.Authority;
			if (string.IsNullOrEmpty(id))
			{
				// fallback: strip the scheme prefix directly in case the id contained
				// characters Uri parsed into path/query rather than authority
				string full = uri.ToString();
				const string scheme = "thump://";
				if (full.StartsWith(scheme))
				{
					id = full.Substring(scheme.Length);
				}
			}
			return id;
		}

		/// <inheritdoc/>
		public override int Read(byte[] buffer, int offset, int readLength)
		{
			Log.Perf("AndroidMediaDataSource::Read-Start offset = " + offset + " readLength=" + readLength);
			if (readLength == 0)
			{
				return 0;
			}
			if (m_bytesRemaining == 0)
			{
				return C.ResultEndOfInput;
			}

			int toRead = readLength;
			if (m_bytesRemaining > 0 && toRead > m_bytesRemaining)
			{
				toRead = (int)m_bytesRemaining;
			}

			int read;
			if (m_memorySource != null)
			{
				read = m_memorySource.Read(buffer, offset, toRead);
			}
			else
			{
				// network read needs its own stall guard; HttpClient.Timeout no
				// longer applies once we're past headers, and Exo's loader thread
				// blocks here indefinitely on a dead tunnel otherwise.
				using CancellationTokenSource cts = new CancellationTokenSource(m_stallWindow);
				read = m_readStream.ReadAsync(buffer, offset, toRead, cts.Token).Result;
			}

			if (read <= 0)
			{
				bool wasStreaming = (m_memorySource == null);
				if (wasStreaming)
				{
					FinalizeStream();
				}
				return C.ResultEndOfInput;
			}
			if (m_pendingCache)
			{
				m_cacheBuffer.Write(buffer, offset, read);
			}
			if (m_bytesRemaining > 0)
			{
				m_bytesRemaining -= read;
			}
			BytesTransferred(read);
			return read;
		}

		private void FinalizeStream()
		{
			if (m_streamFinalized)
			{
				return;
			}
			m_streamFinalized = true;

			bool commit = m_pendingCache && m_cacheBuffer != null && m_cacheBuffer.Length == m_expectedCacheLength;
			if (commit)
			{
				m_data.CacheQueryResults(m_url, m_cacheBuffer.ToArray());
			}

			// release the pipe so prefetch can resume. always fires for a live stream,
			// whether it finished cleanly, stalled out, or got closed early.
			m_data.SetStreamingStatus(false);
		}

		/// <inheritdoc/>
		public override Android.Net.Uri Uri
		{
			get { return m_uri; }
		}

		/// <inheritdoc/>
		public override void Close()
		{
			bool wasStreaming = (m_memorySource == null);

			if (wasStreaming)
			{
				FinalizeStream();   // commits if complete, releases pipe; no-op if EOF already did
			}

			m_pendingCache = false;
			if (m_cacheBuffer != null)
			{
				m_cacheBuffer.Dispose();
				m_cacheBuffer = null;
			}
			if (m_readStream != null)
			{
				try
				{
					m_readStream.Dispose();
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				m_readStream = null;
			}
			m_memorySource = null;
			if (m_response != null)
			{
				m_response.Dispose();
				m_response = null;
			}


			TransferEnded();
		}
	}


}
