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

using Pulse.DataStorage;
using Pulse.MusicLibrary;
using Pulse.Services;
using PulseAPI.CSharp;

namespace Pulse.Protocols
{

	public class AuthEndpoints
	{

		private const int MinPasswordLength = 0;


		private static string s_dummyHash = "";
		private static object s_dummyHashLock = new object();

		private PulseData m_pulseData;
		private LoginRateLimiter m_rateLimiter = new LoginRateLimiter();
		IPulseRouteHost m_host;

		/// <summary>
		/// Intentionally changed from /pulse to add versioning support
		/// </summary>
		string m_apiSpace = "pulse_v1/";


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

		}

		private void RegisterRoute(string route, Func<HttpContext, IResult> handler)
		{
			m_host.RegisterResultRoute(m_apiSpace + route, handler);
		}

		// The caller's identity is the uid it claims, proven by a device token
		// bound to that uid. This is the single identity model -- web and device
		// clients alike send uid + token; there is no separate browser session.
		private bool GetAuthenticatedUserId(HttpContext context, out string userId)
		{
			userId = "";
			string claimedUserId = QueryParameters.GetString(context, "uid", "");
			string token = QueryParameters.GetString(context, "token", "");
			if (!m_pulseData.IsTokenAuthorized(claimedUserId, token))
			{
				return false;
			}
			userId = claimedUserId;
			return true;
		}

		private bool IsAdminCaller(HttpContext context)
		{
			string callerUserId;
			bool callerValid = GetAuthenticatedUserId(context, out callerUserId);
			if (!callerValid)
			{
				return false;
			}
			User callerUser = m_pulseData.GetUser(callerUserId);
			if (callerUser == null)
			{
				return false;
			}
			return callerUser.IsAdmin;
		}

		private IResult Login(HttpContext context)
		{
			string clientIp = GetClientIp(context);
			if (m_rateLimiter.IsLockedOut(clientIp))
			{
				context.Response.Headers["Retry-After"] = "300";
				return RespondLogin(eAuthOutcome.RateLimited, null);
			}

			string body = ReadRequestBody(context);

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
				return RespondLogin(eAuthOutcome.InvalidCredentials, null);
			}

			// Always run a bcrypt compare -- against the real hash when the user
			// exists, against a throwaway hash when it does not -- so an unknown
			// username is indistinguishable from a wrong password in both the
			// response and the timing.
			User user = m_pulseData.LookupUserByName(request.Username);
			string hashToCheck;
			if (user == null)
			{
				hashToCheck = GetDummyHash();
			}
			else
			{
				hashToCheck = m_pulseData.GetUserPasswordHash(user.Id);
				if (string.IsNullOrEmpty(hashToCheck))
				{
					hashToCheck = GetDummyHash();
				}
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

			if (user == null || !verified)
			{
				m_rateLimiter.RecordFailure(clientIp);
				return RespondLogin(eAuthOutcome.InvalidCredentials, null);
			}

			m_rateLimiter.RecordSuccess(clientIp);

			Log.Info("Auth: login for '" + request.Username + "'");

			return RespondLogin(eAuthOutcome.Ok, user);
		}

		private IResult WhoAmI(HttpContext context)
		{
			if (!m_pulseData.AnyUserHasPassword())
			{
				return RespondAuthState(eAuthState.NeedsSetup, null);
			}

			string userId;
			bool valid = GetAuthenticatedUserId(context, out userId);
			if (!valid)
			{
				return RespondAuthState(eAuthState.NotSignedIn, null);
			}

			User user = m_pulseData.GetUser(userId);
			if (user == null)
			{
				return RespondAuthState(eAuthState.NotSignedIn, null);
			}

			return RespondAuthState(eAuthState.SignedIn, user);
		}

		private IResult SetupAdmin(HttpContext context)
		{
			string body = ReadRequestBody(context);

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
				return RespondLogin(eAuthOutcome.Failed, null);
			}

			if (m_pulseData.AnyUserHasPassword())
			{
				return RespondLogin(eAuthOutcome.AlreadyInitialized, null);
			}

			string displayName = request.DisplayName;
			if (string.IsNullOrEmpty(displayName))
			{
				displayName = request.Username;
			}

			string createError;
			User currentUser = m_pulseData.CreateUser(request.Username, displayName, true, out createError);
			if (currentUser == null)
			{
				return RespondLogin(eAuthOutcome.Failed, null);
			}

