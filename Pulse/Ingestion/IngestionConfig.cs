using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseIngestion
{
	public class IngestionConfig
	{
		public string FFMpegLocation = "C:\\ffmpeg\\bin\\ffmpeg.exe";
		public string MusicImportDir = "Z:\\MusicTest";
		public string ReportDirectory = ""; // empty => <MusicSource>\PulseIngestion
		public int ThreadCount = 1;
		public int ScanningIntervalMinutes = 1440; //24h default
		public eMusicFormat DestinationMusicFormat = eMusicFormat.MP3;
		public eMusicFormat[] SourceFormats = { eMusicFormat.FLAC, eMusicFormat.AAC };
		public bool EnableFFMpegDebug = false;
		public bool EnableMediaConversion = true;
		public bool DeleteAfterConversion = false;
		public bool EnableOrganization = true;
		public bool RenameFilesToTrackTitle = true;
		public bool RemoveEmptyDirectories = true;
		public bool CleanupDuplicatesByWildcard = true;
		public string DuplicateFileWildcardToken = " (1)";

		public bool ValidateConfig()
		{

			if (!Directory.Exists(MusicImportDir))
			{
				Log.Warning("Ingestion: disabled, configuration invalid (MusicImportDir missing).");
				return false;
			}
			if (!File.Exists(FFMpegLocation))
			{
				Log.Warning("Ingestion: disabled, configuration invalid (FFMpegLocation missing).");
				return false;
			}


			return true;
		}



	}
}
