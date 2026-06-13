using Pulse.DataStorage;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace Pulse.Data
{
	/// <summary>
	/// Authoritative in-memory store of user accounts (identity + password hash
	/// + device tokens), backed by a dedicated PulseDataStore over user.db.
	/// Mirrors the DiagnosticsData pattern: the dictionary is the source of
	/// truth at runtime; the store is pure persistence. User names key the
	/// dictionary by Ordinal (case-sensitive), matching the rest of the code.
	/// </summary>
	public class UserStore
	{
		private Dictionary<string, UserData> m_users = new Dictionary<string, UserData>(StringComparer.Ordinal);
		private object m_lock = new object();
		private PulseDataStore m_data;

		/// <summary>
		/// Resolves the per-environment user.db path off PulseConfig and opens
		/// the backing PulseDataStore. Call Load() at startup to hydrate.
		/// </summary>
		public UserStore(PulseConfig config)
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
			List<UserData> records = m_data.LoadList<UserData>(eDataType.User);
			lock (m_lock)
			{
				for (int index = 0; index < records.Count; index++)
				{
					UserData user = records[index];
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

		/// <summary>
		/// Returns the UserData for the given name, or null if no such user.
		/// </summary>
		public UserData GetUser(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			lock (m_lock)
			{
				UserData user;
				bool found = m_users.TryGetValue(name, out user);
				if (!found)
				{
					return null;
				}
				return user;
			}
		}

		/// <summary>
		/// Snapshot copy of every UserData currently in the store. Safe to
		/// iterate without holding the lock.
		/// </summary>
		public List<UserData> GetAllUsers()
		{
			lock (m_lock)
			{
				List<UserData> snapshot = new List<UserData>(m_users.Count);
				foreach (UserData user in m_users.Values)
				{
					snapshot.Add(user);
				}
				return snapshot;
			}
		}

		/// <summary>
		/// Creates a new user with the given metadata. Returns "" on success,
		/// or an error message on blank name / duplicate. Stamps Created with
		/// DateTime.UtcNow. A null displayName is stored as "".
		/// </summary>
		public string CreateUser(string name, string displayName, bool isAdmin)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return "Name is required.";
			}

			string storedDisplayName = displayName;
			if (storedDisplayName == null)
			{
				storedDisplayName = "";
			}

			UserData user = new UserData();
			user.Id = name;
			user.DisplayName = storedDisplayName;
			user.IsAdmin = isAdmin;
			user.Created = DateTime.UtcNow;
			user.PasswordHash = "";
			user.Tokens = new List<TokenData>();

			lock (m_lock)
			{
				if (m_users.ContainsKey(name))
				{
					return "A user with that name already exists.";
				}
				m_users[name] = user;
			}

			m_data.Save(eDataType.User, user);
			user.m_bIsDirty = false;
			return "";
		}

		/// <summary>
		/// Updates the named user's display name + admin flag, and (when the
		/// new name differs Ordinal) renames the key. Returns "" on success or
		/// an error message on missing old / colliding new. Carries PasswordHash
		/// and Tokens across the rename untouched.
		/// </summary>
		public string UpdateUser(string oldName, string newName, string displayName, bool isAdmin)
		{
			if (string.IsNullOrWhiteSpace(oldName))
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

			bool renaming = !string.Equals(oldName, newName, StringComparison.Ordinal);
			UserData user;
			bool persistDelete = false;

			lock (m_lock)
			{
				bool found = m_users.TryGetValue(oldName, out user);
				if (!found)
				{
					return "User not found.";
				}

				if (renaming)
				{
					if (m_users.ContainsKey(newName))
					{
						return "A user named '" + newName + "' already exists.";
					}
					m_users.Remove(oldName);
					user.Id = newName;
					m_users[newName] = user;
					persistDelete = true;
				}

				user.DisplayName = storedDisplayName;
				user.IsAdmin = isAdmin;
			}

			if (persistDelete)
			{
				m_data.Delete(eDataType.User, oldName);
			}
			m_data.Save(eDataType.User, user);
			user.m_bIsDirty = false;
			return "";
		}

		/// <summary>
		/// Removes the named user (identity + password hash + tokens). No-op if
		/// the user does not exist.
		/// </summary>
		public void DeleteUser(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return;
			}

			bool removed;
			lock (m_lock)
			{
				removed = m_users.Remove(name);
			}

			if (removed)
			{
				m_data.Delete(eDataType.User, name);
			}
		}

		/// <summary>
		/// Returns the stored BCrypt hash for the named user, or "" when the
		/// user does not exist or has no hash set.
		/// </summary>
		public string GetPasswordHash(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "";
			}
			lock (m_lock)
			{
				UserData user;
				bool found = m_users.TryGetValue(name, out user);
				if (!found)
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

		/// <summary>
		/// Overwrites the named user's password hash. No-op when the user does
		/// not exist; a null hash is stored as "".
		/// </summary>
		public void SetPassword(string name, string passwordHash)
		{
			if (string.IsNullOrEmpty(name))
			{
				return;
			}

			string storedHash = passwordHash;
			if (storedHash == null)
			{
				storedHash = "";
			}

			UserData user;
			lock (m_lock)
			{
				bool found = m_users.TryGetValue(name, out user);
				if (!found)
				{
					return;
				}
				user.PasswordHash = storedHash;
			}

			m_data.Save(eDataType.User, user);
			user.m_bIsDirty = false;
		}

		/// <summary>
		/// True when at least one user has a non-empty PasswordHash. Used by the
		/// auth bootstrap path to decide whether the first-run setup window is
		/// still open.
		/// </summary>
		public bool AnyUserHasPassword()
		{
			lock (m_lock)
			{
				foreach (UserData user in m_users.Values)
				{
					if (!string.IsNullOrEmpty(user.PasswordHash))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Appends a freshly-minted device token to the named user. CreatedAt
		/// is stamped here so every caller writes a consistent round-trip "o"
		/// timestamp; LastUsed starts empty until a successful lookup bumps it.
		/// No-op when the user does not exist.
		/// </summary>
		public void AddToken(string token, string userName, string label)
		{
			if (string.IsNullOrEmpty(token))
			{
				return;
			}
			if (string.IsNullOrEmpty(userName))
			{
				return;
			}

			string storedLabel = label;
			if (storedLabel == null)
			{
				storedLabel = "";
			}

			TokenData tokenData = new TokenData();
			tokenData.Token = token;
			tokenData.Label = storedLabel;
			tokenData.CreatedAt = DateTime.UtcNow.ToString("o");
			tokenData.LastUsed = "";

			UserData user;
			lock (m_lock)
			{
				bool found = m_users.TryGetValue(userName, out user);
				if (!found)
				{
					return;
				}
				user.Tokens.Add(tokenData);
			}

			m_data.Save(eDataType.User, user);
			user.m_bIsDirty = false;
		}

		/// <summary>
		/// Scans every user for a token row matching the given value. Returns
		/// the owning user name, or "" when the token is unknown.
		/// </summary>
		public string LookupTokenUser(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return "";
			}
			lock (m_lock)
			{
				foreach (UserData user in m_users.Values)
				{
					for (int index = 0; index < user.Tokens.Count; index++)
					{
						if (string.Equals(user.Tokens[index].Token, token, StringComparison.Ordinal))
						{
							return user.Id;
						}
					}
				}
			}
			return "";
		}

		/// <summary>
		/// Stamps the last-used timestamp on the matching token (whichever user
		/// holds it) and persists that user. No-op when the token is unknown.
		/// </summary>
		public void UpdateTokenLastUsed(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return;
			}

			UserData owningUser = null;
			lock (m_lock)
			{
				foreach (UserData user in m_users.Values)
				{
					for (int index = 0; index < user.Tokens.Count; index++)
					{
						if (string.Equals(user.Tokens[index].Token, token, StringComparison.Ordinal))
						{
							user.Tokens[index].LastUsed = DateTime.UtcNow.ToString("o");
							owningUser = user;
							break;
						}
					}
					if (owningUser != null)
					{
						break;
					}
				}
			}

			if (owningUser == null)
			{
				return;
			}

			m_data.Save(eDataType.User, owningUser);
			owningUser.m_bIsDirty = false;
		}

		/// <summary>
		/// Removes the matching token from whichever user holds it and persists
		/// that user. No-op when the token is unknown.
		/// </summary>
		public void DeleteToken(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return;
			}

			UserData owningUser = null;
			lock (m_lock)
			{
				foreach (UserData user in m_users.Values)
				{
					int matchIndex = -1;
					for (int index = 0; index < user.Tokens.Count; index++)
					{
						if (string.Equals(user.Tokens[index].Token, token, StringComparison.Ordinal))
						{
							matchIndex = index;
							break;
						}
					}
					if (matchIndex >= 0)
					{
						user.Tokens.RemoveAt(matchIndex);
						owningUser = user;
						break;
					}
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
