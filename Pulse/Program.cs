using Assistant.Services;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Pulse
{
    internal class Program
	{
		static HttpServer m_webServer;
		static PulseService m_pulse;
		static ManualResetEventSlim s_shutdown = new ManualResetEventSlim(false);
		static void Main(string[] args)
        {
			PulseConfig config = PulseConfig.Load();
			List<string> configErrors = config.Validate();
			if (configErrors.Count > 0)
			{
				Log.Error("Pulse: invalid configuration in " + PulseConfig.GetConfigPath());
				for (int idx = 0; idx < configErrors.Count; idx++)
				{
					Log.Error("  - " + configErrors[idx]);
				}
				Log.Error("Fix the config and restart.");
				return;
			}

			m_pulse = new PulseService();
			m_webServer = new HttpServer();

			m_pulse.Run(m_webServer, config);

			m_webServer.Run();

			// Block the main thread until shutdown is requested. Unlike
			// Console.Read(), this does not consume stdin (so reading the console
			// for debugging still works) and does not return immediately when
			// stdin is not interactive (service / nohup / container), which used
			// to make the process exit right after boot (#309).
			Console.CancelKeyPress += OnCancelKeyPress;
			AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
			s_shutdown.Wait();

			Log.Info("Shutting down...");
			m_pulse.Shutdown();
			Log.Info("Server has shutdown.");
		}

		static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
		{
			// Don't let Ctrl+C hard-kill the process; release the wait so Main
			// returns and shuts down cleanly.
			e.Cancel = true;
			s_shutdown.Set();
		}

		static void OnProcessExit(object sender, EventArgs e)
		{
			s_shutdown.Set();
		}
    }
}
