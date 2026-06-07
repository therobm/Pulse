
namespace PulseAPI.CSharp
{
	public class PulseVersion : PulseObject
	{
		public string Version;
		public PulseVersion(string version)
		{
			Version = version;
			Kind = eDataType.Version;
		}
	}
}
