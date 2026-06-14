using System;
using System.Collections.Generic;

namespace Pulse.DataStorage
{
	public class User : PulseDataObject
	{
		public string Name = "";
		public string DisplayName = "";
		public string PasswordHash = "";
		public bool IsAdmin = false;
		public List<UserToken> Tokens = new List<UserToken>();
		public User()
		{
			Id = Guid.NewGuid().ToString();
		}

		public void AddToken(string token, string label)
		{
			if (string.IsNullOrEmpty(token))
			{
				return;
			}

			string storedLabel = label;
			if (storedLabel == null)
			{
				storedLabel = "";
			}

			UserToken tokenData = new UserToken();
			tokenData.Token = token;
			tokenData.Label = storedLabel;
			tokenData.Created = DateTime.UtcNow;
			tokenData.LastUsed = DateTime.UtcNow;

			Tokens.Add(tokenData);
			m_bIsDirty = true;
		}
	}

	public class UserToken
	{
		public string Token = "";
		public string Label = "";
		public DateTime Created;
		public DateTime LastUsed;
	}
}
