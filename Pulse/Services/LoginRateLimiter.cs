using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Pulse.Services
{
	/// <summary>
	/// Counter + lockout state for a single source IP. FailureCount accrues
	/// inside the current window; FirstAttempt marks when that window opened;
	/// LockoutExpiry is the wall-clock instant at which the lockout (if any)
	/// lifts. DateTime.MinValue on LockoutExpiry means "not currently locked
	/// out". Fields are public per the data-bag convention -- no getter
	/// ceremony.
	/// </summary>
	public class AuthAttemptRecord
	{
		public int FailureCount = 0;
		public DateTime FirstAttempt = DateTime.MinValue;
		public DateTime LockoutExpiry = DateTime.MinValue;
	}

	/// <summary>
	/// Per-source-IP brute-force protection (PLS135 P4) for the two endpoints
	/// that accept credentials -- Login and CreateToken. Tracks failed
	/// attempts in-memory and locks an IP out of those two endpoints once it
	/// crosses LockoutThreshold within m_attemptWindow. Lockout lasts
	/// m_lockoutPeriod, then the IP is allowed back in. Nothing else is
	/// gated: this limiter does not police any other route, does not block
	/// unauthenticated access, and is consulted only from the two handlers
	/// that opt in.
	///
	/// Pruning is lazy: CleanupStaleRecords runs at most once per m_cleanupInterval,
	/// piggy-backing on a RecordFailure call, and drops entries older than
	/// two hours whose lockout (if any) has elapsed.
	/// </summary>
	public class LoginRateLimiter
	{
		private ConcurrentDictionary<string, AuthAttemptRecord> m_attempts = new ConcurrentDictionary<string, AuthAttemptRecord>();

		private const int LockoutThreshold = 20;
		private TimeSpan m_attemptWindow = TimeSpan.FromSeconds(10);
		private TimeSpan m_lockoutPeriod = TimeSpan.FromMinutes(5);
		private TimeSpan m_cleanupInterval = TimeSpan.FromMinutes(5);
		private DateTime m_lastCleanup = DateTime.UtcNow;

		/// <summary>
		/// Drops stale entries from m_attempts at most once per
		/// m_cleanupInterval. "Stale" = window opened more than two hours ago
		/// AND any lockout has already lifted. Snapshots the key set first
		/// so the concurrent dictionary is not mutated mid-enumeration.
		/// </summary>
		private void CleanupStaleRecords(DateTime now)
		{
			if (now - m_lastCleanup < m_cleanupInterval)
			{
				return;
			}
			m_lastCleanup = now;
			TimeSpan maxAge = TimeSpan.FromHours(2);
			List<string> keys = new List<string>(m_attempts.Keys);
			int keyCount = keys.Count;
			for (int index = 0; index < keyCount; index++)
			{
				string key = keys[index];
				AuthAttemptRecord entry;
				bool found = m_attempts.TryGetValue(key, out entry);
				if (!found)
				{
					continue;
				}
				bool stale = now - entry.FirstAttempt > maxAge && entry.LockoutExpiry < now;
				if (stale)
				{
					AuthAttemptRecord removed;
					m_attempts.TryRemove(key, out removed);
				}
			}
		}

		/// <summary>
		/// Returns true if the IP currently sits inside an active lockout
		/// window. An unknown IP is never locked out. Read-only -- does not
		/// touch the failure counter or the window.
		/// </summary>
		public bool IsLockedOut(string ipAddress)
		{
			AuthAttemptRecord entry;
			bool found = m_attempts.TryGetValue(ipAddress, out entry);
			if (!found)
			{
				return false;
			}
			if (entry.LockoutExpiry > DateTime.UtcNow)
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// Record one failed authentication attempt from this IP. Creates the
		/// entry on first failure; resets the window if the prior one has
		/// elapsed; arms the lockout when FailureCount crosses LockoutThreshold.
		/// Also opportunistically prunes stale entries.
		/// </summary>
		public void RecordFailure(string ipAddress)
		{
			DateTime now = DateTime.UtcNow;
			AuthAttemptRecord entry;
			bool found = m_attempts.TryGetValue(ipAddress, out entry);
			if (!found)
			{
				AuthAttemptRecord fresh = new AuthAttemptRecord();
				fresh.FailureCount = 0;
				fresh.FirstAttempt = now;
				fresh.LockoutExpiry = DateTime.MinValue;
				m_attempts.TryAdd(ipAddress, fresh);
				// Re-fetch in case another thread won the TryAdd race -- we
				// must mutate the entry that actually lives in the dictionary.
				m_attempts.TryGetValue(ipAddress, out entry);
			}
			// Roll the window forward if the prior one has elapsed.
			if (now - entry.FirstAttempt > m_attemptWindow)
			{
				entry.FailureCount = 0;
				entry.FirstAttempt = now;
				entry.LockoutExpiry = DateTime.MinValue;
			}
			entry.FailureCount = entry.FailureCount + 1;
			if (entry.FailureCount >= LockoutThreshold)
			{
				entry.LockoutExpiry = now + m_lockoutPeriod;
			}
			CleanupStaleRecords(now);
		}

		/// <summary>
		/// Clears the IP's failure state on a successful auth so a legitimate
		/// user does not carry prior typos toward a future lockout.
		/// </summary>
		public void RecordSuccess(string ipAddress)
		{
			AuthAttemptRecord removed;
			m_attempts.TryRemove(ipAddress, out removed);
		}
	}
}
