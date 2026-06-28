using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PulseIngestion.MediaTools
{
	public class FFMPEG : MediaTool
	{
		private string m_ffmpegPath;
		private eMusicFormat m_targetFormat;

		public FFMPEG(IngestionConfig config) : base(config)
		{
			m_ffmpegPath = ResolveFfmpegPath(config.FFMpegLocation);
			m_targetFormat = config.DestinationMusicFormat;

			bool haveExe = !string.IsNullOrEmpty(m_ffmpegPath);
			bool formatOk = m_targetFormat != eMusicFormat.UNKNOWN;
			m_isReady = haveExe && formatOk;

			if (!haveExe)
			{
				Log.Error("ffmpeg.exe not found. Set FFMpegLocation in config.txt or add it to PATH.");
			}
			if (!formatOk)
			{
				Log.Error("DestinationMusicFormat is UNKNOWN; media conversion disabled.");
			}
			if (m_isReady)
			{
				Log.Info("Using ffmpeg: " + m_ffmpegPath + " -> " + m_targetFormat);
			}
		}

		private static string ResolveFfmpegPath(string configuredPath)
		{
			if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
			{
				return configuredPath;
			}

			string pathVar = Environment.GetEnvironmentVariable("PATH");
			if (string.IsNullOrEmpty(pathVar))
			{
				return "";
			}

			string[] directories = pathVar.Split(Path.PathSeparator);
			for (int i = 0; i < directories.Length; i++)
			{
				if (string.IsNullOrEmpty(directories[i]))
				{
					continue;
				}
				string candidate = Path.Combine(directories[i], "ffmpeg.exe");
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}

			return "";
		}

		public override async Task ConvertToOutputFormat(string fileSource)
		{
			Log.Info("Processing: " + Path.GetFileName(fileSource));

			string extension = MusicFormat.GetExtension(m_targetFormat);
			string target = Path.ChangeExtension(fileSource, extension);

			// Guard against converting a file onto itself (source already in target
			// format) — ffmpeg's -y would read and overwrite the same path.
			if (PathsEqual(fileSource, target))
			{
				Log.Warning("Already in target format, skipped: " + fileSource);
				return;
			}

			string fileName = Path.GetFileName(target);
			string codecArgs = GetCodecArgs(m_targetFormat);
			string args = $"-y -threads 1 -i \"{fileSource}\" -vn {codecArgs} -map_metadata 0 \"{target}\"";

			try
			{
				await RunFfmpegCommand(m_ffmpegPath, args, fileName);
				if (m_config.DeleteAfterConversion)
				{
					File.Delete(fileSource);
				}
				else
				{
					string backupFile = fileSource + ".bak";
					File.Move(fileSource, backupFile);
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		private static string GetCodecArgs(eMusicFormat format)
		{
			switch (format)
			{
				case eMusicFormat.MP3:
					return "-codec:a libmp3lame -b:a 320k -ar 44100 -id3v2_version 3 -write_id3v1 1";
				case eMusicFormat.FLAC:
					return "-codec:a flac";
				case eMusicFormat.M4A:
					return "-codec:a aac -b:a 256k";
				case eMusicFormat.AAC:
					return "-codec:a aac -b:a 256k";
				case eMusicFormat.OGG:
					return "-codec:a libvorbis -q:a 6";
				case eMusicFormat.OPUS:
					return "-codec:a libopus -b:a 192k";
				case eMusicFormat.WAV:
					return "-codec:a pcm_s16le";
				case eMusicFormat.WMA:
					return "-codec:a wmav2 -b:a 192k";
				default:
					throw new Exception("Unsupported output format: " + format);
			}
		}

		private static bool PathsEqual(string left, string right)
		{
			string fullLeft = Path.GetFullPath(left);
			string fullRight = Path.GetFullPath(right);
			return string.Equals(fullLeft, fullRight, StringComparison.OrdinalIgnoreCase);
		}

		protected async Task RunFfmpegCommand(string ffmpegPath, string arguments, string consoleOutputPath)
		{
			Process process = new Process();
			process.StartInfo.FileName = ffmpegPath;
			process.StartInfo.Arguments = arguments;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardInput = true;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;

			process.OutputDataReceived += OnFfmpegOutputData;
			process.ErrorDataReceived += OnFfmpegErrorData;

			process.Start();

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			await process.WaitForExitAsync();
			if (process.ExitCode != 0)
			{
				throw new Exception($"FFmpeg exited with code {process.ExitCode}");
			}
		}

		private void OnFfmpegOutputData(object sender, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrEmpty(e.Data) && m_config.EnableFFMpegDebug)
			{
				Log.Info(e.Data);
			}
		}

		private void OnFfmpegErrorData(object sender, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrEmpty(e.Data) && m_config.EnableFFMpegDebug)
			{
				Log.Error(e.Data);
			}
		}
	}
}
