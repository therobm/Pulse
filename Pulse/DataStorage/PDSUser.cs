using System;
using System.Collections.Generic;

namespace Pulse.DataStorage
{
	public class UserData : PulseDataObject
	{
		public string Name = "";
		public string DisplayName = "";
		public string PasswordHash = "";
		public bool IsAdmin;
		public List<UserToken> Tokens = new List<UserToken>();
	}

	public class UserToken
	{
		public string Token = "";
		public string Label = "";
		public DateTime Created;
		public DateTime LastUsed;
	}
}
