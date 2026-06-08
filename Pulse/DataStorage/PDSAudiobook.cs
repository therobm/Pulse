using System;
using System.Collections.Generic;

namespace Pulse.DataStorage
{
	public class AudiobookData : PulseDataObject
	{
		public string Title { get; set; } = "";
		public string Author { get; set; } = "";
		public string Narrator { get; set; } = "";
		public string Description { get; set; } = "";
		public string ArtworkPath { get; set; } = "";
		public DateTime DateAdded { get; set; }
		public string Collection { get; set; } = "";
		public int CollectionIndex { get; set; }
		public Dictionary<string, AudiobookUserState> UserState { get; set; } = new Dictionary<string, AudiobookUserState>();
	}

	public class AudiobookUserState
	{
		public string LastChapterId { get; set; } = "";
		public DateTime LastPlayed { get; set; }
	}

	public class AudiobookChapterData : PulseDataObject
	{
		public string AudiobookId { get; set; } = "";
		public string Title { get; set; } = "";
		public int DurationSeconds { get; set; }
		public int OrderIndex { get; set; }
		public string LocalPath { get; set; } = "";
		public long FileSizeBytes { get; set; }
		public int StartMs { get; set; }
		public int EndMs { get; set; }
		public Dictionary<string, ChapterUserState> UserState { get; set; } = new Dictionary<string, ChapterUserState>();
	}

	public class ChapterUserState
	{
		public int PositionSeconds { get; set; }
		public bool Completed { get; set; }
		public DateTime LastPlayed { get; set; }
	}
}
