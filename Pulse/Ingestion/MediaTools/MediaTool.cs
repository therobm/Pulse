using System.Threading.Tasks;

namespace PulseIngestion.MediaTools
{
	public abstract class MediaTool
	{
		protected IngestionConfig m_config;
		protected bool m_isReady = false;
		public MediaTool(IngestionConfig config)
		{
			m_config = config;
		}
		public bool IsReady()
		{
			return m_isReady;
		}
		public abstract Task ConvertToOutputFormat(string fileSource);
	}
}
