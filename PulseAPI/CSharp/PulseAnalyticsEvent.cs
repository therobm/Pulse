using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	/// <summary>
	/// What the client was trying to do when the event was recorded. Lives in
	/// the shared PulseAPI project so the server-side intake and the Thump
	/// client emitter cannot drift on enum values.
	/// </summary>
	public enum eAction
	{
		StreamOpen,
		StreamRead,
		StreamClose,
		CacheHit,
		CacheMiss,
		CacheWrite,
		HttpRequest,
		HttpResponse,
		TrackResolve,
		QueueBuild,
		PlaybackStateChange,
		PlayerError,
		Login,
		Ping
	}

	/// <summary>
	/// Outcome of the action. Serialized through PulseWire as a string, so the
	/// log reads cleanly and survives enum value renumbering.
	/// </summary>
	public enum eResult
	{
		OK,
		Fail,
		Timeout,
		Cancelled
	}

	/// <summary>
	/// One analytics event recorded on the client. Timestamp is the client
	/// clock at the moment of the event, in ISO-8601 format. Plain public
	/// fields -- PulseWire uses IncludeFields and emits field names verbatim
	/// as wire names.
	/// </summary>
	public class PulseAnalyticsEvent
	{
		public eAction Action;
		public eResult Result;
		public string Location;
		public string Detail;
		public string Timestamp;
	}

	/// <summary>
	/// A batch of analytics events POSTed by the client to the server intake.
	/// Identity fields describe the sender; Events carries the rows. Plain
	/// public fields for the same reason as PulseLogEvent.
	/// </summary>
	public class PulseAnalyticsBatch
	{
		public string DeviceId;
		public string SessionId;
		public string AppVersion;
		public string User;
		public string Platform;
		public List<PulseAnalyticsEvent> Events;
	}
}
