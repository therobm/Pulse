namespace PulseAPI.CSharp
{
	/// <summary>
	/// Wire body for POST /pulse/createToken. Mints a long-lived device token
	/// for the named user. Label is a free-form hint (e.g. "Living room TV")
	/// the management UI shows alongside the token. PascalCase fields are
	/// required: PulseWire serialises by reflecting field names verbatim.
	/// </summary>
	public class PulseCreateTokenRequest : PulseObject
	{
		public string Username;
		public string Label;
	}

	/// <summary>
	/// Wire body for POST /pulse/revokeToken. Carries the raw token value the
	/// caller wants to invalidate.
	/// </summary>
	public class PulseRevokeTokenRequest : PulseObject
	{
		public string Token;
	}

	/// <summary>
	/// Full token returned on creation. The raw Token value is shown to the
	/// caller exactly once -- subsequent list calls also surface the raw value
	/// because it is the table's primary key, but production UIs should treat
	/// the create response as the single moment to copy it.
	/// </summary>
	public class PulseToken : PulseObject
	{
		public string Token;
		public string Username;
		public string Label;
		public string CreatedAt;
	}

	/// <summary>
	/// Summary entry for the token-management list. Adds LastUsed on top of
	/// the PulseToken shape so the UI can show activity for each device.
	/// </summary>
	public class PulseTokenSummary : PulseObject
	{
		public string Token;
		public string Username;
		public string Label;
		public string CreatedAt;
		public string LastUsed;
	}
}
