using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Pulse.MusicLibrary;

namespace Pulse.Services
{
	/// <summary>
	/// P1 of the Pulse authentication epic (PLS129 / PLS132). Provides password
	/// login, logout, and password-set routes that mint an opaque session id
	/// in a cookie. Additive only: no existing route is gated, no existing
	/// behaviour is changed. The legacy `u=` query parameter is untouched and
	/// will be removed in a later phase.
	/// </summary>
	public class AuthEndpoints
	{
		// The name of the session cookie. HttpOnly + Secure + SameSite=Strict
		// are set everywhere the cookie is written; the id itself is opaque
		// (the cookie value is a 256-bit random token, looked up in
		// SessionStore on the server side).
		public const string CookieName = "pulse_session";

		// Minimum acceptable plaintext length for SetPassword. Kept low so the
		// bootstrap flow on a single-user deploy can pick something memorable;
		// the real defence is BCrypt's work factor of 12.
		private const int MinPasswordLength = 8;

		private MusicManager m_musicManager;

		// Lazily-constructed BCrypt hash run against incoming passwords when
		// the candidate user has no stored hash. Verifying against this dummy
		// keeps the response timing of "unknown user" indistinguishable from
		// "known user, wrong password" so login attempts cannot be used to
		// enumerate usernames. Computed once on first use because BCrypt at
		// work-factor 12 takes ~250 ms; doing it at startup would penalise
		// every cold launch even when no one logs in.
		private static string s_dummyHash = "";
		private static object s_dummyHashLock = new object();

		// Shared options for the small request payloads. IncludeFields = true
		// matches the project's serialization rule (the request DTOs use
		// public fields, not properties); PropertyNameCaseInsensitive lets the
		// web client send the natural lowercase JSON shape regardless of the
		// C# field casing.
		private static JsonSerializerOptions s_jsonOptions = BuildJsonOptions();

		public AuthEndpoints(MusicManager musicManager)
		{
			m_musicManager = musicManager;
		}

		public void RegisterRoutes(IPulseRouteHost host)
		{
			host.RegisterResultRoute("pulse/login", HandleLogin);
			host.RegisterResultRoute("pulse/logout", HandleLogout);
			host.RegisterResultRoute("pulse/setPassword", HandleSetPassword);
		}

		/// <summary>
		/// Reads the session cookie off the incoming request and resolves it
		/// against the in-memory SessionStore. Returns false on missing,
		/// unknown, or expired cookie. This is the hook future P-phases will
		/// use to gate the existing routes; P1 itself only consults it inside
		/// the new endpoints.
		/// </summary>
		public bool TryGetSessionUser(HttpContext context, out string userName, out bool isAdmin)
		{
			userName = "";
			isAdmin = false;
			string sessionId = context.Request.Cookies[CookieName];
			if (string.IsNullOrEmpty(sessionId))
			{
				return false;
			}
			return SessionStore.TryValidate(sessionId, out userName, out isAdmin);
		}

		// POST { username, password, rememberMe } -> 200 { username, isAdmin } on
		// success; 401 { error: "Invalid credentials" } on every failure mode
		// so the response cannot be used to enumerate users.
		private IResult HandleLogin(HttpContext context)
		{
			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return Json(StatusCodes.Status400BadRequest, new { error = "Missing body" });
			}

			LoginRequest request = SafeDeserialize<LoginRequest>(body);
			if (request == null || string.IsNullOrEmpty(request.username) || string.IsNullOrEmpty(request.password))
			{
				return Json(StatusCodes.Status401Unauthorized, new { error = "Invalid credentials" });
			}

			string storedHash = m_musicManager.GetUserPasswordHash(request.username);
			bool hasHash = !string.IsNullOrEmpty(storedHash);
			string hashToCheck = storedHash;
			if (!hasHash)
			{
				hashToCheck = GetDummyHash();
			}

			bool verified = false;
			try
			{
				verified = BCrypt.Net.BCrypt.Verify(request.password, hashToCheck);
			}
			catch (Exception)
			{
				verified = false;
			}

			if (!hasHash || !verified)
			{
				return Json(StatusCodes.Status401Unauthorized, new { error = "Invalid credentials" });
			}

			UserRecord user = m_musicManager.GetUser(request.username);
			bool isAdmin = false;
			if (user != null)
			{
				isAdmin = user.IsAdmin;
			}

			string sessionId = SessionStore.CreateSession(request.username, isAdmin, request.rememberMe);
			AppendSessionCookie(context, sessionId, request.rememberMe);

			Log.Info(-1, "Auth: login for '" + request.username + "'");
			return Json(StatusCodes.Status200OK, new { username = request.username, isAdmin = isAdmin });
		}

