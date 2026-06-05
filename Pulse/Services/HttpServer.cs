using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Pulse;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Assistant.Services
{
	public class HttpServer : IPulseRouteHost
	{
		private WebApplication m_app;

		private Dictionary<string, Action<HttpContext>> m_routes = new Dictionary<string, Action<HttpContext>>();

		private class ResultRouteAdapter
		{
			Stopwatch m_watch = new Stopwatch();
			private Func<HttpContext, IResult> m_handler;
			public ResultRouteAdapter(Func<HttpContext, IResult> handler)
			{
				m_handler = handler;
			}
			public void Invoke(HttpContext context)
			{
				m_watch.Restart();
				IResult result = m_handler(context);
				result.ExecuteAsync(context).Wait();
				m_watch.Stop();

				long responseBytes = context.Response.ContentLength ?? 0;
				int status = context.Response.StatusCode;
				Log.Info(0, "[" + m_watch.ElapsedMilliseconds + "ms] [" + status + "] [" + responseBytes + "B] " + context.Request.Path + context.Request.QueryString);
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
			// Pulse hands typed objects to Results.Json and expects wire names to
			// match the C# names exactly. The Web defaults (camelCase + don't
			// serialize public fields) silently mangle PulseResponse and the
			// rest of the envelope-shaped payloads, so override both globally:
			// preserve names verbatim, serialize fields like properties, drop
			// nulls.
			builder.Services.ConfigureHttpJsonOptions(options =>
			{
				options.SerializerOptions.IncludeFields = true;
				options.SerializerOptions.PropertyNamingPolicy = null;
				options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
			});

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
			PulseConfig config = PulseService.GetConfig();
			string certPath = config.HttpsCertPath;
			if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
			{
				listenOptions.UseHttps(certPath);
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

				if (!PulseService.IsReady())
				{
					ServeLoadingPage(context);
					return Task.CompletedTask;
				}

				if (path.Length == 0)
				{
					context.Response.Redirect("/web/pulse.html");
					return Task.CompletedTask;
				}

				Action<HttpContext> handler = null;
				int bestLength = 0;
				foreach (KeyValuePair<string, Action<HttpContext>> route in m_routes)
				{
					string routePath = route.Key;
					if (path == routePath || path.StartsWith(routePath + "/"))
					{
						if (routePath.Length > bestLength)
						{
							bestLength = routePath.Length;
							handler = route.Value;
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
					string pulseDir = Path.Combine(AppContext.BaseDirectory, "Content", "Web");
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

		private void ServeLoadingPage(HttpContext context)
		{
			string html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"2\"><title>Pulse - Loading</title><style>body{margin:0;background:#0e0e12;color:#e8e6f0;font-family:'Segoe UI',system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;}main{text-align:center;}h1{font-size:32px;font-weight:300;letter-spacing:3px;text-transform:uppercase;margin:0 0 12px;}h1 span{color:#6c5ce7;font-weight:600;}p{color:#8a88a0;font-size:14px;margin:0;}.dot{display:inline-block;width:8px;height:8px;border-radius:50%;background:#6c5ce7;margin:0 3px;animation:pulse 1.2s infinite ease-in-out;}.dot:nth-child(2){animation-delay:0.2s;}.dot:nth-child(3){animation-delay:0.4s;}@keyframes pulse{0%,80%,100%{opacity:0.2;}40%{opacity:1;}}</style></head><body><main><h1><span>Pulse</span> is loading</h1><p>Scanning the music library &mdash; this page will refresh.</p><div style=\"margin-top:24px\"><span class=\"dot\"></span><span class=\"dot\"></span><span class=\"dot\"></span></div></main></body></html>";
			byte[] data = System.Text.Encoding.UTF8.GetBytes(html);
			context.Response.StatusCode = 503;
			context.Response.Headers["Retry-After"] = "2";
			context.Response.ContentType = "text/html; charset=utf-8";
			context.Response.ContentLength = data.Length;
			context.Response.Body.Write(data, 0, data.Length);
			context.Response.Body.Flush();
			context.Response.CompleteAsync().Wait();
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
