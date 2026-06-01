using System;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using Thump.Data;
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
		/// <summary>The byte resolver every created data source receives. Set this once before handing the factory to Media3.</summary>
		public Func<Android.Net.Uri, byte[]> m_onResolveBytes;

		/// <summary>
		/// The android process needs access to our data pipeline to fetch
		/// info and track data
		/// </summary>
		private MediaClient m_data;

		public AndroidMediaDataSourceFactory(MediaClient data)
		{
			m_data = data;
		}

		public IDataSource CreateDataSource()
		{
			AndroidMediaDataSource source = new AndroidMediaDataSource(m_data);
			source.m_onResolveBytes = m_onResolveBytes;
			return source;
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
			// Skip a leading ID3v2 tag so the stream starts at the first audio frame.
			if (bytes.Length < 10)
			{
				return 0;
			}
			if (bytes[0] != 0x49 || bytes[1] != 0x44 || bytes[2] != 0x33)   // "ID3"
			{
				return 0;
			}
			int flags = bytes[5];
			int size = ((bytes[6] & 0x7F) << 21)
					 | ((bytes[7] & 0x7F) << 14)
					 | ((bytes[8] & 0x7F) << 7)
					 | (bytes[9] & 0x7F);          // synchsafe int
			int offset = 10 + size;
			if ((flags & 0x10) != 0)                 // footer present
			{
				offset = offset + 10;
			}
			if (offset < 10 || offset > bytes.Length)
			{
				return 0;
			}
			return offset;
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

		public AndroidMediaDataSource(MediaClient data) : base(false)
		{
			m_data = data;
		}

		/// <inheritdoc/>
		public override long Open(DataSpec dataSpec)
		{
			TransferInitializing(dataSpec);
			m_uri = dataSpec.Uri;
			string trackId = m_uri.Host;
			if (string.IsNullOrEmpty(trackId))
			{
				trackId = m_uri.LastPathSegment;
			}

			//Grab the bytes for the requested track
			m_bytes = m_data.GetTrackAudioFromCache(trackId);

			if (m_bytes == null)
			{
				throw new Java.IO.IOException("No audio data for " + m_uri);
			}

			int audioStart = GetAudioStartOffset(m_bytes);

			m_readPosition = audioStart + (int)dataSpec.Position;
			m_bytesRemaining = m_bytes.Length - m_readPosition;
			if (dataSpec.Length != C.LengthUnset)
			{
				m_bytesRemaining = (int)System.Math.Min(m_bytesRemaining, dataSpec.Length);
			}
			if (m_bytesRemaining < 0)
			{
				m_bytesRemaining = 0;
			}

			TransferStarted(dataSpec);
			return m_bytesRemaining;
		}

		/// <inheritdoc/>
		public override int Read(byte[] buffer, int offset, int length)
		{
			if (length == 0)
			{
				return 0;
			}
			if (m_bytesRemaining <= 0)
			{
				return C.ResultEndOfInput;
			}
			int toRead = System.Math.Min(length, m_bytesRemaining);
			System.Array.Copy(m_bytes, m_readPosition, buffer, offset, toRead);
			m_readPosition = m_readPosition + toRead;
			m_bytesRemaining = m_bytesRemaining - toRead;
			BytesTransferred(toRead);
			return toRead;
		}

		/// <inheritdoc/>
		public override Android.Net.Uri Uri
		{
			get { return m_uri; }
		}

		/// <inheritdoc/>
		public override void Close()
		{
			if (m_uri != null)
			{
				TransferEnded();
			}
			m_uri = null;
			m_bytes = null;
		}
	}

	
}
