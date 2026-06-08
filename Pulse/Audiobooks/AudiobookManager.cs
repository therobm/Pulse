using Pulse.Audiobooks;
using Pulse.Data;
using Pulse.DataStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Pulse.Series
{
	public class AudiobookManager
	{
		private class AudiobookFileEntry
		{
			public string Path = "";
			public AudiobookReader.AudiobookTags Tags = new AudiobookReader.AudiobookTags();
		}

		private AudiobookData m_data;
		private AudiobookReader m_reader;

		private string m_audiobooksPath;
		private string m_artCacheRoot;
		private Thread m_scanThread;
		private PulseConfig m_config;

		private static readonly string[] s_audioExtensions = new string[] { ".mp3", ".m4a", ".m4b", ".flac", ".ogg", ".wav", ".wma", ".aac", ".opus" };
		private static readonly string[] s_coverNames = new string[] { "folder.jpg", "cover.jpg", "folder.png", "cover.png", "folder.jpeg", "cover.jpeg" };

		public AudiobookManager(PulseConfig config)
		{
			m_config = config;
			m_audiobooksPath = config.AudiobooksPath;

			if (!Directory.Exists(config.PulseDataPath))
			{
				Directory.CreateDirectory(config.PulseDataPath);
			}

			// Cover art embedded in audio tags is extracted to here (the source
			// library may be read-only and we don't want to litter it).
			m_artCacheRoot = Path.Combine(config.PulseDataPath, "AudiobookArt");

			m_data = new AudiobookData(m_config);
			m_reader = new AudiobookReader(m_artCacheRoot);
		}

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

			// Hydrate dicts from the store so user progress from previous runs
			// is preserved when the scanner overwrites catalogue fields.
			m_data.Load();

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

			HashSet<string> liveBookIds = new HashSet<string>();
			HashSet<string> liveChapterIds = new HashSet<string>();
			int folderCount = 0;
			foreach (KeyValuePair<string, List<string>> pair in filesByFolder)
			{
				try
				{
					ScanBook(pair.Key, pair.Value, liveBookIds, liveChapterIds);
					folderCount++;
				}
				catch (Exception ex)
				{
					Log.Warning(-1, "Audiobook scan failed for " + pair.Key + ": " + ex.Message);
				}
			}

			PruneRemoved(liveBookIds, liveChapterIds);

			Log.Info(-1, "Audiobook scan complete: " + liveBookIds.Count + " book(s) from " + folderCount + " folder(s) under " + m_audiobooksPath);
		}

		private void PruneRemoved(HashSet<string> liveBookIds, HashSet<string> liveChapterIds)
		{
			int removedBooks = 0;
			int removedChapters = 0;
			List<Audiobook> existing = m_data.LoadBooks();
			for (int i = 0; i < existing.Count; i++)
			{
				Audiobook book = existing[i];
				List<Chapter> chapters = m_data.LoadChapters(book.Id);
				if (!liveBookIds.Contains(book.Id))
				{
					for (int j = 0; j < chapters.Count; j++)
					{
						m_data.Delete(chapters[j]);
						removedChapters++;
					}
					m_data.Delete(book);
					removedBooks++;
				}
				else
				{
					for (int j = 0; j < chapters.Count; j++)
					{
						if (!liveChapterIds.Contains(chapters[j].Id))
						{
							m_data.Delete(chapters[j]);
							removedChapters++;
						}
					}
				}
			}
			if (removedBooks > 0 || removedChapters > 0)
			{
				Log.Info(-1, "Audiobook prune: removed " + removedBooks + " book(s) and " + removedChapters + " chapter(s) no longer on disk.");
			}
		}

		private void ScanBook(string folder, List<string> files, HashSet<string> liveBookIds, HashSet<string> liveChapterIds)
		{
			List<AudiobookFileEntry> entries = new List<AudiobookFileEntry>();
			for (int i = 0; i < files.Count; i++)
			{
				AudiobookFileEntry entry = new AudiobookFileEntry();
				entry.Path = files[i];
				entry.Tags = m_reader.ReadFileTags(files[i]);
				entries.Add(entry);
			}

			Dictionary<string, List<AudiobookFileEntry>> byAlbum = new Dictionary<string, List<AudiobookFileEntry>>();
			List<string> albumOrder = new List<string>();
			for (int i = 0; i < entries.Count; i++)
			{
				string key = AlbumKey(entries[i].Tags.Album);
				List<AudiobookFileEntry> group;
				bool found = byAlbum.TryGetValue(key, out group);
				if (!found)
				{
					group = new List<AudiobookFileEntry>();
					byAlbum[key] = group;
					albumOrder.Add(key);
				}
				group.Add(entries[i]);
			}

			bool multipleBooks = byAlbum.Count > 1;
			string folderRelative = MakeRelative(folder);
			for (int i = 0; i < albumOrder.Count; i++)
			{
				List<AudiobookFileEntry> group = byAlbum[albumOrder[i]];
				group.Sort(CompareEntries);
				BuildBook(folder, folderRelative, group, multipleBooks, liveBookIds, liveChapterIds);
			}
		}

		private void BuildBook(string folder, string folderRelative, List<AudiobookFileEntry> entries, bool multipleBooks, HashSet<string> liveBookIds, HashSet<string> liveChapterIds)
		{
			AudiobookFileEntry firstEntry = entries[0];

			// Keep the old folder-only id for the common single-book folder so a
			// rescan updates in place; only album-split folders key on album too.
			string idInput = folderRelative;
			if (multipleBooks)
			{
				idInput = folderRelative + "|" + AlbumKey(firstEntry.Tags.Album);
			}
			string bookId = StableId("ab", idInput);
			liveBookIds.Add(bookId);

			// When a folder yields several books it is acting as an author folder,
			// so its name is the author fallback; a single-book folder is the book
			// folder, so the parent is the author fallback.
			string folderAuthorFallback;
			if (multipleBooks)
			{
				folderAuthorFallback = Path.GetFileName(folder);
			}
			else
			{
				folderAuthorFallback = DeriveAuthorFromFolder(folder);
			}

			Audiobook book = new Audiobook();
			book.Id = bookId;
			book.Title = FirstNonEmpty(firstEntry.Tags.Album, Path.GetFileName(folder));
			book.Author = FirstNonEmpty(firstEntry.Tags.Author, folderAuthorFallback);
			book.ArtworkPath = ResolveCoverArt(folder, entries, bookId);
			m_data.UpdateBook(book);

			List<Chapter> chapters;
			if (entries.Count == 1)
			{
				chapters = BuildSingleFileChapters(bookId, entries[0], liveChapterIds);
			}
			else
			{
				chapters = new List<Chapter>();
				for (int i = 0; i < entries.Count; i++)
				{
					AudiobookFileEntry entry = entries[i];
					Chapter item = new Chapter();
					item.Id = StableId("ch", MakeRelative(entry.Path));
					item.AudiobookId = bookId;
					item.Title = FirstNonEmpty(entry.Tags.Title, "Chapter " + (i + 1).ToString());
					item.OrderIndex = i;
					item.DurationSeconds = entry.Tags.DurationSeconds;
					item.LocalPath = entry.Path;
					item.FileSizeBytes = FileSize(entry.Path);
					chapters.Add(item);
					liveChapterIds.Add(item.Id);
				}
			}
			m_data.UpdateChapters(chapters);
		}

		private List<Chapter> BuildSingleFileChapters(string bookId, AudiobookFileEntry entry, HashSet<string> liveChapterIds)
		{
			List<Chapter> items = new List<Chapter>();
			string relFile = MakeRelative(entry.Path);
			long fileSize = FileSize(entry.Path);

			List<AudiobookReader.ChapterMarker> markers = m_reader.ExtractChapters(entry.Path, entry.Tags.DurationSeconds);
			if (markers.Count == 0)
			{
				Chapter whole = new Chapter();
				whole.Id = StableId("ch", relFile);
				whole.AudiobookId = bookId;
				whole.Title = FirstNonEmpty(entry.Tags.Title, "Chapter 1");
				whole.DurationSeconds = entry.Tags.DurationSeconds;
				whole.LocalPath = entry.Path;
				whole.FileSizeBytes = fileSize;
				items.Add(whole);
				liveChapterIds.Add(whole.Id);
				return items;
			}

			for (int i = 0; i < markers.Count; i++)
			{
				AudiobookReader.ChapterMarker marker = markers[i];
				Chapter item = new Chapter();
				item.Id = StableId("ch", relFile + "|" + i.ToString());
				item.AudiobookId = bookId;
				item.Title = FirstNonEmpty(marker.Title, "Chapter " + (i + 1).ToString());
				item.OrderIndex = i;
				int durationMs = marker.EndMs - marker.StartMs;
				if (durationMs < 0)
				{
					durationMs = 0;
				}
				item.DurationSeconds = durationMs / 1000;
				item.LocalPath = entry.Path;
				item.FileSizeBytes = fileSize;
				item.StartMs = marker.StartMs;
				item.EndMs = marker.EndMs;
				items.Add(item);
				liveChapterIds.Add(item.Id);
			}
			return items;
		}

		private static string AlbumKey(string album)
		{
			if (string.IsNullOrWhiteSpace(album))
			{
				return "";
			}
			return album.Trim().ToLowerInvariant();
		}

		public List<Audiobook> GetAllAudiobooks()
		{
			return m_data.LoadBooks();
		}

		public Audiobook GetBook(string bookId)
		{
			return m_data.LoadBook(bookId);
		}

		public List<Chapter> GetChapters(string bookId)
		{
			List<Chapter> items = m_data.LoadChapters(bookId);
			items.Sort(CompareByOrderIndex);
			return items;
		}

		public Chapter GetChapter(string chapterId)
		{
			return m_data.LoadChapter(chapterId);
		}

		/// <summary>
		/// Resolves the cover art file for an audiobook. Checks the extracted
		/// art cache first (survives library moves), then falls back to the
		/// ArtworkPath stored at scan time.
		/// </summary>
		public string GetCoverArtPath(string bookId)
		{
			string[] extensions = new string[] { ".jpg", ".png", ".jpeg" };
			for (int i = 0; i < extensions.Length; i++)
			{
				string cached = Path.Combine(m_artCacheRoot, bookId, "cover" + extensions[i]);
				if (File.Exists(cached))
				{
					return cached;
				}
			}

			Audiobook book = m_data.LoadBook(bookId);
			if (book != null && !string.IsNullOrEmpty(book.ArtworkPath) && File.Exists(book.ArtworkPath))
			{
				return book.ArtworkPath;
			}
			return "";
		}

		private static int CompareByOrderIndex(Chapter left, Chapter right)
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
			for (int i = 0; i < s_audioExtensions.Length; i++)
			{
				if (s_audioExtensions[i] == ext)
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
		private string ResolveCoverArt(string folder, List<AudiobookFileEntry> entries, string bookId)
		{
			string folderArt = FindFolderCoverArt(folder);
			if (!string.IsNullOrEmpty(folderArt))
			{
				return folderArt;
			}
			for (int i = 0; i < entries.Count; i++)
			{
				string extracted = m_reader.ExtractEmbeddedCover(entries[i].Path, bookId);
				if (!string.IsNullOrEmpty(extracted))
				{
					return extracted;
				}
			}
			return "";
		}

		// Parent folder name as the author, but only when the book folder is nested
		// under a subfolder (AudiobooksPath/Author/Book). A book directly under the
		// root has no author folder, so this returns "".
		private string DeriveAuthorFromFolder(string bookFolder)
		{
			string parent = Path.GetDirectoryName(bookFolder);
			if (string.IsNullOrEmpty(parent))
			{
				return "";
			}
			string parentNormalized = NormalizePath(parent);
			string rootNormalized = NormalizePath(m_audiobooksPath);
			if (string.Equals(parentNormalized, rootNormalized, StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			return Path.GetFileName(parent);
		}

		private static string NormalizePath(string path)
		{
			string full = Path.GetFullPath(path);
			return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}

		private static string FindFolderCoverArt(string folder)
		{
			for (int i = 0; i < s_coverNames.Length; i++)
			{
				string candidate = Path.Combine(folder, s_coverNames[i]);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
			return "";
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
				for (int i = 0; i < 10; i++)
				{
					hex.Append(hash[i].ToString("x2"));
				}
				return prefix + hex.ToString();
			}
			finally
			{
				sha.Dispose();
			}
		}
	}
}
