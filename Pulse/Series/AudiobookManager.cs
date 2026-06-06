using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Pulse.Database;

namespace Pulse.Series
{
	/// <summary>
	/// Audiobook half of the unified Series model, sibling to PodcastManager.
	/// Where PodcastManager pulls items from RSS feeds, AudiobookManager scans a
	/// local folder (PulseConfig.AudiobooksPath): every folder that directly
	/// contains audio files is one audiobook, each file is one chapter (a single
	/// file is a one-chapter book). Chapters are written as ordinary series_items
	/// rows with LocalPath set and DownloadState = Downloaded, so the existing
	/// stream / coverArt / progress endpoints resolve them unchanged. Points at
	/// the same pulse_series_{env}.db the PodcastManager uses (audiobook rows are
	/// distinguished by eSeriesType.Audiobook).
	/// </summary>
	public class AudiobookManager
	{
		private SeriesDBConnector m_connector;
		private SeriesDB m_db;
		private string m_audiobooksPath;
		private string m_artCacheRoot;
		private Thread m_scanThread;

		private static readonly string[] s_audioExtensions = new string[] { ".mp3", ".m4a", ".m4b", ".flac", ".ogg", ".wav", ".wma", ".aac", ".opus" };
		private static readonly string[] s_coverNames = new string[] { "folder.jpg", "cover.jpg", "folder.png", "cover.png", "folder.jpeg", "cover.jpeg" };

		public AudiobookManager(PulseConfig config)
		{
			string environmentName = config.DatabaseEnvironment;
			if (string.IsNullOrWhiteSpace(environmentName))
			{
				environmentName = "Production";
			}
#if DEBUG
			if (!string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase))
			{
				Log.Warning(-1, "Debugger attached: forcing Staging environment for series DB (config said '" + environmentName + "').");
			}
			environmentName = "Staging";
#endif

			m_audiobooksPath = config.AudiobooksPath;

			string pulseDataRoot = Path.Combine(config.MusicPath, "PulseData");
			if (!Directory.Exists(pulseDataRoot))
			{
				Directory.CreateDirectory(pulseDataRoot);
			}

			// Cover art embedded in audio tags is extracted to here (the source
			// library may be read-only and we don't want to litter it).
			m_artCacheRoot = Path.Combine(pulseDataRoot, "AudiobookArt");

			string sqliteFileName = "pulse_series_" + environmentName.ToLowerInvariant() + ".db";
			string sqlitePath = Path.Combine(pulseDataRoot, sqliteFileName);

			SeriesDBConnector connector = new SeriesDBConnector();
			connector.SetDatabaseFilePath(sqlitePath);
			m_connector = connector;

			SeriesDBMigrations migrations = new SeriesDBMigrations(connector);
			migrations.RunMigrations();

			m_db = new SeriesDB(connector);
		}

		/// <summary>
		/// Kicks the library scan on a background thread so server startup is not
		/// blocked by tag-reading every file. Safe to call once at startup.
		/// </summary>
		public void Run()
		{
			m_scanThread = new Thread(RunScan);
			m_scanThread.IsBackground = true;
			m_scanThread.Name = "Pulse.AudiobookScan";
			m_scanThread.Start();
		}

		private void RunScan()
		{
			try
			{
				ScanLibrary();
			}
			catch (Exception ex)
			{
				Log.Error(-1, "Audiobook scan thread failed: " + ex.Message);
			}
		}

		/// <summary>
		/// Walks AudiobooksPath, groups audio files by their containing folder,
		/// and upserts one audiobook series per folder with one chapter per file.
		/// Re-runnable: ids are derived deterministically from the relative path,
		/// so a rescan updates rather than duplicates.
		/// </summary>
		public void ScanLibrary()
		{
			if (string.IsNullOrWhiteSpace(m_audiobooksPath))
			{
				Log.Info(-1, "AudiobooksPath not configured; skipping audiobook scan.");
				return;
			}
			if (!Directory.Exists(m_audiobooksPath))
			{
				Log.Warning(-1, "AudiobooksPath does not exist: " + m_audiobooksPath);
				return;
			}

			Dictionary<string, List<string>> filesByFolder = new Dictionary<string, List<string>>();
			IEnumerable<string> allFiles = Directory.EnumerateFiles(m_audiobooksPath, "*.*", SearchOption.AllDirectories);
			foreach (string file in allFiles)
			{
				if (!IsAudioFile(file))
				{
					continue;
				}
				string folder = Path.GetDirectoryName(file);
				List<string> list;
				bool found = filesByFolder.TryGetValue(folder, out list);
				if (!found)
				{
					list = new List<string>();
					filesByFolder[folder] = list;
				}
				list.Add(file);
			}

			int bookCount = 0;
			foreach (KeyValuePair<string, List<string>> pair in filesByFolder)
			{
				try
				{
					ScanBook(pair.Key, pair.Value);
					bookCount++;
				}
				catch (Exception ex)
				{
					Log.Warning(-1, "Audiobook scan failed for " + pair.Key + ": " + ex.Message);
				}
			}
			Log.Info(-1, "Audiobook scan complete: " + bookCount + " book(s) under " + m_audiobooksPath);
		}

