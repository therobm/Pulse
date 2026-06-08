using Pulse.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Pulse.Series
{
	
	/// <summary>Audiobook scanner and query facade.</summary>
	public class AudiobookManager
	{
		/// <summary>A single chapter window pulled from a file's embedded markers.</summary>
		private class ChapterMarker
		{
			public int StartMs = 0;
			public int EndMs = 0;
			public string Title = "";
		}

		/// <summary>Payload bounds of an MP4 box located by FindBox.</summary>
		private class BoxLocation
		{
			public long PayloadStart = 0;
			public long End = 0;
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

		private AudiobookData m_data;

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
		/// and Updates one audiobook series per folder with one chapter per file.
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
			// Read tags once per file. A folder is not always one book: it can be an
			// author folder holding several single-file books. Partition by album
			// tag - chapters of one book share an album; separate books don't.
			List<AudiobookFileEntry> entries = new List<AudiobookFileEntry>();
			for (int i = 0; i < files.Count; i++)
			{
				AudiobookFileEntry entry = new AudiobookFileEntry();
				entry.Path = files[i];
				entry.Tags = ReadFileTags(files[i]);
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

			List<ChapterMarker> markers = ExtractChapters(entry.Path, entry.Tags.DurationSeconds);
			if (markers.Count == 0)
			{
				// No embedded chapters: the whole file is one chapter.
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
				ChapterMarker marker = markers[i];
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

		/// <summary>All catalogued audiobooks.</summary>
		public List<Audiobook> GetAllAudiobooks()
		{
			return m_data.LoadBooks();
		}

		public Audiobook GetBook(string bookId)
		{
			return m_data.LoadBook(bookId);
		}

		/// <summary>Chapters for an audiobook, ordered by chapter index.</summary>
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
				string extracted = ExtractEmbeddedCover(entries[i].Path, bookId);
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

		// Pulls the first embedded picture from an audio file's tags and writes it
		// to AudiobookArt/{seriesId}/cover.{ext}. Returns the written path, or ""
		// when the file has no embedded art. This is what VLC shows for files with
		// no sidecar image.
		private string ExtractEmbeddedCover(string path, string bookId)
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
					string dir = Path.Combine(m_artCacheRoot, bookId);
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

		// Embedded chapter markers for a single file. Tries ID3v2 CHAP frames (MP3)
		// then the Nero 'chpl' atom (m4b/mp4); ends are normalised from the next
		// chapter's start (or the file duration for the last). Empty list = no
		// embedded chapters, so the caller treats the file as a single chapter.
		private List<ChapterMarker> ExtractChapters(string path, int fileDurationSeconds)
		{
			List<ChapterMarker> markers = new List<ChapterMarker>();
			try
			{
				markers = ExtractId3Chapters(path);
				if (markers.Count == 0)
				{
					markers = ExtractNeroChapters(path);
				}
			}
			catch (Exception ex)
			{
				Log.Warning(-1, "Audiobook chapter extract failed for " + path + ": " + ex.Message);
				markers = new List<ChapterMarker>();
			}
			if (markers.Count > 0)
			{
				NormalizeChapterEnds(markers, fileDurationSeconds * 1000);
			}
			return markers;
		}

		private void NormalizeChapterEnds(List<ChapterMarker> markers, int fileDurationMs)
		{
			markers.Sort(CompareMarkerByStart);
			for (int i = 0; i < markers.Count; i++)
			{
				if (markers[i].EndMs > markers[i].StartMs)
				{
					continue;
				}
				if (i + 1 < markers.Count)
				{
					markers[i].EndMs = markers[i + 1].StartMs;
				}
				else
				{
					markers[i].EndMs = fileDurationMs;
				}
			}
		}

		private static int CompareMarkerByStart(ChapterMarker left, ChapterMarker right)
		{
			return left.StartMs.CompareTo(right.StartMs);
		}

		private List<ChapterMarker> ExtractId3Chapters(string path)
		{
			List<ChapterMarker> markers = new List<ChapterMarker>();
			TagLib.File tagFile = TagLib.File.Create(path);
			try
			{
				TagLib.Id3v2.Tag id3 = tagFile.GetTag(TagLib.TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
				if (id3 == null)
				{
					return markers;
				}
				foreach (TagLib.Id3v2.Frame frame in id3.GetFrames())
				{
					TagLib.Id3v2.ChapterFrame chapter = frame as TagLib.Id3v2.ChapterFrame;
					if (chapter == null)
					{
						continue;
					}
					ChapterMarker marker = new ChapterMarker();
					marker.StartMs = (int)chapter.StartMilliseconds;
					marker.EndMs = (int)chapter.EndMilliseconds;
					marker.Title = ReadId3ChapterTitle(chapter);
					markers.Add(marker);
				}
			}
			finally
			{
				tagFile.Dispose();
			}
			return markers;
		}

		private static string ReadId3ChapterTitle(TagLib.Id3v2.ChapterFrame chapter)
		{
			foreach (TagLib.Id3v2.Frame sub in chapter.SubFrames)
			{
				TagLib.Id3v2.TextInformationFrame text = sub as TagLib.Id3v2.TextInformationFrame;
				if (text != null && text.Text != null && text.Text.Length > 0)
				{
					return text.Text[0];
				}
			}
			return "";
		}

		// Nero 'chpl' atom (moov.udta.chpl): the most common chapter store in m4b
		// audiobooks. Layout: FullBox header (4) + reserved (1) + count (1), then
		// per chapter a u64 start in 100ns units + u8 title length + UTF-8 title.
		// Best-effort - validate against real files; degrades to no-chapters.
		private List<ChapterMarker> ExtractNeroChapters(string path)
		{
			List<ChapterMarker> markers = new List<ChapterMarker>();
			FileStream stream = File.OpenRead(path);
			try
			{
				long fileLength = stream.Length;
				BoxLocation moov = FindBox(stream, 0, fileLength, "moov");
				if (moov == null)
				{
					return markers;
				}
				BoxLocation udta = FindBox(stream, moov.PayloadStart, moov.End, "udta");
				if (udta == null)
				{
					return markers;
				}
				BoxLocation chpl = FindBox(stream, udta.PayloadStart, udta.End, "chpl");
				if (chpl == null)
				{
					return markers;
				}

				long payloadLength = chpl.End - chpl.PayloadStart;
				if (payloadLength < 6 || payloadLength > 1000000)
				{
					return markers;
				}
				byte[] payload = new byte[payloadLength];
				stream.Seek(chpl.PayloadStart, SeekOrigin.Begin);
				if (!ReadFully(stream, payload, 0, payload.Length))
				{
					return markers;
				}

				// The reserved bytes before the 1-byte chapter count vary by writer:
				// 1 byte (mp4v2/GPAC -> entries at 6), 4 bytes (ffmpeg, which made
				// most .m4b files -> entries at 9), or none (-> entries at 5). Try
				// each and keep the first that parses cleanly (validated in
				// ParseNeroPayload), preferring the layout that consumes the payload
				// most fully.
				int[] candidateStarts = new int[] { 9, 6, 5 };
				List<ChapterMarker> best = null;
				int bestLeftover = int.MaxValue;
				for (int i = 0; i < candidateStarts.Length; i++)
				{
					int leftover;
					List<ChapterMarker> parsed = ParseNeroPayload(payload, candidateStarts[i], out leftover);
					if (parsed != null && leftover < bestLeftover)
					{
						best = parsed;
						bestLeftover = leftover;
					}
				}
				if (best != null)
				{
					markers = best;
				}
			}
			finally
			{
				stream.Close();
			}
			return markers;
		}

		// Parse the Nero chapter entries assuming the 1-byte count sits at
		// entriesStart-1 and entries begin at entriesStart. Returns null (so the
		// caller tries the other layout) if the count is 0, the entries run past
		// the payload, a timestamp is implausible, or the starts aren't monotonic.
		private List<ChapterMarker> ParseNeroPayload(byte[] payload, int entriesStart, out int leftover)
		{
			leftover = int.MaxValue;
			if (entriesStart < 1 || entriesStart > payload.Length)
			{
				return null;
			}
			int count = payload[entriesStart - 1];
			if (count <= 0)
			{
				return null;
			}

			List<ChapterMarker> markers = new List<ChapterMarker>();
			int position = entriesStart;
			for (int i = 0; i < count; i++)
			{
				if (position + 9 > payload.Length)
				{
					return null;
				}
				long start100ns = ReadUInt64BE(payload, position);
				position = position + 8;
				int titleLength = payload[position];
				position = position + 1;
				if (position + titleLength > payload.Length)
				{
					return null;
				}
				string title = Encoding.UTF8.GetString(payload, position, titleLength);
				position = position + titleLength;

				long startMs = start100ns / 10000L;
				// Reject implausible timestamps (> 100h) - a sign of a wrong offset.
				if (startMs < 0 || startMs > 360000000L)
				{
					return null;
				}
				ChapterMarker marker = new ChapterMarker();
				marker.StartMs = (int)startMs;
				marker.EndMs = 0;
				marker.Title = title;
				markers.Add(marker);
			}

			for (int i = 1; i < markers.Count; i++)
			{
				if (markers[i].StartMs < markers[i - 1].StartMs)
				{
					return null;
				}
			}
			leftover = payload.Length - position;
			return markers;
		}

		// Find a top-level box of the given 4-char type within [start, end). Returns
		// the box's payload start and end offsets, or null if not found.
		private static BoxLocation FindBox(FileStream stream, long start, long end, string type)
		{
			long position = start;
			while (position + 8 <= end)
			{
				stream.Seek(position, SeekOrigin.Begin);
				byte[] header = new byte[8];
				if (!ReadFully(stream, header, 0, 8))
				{
					break;
				}
				long size = ReadUInt32BE(header, 0);
				string boxType = Encoding.ASCII.GetString(header, 4, 4);
				long payloadStart = position + 8;
				long boxEnd;
				if (size == 1)
				{
					byte[] extended = new byte[8];
					if (!ReadFully(stream, extended, 0, 8))
					{
						break;
					}
					long bigSize = ReadUInt64BE(extended, 0);
					payloadStart = position + 16;
					boxEnd = position + bigSize;
				}
				else if (size == 0)
				{
					boxEnd = end;
				}
				else
				{
					boxEnd = position + size;
				}
				if (boxEnd <= position || boxEnd > end)
				{
					break;
				}
				if (boxType == type)
				{
					BoxLocation location = new BoxLocation();
					location.PayloadStart = payloadStart;
					location.End = boxEnd;
					return location;
				}
				position = boxEnd;
			}
			return null;
		}

		private static bool ReadFully(FileStream stream, byte[] buffer, int offset, int count)
		{
			int read = 0;
			while (read < count)
			{
				int got = stream.Read(buffer, offset + read, count - read);
				if (got <= 0)
				{
					return false;
				}
				read = read + got;
			}
			return true;
		}

		private static long ReadUInt32BE(byte[] data, int offset)
		{
			long value = 0;
			for (int i = 0; i < 4; i++)
			{
				value = (value << 8) | (long)data[offset + i];
			}
			return value;
		}

		private static long ReadUInt64BE(byte[] data, int offset)
		{
			long value = 0;
			for (int i = 0; i < 8; i++)
			{
				value = (value << 8) | (long)data[offset + i];
			}
			return value;
		}
	}
}
