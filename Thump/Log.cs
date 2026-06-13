using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;

namespace Thump
{
	public static class Log
	{
		private static readonly object s_fileLock = new object();
		private static string s_logFilePath = "";

		public static void Info(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
		{
			Write("INFO", message, filePath, memberName);
		}

		public static void Warn(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
		{
			Write("WARN", message, filePath, memberName);
		}

		public static void Error(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
		{
			string location = Path.GetFileNameWithoutExtension(filePath) + "." + memberName;
			MainView.Analytics.DiagnosticEvent(message, "", "", location);
			Write("ERROR", message, filePath, memberName);
		}

		public static void Perf(string message)
		{
#if DEBUG
		//	Write("PERF", message, false);
#endif
		}

		public static void Exception(Exception ex, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
		{
			string errorType = ex.GetType().Name;
			string location = Path.GetFileNameWithoutExtension(filePath) + "." + memberName;
			if (ex.TargetSite != null)
			{
				string declaringType = "";
				if (ex.TargetSite.DeclaringType != null)
				{
					declaringType = ex.TargetSite.DeclaringType.Name + ".";
				}
				location = declaringType + ex.TargetSite.Name;
			}

			MainView.Analytics.DiagnosticEvent(ex.Message, ex.ToString(), errorType, location);

			Write("EXCEPTION", ex.ToString(), filePath, memberName);
		}

		public static string GetLogFilePath()
		{
			lock (s_fileLock)
			{
				return ResolveLogFilePath();
			}
		}

		/// <summary>Clear the on-disk log file. Used to drop stale data before testing a new build.</summary>
		public static void Reset()
		{
			lock (s_fileLock)
			{
				try
				{
					string path = ResolveLogFilePath();
					File.WriteAllText(path, "");
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[EXCEPTION] Log reset failed: " + ex);
				}
			}
		}


		private static void Write(string level, string message, string filePath, string memberName, bool saveToDisk = true)
		{

			string caller = Path.GetFileNameWithoutExtension(filePath);
			string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "][" + caller + "." + memberName + "] " + message;
			Debug.WriteLine(line);

			if (saveToDisk)
			{
				lock (s_fileLock)
				{
					try
					{
						string path = ResolveLogFilePath();
						bool oversized = File.Exists(path) && new FileInfo(path).Length > 2_000_000;
						if (oversized)
						{
							File.WriteAllText(path, "");
						}
						File.AppendAllText(path, line + Environment.NewLine);
					}
					catch (Exception ex)
					{
						Debug.WriteLine("[EXCEPTION] Log file write failed: " + ex);
					}
				}
			}
		}

		private static string ResolveLogFilePath()
		{
			if (string.IsNullOrEmpty(s_logFilePath))
			{
				string directory = FileSystem.AppDataDirectory;
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				s_logFilePath = Path.Combine(directory, "thump.log");
			}
			return s_logFilePath;
		}
	}
}
