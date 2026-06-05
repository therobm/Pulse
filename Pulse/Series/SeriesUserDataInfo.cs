namespace Pulse.Series
{
	/// <summary>
	/// Per-user, per-series relationship row stored in series_user_data.
	/// Carries both the subscription/library-membership flag and the user's
	/// resume anchor (LastItemId / LastPlayed) so the UI can jump back to
	/// where they left off in the series. Read-state (finished / unread) is
	/// intentionally NOT stored here: it is derived elsewhere from
	/// series_items_user_data.completed counts across the series' items, so
	/// the only source of truth for "finished" is the per-item table.
	/// </summary>
	public class SeriesUserDataInfo
	{
		public string SeriesId = "";
		public string UserName = "";
		public bool Subscribed = false;
		public string LastItemId = "";
		public string LastPlayed = "";
		public string DateAdded = "";
	}
}
