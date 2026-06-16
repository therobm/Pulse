namespace PulseAPI.CSharp
{
	public enum eAuthOutcome
	{
		Ok,
		InvalidCredentials,
		RateLimited,
		NotSignedIn,
		Forbidden,
		UnknownUser,
		AlreadyInitialized,
		PasswordTooShort,
		Failed
	}

	public enum eAuthState
	{
		NeedsSetup,
		SignedIn,
		NotSignedIn
	}

	public class PulseLoginRequest : PulseObject
	{
		public string Username;
		public string Password;
		public bool RememberMe;
	}

	public class PulseSetPasswordRequest : PulseObject
	{
		public string Password;
	}

	public class PulseSetupAdminRequest : PulseObject
	{
		public string Username;
		public string DisplayName;
		public string Password;
	}

	/// <summary>
	/// Result of an action that signs the caller in (login, setupAdmin). When
	/// Outcome is Ok the inherited Id is the user id and Username/IsAdmin carry
	/// the signed-in identity; on any other Outcome those fields are empty.
	/// </summary>
	public class PulseLoginResult : PulseObject
	{
		public eAuthOutcome Outcome;
		public string Username = "";
		public bool IsAdmin = false;
		public string Token = "";
	}

	/// <summary>
	/// whoami probe. State is what the caller routes on; the inherited Id +
	/// Username + IsAdmin are populated only when State is SignedIn.
	/// </summary>
	public class PulseAuthState : PulseObject
	{
		public eAuthState State;
		public string Username = "";
		public bool IsAdmin = false;
	}

	public class PulseSetPasswordResult : PulseObject
	{
		public eAuthOutcome Outcome;
	}

	/// <summary>
	/// createToken returns the outcome plus, on Ok, the minted token -- so the
	/// route hands back this rather than a bare PulseToken.
	/// </summary>
	public class PulseCreateTokenResult : PulseObject
	{
		public eAuthOutcome Outcome;
		public PulseToken Token;
	}
}
