namespace Pulse.Series
{
	/// <summary>
	/// Per-user playback progress on one SeriesItem. Keyed by (ItemId,
	/// UserName) in the item_progress table; one row per user per item.
	/// PositionSeconds is the resume point; Completed is the terminal flag
	/// flipped once the user reaches the end (separate from PositionSeconds so
	/// "finished" doesn't get unset by a stray seek).
	/// </summary>
	public class ItemProgressInfo
	{
		public string ItemId = "";
		public string UserName = "";
		public int PositionSeconds = 0;
		public bool Completed = false;
		public string LastPlayed = "";
	}
}