			string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
			m_pulseData.SetUserPassword(currentUser.Id, passwordHash);

			Log.Info("Auth: admin account set up for '" + currentUser.Name + "'");

			return RespondLogin(eAuthOutcome.Ok, currentUser);
		}

		private IResult Logout(HttpContext context)
		{
			return Respond("ok", HttpStatusCode.OK);
		}

		private IResult SetPassword(HttpContext context)
		{
			string body = ReadRequestBody(context);

			PulseSetPasswordRequest request;
			try
			{
				request = PulseWire.Parse<PulseSetPasswordRequest>(body);
			}
			catch (Exception)
			{
				request = null;
			}

			if (request == null || string.IsNullOrEmpty(request.Id) || request.Password == null)
			{
				return RespondSetPassword(eAuthOutcome.Failed);
			}
			if (request.Password.Length < MinPasswordLength)
			{
				return RespondSetPassword(eAuthOutcome.PasswordTooShort);
			}

			User targetUser = m_pulseData.GetUser(request.Id);
			if (targetUser == null)
			{
				return RespondSetPassword(eAuthOutcome.UnknownUser);
			}

			bool systemInitialized = m_pulseData.AnyUserHasPassword();
			if (systemInitialized)
			{
				string callerUserId;
				bool callerValid = GetAuthenticatedUserId(context, out callerUserId);
				if (!callerValid)
				{
					return RespondSetPassword(eAuthOutcome.NotSignedIn);
				}

				User callerUser = m_pulseData.GetUser(callerUserId);
				if (callerUser == null)
				{
					return RespondSetPassword(eAuthOutcome.NotSignedIn);
				}

				bool isSelf = targetUser.Id == callerUser.Id;
				if (!callerUser.IsAdmin && !isSelf)
				{
					return RespondSetPassword(eAuthOutcome.Forbidden);
				}
			}

			string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
			m_pulseData.SetUserPassword(targetUser.Id, passwordHash);

			Log.Info("Auth: password set for '" + targetUser.Name + "'");
			return RespondSetPassword(eAuthOutcome.Ok);
		}

		public IResult ListUsers(HttpContext context)
		{
			if (!IsAdminCaller(context))
			{
				return Respond("forbidden", HttpStatusCode.Forbidden);
			}

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
			if (!IsAdminCaller(context))
			{
				return Respond("forbidden", HttpStatusCode.Forbidden);
			}

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
			if (!IsAdminCaller(context))
			{
				return Respond("forbidden", HttpStatusCode.Forbidden);
			}

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
			if (!IsAdminCaller(context))
			{
				return Respond("forbidden", HttpStatusCode.Forbidden);
			}

			string userId = QueryParameters.GetString(context, "userId");
			if (string.IsNullOrEmpty(userId))
			{
				return Respond("Missing user", HttpStatusCode.OK);
			}
			m_pulseData.DeleteUser(userId);
			Log.Info("Settings: deleted user '" + userId + "'");
			return Respond(HttpStatusCode.OK);
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

		// On any outcome other than Ok the identity fields stay empty -- a failed
		// login must not leak whether the user exists.
		private IResult RespondLogin(eAuthOutcome outcome, User user)
		{
			PulseLoginResult result = new PulseLoginResult();
			result.Outcome = outcome;
			if (user != null)
			{
				result.Id = user.Id;
				result.Username = user.Name;
				result.IsAdmin = user.IsAdmin;
				result.Token = m_pulseData.CreateToken(user.Id);
			}
			return Respond(result);
		}

		private IResult RespondAuthState(eAuthState state, User user)
		{
			PulseAuthState result = new PulseAuthState();
			result.State = state;
			if (user != null)
			{
				result.Id = user.Id;
				result.Username = user.Name;
				result.IsAdmin = user.IsAdmin;
			}
			return Respond(result);
		}

		private IResult RespondSetPassword(eAuthOutcome outcome)
		{
			PulseSetPasswordResult result = new PulseSetPasswordResult();
			result.Outcome = outcome;
			return Respond(result);
		}

		private IResult RespondCreateToken(eAuthOutcome outcome, PulseToken token)
		{
			PulseCreateTokenResult result = new PulseCreateTokenResult();
			result.Outcome = outcome;
			result.Token = token;
			return Respond(result);
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
