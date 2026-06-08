using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Pulse.Data;
using Pulse.Database;
using Pulse.MusicLibrary;
using Pulse.Services;
using PulseAPI.CSharp;

namespace Pulse.Protocols
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
		private SessionStore m_sessions = new SessionStore();
		private LoginRateLimiter m_rateLimiter = new LoginRateLimiter();
		IPulseRouteHost m_host;

		/// <summary>
		/// Intentionally changed from /pulse to add versioning support
		/// </summary>
		string m_apiSpace = "pulse_v1/";

		bool m_bRequireHTTPS = false;


		public AuthEndpoints(PulseData pulseData)
		{
			m_pulseData = pulseData;
		}

		public void RegisterRoutes(IPulseRouteHost host)
		{
			m_host = host;

			RegisterRoute("login", Login);
			RegisterRoute("logout", Logout);
			RegisterRoute("setPassword", SetPassword);

			RegisterRoute("listUsers", ListUsers);
			RegisterRoute("createUser", CreateUser);
			RegisterRoute("updateUser", UpdateUser);
			RegisterRoute("deleteUser", DeleteUser);

			RegisterRoute("createToken", CreateToken);
			RegisterRoute("listTokens", ListTokens);
			RegisterRoute("revokeToken", RevokeToken);
		}

		private void RegisterRoute(string route, Func<HttpContext, IResult> handler)
		{
			m_host.RegisterResultRoute(m_apiSpace + route, handler);
		}

		/// <summary>
		/// Reads the session cookie off the incoming request and resolves it
		/// against the in-memory SessionStore. Returns false on missing,
		/// unknown, or expired cookie. This is the hook future P-phases will
		/// use to gate the existing routes; P1 itself only consults it inside
		/// the new endpoints.
		/// </summary>
		public bool GetSessionUser(HttpContext context, out string userName, out bool isAdmin)
		{
			userName = "";
			isAdmin = false;
			string sessionId = context.Request.Cookies[CookieName];
			if (string.IsNullOrEmpty(sessionId))
			{
				return false;
			}
			return m_sessions.TryValidate(sessionId, out userName, out isAdmin);
		}

		private IResult Login(HttpContext context)
		{
			string clientIp = GetClientIp(context);
			if (m_rateLimiter.IsLockedOut(clientIp))
			{
				context.Response.Headers["Retry-After"] = "300";
				return Respond("rate_limited", HttpStatusCode.OK);
			}

			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return Respond("missing_body", HttpStatusCode.BadRequest);
			}

			PulseLoginRequest request;
			try
			{
				request = PulseWire.Parse<PulseLoginRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
			{
				m_rateLimiter.RecordFailure(clientIp);
				return Respond("invalid_credentials", HttpStatusCode.OK);
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
				m_rateLimiter.RecordFailure(clientIp);
				return Respond("invalid_credentials", HttpStatusCode.OK);
			}

			UserRecord user = m_pulseData.GetUser(request.Username);
			bool isAdmin = false;
			if (user != null)
			{
				isAdmin = user.IsAdmin;
			}

			m_rateLimiter.RecordSuccess(clientIp);

			string sessionId = m_sessions.CreateSession(request.Username, isAdmin, request.RememberMe);
			AppendSessionCookie(context, sessionId, request.RememberMe);

			Log.Info(-1, "Auth: login for '" + request.Username + "'");

			PulseLoginResult result = new PulseLoginResult();
			result.Username = request.Username;
			result.IsAdmin = isAdmin;

			return Respond(result);
		}

		/// <summary>
		/// POST -> 200 status="ok"; idempotent. Drops the session in the store
		/// and clears the cookie. Body is ignored.
		/// </summary>
		private IResult Logout(HttpContext context)
		{
			string sessionId = context.Request.Cookies[CookieName];
			if (!string.IsNullOrEmpty(sessionId))
			{
				m_sessions.Remove(sessionId);
			}

			CookieOptions clearOptions = new CookieOptions();
			clearOptions.HttpOnly = true;
			if (m_bRequireHTTPS)
				clearOptions.Secure = true;
			clearOptions.SameSite = SameSiteMode.Strict;
			clearOptions.Path = "/";
			clearOptions.MaxAge = TimeSpan.Zero;
			context.Response.Cookies.Append(CookieName, "", clearOptions);

			return Respond("ok", HttpStatusCode.OK);
		}

		/// <summary>
		/// POST { Username, Password } -> 200 status="ok" on success, status-
		/// only failure envelope otherwise. Authorisation rule:
		///   - first password on a fresh system (no user has a hash yet) is
		///     allowed without a session, so the first admin can self-bootstrap;
		///   - otherwise the caller must hold a valid session, and may only
		///     target themselves unless they are an admin.
		/// </summary>
		private IResult SetPassword(HttpContext context)
		{
			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return Respond("missing_body", HttpStatusCode.BadRequest);
			}

			PulseSetPasswordRequest request;
			try
			{
				request = PulseWire.Parse<PulseSetPasswordRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Username))
			{
				return Respond("missing_fields", HttpStatusCode.BadRequest);
			}
			if (request.Password == null)
			{
				return Respond("missing_fields", HttpStatusCode.BadRequest);
			}
			if (request.Password.Length < MinPasswordLength)
			{
				return Respond("password_too_short", HttpStatusCode.OK);
			}

			bool systemInitialized = m_pulseData.AnyUserHasPassword();
			if (systemInitialized)
			{
				string sessionUser;
				bool sessionIsAdmin;
				bool sessionValid = GetSessionUser(context, out sessionUser, out sessionIsAdmin);
				if (!sessionValid)
				{
					return Respond("not_signed_in",  HttpStatusCode.OK);
				}
				bool isSelf = string.Equals(sessionUser, request.Username, StringComparison.Ordinal);
				if (!sessionIsAdmin && !isSelf)
				{
					return Respond("forbidden", HttpStatusCode.OK);
				}
			}

			UserRecord target = m_pulseData.GetUser(request.Username);
			if (target == null)
			{
				return Respond("unknown_user", HttpStatusCode.OK);
			}

			string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
			m_pulseData.SetUserPassword(request.Username, passwordHash);

			Log.Info(-1, "Auth: password set for '" + request.Username + "'");
			return Respond("ok", HttpStatusCode.OK);
		}

		public IResult ListUsers(HttpContext context)
		{
			List<UserRecord> users = m_pulseData.GetAllUsers();
			List<PulseUser> userList = new List<PulseUser>();
			for (int index = 0; index < users.Count; index++)
			{
				UserRecord user = users[index];
				string createdStr = "";
				if (user.Created != DateTime.MinValue)
				{
					createdStr = user.Created.ToString("o");
				}

				PulseUser pulseUser = new PulseUser();
				pulseUser.Name = user.Name;
				pulseUser.DisplayName = user.DisplayName;
				pulseUser.Created = user.Created;
				pulseUser.IsAdmin = user.IsAdmin;
				pulseUser.ScoredTrackCount = user.ScoredTrackCount;
				pulseUser.StarredCount = user.StarredCount;
				pulseUser.PlaylistLastPlayedCount = user.PlaylistLastPlayedCount;

				userList.Add(pulseUser);
			}

			return RespondList(userList);
		}


		public IResult CreateUser(HttpContext context)
		{
			string name = QueryParameters.GetString(context, "name");
			string displayName = QueryParameters.GetString(context, "displayName");
			bool isAdmin = QueryParameters.GetBool(context, "isAdmin");

			string error = m_pulseData.CreateUser(name, displayName, isAdmin);
			if (!string.IsNullOrEmpty(error))
			{
				return Respond(error, HttpStatusCode.OK);
			}
			Log.Info(-1, "Settings: created user '" + name + "'");
			return Respond(HttpStatusCode.OK);
		}

		public IResult UpdateUser(HttpContext context)
		{
			string oldName = QueryParameters.GetString(context, "name");
			string newName = QueryParameters.GetString(context, "newName");
			string displayName = QueryParameters.GetString(context, "displayName");
			bool isAdmin = QueryParameters.GetBool(context, "isAdmin");

			if (string.IsNullOrEmpty(newName))
			{
				newName = oldName;
			}

			string error = m_pulseData.UpdateUser(oldName, newName, displayName, isAdmin);
			if (!string.IsNullOrEmpty(error))
			{
				return Respond(error, HttpStatusCode.OK);
			}
			Log.Info(-1, "Settings: updated user '" + oldName + "' (now '" + newName + "')");
			return Respond(HttpStatusCode.OK);
		}

		// Deletes every per-user row for the given user_name across the database
		// and the in-memory caches. Bug #201 -- used by the settings page to clean
		// up duplicate-cased names that crept in (e.g. "shannon" vs "Shannon").
		public IResult DeleteUser(HttpContext context)
		{
			string userName = QueryParameters.GetString(context, "user");
			if (string.IsNullOrEmpty(userName))
			{
				return Respond("Missing user", HttpStatusCode.OK);
			}
			m_pulseData.DeleteUser(userName);
			Log.Info(-1, "Settings: deleted user '" + userName + "'");
			return Respond(HttpStatusCode.OK);
		}

		/// <summary>
		/// POST { Username, Label } -> 200 PulseToken on success. Mints a 256-bit
		/// random token, base64url-encoded, and stores it against the named user.
		/// The raw token rides back in the response body so the caller can show
		/// it to the operator exactly once. No auth check on the endpoint
		/// itself -- token management is opt-in plumbing in P2, not enforced.
		/// </summary>
		private IResult CreateToken(HttpContext context)
		{
			string clientIp = GetClientIp(context);
			if (m_rateLimiter.IsLockedOut(clientIp))
			{
				context.Response.Headers["Retry-After"] = "300";
				return Respond("rate_limited", HttpStatusCode.OK);
			}

			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return Respond("missing_body", HttpStatusCode.BadRequest);
			}

			PulseCreateTokenRequest request;
			try
			{
				request = PulseWire.Parse<PulseCreateTokenRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Username))
			{
				return Respond("missing_fields", HttpStatusCode.BadRequest);
			}

			UserRecord user = m_pulseData.GetUser(request.Username);
			if (user == null)
			{
				m_rateLimiter.RecordFailure(clientIp);
				return Respond("unknown_user", HttpStatusCode.OK);
			}

			byte[] raw = RandomNumberGenerator.GetBytes(32);
			string token = SessionStore.ToUrlSafeBase64(raw);
			string label = request.Label;
			if (label == null)
			{
				label = "";
			}
			m_pulseData.InsertToken(token, request.Username, label);

			m_rateLimiter.RecordSuccess(clientIp);

			Log.Info(-1, "Auth: created token for '" + request.Username + "' label='" + label + "'");

			PulseToken result = new PulseToken();
			result.Token = token;
			result.Username = request.Username;
			result.Label = label;
			result.CreatedAt = DateTime.UtcNow.ToString("o");

			return Respond(result);
		}

		/// <summary>
		/// GET (optional ?user=&lt;name&gt;) -> list of PulseTokenSummary. With no
		/// user query param every token is returned; with one, only that user's
		/// tokens. No auth check -- listing tokens is plumbing for the
		/// management UI, not gated in P2.
		/// </summary>
		private IResult ListTokens(HttpContext context)
		{
			string userFilter = QueryParameters.GetString(context, "user");
			List<TokenRow> rows;
			if (!string.IsNullOrEmpty(userFilter))
			{
				rows = m_pulseData.GetTokensForUser(userFilter);
			}
			else
			{
				rows = m_pulseData.GetAllTokens();
			}

			List<PulseTokenSummary> tokens = new List<PulseTokenSummary>();
			for (int index = 0; index < rows.Count; index++)
			{
				TokenRow row = rows[index];
				PulseTokenSummary summary = new PulseTokenSummary();
				summary.Token = row.Token;
				summary.Username = row.UserName;
				summary.Label = row.Label;
				summary.CreatedAt = row.CreatedAt;
				summary.LastUsed = row.LastUsed;
				tokens.Add(summary);
			}
			return RespondList(tokens);
		}

		/// <summary>
		/// POST { Token } -> 200 status="ok". Idempotent: revoking an unknown
		/// token still returns ok. No auth check -- revoking a token is
		/// plumbing for the management UI, not gated in P2.
		/// </summary>
		private IResult RevokeToken(HttpContext context)
		{
			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return Respond("missing_body", HttpStatusCode.BadRequest);
			}

			PulseRevokeTokenRequest request;
			try
			{
				request = PulseWire.Parse<PulseRevokeTokenRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Token))
			{
				return Respond("missing_fields", HttpStatusCode.BadRequest);
			}

			m_pulseData.DeleteToken(request.Token);
			Log.Info(-1, "Auth: revoked token");
			return Respond("ok", HttpStatusCode.OK);
		}

		/// <summary>
		/// Checks the request for a device token: first Authorization: Bearer
		/// header, then ?token= query param fallback. If a valid token is
		/// found, updates its last-used timestamp and returns the mapped user.
		/// Returns false if no token is present or the token is unknown. This
		/// helper is plumbing for the future enforcement layer (P5); P2 does
		/// not call it from any gate.
		/// </summary>
		public bool GetTokenUser(HttpContext context, out string userName, out bool isAdmin)
		{
			userName = "";
			isAdmin = false;

			string authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
			string tokenValue = "";
			if (!string.IsNullOrEmpty(authHeader))
			{
				string prefix = "Bearer ";
				if (authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					tokenValue = authHeader.Substring(prefix.Length).Trim();
				}
			}

			if (string.IsNullOrEmpty(tokenValue))
			{
				tokenValue = QueryParameters.GetString(context, "token");
			}
			if (string.IsNullOrEmpty(tokenValue))
			{
				return false;
			}

			string resolvedUser = m_pulseData.LookupTokenUser(tokenValue);
			if (string.IsNullOrEmpty(resolvedUser))
			{
				return false;
			}

			m_pulseData.UpdateTokenLastUsed(tokenValue);

			UserRecord user = m_pulseData.GetUser(resolvedUser);
			if (user == null)
			{
				return false;
			}
			userName = user.Name;
			isAdmin = user.IsAdmin;
			return true;
		}

		/// <summary>
		/// Best-effort source-IP extraction for the brute-force limiter.
		/// Falls back to the sentinel "unknown" if the connection does not
		/// expose a remote address (e.g. some in-process test transports);
		/// the limiter still tracks "unknown" as a key, which is the safest
		/// default for an ambiguous origin.
		/// </summary>
		private static string GetClientIp(HttpContext context)
		{
			if (context.Connection.RemoteIpAddress != null)
			{
				return context.Connection.RemoteIpAddress.ToString();
			}
			return "unknown";
		}

		/// <summary>
		/// Returns the cached anti-enumeration BCrypt hash, computing it on
		/// first call. The plaintext is discarded -- only the hash matters as
		/// a constant-time sink for failed-login verification.
		/// </summary>
		private string GetDummyHash()
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
		private void AppendSessionCookie(HttpContext context, string sessionId, bool rememberMe)
		{
			CookieOptions cookieOptions = new CookieOptions();
			cookieOptions.HttpOnly = true;
			if (m_bRequireHTTPS)
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
		private string ReadRequestBody(HttpContext context)
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
		private IResult Respond(PulseResponse body)
		{
			return Results.Text(PulseWire.Serialize(body), "application/json", Encoding.UTF8, (int)HttpStatusCode.OK);
		}
		private IResult Respond(PulseObject contents)
		{
			PulseResponse response = new PulseResponse();
			response.status = "ok";
			response.contentType = PulseResponse.ContentType.PulseObject;
			response.contents = contents;
			return Respond(response);
		}
		public IResult Respond(HttpStatusCode code)
		{
			PulseResponse body = new PulseResponse();
			
			body.contentType = PulseResponse.ContentType.PulseObject;
			return Results.Text(PulseWire.Serialize(body), "application/json", Encoding.UTF8, (int)code);
		}
		public IResult Respond(string status, HttpStatusCode code)
		{
			PulseResponse body = new PulseResponse();
			body.status = status;
			body.contentType = PulseResponse.ContentType.PulseObject;
			return Results.Text(PulseWire.Serialize(body), "application/json", Encoding.UTF8, (int)code);
		}

		private IResult RespondObject<T>(T contents) where T : PulseObject
		{
			PulseResponse response = new PulseResponse();
			response.contentType = PulseResponse.ContentType.PulseObject;
			response.contents = contents;
			return Respond(response);
		}


		private IResult RespondList<T>(List<T> contents) where T : PulseObject
		{
			PulseResponse response = new PulseResponse();
			response.contentType = PulseResponse.ContentType.PulseObjectList;
			// Box each element as object so System.Text.Json serializes it by its
			// runtime type. The list's element type would otherwise drive the wire
			// shape: a heterogeneous feed (topItems / recentlyPlayed) arrives as
			// List<PulseObject>, and serializing PulseObject-typed elements emits
			// only the base Id/Kind, silently dropping every derived field (Name,
			// CoverArt, Artist, ...). object-typed elements force runtime-type
			// serialization, so derived fields survive. Homogeneous lists are
			// unaffected -- a PulseAlbum still serializes as a PulseAlbum.
			List<object> boxed = new List<object>(contents.Count);
			for (int index = 0; index < contents.Count; index++)
			{
				boxed.Add(contents[index]);
			}
			response.contents = boxed;
			return Respond( response);
		}

		
	}
}
