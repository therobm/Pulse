namespace PulseAPI.CSharp
{
	public class PulseLoginRequest : PulseObject
	{
		public string Username;
		public string Password;
		public bool RememberMe;
	}


	public class PulseSetPasswordRequest : PulseObject
	{
		public string Username;
		public string Password;
	}

	public class PulseLoginResult : PulseObject
	{
		public string Username;
		public bool IsAdmin;
	}

	public class PulseSetupAdminRequest : PulseObject
	{
		public string Username;
		public string DisplayName;
		public string Password;
	}
}
