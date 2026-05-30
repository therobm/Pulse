using Pulse.MusicLibrary;
using System.Collections.Generic;

namespace Pulse.Protocols.Pulse
{
	public class PulseResponse
	{
		public Error error;
		public PulseInfo item;
		public List<PulseInfo> itemList;
		public byte[] data;
	}
	public class Error
	{
		public int code;
		public string message;
		public Error(ePulseCode _code, string _message)
		{
			code = (int)_code;
			message = _message;
		}
	}

	public class Ping
	{
		public bool ok = true;
		public string serverVersion = "";
	}

	public class Podcast
	{
		
	}

}
