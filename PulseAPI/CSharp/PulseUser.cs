
using System;

namespace PulseAPI.CSharp
{
	public class PulseUser : PulseObject
	{
		public string Name = "";
		public string DisplayName = "";
		public DateTime Created = DateTime.MinValue;
		public bool IsAdmin = false;
		public int ScoredTrackCount;
		public int StarredCount;
		public int PlaylistLastPlayedCount;
	}
}
