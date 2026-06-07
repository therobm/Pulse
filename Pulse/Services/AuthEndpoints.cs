using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Pulse.Data;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;

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
		/// <summary>
		/// The name of the session cookie. HttpOnly + Secure + SameSite=Strict
		/// are set everywhere the cookie is written; the id itself is opaque
		/// (the cookie value is a 256-bit random token, looked up in
		/// SessionStore on the server side).
		/// </summary>
		public const string CookieName = "pulse_session";

		/// <summary>
		/// Minimum acceptable plaintext length for SetPassword. The knob is
		/// kept so the policy can be tightened later without touching the
		/// client, but at 0 the check never trips -- empty passwords are
		/// permitted. The real defence is BCrypt's work factor of 12.
		/// </summary>
		private const int MinPasswordLength = 0;

		/// <summary>
		/// Lazily-constructed BCrypt hash run against incoming passwords when
		/// the candidate user has no stored hash. Verifying against this dummy
		/// keeps the response timing of "unknown user" indistinguishable from
		/// "known user, wrong password" so login attempts cannot be used to
		/// enumerate usernames. Computed once on first use because BCrypt at
		/// work-factor 12 takes ~250 ms; doing it at startup would penalise
		/// every cold launch even when no one logs in.
		/// </summary>
		private static string s_dummyHash = "";
		private static object s_dummyHashLock = new object();

		private PulseData m_pulseData;

		/// <summary>
		/// Returns the cached anti-enumeration BCrypt hash, computing it on
		/// first call. The plaintext is discarded -- only the hash matters as
		/// a constant-time sink for failed-login verification.
		/// </summary>
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
					byte[] random = RandomNumberGenerator.GetBytes(32);
					string scratchPlaintext = Convert.ToBase64String(random);
					s_dummyHash = BCrypt.Net.BCrypt.HashPassword(scratchPlaintext, 12);
				}
				return s_dummyHash;
			}
		}

		/// <summary>
		/// Writes the session id into the response as an HttpOnly + Secure +
		/// SameSite=Strict cookie. RememberMe stretches the max-age to 30 days;
		/// otherwise the cookie has no max-age and lasts the browser session.
		/// </summary>
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

		/// <summary>
		/// Synchronously reads the request body as UTF-8. Auth bodies are tiny
		/// JSON envelopes and the host enables AllowSynchronousIO already.
		/// </summary>
		private static string ReadRequestBody(HttpContext context)
		{
			using (StreamReader reader = new StreamReader(context.Request.Body))
			{
				return reader.ReadToEnd();
			}
		}

		/// <summary>
		/// Writes a PulseResponse envelope through PulseWire and sets the HTTP
		/// status from response.statusCode so the wire status and the HTTP
		/// status agree. All auth responses go through this helper -- they
		/// never hand-serialize JSON or use Results.Json/Results.Content.
		/// </summary>
		private static IResult Respond(PulseResponse response)
		{
			string serialized = PulseWire.Serialize(response);
			return Results.Text(serialized, "application/json", Encoding.UTF8, response.statusCode);
		}

		/// <summary>
		/// Builds a status-only envelope (no contents) carrying the supplied
		/// machine-parsable status code and HTTP status. Used for every
		/// non-success path so the wire shape stays the same as the success
		/// shape -- just with status set to the failure code.
		/// </summary>
		private static IResult RespondStatus(string status, int httpStatusCode)
		{
			PulseResponse response = new PulseResponse();
			response.status = status;
			response.statusCode = httpStatusCode;
			return Respond(response);
		}

		public AuthEndpoints(PulseData pulseData)
		{
			m_pulseData = pulseData;
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

		/// <summary>
		/// POST { Username, Password, RememberMe } -> 200 with a LoginResult
		/// payload on success; a status-only envelope with status =
		/// "invalid_credentials" and HTTP 401 on every failure mode so the
		/// response cannot be used to enumerate users.
		/// </summary>
		private IResult HandleLogin(HttpContext context)
		{
			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return RespondStatus("missing_body", StatusCodes.Status400BadRequest);
			}

			LoginRequest request;
			try
			{
				request = PulseWire.Parse<LoginRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
			{
				return RespondStatus("invalid_credentials", StatusCodes.Status401Unauthorized);
			}

			string storedHash = m_pulseData.GetUserPasswordHash(request.Username);
			bool hasHash = !string.IsNullOrEmpty(storedHash);
			string hashToCheck = storedHash;
			if (!hasHash)
			{
				hashToCheck = GetDummyHash();
			}

			bool verified = false;
			try
			{
				verified = BCrypt.Net.BCrypt.Verify(request.Password, hashToCheck);
			}
			catch (Exception)
			{
				verified = false;
			}

			if (!hasHash || !verified)
			{
				return RespondStatus("invalid_credentials", StatusCodes.Status401Unauthorized);
			}

			UserRecord user = m_pulseData.GetUser(request.Username);
			bool isAdmin = false;
			if (user != null)
			{
				isAdmin = user.IsAdmin;
			}

			string sessionId = SessionStore.CreateSession(request.Username, isAdmin, request.RememberMe);
			AppendSessionCookie(context, sessionId, request.RememberMe);

			Log.Info(-1, "Auth: login for '" + request.Username + "'");

			LoginResult result = new LoginResult();
			result.Username = request.Username;
			result.IsAdmin = isAdmin;

			PulseResponse response = new PulseResponse();
			response.status = "ok";
			response.statusCode = StatusCodes.Status200OK;
			response.contentType = PulseResponse.ContentType.PulseObject;
			response.contents = result;
			return Respond(response);
		}

		/// <summary>
		/// POST -> 200 status="ok"; idempotent. Drops the session in the store
		/// and clears the cookie. Body is ignored.
		/// </summary>
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

			return RespondStatus("ok", StatusCodes.Status200OK);
		}

		/// <summary>
		/// POST { Username, Password } -> 200 status="ok" on success, status-
		/// only failure envelope otherwise. Authorisation rule:
		///   - first password on a fresh system (no user has a hash yet) is
		///     allowed without a session, so the first admin can self-bootstrap;
		///   - otherwise the caller must hold a valid session, and may only
		///     target themselves unless they are an admin.
		/// </summary>
		private IResult HandleSetPassword(HttpContext context)
		{
			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return RespondStatus("missing_body", StatusCodes.Status400BadRequest);
			}

			SetPasswordRequest request;
			try
			{
				request = PulseWire.Parse<SetPasswordRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Username))
			{
				return RespondStatus("missing_fields", StatusCodes.Status400BadRequest);
			}
			if (request.Password == null)
			{
				return RespondStatus("missing_fields", StatusCodes.Status400BadRequest);
			}
			if (request.Password.Length < MinPasswordLength)
			{
				return RespondStatus("password_too_short", StatusCodes.Status400BadRequest);
			}

			bool systemInitialized = m_pulseData.AnyUserHasPassword();
			if (systemInitialized)
			{
				string sessionUser;
				bool sessionIsAdmin;
				bool sessionValid = TryGetSessionUser(context, out sessionUser, out sessionIsAdmin);
				if (!sessionValid)
				{
					return RespondStatus("not_signed_in", StatusCodes.Status401Unauthorized);
				}
				bool isSelf = string.Equals(sessionUser, request.Username, StringComparison.Ordinal);
				if (!sessionIsAdmin && !isSelf)
				{
					return RespondStatus("forbidden", StatusCodes.Status403Forbidden);
				}
			}

			UserRecord target = m_pulseData.GetUser(request.Username);
			if (target == null)
			{
				return RespondStatus("unknown_user", StatusCodes.Status404NotFound);
			}

			string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
			m_pulseData.SetUserPassword(request.Username, passwordHash);

			Log.Info(-1, "Auth: password set for '" + request.Username + "'");
			return RespondStatus("ok", StatusCodes.Status200OK);
		}
	}
}
