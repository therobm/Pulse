using Pulse.DataStorage;
using Pulse.Services;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Pulse.Data
{

	public class UserData
	{
		private Dictionary<string, User> m_users = new Dictionary<string, User>(StringComparer.Ordinal);
		private object m_lock = new object();
		private PulseDataStore m_data;

	
		public UserData(PulseConfig config)
		{
			string userDB = "user.db";
#if DEBUG
			userDB = "user_staging.db";
#endif
			string dbPath = Path.Combine(config.PulseDataPath, userDB);
			m_data = new PulseDataStore(dbPath);
		}

		/// <summary>
		/// Hydrate the in-memory dictionary from the store. Call once at startup.
		/// </summary>
		public void Load()
		{
			List<User> records = m_data.LoadList<User>(eDataType.User);
			lock (m_lock)
			{
				for (int index = 0; index < records.Count; index++)
				{
					User user = records[index];
					if (user == null)
					{
						continue;
					}
					if (string.IsNullOrEmpty(user.Id))
					{
						continue;
					}
					m_users[user.Id] = user;
				}
			}
		}

		public User LookupUserByName(string userName)
		{
			lock (m_lock)
			{
				foreach (KeyValuePair<string, User> kvp in m_users)
				{
					if (string.Equals(kvp.Value.Name,userName, StringComparison.OrdinalIgnoreCase))
						return kvp.Value;
				}
			}
			return null;
		}
		public User GetUser(string userId)
		{
			if (string.IsNullOrEmpty(userId))
			{
				return null;
			}
			lock (m_lock)
			{
				User user;
				bool found = m_users.TryGetValue(userId, out user);
				if (!found)
				{
					return null;
				}
				return user;
			}
		}

	
		public List<User> GetAllUsers()
		{
			lock (m_lock)
			{
				List<User> snapshot = new List<User>(m_users.Count);
				foreach (User user in m_users.Values)
				{
					snapshot.Add(user);
				}
				return snapshot;
			}
		}

	
		public User CreateUser(string name, string displayName, bool isAdmin, out string error)
		{
			error = "";
			if (string.IsNullOrWhiteSpace(name))
			{
				error = "name must not be empty";
				return null;
			}

			string storedDisplayName = displayName;
			if (storedDisplayName == null)
			{
				storedDisplayName = "";
			}

			User user = new User();
			user.Name = name;
			user.DisplayName = storedDisplayName;
			user.IsAdmin = isAdmin;
			user.PasswordHash = "";
			user.Tokens = new List<UserToken>();

			lock (m_lock)
			{
				if (m_users.ContainsKey(name))
				{
					error = "A user with that name already exists.";
					return null;
				}
				m_users[name] = user;
			}

			m_data.Save(eDataType.User, user);
			user.m_bIsDirty = false;
			return user;
		}


		public string UpdateUser(string userId, string newName, string displayName)
		{
			if (string.IsNullOrWhiteSpace(userId))
			{
				return "Old name is required.";
			}
			if (string.IsNullOrWhiteSpace(newName))
			{
				return "New name is required.";
			}

			string storedDisplayName = displayName;
			if (storedDisplayName == null)
			{
				storedDisplayName = "";
			}
			
			User user = GetUser(userId);


			bool renaming = !string.Equals(user.Name, newName, StringComparison.Ordinal);
			if (!renaming)
				return "Name was already " + newName;
			
			user.Name = newName;

			m_data.Save(eDataType.User, user);
			user.m_bIsDirty = false;
			return "";
		}

	
		public void DeleteUser(string userId)
		{
			if (string.IsNullOrEmpty(userId))
			{
				return;
			}

			bool removed;
			lock (m_lock)
			{
				removed = m_users.Remove(userId);
			}

			if (removed)
			{
				m_data.Delete(eDataType.User, userId);
			}
		}

		public string GetPasswordHash(string userId)
		{
			if (string.IsNullOrEmpty(userId))
			{
				return "";
			}
			lock (m_lock)
			{
				User user = GetUser(userId);
				if (user == null)
				{
					return "";
				}
				if (string.IsNullOrEmpty(user.PasswordHash))
				{
					return "";
				}
				return user.PasswordHash;
			}
		}

	
		public void SetPassword(string userId, string passwordHash)
		{
			if (string.IsNullOrEmpty(userId))
			{
				return;
			}

			string storedHash = passwordHash;
			if (storedHash == null)
			{
				storedHash = "";
			}

			User user;
			lock (m_lock)
			{
				user = GetUser(userId);
				if (user == null)
				{
					return;
				}
				user.PasswordHash = storedHash;
			}

			m_data.Save(eDataType.User, user);
			user.m_bIsDirty = false;
		}

		public bool AnyUserHasPassword()
		{
			lock (m_lock)
			{
				foreach (User user in m_users.Values)
				{
					if (!string.IsNullOrEmpty(user.PasswordHash))
					{
						return true;
					}
				}
			}
			return false;
		}

		public void Save()
		{
			List<User> dirtyUsers = new List<User>();
			foreach (User user in m_users.Values)
			{
				if (user.m_bIsDirty) { dirtyUsers.Add(user); }
			}
			for (int i = 0; i < dirtyUsers.Count; i++)
			{
				m_data.Save<User>(eDataType.User, dirtyUsers[i]);
			}
		}

		public string CreateToken(string userId, string label)
		{
			User user = GetUser(userId);
			if (user == null)
				return "";

			byte[] raw = RandomNumberGenerator.GetBytes(32);
			string token = SessionStore.ToUrlSafeBase64(raw);
			if (label == null)
			{
				label = "";
			}

			user.AddToken(token, label);
			Save();
			return token;
		}

		/// <summary>
		/// Stamps the last-used timestamp on the matching token (whichever user
		/// holds it) and persists that user. No-op when the token is unknown.
		/// </summary>
		public void UpdateTokenLastUsed(string userId, string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return;
			}

			User owningUser = GetUser(userId);
			for (int index = 0; index < owningUser.Tokens.Count; index++)
			{
				if (string.Equals(owningUser.Tokens[index].Token, token, StringComparison.Ordinal))
				{
					owningUser.Tokens[index].LastUsed = DateTime.UtcNow;
					break;
				}
			}

			if (owningUser == null)
			{
				return;
			}

			m_data.Save(eDataType.User, owningUser);
			owningUser.m_bIsDirty = false;
		}

		
	}
}
