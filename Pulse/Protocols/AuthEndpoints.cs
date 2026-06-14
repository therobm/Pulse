using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Pulse.Data;
using Pulse.Database;
using Pulse.DataStorage;
using Pulse.MusicLibrary;
using Pulse.Services;
using PulseAPI.CSharp;

namespace Pulse.Protocols
{

	public class AuthEndpoints
	{
	
		public const string CookieName = "pulse_session";

	
		private const int MinPasswordLength = 0;


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
			RegisterRoute("whoami", WhoAmI);
			RegisterRoute("setupAdmin", SetupAdmin);

			RegisterRoute("listUsers", ListUsers);
			RegisterRoute("createUser", CreateUser);
			RegisterRoute("updateUser", UpdateUser);
			RegisterRoute("deleteUser", DeleteUser);

			RegisterRoute("createToken", CreateToken);
		}

		private void RegisterRoute(string route, Func<HttpContext, IResult> handler)
		{
			m_host.RegisterResultRoute(m_apiSpace + route, handler);
		}

		public bool GetSessionUserId(HttpContext context, out string userId)
		{
			userId = "";
			string sessionId = context.Request.Cookies[CookieName];
			if (string.IsNullOrEmpty(sessionId))
			{
				return false;
			}
			return m_sessions.GetUserIdForSession(sessionId, out userId);
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
			User user = m_pulseData.LookupUserByName(request.Username);
			if (user == null)
			{
				return Respond("invalid_user", HttpStatusCode.OK);
			}

			string storedHash = m_pulseData.GetUserPasswordHash(user.Id);
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

		

			m_rateLimiter.RecordSuccess(clientIp);

			string sessionId = m_sessions.CreateSession(user.Id, request.RememberMe);
			AppendSessionCookie(context, sessionId, request.RememberMe);

			Log.Info("Auth: login for '" + request.Username + "'");

			PulseLoginResult result = new PulseLoginResult();
			result.Id = user.Id;
			result.Username = user.Name;
			result.IsAdmin = user.IsAdmin;

			return Respond(result);
		}

		private IResult WhoAmI(HttpContext context)
		{
			if (!m_pulseData.AnyUserHasPassword())
			{
				return Respond("needs_setup", HttpStatusCode.OK);
			}

			string userId;
			bool valid = GetSessionUserId(context, out userId);
			if (valid)
			{
				User user = m_pulseData.GetUser(userId);
				if (user == null)
				{

					return Respond("invalid_user", HttpStatusCode.OK);
				}
				PulseLoginResult result = new PulseLoginResult();
				result.Id = user.Id;
				result.Username = user.Name;
				result.IsAdmin = user.IsAdmin;
				return Respond(result);
			}

			return Respond("not_signed_in", HttpStatusCode.OK);
		}

		private IResult SetupAdmin(HttpContext context)
		{
			string body = ReadRequestBody(context);
			if (string.IsNullOrEmpty(body))
			{
				return Respond("missing_body", HttpStatusCode.BadRequest);
			}

			PulseSetupAdminRequest request;
			try
			{
				request = PulseWire.Parse<PulseSetupAdminRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Username) || request.Password == null)
			{
				return Respond("missing_fields", HttpStatusCode.BadRequest);
			}

			if (m_pulseData.AnyUserHasPassword())
			{
				return Respond("already_initialized", HttpStatusCode.OK);
			}

			string displayName = request.DisplayName;
			if (string.IsNullOrEmpty(displayName))
			{
				displayName = request.Username;
			}

			User currentUser = m_pulseData.CreateUser(request.Username, displayName, true, out string error);
			if (currentUser == null)
			{
				return Respond(error, HttpStatusCode.OK);
			}

			string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
			m_pulseData.SetUserPassword(currentUser.Id, passwordHash);

			string sessionId = m_sessions.CreateSession(currentUser.Id, false);
			AppendSessionCookie(context, sessionId, false);

			Log.Info("Auth: admin account set up for '" + currentUser.Name + "'");

