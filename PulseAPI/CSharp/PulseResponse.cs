

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
		/// <summary>
		/// HTTP status code the responder should set when writing this envelope.
		/// Defaults to 200 so envelopes built by existing endpoints keep their
		/// wire behaviour (those endpoints do not consult the field, but auth
		/// responses honour it via their own respond helper).
		/// </summary>
		public int statusCode = 200;
		public ContentType contentType;
		public object contents;
	}
}
