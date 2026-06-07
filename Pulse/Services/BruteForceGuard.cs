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
	/// crosses LockoutThreshold within s_attemptWindow. Lockout lasts
	/// s_lockoutPeriod, then the IP is allowed back in. Nothing else is
	/// gated: this limiter does not police any other route, does not block
	/// unauthenticated access, and is consulted only from the two handlers
	/// that opt in.
	///
	/// Pruning is lazy: CleanupStaleRecords runs at most once per s_cleanupInterval,
	/// piggy-backing on a RecordFailure call, and drops entries older than
	/// two hours whose lockout (if any) has elapsed.
	/// </summary>
	public static class BruteForceGuard
	{
		private static ConcurrentDictionary<string, AuthAttemptRecord> s_attempts = new ConcurrentDictionary<string, AuthAttemptRecord>();

		private const int LockoutThreshold = 20;
		private static TimeSpan s_attemptWindow = TimeSpan.FromSeconds(10);
		private static TimeSpan s_lockoutPeriod = TimeSpan.FromMinutes(5);
		private static TimeSpan s_cleanupInterval = TimeSpan.FromMinutes(5);
		private static DateTime s_lastCleanup = DateTime.UtcNow;

		/// <summary>
		/// Drops stale entries from s_attempts at most once per
		/// s_cleanupInterval. "Stale" = window opened more than two hours ago
		/// AND any lockout has already lifted. Snapshots the key set first
		/// so the concurrent dictionary is not mutated mid-enumeration.
		/// </summary>
		private static void CleanupStaleRecords(DateTime now)
		{
			if (now - s_lastCleanup < s_cleanupInterval)
			{
				return;
			}
			s_lastCleanup = now;
			TimeSpan maxAge = TimeSpan.FromHours(2);
			List<string> keys = new List<string>(s_attempts.Keys);
			int keyCount = keys.Count;
			for (int index = 0; index < keyCount; index++)
			{
				string key = keys[index];
				AuthAttemptRecord entry;
				bool found = s_attempts.TryGetValue(key, out entry);
				if (!found)
				{
					continue;
				}
				bool stale = now - entry.FirstAttempt > maxAge && entry.LockoutExpiry < now;
				if (stale)
				{
					AuthAttemptRecord removed;
					s_attempts.TryRemove(key, out removed);
				}
			}
		}

		/// <summary>
		/// Returns true if the IP currently sits inside an active lockout
		/// window. An unknown IP is never locked out. Read-only -- does not
		/// touch the failure counter or the window.
		/// </summary>
		public static bool IsLockedOut(string ipAddress)
		{
			AuthAttemptRecord entry;
			bool found = s_attempts.TryGetValue(ipAddress, out entry);
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
		public static void RecordFailure(string ipAddress)
		{
			DateTime now = DateTime.UtcNow;
			AuthAttemptRecord entry;
			bool found = s_attempts.TryGetValue(ipAddress, out entry);
			if (!found)
			{
				AuthAttemptRecord fresh = new AuthAttemptRecord();
				fresh.FailureCount = 0;
				fresh.FirstAttempt = now;
				fresh.LockoutExpiry = DateTime.MinValue;
				s_attempts.TryAdd(ipAddress, fresh);
				// Re-fetch in case another thread won the TryAdd race -- we
				// must mutate the entry that actually lives in the dictionary.
				s_attempts.TryGetValue(ipAddress, out entry);
			}
			// Roll the window forward if the prior one has elapsed.
			if (now - entry.FirstAttempt > s_attemptWindow)
			{
				entry.FailureCount = 0;
				entry.FirstAttempt = now;
				entry.LockoutExpiry = DateTime.MinValue;
			}
			entry.FailureCount = entry.FailureCount + 1;
			if (entry.FailureCount >= LockoutThreshold)
			{
				entry.LockoutExpiry = now + s_lockoutPeriod;
			}
			CleanupStaleRecords(now);
		}

		/// <summary>
		/// Clears the IP's failure state on a successful auth so a legitimate
		/// user does not carry prior typos toward a future lockout.
		/// </summary>
		public static void RecordSuccess(string ipAddress)
		{
			AuthAttemptRecord removed;
			s_attempts.TryRemove(ipAddress, out removed);
		}
	}
}
