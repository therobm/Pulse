namespace Pulse.DataStorage
{
	/// <summary>
	/// Stored shape of one client diagnostic event. Mirrors the wire-side
	/// PulseDiagnosticsEvent plus a server-stamped ReceivedAt. Persisted through
	/// PulseDataStore as a (Diagnostic, Id) json blob; the in-memory list in
	/// DiagnosticsData is authoritative at runtime.
	/// </summary>
	public class DiagnosticRecord : PulseDataObject
	{
		public string DeviceId = "";
		public string SessionId = "";
		public string AppVersion = "";
		public int BuildNumber;
		public string User = "";
		public string Platform = "";
		public string OsVersion = "";
		public string DeviceModel = "";
		public string NetworkType = "";
		public string Caller = "";
		public string MemberName = "";
		public string ErrorMessage = "";
		public string Detail = "";
		public string Timestamp = "";
	}
}
