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


}