		private void ScanBook(string folder, List<string> files)
		{
			// Read tags once per file, then order by track number, falling back to
			// filename (the "users conform" surface: tag your tracks or zero-pad).
			List<AudiobookFileEntry> entries = new List<AudiobookFileEntry>();
			for (int index = 0; index < files.Count; index++)
			{
				AudiobookFileEntry entry = new AudiobookFileEntry();
				entry.Path = files[index];
				entry.Tags = ReadFileTags(files[index]);
				entries.Add(entry);
			}
			entries.Sort(CompareEntries);

			string folderRelative = MakeRelative(folder);
			string seriesId = StableId("ab", folderRelative);

			AudiobookFileEntry firstEntry = entries[0];
			SeriesInfo series = new SeriesInfo();
			series.Id = seriesId;
			series.Type = eSeriesType.Audiobook;
			series.Title = FirstNonEmpty(firstEntry.Tags.Album, Path.GetFileName(folder));
			series.Author = firstEntry.Tags.Author;
			series.Narrator = "";
			series.Description = "";
			series.ArtworkPath = ResolveCoverArt(folder, entries, seriesId);
			series.DateAdded = DateTime.UtcNow.ToString("o");
			m_db.UpsertSeries(series);

			List<SeriesItemInfo> items = new List<SeriesItemInfo>();
			for (int index = 0; index < entries.Count; index++)
			{
				AudiobookFileEntry entry = entries[index];
				SeriesItemInfo item = new SeriesItemInfo();
				item.Id = StableId("ch", MakeRelative(entry.Path));
				item.SeriesId = seriesId;
				item.Title = FirstNonEmpty(entry.Tags.Title, "Chapter " + (index + 1).ToString());
				item.OrderIndex = index;
				item.DurationSeconds = entry.Tags.DurationSeconds;
				item.LocalPath = entry.Path;
				item.FileSizeBytes = FileSize(entry.Path);
				item.DownloadState = eDownloadState.Downloaded;
				items.Add(item);
			}
			m_db.UpsertItems(items);
		}

		/// <summary>All catalogued audiobooks (every series of type Audiobook).</summary>
		public List<SeriesInfo> GetAllAudiobooks()
		{
			return m_db.LoadAllSeriesByType(eSeriesType.Audiobook);
		}

		public SeriesInfo GetSeries(string seriesId)
		{
			return m_db.LoadSeries(seriesId);
		}

		/// <summary>Chapters for an audiobook, ordered by chapter index.</summary>
		public List<SeriesItemInfo> GetItems(string seriesId)
		{
			List<SeriesItemInfo> items = m_db.LoadItemsForSeries(seriesId);
			items.Sort(CompareByOrderIndex);
			return items;
		}

		public SeriesItemInfo GetItem(string itemId)
		{
			return m_db.LoadItem(itemId);
		}

		public SeriesUserDataInfo GetUserSeries(string seriesId, string userName)
		{
			return m_db.LoadUserSeries(seriesId, userName);
		}

		public SeriesItemUserDataInfo GetProgress(string itemId, string userName)
		{
			return m_db.LoadProgress(itemId, userName);
		}

		private static int CompareByOrderIndex(SeriesItemInfo left, SeriesItemInfo right)
		{
			return left.OrderIndex.CompareTo(right.OrderIndex);
		}

