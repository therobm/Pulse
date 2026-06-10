using Pulse.DataStorage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Pulse.Data
{
	/// <summary>
	/// In-memory cache of audiobooks and chapters, backed by PulseDataStore.
	/// The dictionaries are authoritative at runtime; the store is pure persistence.
	/// </summary>
	public class AudiobookData
	{
		private ConcurrentDictionary<string, Audiobook> m_audiobooks = new ConcurrentDictionary<string, Audiobook>();
		private ConcurrentDictionary<string, Chapter> m_chapters = new ConcurrentDictionary<string, Chapter>();

		private PulseDataStore m_data;
		private Timer m_saveTimer;

		public AudiobookData(PulseConfig config)
		{
			string audiobookDB = "audiobooks.db";
#if DEBUG
			audiobookDB = "audiobooks_staging.db";
#endif
			string dbPath = Path.Combine(config.PulseDataPath, audiobookDB);
			m_data = new PulseDataStore(dbPath);
		}

		/// <summary>
		/// Hydrate in-memory dictionaries from the store. Call once before
		/// the first scan so that user state from a previous run survives.
		/// </summary>
		public void Load()
		{
			List<Audiobook> books = m_data.LoadList<Audiobook>(eDataType.Audiobook);
			for (int i = 0; i < books.Count; i++)
			{
				m_audiobooks[books[i].Id] = books[i];
			}

			List<Chapter> chapters = m_data.LoadList<Chapter>(eDataType.AudiobookChapter);
			for (int i = 0; i < chapters.Count; i++)
			{
				m_chapters[chapters[i].Id] = chapters[i];
			}

			m_saveTimer = new Timer(OnSaveTimer, null, 10000, 10000);
		}

		/// <summary>
		/// Write all dirty audiobooks and chapters to the store, then clear their flags.
		/// </summary>
		public void Save()
		{
			List<Audiobook> dirtyBooks = new List<Audiobook>();
			foreach (KeyValuePair<string, Audiobook> pair in m_audiobooks)
			{
				if (pair.Value.m_bIsDirty)
				{
					dirtyBooks.Add(pair.Value);
				}
			}

			List<Chapter> dirtyChapters = new List<Chapter>();
			foreach (KeyValuePair<string, Chapter> pair in m_chapters)
			{
				if (pair.Value.m_bIsDirty)
				{
					dirtyChapters.Add(pair.Value);
				}
			}

			if (dirtyBooks.Count > 0)
			{
				m_data.SaveList(eDataType.Audiobook, dirtyBooks);
				for (int i = 0; i < dirtyBooks.Count; i++)
				{
					dirtyBooks[i].m_bIsDirty = false;
				}
			}

			if (dirtyChapters.Count > 0)
			{
				m_data.SaveList(eDataType.AudiobookChapter, dirtyChapters);
				for (int i = 0; i < dirtyChapters.Count; i++)
				{
					dirtyChapters[i].m_bIsDirty = false;
				}
			}
		}

		private void OnSaveTimer(object state)
		{
			try
			{
				Save();
			}
			catch (Exception)
			{
			}
		}

		/// <summary>
		/// Stop the periodic save timer and flush any remaining dirty objects.
		/// Call once during process shutdown.
		/// </summary>
		public void Shutdown()
		{
			if (m_saveTimer != null)
			{
				m_saveTimer.Dispose();
				m_saveTimer = null;
			}
			try
			{
				Save();
				Log.Info("AudiobookData: shutdown flush complete");
			}
			catch (Exception ex)
			{
				Log.Error("AudiobookData: shutdown flush failed - " + ex.Message);
			}
		}

		public List<Audiobook> LoadBooks()
		{
			return new List<Audiobook>(m_audiobooks.Values);
		}

		public Audiobook LoadBook(string bookId)
		{
			Audiobook book;
			m_audiobooks.TryGetValue(bookId, out book);
			return book;
		}

		/// <summary>
		/// Insert or update a book. Preserves the Users dict from the
		/// existing entry so a rescan does not wipe user progress.
		/// </summary>
		public void UpdateBook(Audiobook book)
		{
			Audiobook existing;
			bool found = m_audiobooks.TryGetValue(book.Id, out existing);
			if (found)
			{
				book.Users = existing.Users;
			}
			m_audiobooks[book.Id] = book;
			m_data.Save(eDataType.Audiobook, book);
			book.m_bIsDirty = false;
		}

		/// <summary>
		/// Insert or update chapters. Preserves the Users dict from existing
		/// entries so a rescan does not wipe user progress.
		/// </summary>
		public void UpdateChapters(List<Chapter> chapters)
		{
			for (int i = 0; i < chapters.Count; i++)
			{
				Chapter chapter = chapters[i];
				Chapter existing;
				bool found = m_chapters.TryGetValue(chapter.Id, out existing);
				if (found)
				{
					chapter.Users = existing.Users;
				}
				m_chapters[chapter.Id] = chapter;
			}
			m_data.SaveList(eDataType.AudiobookChapter, chapters);
			for (int i = 0; i < chapters.Count; i++)
			{
				chapters[i].m_bIsDirty = false;
			}
		}

		public Chapter LoadChapter(string chapterId)
		{
			Chapter chapter;
			m_chapters.TryGetValue(chapterId, out chapter);
			return chapter;
		}

		public List<Chapter> LoadChapters(string bookId)
		{
			List<Chapter> result = new List<Chapter>();
			foreach (KeyValuePair<string, Chapter> pair in m_chapters)
			{
				if (pair.Value.AudiobookId == bookId)
				{
					result.Add(pair.Value);
				}
			}
			return result;
		}

		public void Delete(Audiobook book)
		{
			m_audiobooks.TryRemove(book.Id, out _);
			m_data.Delete(eDataType.Audiobook, book.Id);
		}

		public void Delete(Chapter chapter)
		{
			m_chapters.TryRemove(chapter.Id, out _);
			m_data.Delete(eDataType.AudiobookChapter, chapter.Id);
		}
	}
}
