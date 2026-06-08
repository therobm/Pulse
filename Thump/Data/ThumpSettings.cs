using Microsoft.Maui.Storage;
using Thump.Playback;
using Thump.Pulse;

namespace Thump.Data
{
	public enum eNormalizeVolume
	{
		Off,
		PerTrack,
		PerAlbum,
	}

	public static class ThumpSettings
	{
		private const string s_keyNormalize = "thump.playback.normalize";
		private const string s_keyShuffle = "thump.playback.shuffle";
		private const string s_keyRepeat = "thump.playback.repeat";
		private const string s_keyPrefetchCount = "thump.cache.prefetch";
		private const string s_keyCacheLimitBytes = "thump.cache.limitBytes";
		private const string s_keyServerIp = "thump.login.ip";
		private const string s_keyServerPort = "thump.login.port";
		private const string s_keyUsername = "thump.login.username";
		private const string s_keyPassword = "thump.login.password";
		private const string s_keyUseHttps = "thump.login.useHttps";
		private const string s_keyToken = "thump.login.token";
		private const string s_keyAnalyticsEnabled = "thump.analytics.enabled";
		private const string s_keyDeviceId = "thump.analytics.deviceId";

		public static eNormalizeVolume GetNormalizeVolume()
		{
			int stored = Preferences.Get(s_keyNormalize, (int)eNormalizeVolume.Off);
			return (eNormalizeVolume)stored;
		}
		public static void SetNormalizeVolume(eNormalizeVolume value)
		{
			Preferences.Set(s_keyNormalize, (int)value);
		}

		public static bool GetShuffleEnabled()
		{
			return Preferences.Get(s_keyShuffle, false);
		}
		public static void SetShuffleEnabled(bool value)
		{
			Preferences.Set(s_keyShuffle, value);
		}

		public static eRepeatMode GetRepeatMode()
		{
			int stored = Preferences.Get(s_keyRepeat, (int)eRepeatMode.Off);
			return (eRepeatMode)stored;
		}
		public static void SetRepeatMode(eRepeatMode value)
		{
			Preferences.Set(s_keyRepeat, (int)value);
		}

		public static int GetPrefetchCount()
		{
			return Preferences.Get(s_keyPrefetchCount, 10);
		}
		public static void SetPrefetchCount(int value)
		{
			Preferences.Set(s_keyPrefetchCount, value);
		}

		public static long GetCacheLimitBytes()
		{
			return Preferences.Get(s_keyCacheLimitBytes, 500L * 1024L * 1024L);
		}
		public static void SetCacheLimitBytes(long value)
		{
			Preferences.Set(s_keyCacheLimitBytes, value);
		}

		public static string GetServerIp()
		{
			return Preferences.Get(s_keyServerIp, "192.168.5.5");
		}
		public static void SetServerIp(string value)
		{
			Preferences.Set(s_keyServerIp, value);
		}

		public static string GetServerPort()
		{
			return Preferences.Get(s_keyServerPort, "32458");
		}
		public static void SetServerPort(string value)
		{
			Preferences.Set(s_keyServerPort, value);
		}

		public static string GetUsername()
		{
			return Preferences.Get(s_keyUsername, "Rob");
		}
		public static void SetUsername(string value)
		{
			Preferences.Set(s_keyUsername, value);
		}

		public static string GetPassword()
		{
			string secureValue = SecureStorage.Default.GetAsync(s_keyPassword).GetAwaiter().GetResult();
			if (!string.IsNullOrEmpty(secureValue))
			{
				return secureValue;
			}
			string legacyValue = Preferences.Get(s_keyPassword, "");
			if (!string.IsNullOrEmpty(legacyValue))
			{
				SecureStorage.Default.SetAsync(s_keyPassword, legacyValue).GetAwaiter().GetResult();
				Preferences.Remove(s_keyPassword);
				return legacyValue;
			}
			return "";
		}
		public static void SetPassword(string value)
		{
			SecureStorage.Default.SetAsync(s_keyPassword, value).GetAwaiter().GetResult();
		}

		public static bool GetUseHttps()
		{
			return Preferences.Get(s_keyUseHttps, true);
		}
		public static void SetUseHttps(bool value)
		{
			Preferences.Set(s_keyUseHttps, value);
		}

		/// <summary>
		/// Device token for Pulse API authentication. Stored in SecureStorage
		/// so it stays encrypted at rest. Returns "" if no token is configured.
		/// </summary>
		public static string GetToken()
		{
			string value = SecureStorage.Default.GetAsync(s_keyToken).GetAwaiter().GetResult();
			if (!string.IsNullOrEmpty(value))
			{
				return value;
			}
			return "";
		}
		public static void SetToken(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				SecureStorage.Default.Remove(s_keyToken);
				return;
			}
			SecureStorage.Default.SetAsync(s_keyToken, value).GetAwaiter().GetResult();
		}

		/// <summary>
		/// Whether the product-analytics pipeline ships usage events to the
		/// server. Opt-in: defaults to false so a fresh install does not POST
		/// analytics until the user enables it.
		/// </summary>
		public static bool GetAnalyticsEnabled()
		{
			return Preferences.Get(s_keyAnalyticsEnabled, false);
		}
		/// <summary>Persist the analytics opt-in toggle.</summary>
		public static void SetAnalyticsEnabled(bool value)
		{
			Preferences.Set(s_keyAnalyticsEnabled, value);
		}

		/// <summary>
		/// Returns a stable per-install identifier for analytics batches.
		/// Generates a new Guid on first call and persists it so subsequent
		/// launches reuse the same value.
		/// </summary>
		public static string GetOrCreateDeviceId()
		{
			string stored = Preferences.Get(s_keyDeviceId, "");
			if (!string.IsNullOrEmpty(stored))
			{
				return stored;
			}
			string created = System.Guid.NewGuid().ToString();
			Preferences.Set(s_keyDeviceId, created);
			return created;
		}
	}
}
