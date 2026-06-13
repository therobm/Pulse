using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	/// <summary>
	/// What the user or the app did. The leaf of the analytics taxonomy; each
	/// value rolls up to exactly one category, which the server derives at
	/// ingest (the client never sends a category). Lives in the shared PulseAPI
	/// project so the client emitter and the server intake cannot drift on the
	/// value set. Adding a value here without also adding it to the server's
	/// category map lands its rows under "Uncategorized" -- they are still
	/// stored and can be re-categorised later, never dropped.
	/// </summary>
	public enum eAction
	{
		Invalid,
		// App lifecycle
		Launch,
		Quit,
		Login,
		SettingsChange,
		// Navigation
		Browse,
		OpenNowPlaying,
		Tab,
		// Search
		Search,
		// Playback
		Play,
		Pause,
		Resume,
		Next,
		Previous,
		Seek,
		QueueAdd,
		ModeChange,
		TrackLoad,
		TrackStream,
		// Library
		PlaylistCreate,
		PlaylistEdit,
		FavoriteToggle,
		// Network / health
		Connectivity,
		Scrobble,
	}

	/// <summary>
	/// Outcome of the action. Serialized through PulseWire as a string, so the
	/// data reads cleanly and survives enum value renumbering.
	/// </summary>
	public enum eResult
	{
		Invalid,
		OK,
		Fail,
		Timeout,
		Cancelled,
	}

	public class PulseAnalyticsSession
	{
		public string SessionId;
		public string DeviceId;
		public string User;
		public string AppVersion;
		public string Platform;
		public string StartedAt;
	}
	
	public class PulseAnalyticsEvent
	{
		public eAction Action;
		public eResult Result;
		public string ObjectType;
		public string ObjectId;
		public long DurationMs;
		public string Detail;
		public string Timestamp;
	}

	public class PulseAnalyticsBatch
	{
		public string DeviceId;
		public string SessionId;
		public string AppVersion;
		public string User;
		public string Platform;
		public List<PulseAnalyticsEvent> Events;
	}


	public class PulseDiagnosticsEvent
	{
		public string DeviceId;
		public string SessionId;
		public string AppVersion;
		public string User;
		public string Platform;
		public string Caller;
		public string MemberName;
		public string ErrorMessage;
		public string Detail;
		public string Timestamp;
	}
}