		private static int CompareEntries(AudiobookFileEntry left, AudiobookFileEntry right)
		{
			uint leftTrack = left.Tags.Track;
			uint rightTrack = right.Tags.Track;
			if (leftTrack > 0 && rightTrack > 0 && leftTrack != rightTrack)
			{
				return leftTrack.CompareTo(rightTrack);
			}
			return string.Compare(Path.GetFileName(left.Path), Path.GetFileName(right.Path), StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsAudioFile(string path)
		{
			string ext = Path.GetExtension(path).ToLowerInvariant();
			for (int index = 0; index < s_audioExtensions.Length; index++)
			{
				if (s_audioExtensions[index] == ext)
				{
					return true;
				}
			}
			return false;
		}

		private string MakeRelative(string fullPath)
		{
			string relative = Path.GetRelativePath(m_audiobooksPath, fullPath);
			return relative.Replace('\\', '/');
		}

		// Cover art, in priority order: an explicit folder image, else a picture
		// embedded in one of the audio files (extracted to the art cache).
		private string ResolveCoverArt(string folder, List<AudiobookFileEntry> entries, string seriesId)
		{
			string folderArt = FindFolderCoverArt(folder);
			if (!string.IsNullOrEmpty(folderArt))
			{
				return folderArt;
			}
			for (int index = 0; index < entries.Count; index++)
			{
				string extracted = ExtractEmbeddedCover(entries[index].Path, seriesId);
				if (!string.IsNullOrEmpty(extracted))
				{
					return extracted;
				}
			}
			return "";
		}

		private static string FindFolderCoverArt(string folder)
		{
			for (int index = 0; index < s_coverNames.Length; index++)
			{
				string candidate = Path.Combine(folder, s_coverNames[index]);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			return "";
		}

		// Pulls the first embedded picture from an audio file's tags and writes it
		// to AudiobookArt/{seriesId}/cover.{ext}. Returns the written path, or ""
		// when the file has no embedded art. This is what VLC shows for files with
		// no sidecar image.
		private string ExtractEmbeddedCover(string path, string seriesId)
		{
			try
			{
				TagLib.File tagFile = TagLib.File.Create(path);
				try
				{
					if (tagFile.Tag == null || tagFile.Tag.Pictures == null || tagFile.Tag.Pictures.Length == 0)
					{
						return "";
					}
					TagLib.IPicture picture = tagFile.Tag.Pictures[0];
					if (picture.Data == null || picture.Data.Data == null || picture.Data.Data.Length == 0)
					{
						return "";
					}

					string extension = ".jpg";
					if (picture.MimeType != null && picture.MimeType.ToLowerInvariant().Contains("png"))
					{
						extension = ".png";
					}
					string dir = Path.Combine(m_artCacheRoot, seriesId);
					if (!Directory.Exists(dir))
					{
						Directory.CreateDirectory(dir);
					}
					string outPath = Path.Combine(dir, "cover" + extension);
					File.WriteAllBytes(outPath, picture.Data.Data);
					return outPath;
				}
				finally
				{
					tagFile.Dispose();
				}
			}
			catch (Exception ex)
			{
				Log.Warning(-1, "Audiobook cover extract failed for " + path + ": " + ex.Message);
				return "";
			}
		}

		private AudiobookTags ReadFileTags(string path)
		{
			AudiobookTags result = new AudiobookTags();
			try
			{
				TagLib.File tagFile = TagLib.File.Create(path);
				try
				{
					if (tagFile.Tag != null)
					{
						result.Title = SafeString(tagFile.Tag.Title);
						result.Album = SafeString(tagFile.Tag.Album);
						string author = SafeString(tagFile.Tag.FirstAlbumArtist);
						if (string.IsNullOrEmpty(author))
						{
							author = SafeString(tagFile.Tag.FirstPerformer);
						}
						result.Author = author;
						result.Track = tagFile.Tag.Track;
					}
					if (tagFile.Properties != null)
					{
						result.DurationSeconds = (int)tagFile.Properties.Duration.TotalSeconds;
					}
				}
				finally
				{
					tagFile.Dispose();
				}
			}
			catch (Exception ex)
			{
				Log.Warning(-1, "Audiobook tag read failed for " + path + ": " + ex.Message);
			}
			return result;
		}

		private static long FileSize(string path)
		{
			try
			{
				FileInfo info = new FileInfo(path);
				return info.Length;
			}
			catch (Exception ex)
			{
				Log.Warning(-1, "Audiobook file size read failed for " + path + ": " + ex.Message);
				return 0;
			}
		}

		private static string SafeString(string value)
		{
			if (value == null)
			{
				return "";
			}
			return value;
		}

		private static string FirstNonEmpty(string first, string second)
		{
			if (!string.IsNullOrEmpty(first))
			{
				return first;
			}
			return second;
		}

		// Deterministic id from a relative path so a rescan updates the same rows.
		private static string StableId(string prefix, string input)
		{
			SHA1 sha = SHA1.Create();
			try
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
				StringBuilder hex = new StringBuilder();
				for (int index = 0; index < 10; index++)
				{
					hex.Append(hash[index].ToString("x2"));
				}
				return prefix + hex.ToString();
			}
			finally
			{
				sha.Dispose();
			}
		}

		/// <summary>One scanned audio file plus its parsed tags, before ordering.</summary>
		private class AudiobookFileEntry
		{
			public string Path = "";
			public AudiobookTags Tags = new AudiobookTags();
		}

		/// <summary>Tags pulled from one file during the scan.</summary>
		private class AudiobookTags
		{
			public string Title = "";
			public string Album = "";
			public string Author = "";
			public uint Track = 0;
			public int DurationSeconds = 0;
		}
	}
}
