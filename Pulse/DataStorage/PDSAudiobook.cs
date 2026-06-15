using Pulse.Series;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;

namespace Pulse.DataStorage
{
	/// <summary>One audiobook in the library.</summary>
	public class Audiobook : PulseDataObject
	{
		public class UserData
		{
			public string LastChapterId = "";
			public DateTime LastPlayed;
		}

		public string Title = "";
		public string Author = "";
		public string Narrator = "";
		public string Description = "";
		public string ArtworkPath = "";
		public string Collection = "";
		public int CollectionIndex;
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();

		public PulseAudiobook BuildPulseAudiobook(AudiobookManager audiobookManager, string user)
		{
			PulseAudiobook book = new PulseAudiobook();
			book.Id = Id;
			book.Title = Title;
			book.Author = Author;
			book.Narrator = Narrator;
			book.Description = Description;
			book.Collection = Collection;
			book.CollectionIndex = CollectionIndex;
			book.CoverArt = "se-" + Id;

			List<Chapter> chapters = audiobookManager.GetChapters(Id);
			book.ItemCount = chapters.Count;
			int totalDuration = 0;
			for (int i = 0; i < chapters.Count; i++)
			{
				totalDuration = totalDuration + chapters[i].DurationSeconds;
			}
			book.TotalDuration = totalDuration;
			book.UnplayedCount = 0;

			Audiobook.UserData userData;
			bool hasUser = Users.TryGetValue(user, out userData);
			if (hasUser)
			{
				book.LastItemId = userData.LastChapterId;
				book.LastPlayed = userData.LastPlayed.ToString("o");
			}
			return book;
		}
	}

	public class Chapter : PulseDataObject
	{
		public class UserData
		{
			public int PositionSeconds;
			public bool Completed;
			public DateTime LastPlayed;
		}

		public string AudiobookId = "";
		public string Title = "";
		public int DurationSeconds;
		public int OrderIndex;
		public string LocalPath = "";
		public long FileSizeBytes;
		/// <summary>
		/// Chapter offset into LocalPath, in milliseconds. EndMs == 0 means
		/// play the whole file. When EndMs > StartMs the chapter is a time
		/// window into a shared single file (embedded-chapter audiobook).
		/// </summary>
		public int StartMs;
		public int EndMs;
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();
	}


}
