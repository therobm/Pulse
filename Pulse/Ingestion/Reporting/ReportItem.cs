namespace PulseIngestion.Reporting
{
	public class ReportItem
	{
		public eReportLevel Level;
		public string Message;

		public ReportItem(eReportLevel level, string message)
		{
			Level = level;
			Message = message;
		}
	}
}
