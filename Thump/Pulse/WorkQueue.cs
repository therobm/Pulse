
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
		private Queue<WorkItem> m_queue = new Queue<WorkItem>();
		private object m_queueLock = new object();
		private SemaphoreSlim m_signal = new SemaphoreSlim(0);
		private int m_workerThreads = 1;
		public WorkQueue(int concurrent)
		{
			m_workerThreads = concurrent;
			for(int i = 0; i < m_workerThreads; i++)
			{
				Thread thread = new Thread(Process);
				thread.IsBackground = true;
				thread.Start();
			}
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
