using System;
using System.Collections.Generic;

namespace Pulse.DataStorage
{
	public class UserData : PulseDataObject
	{
		public string Name { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public string PasswordHash { get; set; } = "";
		public bool IsAdmin { get; set; }
		public List<UserToken> Tokens { get; set; } = new List<UserToken>();
	}

	public class UserToken
	{
		public string Token { get; set; } = "";
		public string Label { get; set; } = "";
		public DateTime Created { get; set; }
		public DateTime LastUsed { get; set; }
	}
}
