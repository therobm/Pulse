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
		public ePulseWireType MediaType;
		public string MediaId;
	}
}
