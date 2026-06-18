using AndroidX.Media3.DataSource;
using System;
using System.IO;
using System.Net.Http;
using PulseApp.Pulse;

namespace PulseApp.Playback.AndroidOS
{
	public class TrackResolver : Java.Lang.Object, ResolvingDataSource.IResolver
	{
		static int s_tempId = 0;
		private MediaClient m_data;
		private string m_cacheDir;

		public TrackResolver(MediaClient data, string cacheDir)
		{
			m_data = data;
			m_cacheDir = cacheDir;
		}

		public DataSpec ResolveDataSpec(DataSpec dataSpec)
		{
			string trackId = ExtractTrackId(dataSpec.Uri);
			string url = m_data.GetTrackAudioURL(trackId);

			// cache hit — write to file, point at it
			byte[] raw;
			if (m_data.GetCachedResults(url, out raw) && raw != null)
			{
				s_tempId++;
				if (s_tempId > 20)
					s_tempId = 1;
				string tempPath = Path.Combine(m_cacheDir, "playing_"+s_tempId+".tmp");
				using (FileStream fs = new FileStream(tempPath, FileMode.Create))
				{
					fs.Write(raw, 0, raw.Length);
				}
				return dataSpec.BuildUpon()
					.SetUri(global::Android.Net.Uri.FromFile(new Java.IO.File(tempPath)))
					.SetPosition(0)
					.Build();
			}

			// cache miss — give Media3 the real HTTP URL, let it stream natively
			return dataSpec.BuildUpon()
				.SetUri(global::Android.Net.Uri.Parse(url))
				.Build();
		}

		public void BeginStreamToDisk(string url, string filePath)
		{

		}
		public Android.Net.Uri ResolveReportedUri(Android.Net.Uri uri)
		{
			return uri;
		}
		private string ExtractTrackId(global::Android.Net.Uri uri)
		{
			if (uri == null)
			{
				return null;
			}
			string id = uri.Authority;
			if (string.IsNullOrEmpty(id))
			{
				string full = uri.ToString();
				const string scheme = "pulse://";
				if (full.StartsWith(scheme))
				{
					id = full.Substring(scheme.Length);
				}
			}
			return id;
		}

	}
}