			PulseLoginResult result = new PulseLoginResult();
			result.Id = currentUser.Id;
			result.Username = currentUser.Name;
			result.IsAdmin = currentUser.IsAdmin;
			return Respond(result);
		}

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

			if (request == null || string.IsNullOrEmpty(request.Id))
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

			User targetUser = m_pulseData.GetUser(request.Id);
			if (targetUser == null)
			{
				return Respond("user_not_found", HttpStatusCode.OK);
			}

			User sessionUser = null;
			bool systemInitialized = m_pulseData.AnyUserHasPassword();
			if (systemInitialized)
			{
				string userId;
				bool sessionValid = GetSessionUserId(context, out userId);
				if (!sessionValid)
				{
					return Respond("not_signed_in", HttpStatusCode.OK);
				}

				sessionUser = m_pulseData.GetUser(userId);

				bool isSelf = targetUser.Id == sessionUser.Id;

				if (sessionUser == null)
				{
					return Respond("unknown_session_user", HttpStatusCode.OK);
				}
				if (!sessionUser.IsAdmin && !isSelf)
				{
					return Respond("forbidden", HttpStatusCode.OK);
				}
			}
			

			string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
			m_pulseData.SetUserPassword(targetUser.Id, passwordHash);

			Log.Info("Auth: password set for '" + sessionUser.Name + "'");
			return Respond("ok", HttpStatusCode.OK);
		}

		public IResult ListUsers(HttpContext context)
		{
			List<User> users = m_pulseData.GetAllUsers();
			List<PulseUser> userList = new List<PulseUser>();
			for (int index = 0; index < users.Count; index++)
			{
				User user = users[index];
				
				PulseUser pulseUser = new PulseUser();
				pulseUser.Id = user.Id;
				pulseUser.Name = user.Name;
				pulseUser.DisplayName = user.DisplayName;
				pulseUser.IsAdmin = user.IsAdmin;

				userList.Add(pulseUser);
			}

			return RespondList(userList);
		}


		public IResult CreateUser(HttpContext context)
		{
			string name = QueryParameters.GetString(context, "name");
			string displayName = QueryParameters.GetString(context, "displayName");
			bool isAdmin = QueryParameters.GetBool(context, "isAdmin");

			User user = m_pulseData.CreateUser(name, displayName, isAdmin, out string error);
			if (!string.IsNullOrEmpty(error))
			{
				return Respond(error, HttpStatusCode.OK);
			}
			Log.Info("Settings: created user '" + name + "'");
			return Respond(HttpStatusCode.OK);
		}

		public IResult UpdateUser(HttpContext context)
		{
			string userId = QueryParameters.GetString(context, "userId");
			string newName = QueryParameters.GetString(context, "newName");
			string displayName = QueryParameters.GetString(context, "displayName");

			if (string.IsNullOrEmpty(newName))
			{
				return Respond("invalid new name", HttpStatusCode.OK);
			}

			string error = m_pulseData.UpdateUser(userId, newName, displayName);
			if (!string.IsNullOrEmpty(error))
			{
				return Respond(error, HttpStatusCode.OK);
			}
			Log.Info("Settings: updated user '" + userId + "' (now '" + newName + "')");
			return Respond(HttpStatusCode.OK);
		}


		public IResult DeleteUser(HttpContext context)
		{
			string userId = QueryParameters.GetString(context, "useId");
			if (string.IsNullOrEmpty(userId))
			{
				return Respond("Missing user", HttpStatusCode.OK);
			}
			m_pulseData.DeleteUser(userId);
			Log.Info("Settings: deleted user '" + userId + "'");
			return Respond(HttpStatusCode.OK);
		}

	
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

			if (request == null || string.IsNullOrEmpty(request.Id))
			{
				return Respond("missing_fields", HttpStatusCode.BadRequest);
			}

			string token = m_pulseData.CreateToken(request.Id, request.Label);
			if (string.IsNullOrEmpty(token))
			{
				m_rateLimiter.RecordFailure(clientIp);
				return Respond("unknown_user", HttpStatusCode.OK);
			}

			m_rateLimiter.RecordSuccess(clientIp);

			Log.Info("Auth: created token for '" + request.Id + "' label='" + request.Label + "'");

			User user = m_pulseData.GetUser(request.Id);

			PulseToken result = new PulseToken();
			result.Token = token;
			result.Id = user.Id;
			result.Username = user.Name;
			result.Label = request.Label;
			result.CreatedAt = DateTime.UtcNow.ToString("o");

			return Respond(result);
		}

		
		private static string GetClientIp(HttpContext context)
		{
			if (context.Connection.RemoteIpAddress != null)
			{
				return context.Connection.RemoteIpAddress.ToString();
			}
			return "unknown";
		}

	
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

		private string ReadRequestBody(HttpContext context)
		{
			using (StreamReader reader = new StreamReader(context.Request.Body))
			{
				return reader.ReadToEnd();
			}
		}

	
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
