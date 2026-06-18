using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace PulseApp.Utility
{
	public class QuietObservableCollection<T> : ObservableCollection<T>
	{
		private bool m_suppressNotifications = false;

		public void BeginBatch()
		{
			m_suppressNotifications = true;
		}

		public void EndBatch()
		{
			m_suppressNotifications = false;
			OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
		}

		protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
		{
			if (!m_suppressNotifications)
			{
				base.OnCollectionChanged(e);
			}
		}
	}
}