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
	}

	/// <summary>One chapter of an audiobook (a file, or a time window into a file).</summary>
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
