namespace PulseAPI.CSharp
{
	/// <summary>
	/// Wire body for POST /pulse/login. PascalCase fields match the verbatim
	/// naming PulseWire applies on parse: clients must send Username/Password/
	/// RememberMe exactly. Plain data bag (public fields, no properties) so it
	/// composes naturally with the rest of PulseAPI.
	/// </summary>
	public class LoginRequest
	{
		public string Username;
		public string Password;
		public bool RememberMe;
	}

	/// <summary>
	/// Wire body for POST /pulse/setPassword. Same PascalCase rule as
	/// LoginRequest -- the client must send Username/Password verbatim.
	/// </summary>
	public class SetPasswordRequest
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
	public class LoginResult
	{
		public string Username;
		public bool IsAdmin;
	}
}
