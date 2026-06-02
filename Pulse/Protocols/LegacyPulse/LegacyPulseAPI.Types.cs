using Pulse.MusicLibrary;
using System.Collections.Generic;

namespace Pulse.Protocols.LegacyPulse
{
	public class PulseResponse
	{
		public Error error;
		// Declared as object (not PulseInfo) so System.Text.Json serializes the
		// runtime type. With the abstract declared type the only members visible
		// to the serializer are PulseInfo's own, and the wire becomes `{}`.
		public object item;
		public List<object> itemList;
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
