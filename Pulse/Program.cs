using Assistant.Services;

namespace Pulse
{
    internal class Program
	{
		static HttpServer m_webServer;
		static PulseService m_pulse;
		static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");


			m_pulse = new PulseService();
			m_webServer = new HttpServer();

			m_pulse.Run(m_webServer, PulseConfig.Load());

			m_webServer.Run();

			Console.Read();
			Log.Info(-1, "Server has shutdown.");
		}
    }
}
