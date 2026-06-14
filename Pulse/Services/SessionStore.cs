using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Pulse.Services
{
	public class SessionEntry
	{
		public string UserId = "";
		public DateTime ExpiresUtc = DateTime.MinValue;
		public TimeSpan IdleTimeout = TimeSpan.Zero;
	}

	/// <summary>
	/// Expired entries are removed lazily on the next lookup, not by a
	/// background sweep -- don't add a pruner expecting one to be missing.
	/// </summary>
	public class SessionStore
	{
		private ConcurrentDictionary<string, SessionEntry> m_sessions = new ConcurrentDictionary<string, SessionEntry>();

		/// <summary>
		/// Base64Url variant ('+'->'-', '/'->'_', no '=' padding) so the id is
		/// safe in a cookie value without escaping.
		/// </summary>
		public static string ToUrlSafeBase64(byte[] bytes)
		{
			string standard = Convert.ToBase64String(bytes);
			string replaced = standard.Replace('+', '-').Replace('/', '_');
			return replaced.TrimEnd('=');
		}

		public string CreateSession(string userId, bool rememberMe)
		{
			byte[] raw = RandomNumberGenerator.GetBytes(32);
			string sessionId = ToUrlSafeBase64(raw);

			TimeSpan idle;
			if (rememberMe)
			{
				idle = TimeSpan.FromDays(30);
			}
			else
			{
				idle = TimeSpan.FromHours(24);
			}

			SessionEntry entry = new SessionEntry();
			entry.UserId = userId;
			entry.IdleTimeout = idle;
			entry.ExpiresUtc = DateTime.UtcNow + idle;

			m_sessions.TryAdd(sessionId, entry);
			return sessionId;
		}

		/// <summary>
		/// A successful lookup slides the idle window forward (ExpiresUtc reset
		/// to now + IdleTimeout), so an active user is not logged out mid-use.
		/// </summary>
		public bool GetUserIdForSession(string sessionId, out string userId)
		{
			userId = "";
			if (string.IsNullOrEmpty(sessionId))
			{
				return false;
			}

			SessionEntry entry;
			bool found = m_sessions.TryGetValue(sessionId, out entry);
			if (!found)
			{
				return false;
			}

			DateTime now = DateTime.UtcNow;
			if (now > entry.ExpiresUtc)
			{
				SessionEntry removed;
				m_sessions.TryRemove(sessionId, out removed);
				return false;
			}

			entry.ExpiresUtc = now + entry.IdleTimeout;
			userId = entry.UserId;
			return true;
		}

		public void Remove(string sessionId)
		{
			if (string.IsNullOrEmpty(sessionId))
			{
				return;
			}
			SessionEntry removed;
			m_sessions.TryRemove(sessionId, out removed);
		}
	}
}
