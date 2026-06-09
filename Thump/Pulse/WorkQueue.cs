using Microsoft.Maui.ApplicationModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Thump.Pulse
{
	public class WorkItem
	{
		public Action m_work;
	}

	public class WorkQueue
	{
		private Thread m_thread;
		private Queue<WorkItem> m_queue = new Queue<WorkItem>();
		private object m_queueLock = new object();
		private SemaphoreSlim m_signal = new SemaphoreSlim(0);

		public WorkQueue()
		{
			m_thread = new Thread(Process);
			m_thread.IsBackground = true;
			m_thread.Start();
		}

		private void Process()
		{
			while (true)
			{
				m_signal.Wait();
				WorkItem item = null;
				lock (m_queueLock)
				{
					if (m_queue.Count > 0)
					{
						item = m_queue.Dequeue();
					}
				}
				if (item == null)
				{
					continue;
				}
				try
				{
					item.m_work();
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			}
		}

		public void Enqueue(Action work)
		{
			WorkItem item = new WorkItem();
			item.m_work = work;
			lock (m_queueLock)
			{
				m_queue.Enqueue(item);
			}
			m_signal.Release();
		}
	}
}
