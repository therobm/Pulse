using AndroidX.Media3.DataSource;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using Thump.Pulse;

namespace Thump.Playback.AndroidOS
{
	public class TrackResolver : Java.Lang.Object, ResolvingDataSource.IResolver
	{
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
				string tempPath = Path.Combine(m_cacheDir, SanitizeFileName(trackId) + ".tmp");
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
		/// <summary>
		/// Returns a filesystem-safe form of the supplied track id by replacing any
		/// character outside [A-Za-z0-9._-] with '_'. Null or empty input returns "track".
		/// </summary>
		private static string SanitizeFileName(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return "track";
			}
			StringBuilder builder = new StringBuilder(id.Length);
			for (int i = 0; i < id.Length; i++)
			{
				char c = id[i];
				bool safe = false;
				if (c >= 'A' && c <= 'Z')
				{
					safe = true;
				}
				if (c >= 'a' && c <= 'z')
				{
					safe = true;
				}
				if (c >= '0' && c <= '9')
				{
					safe = true;
				}
				if (c == '.' || c == '_' || c == '-')
				{
					safe = true;
				}
				if (safe)
				{
					builder.Append(c);
				}
				else
				{
					builder.Append('_');
				}
			}
			return builder.ToString();
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
				const string scheme = "thump://";
				if (full.StartsWith(scheme))
				{
					id = full.Substring(scheme.Length);
				}
			}
			return id;
		}

	}
}