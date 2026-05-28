using System;
using System.Diagnostics;

namespace Thump
{
	public static class Log
	{
		public static void Info(string message)
		{
			Debug.WriteLine("[INFO]  " + message);
		}

		public static void Warn(string message)
		{
			Debug.WriteLine("[WARN]  " + message);
		}

		public static void Error(string message)
		{
			Debug.WriteLine("[ERROR] " + message);
		}

		public static void Exception(Exception ex)
		{
			Debug.WriteLine("[EXCEPTION] " + ex);
		}
	}
}
