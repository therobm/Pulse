namespace PulseIngestion.Metadata
{
	public abstract class MetadataReader
	{
		public abstract TrackTags Read(string filePath);
	}
}
