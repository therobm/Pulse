

namespace PulseAPI.CSharp
{
	public class PulseResponse
	{
		public enum ContentType
		{
			PulseObject,
			PulseObjectList,
		}
		public string status = "ok";
		public string version = "1.16.1";
		public ContentType contentType;
		public object contents;
	}
}
