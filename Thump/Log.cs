using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using PulseAPI.CSharp;
using Thump.Data;
using Thump.Pulse;

namespace Thump
{
	public static class Log
	{
		private static readonly object s_fileLock = new object();
		private static string s_logFilePath = "";

		/// <summary>Per-launch session identifier; constant for the lifetime of the process.</summary>
		private static readonly string s_sessionId = System.Guid.NewGuid().ToString();

		/// <summary>Bounded ring buffer of structured events awaiting batch POST. Drops oldest when full.</summary>
		private static readonly List<PulseLogEvent> s_eventBuffer = new List<PulseLogEvent>();

		/// <summary>Guards s_eventBuffer. Must NEVER be held together with s_fileLock.</summary>
		private static readonly object s_bufferLock = new object();

		/// <summary>Signals the flush thread to drain immediately rather than wait for the next tick.</summary>
		private static readonly AutoResetEvent s_flushSignal = new AutoResetEvent(false);

		/// <summary>Maximum events retained before the oldest is dropped.</summary>
		private const int s_bufferCapacity = 500;

		/// <summary>Buffer length that triggers a flush signal between scheduled ticks.</summary>
		private const int s_flushHighWater = 50;

		/// <summary>Reference to the network client used to ship batches. Volatile so the flush thread sees the assignment.</summary>
		private static volatile MediaClient s_remoteClient = null;

		/// <summary>Background thread that periodically drains s_eventBuffer.</summary>
		private static Thread s_flushThread = null;

		/// <summary>Guards lazy creation of s_flushThread.</summary>
		private static readonly object s_flushThreadLock = new object();

		public static void Info(string message)
		{
			Write("INFO", message);
		}

		public static void Warn(string message)
		{
			Write("WARN", message);
		}

		public static void Error(string message)
		{
			Write("ERROR", message);
		}

		public static void Perf(string message)
		{
			Write("PERF", message, false);
		}

		public static void Exception(Exception ex)
		{
			Write("EXCEPTION", ex.ToString());
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

		/// <summary>
		/// Register the network client used to ship buffered diagnostic events
		/// and start the background flush thread (idempotent). Called once from
		/// MainView after the MediaClient is constructed.
		/// </summary>
		public static void SetRemoteClient(MediaClient client)
		{
			s_remoteClient = client;
			lock (s_flushThreadLock)
			{
				if (s_flushThread != null)
				{
					return;
				}
				s_flushThread = new Thread(FlushLoop);
				s_flushThread.IsBackground = true;
				s_flushThread.Start();
			}
		}

		/// <summary>
		/// Record a structured diagnostic event with no detail string. Forwards
		/// to the detail overload with an empty detail. The caller-info default
		/// arguments are a sanctioned exception to the no-default-params rule.
		/// </summary>
		public static void Event(eAction action, eResult result, [CallerMemberName] string member = "", [CallerFilePath] string file = "")
		{
			Event(action, result, "", member, file);
		}

		/// <summary>
		/// Record a structured diagnostic event with a free-form detail string.
		/// Always writes to the local log file; also enqueues the event for the
		/// remote pipeline when ThumpSettings.GetRemoteLoggingEnabled() is true.
		/// Never throws into the caller -- logging must not destabilise app code.
		/// </summary>
		public static void Event(eAction action, eResult result, string detail, [CallerMemberName] string member = "", [CallerFilePath] string file = "")
		{
			string location = Path.GetFileNameWithoutExtension(file) + "." + member;
			string detailSuffix = "";
			if (!string.IsNullOrEmpty(detail))
			{
				detailSuffix = " " + detail;
			}

			// Always write to the local file. Write has its own try/catch.
			Write("EVENT", action + " " + result + " " + location + detailSuffix);

			try
			{
				bool remoteEnabled = false;
				try
				{
					remoteEnabled = ThumpSettings.GetRemoteLoggingEnabled();
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[diag] remote-enabled probe failed: " + ex.Message);
					return;
				}
				if (!remoteEnabled)
				{
					return;
				}

				PulseLogEvent record = new PulseLogEvent();
				record.Action = action;
				record.Result = result;
				record.Location = location;
				record.Detail = detail;
				// Round-trip "o" UTC so the server can order and prune correctly.
				record.Timestamp = System.DateTime.UtcNow.ToString("o");

				bool signalFlush = false;
				lock (s_bufferLock)
				{
					if (s_eventBuffer.Count >= s_bufferCapacity)
					{
						s_eventBuffer.RemoveAt(0);
					}
					s_eventBuffer.Add(record);
					if (s_eventBuffer.Count >= s_flushHighWater)
					{
						signalFlush = true;
					}
				}
				if (result == eResult.Fail)
				{
					signalFlush = true;
				}
				if (signalFlush)
				{
					s_flushSignal.Set();
				}
			}
			catch (Exception ex)
			{
				// Logging must never throw into callers. Use Debug only -- no
				// Log.* call here or a failing remote path would loop back in.
				Debug.WriteLine("[diag] Event enqueue failed: " + ex.Message);
			}
		}

		private static void FlushLoop()
		{
			// Sanctioned infinite background loop -- the flush thread runs for
			// the lifetime of the process.
			while (true)
			{
				try
				{
					s_flushSignal.WaitOne(TimeSpan.FromSeconds(10));
					FlushOnce();
				}
				catch (Exception ex)
				{
					// Catch everything so a transient failure does not kill the
					// flush thread. No Log.* call -- diagnostics path only.
					Debug.WriteLine("[diag] flush loop iteration failed: " + ex.Message);
				}
			}
		}

		private static void FlushOnce()
		{
			try
			{
				MediaClient client = s_remoteClient;
				if (client == null)
				{
					return;
				}
				if (!ThumpSettings.GetRemoteLoggingEnabled())
				{
					return;
				}

				List<PulseLogEvent> drained = new List<PulseLogEvent>();
				lock (s_bufferLock)
				{
					int count = s_eventBuffer.Count;
					if (count == 0)
					{
						return;
					}
					for (int index = 0; index < count; index++)
					{
						drained.Add(s_eventBuffer[index]);
					}
					s_eventBuffer.Clear();
				}
				if (drained.Count == 0)
				{
					return;
				}

				PulseLogBatch batch = new PulseLogBatch();
				batch.DeviceId = ThumpSettings.GetOrCreateDeviceId();
				batch.SessionId = s_sessionId;
				batch.User = ThumpSettings.GetUsername();
				batch.AppVersion = "";
				try
				{
					batch.AppVersion = AppInfo.VersionString;
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[diag] AppInfo.VersionString unavailable: " + ex.Message);
					batch.AppVersion = "";
				}
				batch.Platform = "Android";
				try
				{
					batch.Platform = DeviceInfo.Platform.ToString();
				}
				catch (Exception ex)
				{
					Debug.WriteLine("[diag] DeviceInfo.Platform unavailable: " + ex.Message);
					batch.Platform = "Android";
				}
				batch.Events = drained;

				// PostDiagnostics is fire-and-forget; do not re-enqueue on
				// failure. Dropped diagnostics are acceptable.
				client.PostDiagnostics(batch);
			}
			catch (Exception ex)
			{
				// Whole-body guard. No Log.* -- never feed failures back in.
				Debug.WriteLine("[diag] FlushOnce failed: " + ex.Message);
			}
		}


		private static void Write(string level, string message, bool saveToDisk = true)
		{
			string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
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
