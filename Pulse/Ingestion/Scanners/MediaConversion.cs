

using PulseIngestion.MediaTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PulseIngestion.Scanners
{
	public class MediaConversion : Scanner
	{
		private MediaTool m_conversionTool;
		private int m_completedCount = 0;
		public MediaConversion(IngestionConfig config) : base(config)
		{
			m_bIsActive = m_config.EnableMediaConversion;
			m_conversionTool = new FFMPEG(config);
		}
		public override void Initialize()
		{
			base.Initialize();
		}
		protected override void DoWork()
		{
			// Block the pump until every conversion finishes — the scanner pipeline
			// is synchronous, so we wait the async batch out on the loop thread.
			ConvertFiles().GetAwaiter().GetResult();
			base.DoWork();
		}
		protected async Task ConvertFiles()
		{
			m_completedCount = 0;

			if (!m_conversionTool.IsReady())
			{
				string message = "ffmpeg not available; skipping media conversion. Set FFMpegLocation in config.txt.";
				Log.Error(message);
				RecordError(message);
				OnComplete();
				return;
			}

			List<string> filesToConvert = new List<string>();
			foreach (string f in Directory.EnumerateFiles(m_config.MusicImportDir, "*.*", SearchOption.AllDirectories))
			{
				ReportProgress();
				bool found = false;
				foreach (eMusicFormat sourceFormat in m_config.SourceFormats)
				{
					string ext = MusicFormat.GetExtension(sourceFormat);
					if (f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
					{
						found = true;
						break;
					}
				}
				if (found)
				{
					filesToConvert.Add(f);
					continue;
				}
			}

			//block on file conversion stage, don't allow other scanners to work until we're done
			int total = filesToConvert.Count;
			Log.Info("Found " + filesToConvert.Count + " to convert.");
			RecordInfo("Found " + total + " files to convert.");

			SemaphoreSlim throttler = new SemaphoreSlim(m_config.ThreadCount);
			List<Task> runningTasks = new List<Task>();

			int limit = 0;
			int queueCount = 0;

			Stopwatch conversionStopwatch = new Stopwatch();
			conversionStopwatch.Start();
			foreach (string file in filesToConvert)
			{
				if (limit > 0 && queueCount >= limit)
				{
					break;
				}
				await throttler.WaitAsync();
				queueCount++;
				string pinnedFile = file;
				Task conversionTask = ConvertFileAsync(pinnedFile, total, conversionStopwatch, throttler);
				runningTasks.Add(conversionTask);
			}
			await Task.WhenAll(runningTasks);

			RecordInfo("Converted " + m_completedCount + " of " + total + " files.");
			OnComplete();
		}

		private async Task ConvertFileAsync(string file, int total, Stopwatch conversionStopwatch, SemaphoreSlim throttler)
		{
			try
			{
				await m_conversionTool.ConvertToOutputFormat(file);
				int completed = Interlocked.Increment(ref m_completedCount);
				int remaining = total - completed;
				double secondsPerFile = conversionStopwatch.Elapsed.TotalSeconds / completed;
				TimeSpan eta = TimeSpan.FromSeconds(secondsPerFile * remaining);
				Log.Info("[" + completed + "/" + total + "] ETA: " + eta.ToString(@"hh\:mm\:ss") + " - " + Path.GetFileName(file));
			}
			catch (Exception ex)
			{
				Log.Error("Error converting " + file + ": " + ex.Message);
				RecordError("Failed to convert: " + file + " - " + ex.Message);
			}
			finally
			{
				throttler.Release();
			}
		}
	}
}
