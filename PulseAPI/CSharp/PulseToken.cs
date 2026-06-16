namespace PulseAPI.CSharp
{
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
		public string Name;
		public string Label;
		public string Create;
		public string LastUsed;
	}
}