		// POST -> 200; idempotent. Drops the session in the store and clears
		// the cookie. Body is ignored.
		private IResult HandleLogout(HttpContext context)
		{
			string sessionId = context.Request.Cookies[CookieName];
			if (!string.IsNullOrEmpty(sessionId))
			{
				SessionStore.Remove(sessionId);
			}

			CookieOptions clearOptions = new CookieOptions();
			clearOptions.HttpOnly = true;
			clearOptions.Secure = true;
			clearOptions.SameSite = SameSiteMode.Strict;
			clearOptions.Path = "/";
			clearOptions.MaxAge = TimeSpan.Zero;
			context.Response.Cookies.Append(CookieName, "", clearOptions);

			return Json(StatusCodes.Status200OK, new { ok = true });
		}

		// POST { username, password } -> 200 on success, 4xx with an opaque
		// error otherwise. Authorisation rule:
		//   - first password on a fresh system (no user has a hash yet) is
		//     allowed without a session, so the first admin can self-bootstrap;
		//   - otherwise the caller must hold a valid session, and may only
		//     target themselves unless they are an admin.
		private IResult HandleSetPassword(HttpContext context)
		{
			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return Json(StatusCodes.Status400BadRequest, new { error = "Missing body" });
			}

			SetPasswordRequest request = SafeDeserialize<SetPasswordRequest>(body);
			if (request == null || string.IsNullOrEmpty(request.username) || string.IsNullOrEmpty(request.password))
			{
				return Json(StatusCodes.Status400BadRequest, new { error = "Missing username or password" });
			}
			if (request.password.Length < MinPasswordLength)
			{
				return Json(StatusCodes.Status400BadRequest, new { error = "Password too short" });
			}

			bool systemInitialized = m_musicManager.AnyUserHasPassword();
			if (systemInitialized)
			{
				string sessionUser;
				bool sessionIsAdmin;
				bool sessionValid = TryGetSessionUser(context, out sessionUser, out sessionIsAdmin);
				if (!sessionValid)
				{
					return Json(StatusCodes.Status401Unauthorized, new { error = "Not signed in" });
				}
				bool isSelf = string.Equals(sessionUser, request.username, StringComparison.Ordinal);
				if (!sessionIsAdmin && !isSelf)
				{
					return Json(StatusCodes.Status403Forbidden, new { error = "Forbidden" });
				}
			}

			UserRecord target = m_musicManager.GetUser(request.username);
			if (target == null)
			{
				return Json(StatusCodes.Status404NotFound, new { error = "Unknown user" });
			}

			string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.password, 12);
			m_musicManager.SetUserPassword(request.username, passwordHash);

			Log.Info(-1, "Auth: password set for '" + request.username + "'");
			return Json(StatusCodes.Status200OK, new { ok = true });
		}

		private static string GetDummyHash()
		{
			string current = s_dummyHash;
			if (!string.IsNullOrEmpty(current))
			{
				return current;
			}
			lock (s_dummyHashLock)
			{
				if (string.IsNullOrEmpty(s_dummyHash))
				{
					// Hash a random 32-byte token at the same work factor real
					// passwords use. The plaintext is discarded -- we only need
					// the resulting hash as a constant-time sink.
					byte[] random = RandomNumberGenerator.GetBytes(32);
					string scratchPlaintext = Convert.ToBase64String(random);
					s_dummyHash = BCrypt.Net.BCrypt.HashPassword(scratchPlaintext, 12);
				}
				return s_dummyHash;
			}
		}

		private static void AppendSessionCookie(HttpContext context, string sessionId, bool rememberMe)
		{
			CookieOptions cookieOptions = new CookieOptions();
			cookieOptions.HttpOnly = true;
			cookieOptions.Secure = true;
			cookieOptions.SameSite = SameSiteMode.Strict;
			cookieOptions.Path = "/";
			if (rememberMe)
			{
				cookieOptions.MaxAge = TimeSpan.FromDays(30);
			}
			context.Response.Cookies.Append(CookieName, sessionId, cookieOptions);
		}

		private static string ReadRequestBody(HttpContext context)
		{
			using (StreamReader reader = new StreamReader(context.Request.Body))
			{
				return reader.ReadToEnd();
			}
		}

		private static T SafeDeserialize<T>(string body) where T : class
		{
			try
			{
				return JsonSerializer.Deserialize<T>(body, s_jsonOptions);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static IResult Json(int statusCode, object body)
		{
			string serialized = JsonSerializer.Serialize(body);
			return Results.Content(serialized, "application/json", null, statusCode);
		}

		private static JsonSerializerOptions BuildJsonOptions()
		{
			JsonSerializerOptions options = new JsonSerializerOptions();
			options.IncludeFields = true;
			options.PropertyNameCaseInsensitive = true;
			return options;
		}

		private class LoginRequest
		{
			public string username = "";
			public string password = "";
			public bool rememberMe = false;
		}

		private class SetPasswordRequest
		{
			public string username = "";
			public string password = "";
		}
	}
}
