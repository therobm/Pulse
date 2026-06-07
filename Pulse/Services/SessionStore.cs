using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Pulse.Services
{
	/// <summary>
	/// One server-side session: the user the session was created for, whether
	/// they were an admin at the time, the absolute expiry instant (slid
	/// forward on every validation), and the per-session idle timeout the
	/// slide uses. Fields are public per the data-bag convention -- no getter
	/// ceremony.
	/// </summary>
	public class SessionEntry
	{
		public string UserName = "";
		public bool IsAdmin = false;
		public DateTime ExpiresUtc = DateTime.MinValue;
		public TimeSpan IdleTimeout = TimeSpan.Zero;
	}

	/// <summary>
	/// In-memory session table for the new cookie-based auth (PLS132 / parent
	/// PLS129). The cookie carries a 256-bit random id; this static dictionary
	/// is the only place that knows what user / admin flag / expiry that id
	/// belongs to, so the cookie itself is opaque to the client.
	///
	/// Lifetimes are lazy: expired entries are not eagerly swept -- the next
	/// TryValidate that sees one removes it. P1 has no background pruner.
	/// </summary>
	public static class SessionStore
	{
		private static ConcurrentDictionary<string, SessionEntry> s_sessions = new ConcurrentDictionary<string, SessionEntry>();

		/// <summary>
		/// Base64Url variant: '+' -> '-', '/' -> '_', strip '=' padding. Keeps
		/// the id safe to drop into a cookie value without escaping.
		/// </summary>
		private static string ToUrlSafeBase64(byte[] bytes)
		{
			string standard = Convert.ToBase64String(bytes);
			string replaced = standard.Replace('+', '-').Replace('/', '_');
			return replaced.TrimEnd('=');
		}

		/// <summary>
		/// Mints a new opaque session id, stores it against the supplied user
		/// metadata, and returns it. "Remember me" stretches the idle window
		/// to 30 days; otherwise the session expires after 24 hours of
		/// inactivity. Slides on every successful validation.
		/// </summary>
		public static string CreateSession(string userName, bool isAdmin, bool rememberMe)
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
			entry.UserName = userName;
			entry.IsAdmin = isAdmin;
			entry.IdleTimeout = idle;
			entry.ExpiresUtc = DateTime.UtcNow + idle;

			s_sessions.TryAdd(sessionId, entry);
			return sessionId;
		}

		/// <summary>
		/// Looks up a session by its id. Returns false if the id is unknown or
		/// already expired (and removes the expired entry on the way out). On
		/// success, the entry's idle window slides: ExpiresUtc is reset to now
		/// plus IdleTimeout so an active user does not get logged out mid-use.
		/// </summary>
		public static bool TryValidate(string sessionId, out string userName, out bool isAdmin)
		{
			userName = "";
			isAdmin = false;
			if (string.IsNullOrEmpty(sessionId))
			{
				return false;
			}

			SessionEntry entry;
			bool found = s_sessions.TryGetValue(sessionId, out entry);
			if (!found)
			{
				return false;
			}

			DateTime now = DateTime.UtcNow;
			if (now > entry.ExpiresUtc)
			{
				SessionEntry removed;
				s_sessions.TryRemove(sessionId, out removed);
				return false;
			}

			entry.ExpiresUtc = now + entry.IdleTimeout;
			userName = entry.UserName;
			isAdmin = entry.IsAdmin;
			return true;
		}

		/// <summary>
		/// Drops a session by id. Safe to call with an unknown id.
		/// </summary>
		public static void Remove(string sessionId)
		{
			if (string.IsNullOrEmpty(sessionId))
			{
				return;
			}
			SessionEntry removed;
			s_sessions.TryRemove(sessionId, out removed);
		}
	}
}
