using System.Runtime.CompilerServices;

public class Log
{
	enum LogType
	{
		INFO,
		WARN,
		ERROR
	}
	static readonly object s_consoleLock = new object();
	static StreamWriter s_fileWriter;
	static string s_logDirectory = "logs";
	static long s_maxFileSize = 1 * 1024 * 1024; // 1MB
	static long s_currentSize = 0;
	static int s_maxFiles = 3;

	static Log()
	{
		Directory.CreateDirectory(s_logDirectory);
		OpenNewLogFile();
	}

	static void OpenNewLogFile()
	{
		if (s_fileWriter != null)
		{
			s_fileWriter.Close();
		}

		string filename = Path.Combine(s_logDirectory, "log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
		s_fileWriter = new StreamWriter(filename, false);
		s_fileWriter.AutoFlush = true;
		s_currentSize = 0;

		PruneOldFiles();
	}

	static void PruneOldFiles()
	{
		string[] files = Directory.GetFiles(s_logDirectory, "log_*.txt");
		if (files.Length <= s_maxFiles)
		{
			return;
		}

		Array.Sort(files);
		for (int i = 0; i < files.Length - s_maxFiles; i++)
			File.Delete(files[i]);
	}

	static void WriteToFile(string line)
	{
		s_fileWriter.WriteLine(line);
		s_currentSize += line.Length + 2;
		if (s_currentSize >= s_maxFileSize)
		{
			OpenNewLogFile();
		}
	}

	static void LogInternal(int device, string message, LogType logType, string filePath, string memberName)
	{
		LogInternalWithColor(device, message, logType, filePath, memberName, ConsoleColor.Gray, false);
	}

	static void LogInternal(int device, string message, LogType logType, string filePath, string memberName, ConsoleColor color)
	{
		LogInternalWithColor(device, message, logType, filePath, memberName, color, true);
	}

	static void LogInternalWithColor(int device, string message, LogType logType, string filePath, string memberName, ConsoleColor color, bool useColor)
	{
		lock (s_consoleLock)
		{
			string caller = Path.GetFileNameWithoutExtension(filePath);
			string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

			string deviceID = "[dID" + device.ToString() + "]";
			if (device < 0)
			{
				deviceID = "[HOST]";
			}
			string line = deviceID + "[" + logType.ToString() + "][" + timestamp + "][" + caller + "." + memberName + "] " + message;
			if (useColor)
			{
				Console.ForegroundColor = color;
			}
			Console.WriteLine(line);
			if (useColor)
			{
				Console.ResetColor();
			}
			WriteToFile(line);
		}
	}
	public static void Info(int device, string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
	{
		LogInternal(device, message, LogType.INFO, filePath, memberName);
	}

	public static void Warning(int device, string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
	{
		LogInternal(device, message, LogType.WARN, filePath, memberName, ConsoleColor.Yellow);
	}

	public static void Error(int device, string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "")
	{
		LogInternal(device, message, LogType.ERROR, filePath, memberName, ConsoleColor.Red);
	}
}
