using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Pulse.Services
{
	/// <summary>
	/// Counter + lockout state for a single source IP. FailureCount accrues
	/// inside the current window; WindowStart marks when that window opened;
	/// LockoutExpiry is the wall-clock instant at which the lockout (if any)
	/// lifts. DateTime.MinValue on LockoutExpiry means "not currently locked
	/// out". Fields are public per the data-bag convention -- no getter
	/// ceremony.
	/// </summary>
	public class RateLimitEntry
	{
		public int FailureCount = 0;
		public DateTime WindowStart = DateTime.MinValue;
		public DateTime LockoutExpiry = DateTime.MinValue;
	}

	/// <summary>
	/// Per-source-IP brute-force protection (PLS135 P4) for the two endpoints
	/// that accept credentials -- Login and CreateToken. Tracks failed
	/// attempts in-memory and locks an IP out of those two endpoints once it
	/// crosses MaxFailures within s_windowDuration. Lockout lasts
	/// s_lockoutDuration, then the IP is allowed back in. Nothing else is
	/// gated: this limiter does not police any other route, does not block
	/// unauthenticated access, and is consulted only from the two handlers
	/// that opt in.
	///
	/// Pruning is lazy: PruneIfDue runs at most once per s_pruneInterval,
	/// piggy-backing on a RecordFailure call, and drops entries older than
	/// two hours whose lockout (if any) has elapsed.
	/// </summary>
	public static class LoginRateLimiter
	{
		private static ConcurrentDictionary<string, RateLimitEntry> s_entries = new ConcurrentDictionary<string, RateLimitEntry>();

		private const int MaxFailures = 5;
		private static readonly TimeSpan s_windowDuration = TimeSpan.FromMinutes(10);
		private static readonly TimeSpan s_lockoutDuration = TimeSpan.FromHours(1);
		private static readonly TimeSpan s_pruneInterval = TimeSpan.FromMinutes(30);
		private static DateTime s_lastPrune = DateTime.UtcNow;

		/// <summary>
		/// Drops stale entries from s_entries at most once per
		/// s_pruneInterval. "Stale" = window opened more than two hours ago
		/// AND any lockout has already lifted. Snapshots the key set first
		/// so the concurrent dictionary is not mutated mid-enumeration.
		/// </summary>
		private static void PruneIfDue(DateTime now)
		{
			if (now - s_lastPrune < s_pruneInterval)
			{
				return;
			}
			s_lastPrune = now;
			TimeSpan maxAge = TimeSpan.FromHours(2);
			List<string> keys = new List<string>(s_entries.Keys);
			int keyCount = keys.Count;
			for (int index = 0; index < keyCount; index++)
			{
				string key = keys[index];
				RateLimitEntry entry;
				bool found = s_entries.TryGetValue(key, out entry);
				if (!found)
				{
					continue;
				}
				bool stale = now - entry.WindowStart > maxAge && entry.LockoutExpiry < now;
				if (stale)
				{
					RateLimitEntry removed;
					s_entries.TryRemove(key, out removed);
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
			RateLimitEntry entry;
			bool found = s_entries.TryGetValue(ipAddress, out entry);
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
		/// elapsed; arms the lockout when FailureCount crosses MaxFailures.
		/// Also opportunistically prunes stale entries.
		/// </summary>
		public static void RecordFailure(string ipAddress)
		{
			DateTime now = DateTime.UtcNow;
			RateLimitEntry entry;
			bool found = s_entries.TryGetValue(ipAddress, out entry);
			if (!found)
			{
				RateLimitEntry fresh = new RateLimitEntry();
				fresh.FailureCount = 0;
				fresh.WindowStart = now;
				fresh.LockoutExpiry = DateTime.MinValue;
				s_entries.TryAdd(ipAddress, fresh);
				// Re-fetch in case another thread won the TryAdd race -- we
				// must mutate the entry that actually lives in the dictionary.
				s_entries.TryGetValue(ipAddress, out entry);
			}
			// Roll the window forward if the prior one has elapsed.
			if (now - entry.WindowStart > s_windowDuration)
			{
				entry.FailureCount = 0;
				entry.WindowStart = now;
				entry.LockoutExpiry = DateTime.MinValue;
			}
			entry.FailureCount = entry.FailureCount + 1;
			if (entry.FailureCount >= MaxFailures)
			{
				entry.LockoutExpiry = now + s_lockoutDuration;
			}
			PruneIfDue(now);
		}

		/// <summary>
		/// Clears the IP's failure state on a successful auth so a legitimate
		/// user does not carry prior typos toward a future lockout.
		/// </summary>
		public static void RecordSuccess(string ipAddress)
		{
			RateLimitEntry removed;
			s_entries.TryRemove(ipAddress, out removed);
		}
	}
}
