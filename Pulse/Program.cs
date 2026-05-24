using Assistant.Services;

namespace Pulse
{
    internal class Program
	{
		static HttpServer m_webServer;
		static PulseService m_pulse;
		static void Main(string[] args)
        {
			PulseConfig config = PulseConfig.Load();
			List<string> configErrors = config.Validate();
			if (configErrors.Count > 0)
			{
				Console.WriteLine("Pulse: invalid configuration in " + PulseConfig.GetConfigPath());
				for (int idx = 0; idx < configErrors.Count; idx++)
				{
					Console.WriteLine("  - " + configErrors[idx]);
				}
				Console.WriteLine("Fix the config and restart.");
				return;
			}

			m_pulse = new PulseService();
			m_webServer = new HttpServer();

			m_pulse.Run(m_webServer, config);

			m_webServer.Run();

			Console.Read();
			Log.Info(-1, "Server has shutdown.");
		}
    }
}
