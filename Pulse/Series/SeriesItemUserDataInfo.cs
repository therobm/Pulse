namespace Pulse.Series
{
	/// <summary>
	/// Per-user playback progress on one SeriesItem. Keyed by (ItemId,
	/// UserName) in the series_items_user_data table; one row per user per
	/// item. PositionSeconds is the resume point; Completed is the terminal
	/// flag flipped once the user reaches the end (separate from
	/// PositionSeconds so "finished" doesn't get unset by a stray seek).
	/// </summary>
	public class SeriesItemUserDataInfo
	{
		public string ItemId = "";
		public string UserName = "";
		public int PositionSeconds = 0;
		public bool Completed = false;
		public string LastPlayed = "";
	}
}
