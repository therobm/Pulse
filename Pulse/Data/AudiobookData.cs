

using Pulse.Database;
using Pulse.DataStorage;
using Pulse.Series;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using TagLib.Riff;

namespace Pulse.Data
{
	public class AudiobookData
	{
		private ConcurrentDictionary<string, Audiobook> m_audiobooks = new ConcurrentDictionary<string, Audiobook>();
		private ConcurrentDictionary<string, Chapter> m_chapters = new ConcurrentDictionary<string, Chapter>();


		private PulseConfig m_config;
		private PulseDataStore m_audiobookData;

		public AudiobookData(PulseConfig pulseConfig)
		{
			m_config = pulseConfig;

			string audiobookDB = "audiobooks.db";
#if DEBUG
			audiobookDB = "audiobooks_staging.db";
#endif

			string dbPath = Path.Combine(m_config.PulseDataPath, audiobookDB);
			m_audiobookData = new PulseDataStore(dbPath);
		}

		public List<Audiobook> LoadBooks()
		{
			return new List<Audiobook>();
		}
		public Audiobook LoadBook(string bookId)
		{
			return null;
		}
		public void UpdateBook(Audiobook bookData)
		{
		}
		public void UpdateBooks(List<Audiobook> books)
		{

		}
		public void UpdateChapter(Chapter chapter)
		{
			List<Chapter> chapters = new List<Chapter>();
			chapters.Add(chapter);
			UpdateChapters(chapters);
		}
		public void UpdateChapters(List<Chapter> chapters)
		{
		}
		public Chapter LoadChapter(string chapterId)
		{
			return null;
		}
		public List<Chapter> LoadChapters(string bookId)
		{
			return new List<Chapter>();
		}
		public void Delete(Audiobook book)
		{

		}
		public void Delete(Chapter chapter)
		{
		}

	}
}
