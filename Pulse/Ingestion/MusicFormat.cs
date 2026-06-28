using System;
using System.IO;

namespace PulseIngestion
{
	public enum eMusicFormat
	{
		MP3,
		FLAC,
		M4A,
		AAC,
		OGG,
		OPUS,
		WAV,
		WMA,
		UNKNOWN
	}

	/// <summary>
	/// Format-intrinsic facts about an eMusicFormat. ffmpeg codec/encoder choices
	/// live with the ffmpeg tool; this is just what every consumer agrees on.
	/// </summary>
	public static class MusicFormat
	{
		private static readonly string[] s_audioExtensions = { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma" };

		public static bool IsAudioFile(string path)
		{
			string extension = Path.GetExtension(path);
			for (int i = 0; i < s_audioExtensions.Length; i++)
			{
				if (string.Equals(extension, s_audioExtensions[i], StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		public static string GetExtension(eMusicFormat format)
		{
			switch (format)
			{
				case eMusicFormat.MP3:
					return ".mp3";
				case eMusicFormat.FLAC:
					return ".flac";
				case eMusicFormat.M4A:
					return ".m4a";
				case eMusicFormat.AAC:
					return ".aac";
				case eMusicFormat.OGG:
					return ".ogg";
				case eMusicFormat.OPUS:
					return ".opus";
				case eMusicFormat.WAV:
					return ".wav";
				case eMusicFormat.WMA:
					return ".wma";
				default:
					throw new Exception("Unsupported music format: " + format);
			}
		}
	}
}
