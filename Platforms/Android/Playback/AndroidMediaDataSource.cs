using System;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;

namespace Thump.Playback
{
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
		/// Host-supplied resolver. Given the URI Media3 is asking for, the
		/// host returns the full bytes of the asset, or null if the asset
		/// can't be served. A null result causes Open to throw an IOException,
		/// which Media3 surfaces as a playback error.
		/// </summary>
		public Func<Android.Net.Uri, byte[]> m_onResolveBytes;

		private byte[] m_bytes;
		private Android.Net.Uri m_uri;
		private int m_readPosition;
		private int m_bytesRemaining;

		public AndroidMediaDataSource() : base(false)
		{
		}

		/// <inheritdoc/>
		public override long Open(DataSpec dataSpec)
		{
			TransferInitializing(dataSpec);
			m_uri = dataSpec.Uri;

			if (m_onResolveBytes == null)
			{
				throw new Java.IO.IOException("No byte resolver attached for " + m_uri);
			}
			m_bytes = m_onResolveBytes(m_uri);
			if (m_bytes == null)
			{
				throw new Java.IO.IOException("No audio data for " + m_uri);
			}

			m_readPosition = (int)dataSpec.Position;
			m_bytesRemaining = m_bytes.Length - (int)dataSpec.Position;
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

		public IDataSource CreateDataSource()
		{
			AndroidMediaDataSource source = new AndroidMediaDataSource();
			source.m_onResolveBytes = m_onResolveBytes;
			return source;
		}
	}
}
