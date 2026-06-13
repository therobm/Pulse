using System;
using System.Collections.Generic;
using Pulse.DataStorage;

namespace Pulse.Data
{
	/// <summary>
	/// Persisted record for one user account. Lives in PulseDataStore under
	/// eDataType.User; the inherited Id holds the user name (the key everywhere
	/// else uses). Serialization container -- bare PascalCase public fields,
	/// no m_ prefix, mirroring the TrackData / AlbumData convention.
	/// </summary>
	public class UserData : PulseDataObject
	{
		public string DisplayName = "";
		public bool IsAdmin = false;
		public DateTime Created = DateTime.MinValue;
		public string PasswordHash = "";
		public List<TokenData> Tokens = new List<TokenData>();
	}

	/// <summary>
	/// Persisted device-token record attached to a UserData. Plain serialization
	/// container; the owning user is implicit (whichever UserData.Tokens list
	/// holds the row), so there is no UserName field here.
	/// </summary>
	public class TokenData
	{
		public string Token = "";
		public string Label = "";
		public string CreatedAt = "";
		public string LastUsed = "";
	}
}
