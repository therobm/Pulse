using Java.Security;
using Java.Security.Cert;
using Javax.Net.Ssl;

namespace PulseApp.Playback.AndroidOS
{
	/// <summary>
	/// Installs a process-wide trust-all TLS configuration on the JVM's
	/// HttpsURLConnection so Media3's native DefaultHttpDataSource can stream
	/// audio from the Pulse server regardless of certificate validity (e.g. a
	/// self-signed cert). This mirrors the managed HttpClient's
	/// AcceptAnyServerCertificate posture already used elsewhere; the native
	/// HTTP stack is the only consumer of these JVM defaults in this app, so the
	/// managed HttpClient path is unaffected.
	/// </summary>
	public static class InsecureTls
	{
		private static bool s_installed = false;
		private static object s_lock = new object();

		/// <summary>Install the trust-all socket factory and hostname verifier exactly once.</summary>
		public static void Install()
		{
			lock (s_lock)
			{
				if (s_installed)
				{
					return;
				}
				s_installed = true;
			}

			ITrustManager[] trustManagers = new ITrustManager[] { new TrustAllManager() };
			SSLContext sslContext = SSLContext.GetInstance("TLS");
			sslContext.Init(null, trustManagers, new SecureRandom());
			HttpsURLConnection.DefaultSSLSocketFactory = sslContext.SocketFactory;
			HttpsURLConnection.DefaultHostnameVerifier = new TrustAllHostnameVerifier();
		}
	}

	/// <summary>
	/// X509 trust manager that accepts any certificate chain. The empty check
	/// bodies are intentional — validation is deliberately skipped; see
	/// <see cref="InsecureTls"/> for the rationale.
	/// </summary>
	public class TrustAllManager : Java.Lang.Object, IX509TrustManager
	{
		/// <summary>Accepts any client certificate chain without validation.</summary>
		public void CheckClientTrusted(X509Certificate[] chain, string authType)
		{
		}

		/// <summary>Accepts any server certificate chain without validation.</summary>
		public void CheckServerTrusted(X509Certificate[] chain, string authType)
		{
		}

		/// <summary>Advertises no accepted issuers.</summary>
		public X509Certificate[] GetAcceptedIssuers()
		{
			return new X509Certificate[0];
		}
	}

	/// <summary>Hostname verifier that accepts any hostname. Pairs with <see cref="TrustAllManager"/>.</summary>
	public class TrustAllHostnameVerifier : Java.Lang.Object, IHostnameVerifier
	{
		/// <summary>Accepts any hostname / session pairing.</summary>
		public bool Verify(string hostname, ISSLSession session)
		{
			return true;
		}
	}
}
