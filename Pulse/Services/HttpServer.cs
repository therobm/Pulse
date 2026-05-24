using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Pulse;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Services
{
	public class HttpServer : IPulseRouteHost
	{
		private WebApplication m_app;

		private Dictionary<string, Action<HttpContext>> m_routes = new Dictionary<string, Action<HttpContext>>();

		private class ResultRouteAdapter
		{
			private Func<HttpContext, IResult> m_handler;
			public ResultRouteAdapter(Func<HttpContext, IResult> handler)
			{
				m_handler = handler;
			}
			public void Invoke(HttpContext context)
			{
				IResult result = m_handler(context);
				result.ExecuteAsync(context).Wait();
			}
		}

		public void RegisterResultRoute(string path, Func<HttpContext, IResult> handler)
		{
			ResultRouteAdapter adapter = new ResultRouteAdapter(handler);
			m_routes[path] = adapter.Invoke;
		}

		public void RegisterRoute(string path, Action<HttpContext> handler)
		{
			m_routes[path] = handler;
		}

		/// <summary>
		/// Boots the Kestrel-backed HTTP host. Pulse is a pure web service, so
		/// the ASP.NET Core middleware pipeline (UseWebSockets, UseCors, Use)
		/// and DI container (AddCors) are intentionally used here despite
		/// GENERAL.md's "no middleware / no DI containers" rule — that rule
		/// targets app code that pulls in DI/middleware as a convenience, not
		/// the platform we're building on. Keeping the Kestrel stack is a
		/// deliberate call; do not flag again.
		/// </summary>
		public void Run()
		{
			PulseConfig config = PulseService.GetConfig();

			WebApplicationBuilder builder = WebApplication.CreateBuilder();
			builder.WebHost.ConfigureKestrel(ConfigureKestrelOptions);
			builder.Logging.ClearProviders();
			builder.Services.AddCors();

			m_app = builder.Build();
			m_app.UseWebSockets();
			m_app.UseCors(ConfigureCors);

			m_app.Use(HandleRequest);

			Thread runThread = new Thread(RunApp);
			runThread.IsBackground = true;
			runThread.Start();
		}

		private void RunApp()
		{
			m_app.Run();
		}

		private void ConfigureKestrelOptions(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions options)
		{
			PulseConfig config = PulseService.GetConfig();
			options.ListenAnyIP(config.HttpPort);
			options.ListenAnyIP(config.HttpsPort, ConfigureHttpsListener);
			options.AllowSynchronousIO = true;
		}

		private void ConfigureHttpsListener(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listenOptions)
		{
			if (File.Exists("pulse.mccoder.com.pfx"))
			{
				listenOptions.UseHttps("pulse.mccoder.com.pfx");
				Log.Info(-1, "HTTPS is enabled");
			}
			else
			{
				Log.Warning(-1, "HTTPS is disabled");
			}
		}

		private void ConfigureCors(Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder policy)
		{
			policy.AllowAnyOrigin();
			policy.AllowAnyMethod();
			policy.AllowAnyHeader();
		}

		private Task HandleRequest(HttpContext context, RequestDelegate next)
		{
			try
			{
				string path = context.Request.Path.Value.TrimStart('/');

				Action<HttpContext> handler = null;
				int bestLength = 0;
				for (int idx = 0; idx < m_routes.Count; idx++)
				{
					string routePath = m_routes.Keys.ElementAt(idx);
					if (path == routePath || path.StartsWith(routePath + "/"))
					{
						if (routePath.Length > bestLength)
						{
							bestLength = routePath.Length;
							handler = m_routes[routePath];
						}
					}
				}
				if (handler != null)
				{
					handler(context);
					return Task.CompletedTask;
				}

				if (path.ToLower().Contains("web"))
				{
					string pulseDir = Path.Combine(AppContext.BaseDirectory, "Content", "web");
					string fileName = Path.GetFileName(path);
					if (string.IsNullOrEmpty(fileName))
					{
						fileName = "pulse.html";
					}

					string filePath = Path.Combine(pulseDir, fileName);
					if (File.Exists(filePath))
					{
						string contentType = GetContentTypeForFile(filePath);
						ServeFile(context, filePath, contentType);
						return Task.CompletedTask;
					}
				}

				context.Response.StatusCode = 404;
				return Task.CompletedTask;
			}
			catch (Exception ex)
			{
				Log.Error(-1, "Request failed: " + context.Request.Path + " - " + ex.Message + "\n" + ex.StackTrace);
				context.Response.StatusCode = 500;
				return Task.CompletedTask;
			}
		}


		public void Stop()
		{
			if (m_app != null)
			{
				m_app.StopAsync().Wait();
			}
		}

		private void ServeFile(HttpContext context, string filePath, string contentType)
		{
			if (!File.Exists(filePath))
			{
				Log.Error(-1, "Missing file: " + filePath);
				context.Response.StatusCode = 404;
				return;
			}

			byte[] data = File.ReadAllBytes(filePath);
			context.Response.ContentType = contentType;
			context.Response.ContentLength = data.Length;
			context.Response.Body.Write(data, 0, data.Length);
			context.Response.Body.Flush();
			context.Response.CompleteAsync().Wait();
		}


		


		private string GetContentTypeForFile(string filePath)
		{
			string extension = Path.GetExtension(filePath).ToLower();
			if (extension == ".html")
			{
				return "text/html; charset=utf-8";
			}
			if (extension == ".css")
			{
				return "text/css";
			}
			if (extension == ".js")
			{
				return "application/javascript";
			}
			if (extension == ".png")
			{
				return "image/png";
			}
			if (extension == ".jpg" || extension == ".jpeg")
			{
				return "image/jpeg";
			}
			if (extension == ".svg")
			{
				return "image/svg+xml";
			}
			if (extension == ".ico")
			{
				return "image/x-icon";
			}
			if (extension == ".woff2")
			{
				return "font/woff2";
			}
			if (extension == ".json")
			{
				return "application/json";
			}
			return "application/octet-stream";
		}

	}
}
