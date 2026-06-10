namespace PulseAPI.CSharp
{
	/// <summary>
	/// Wire body for POST /pulse/login. PascalCase fields match the verbatim
	/// naming PulseWire applies on parse: clients must send Username/Password/
	/// RememberMe exactly. Plain data bag (public fields, no properties) so it
	/// composes naturally with the rest of PulseAPI.
	/// </summary>
	public class PulseLoginRequest : PulseObject
	{
		public string Username;
		public string Password;
		public bool RememberMe;
	}

	/// <summary>
	/// Wire body for POST /pulse/setPassword. Same PascalCase rule as
	/// LoginRequest -- the client must send Username/Password verbatim.
	/// </summary>
	public class PulseSetPasswordRequest : PulseObject
	{
		public string Username;
		public string Password;
	}

	/// <summary>
	/// Login-success payload, written into PulseResponse.contents when the
	/// server accepts a /pulse/login call. Carries only what the client needs
	/// to render its signed-in state -- the session itself rides in the
	/// HttpOnly cookie, not in the JSON body.
	/// </summary>
	public class PulseLoginResult : PulseObject
	{
		public string Username;
		public bool IsAdmin;
	}

	/// <summary>
	/// Wire body for POST /pulse/setupAdmin -- the first-run bootstrap that
	/// creates the initial admin account and signs the caller in. Same
	/// PascalCase rule as the other auth requests; DisplayName is optional
	/// and falls back to Username server-side when blank.
	/// </summary>
	public class PulseSetupAdminRequest : PulseObject
	{
		public string Username;
		public string DisplayName;
		public string Password;
	}
}
