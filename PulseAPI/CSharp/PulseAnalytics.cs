using System;
using System.Collections.Generic;
using System.Text;

namespace PulseAPI.CSharp
{
	[Obsolete]
	public class PulseAnalytics : PulseObject
	{
		public enum eAction
		{
			Started,
			Paused,
			Skipped,
			Completed
		}

		public eAction Action;
		public eDataType MediaType;
		public string MediaId;
	}
}
