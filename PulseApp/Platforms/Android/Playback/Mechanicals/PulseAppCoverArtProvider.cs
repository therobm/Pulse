using System;
using System.Threading;
using Android.Content;
using PulseApp.Data;
using PulseApp.Pulse;


namespace PulseApp.Playback.AndroidOS
{
	[Android.Content.ContentProvider(new string[] { "com.therobm.pulse.coverart" }, Exported = true, GrantUriPermissions = true)]
	public class PulseAppCoverArtProvider : Android.Content.ContentProvider
	{
		private const string s_authority = "com.therobm.pulse.coverart";
		private const string s_logTag = "PulseAppCoverArtProvider";

		public override bool OnCreate()
		{
			return true;
		}

		public override string GetType(Android.Net.Uri uri)
		{
			return "image/jpeg";
		}

		public override Android.Database.ICursor Query(Android.Net.Uri uri, string[] projection, string selection, string[] selectionArgs, string sortOrder)
		{
			return null;
		}

		public override Android.Net.Uri Insert(Android.Net.Uri uri, Android.Content.ContentValues values)
		{
			return null;
		}

		public override int Update(Android.Net.Uri uri, Android.Content.ContentValues values, string selection, string[] selectionArgs)
		{
			return 0;
		}

		public override int Delete(Android.Net.Uri uri, string selection, string[] selectionArgs)
		{
			return 0;
		}

		public override Android.OS.ParcelFileDescriptor OpenFile(Android.Net.Uri uri, string mode)
		{
			try
			{
				string artId = uri.LastPathSegment;
				if (string.IsNullOrEmpty(artId))
				{
					return null;
				}

				MediaClient data = PulseAppMediaLibraryService.s_mediaClient;
				if (data == null)
				{
					return null;
				}

				byte[] result = data.GetCachedCoverArt(artId);

				if (result == null || result.Length == 0)
				{
					return null;
				}

				Java.IO.File cacheDir = Context.CacheDir;
				Java.IO.File outFile = new Java.IO.File(cacheDir, "coverart_" + artId + ".img");
				if (!outFile.Exists())
				{
					System.IO.File.WriteAllBytes(outFile.AbsolutePath, result);
				}
				return Android.OS.ParcelFileDescriptor.Open(outFile, Android.OS.ParcelFileMode.ReadOnly);
			}
			catch (Exception exception)
			{
				PulseApp.Log.Exception(exception);
				return null;
			}
		}
	}
}
