using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using PulseAPI.CSharp;
using Thump.Utility;

namespace Thump.Views
{
	public class ThumpView : ContentView
	{
		protected MainView m_mainView;

		public ThumpView(MainView mainView)
		{
			m_mainView = mainView;
            BuildLayout();
        }

        protected virtual void BuildLayout()
        {

        }

		public virtual void Initialize()
		{
		}

		protected virtual void RefreshData()
		{

		}
		// Fired when this view becomes the active content (or its parent passes
		// the signal down). The base implementation forwards to child ThumpViews;
		// overrides that do their own work should call base to keep it flowing.
		public virtual void OnNavigatedTo()
		{
			RefreshData();
			ForwardNavigatedToChildren(this);
		}

		// Walk down through plain layout containers and hand the signal to the
		// nearest descendant ThumpViews. Each of those forwards to its own
		// children via its OnNavigatedTo, so one pass covers the whole subtree.
		// CollectionView item templates are virtualized and not reached here;
		// those tiles re-request cover art when they rebind on scroll.
		protected void ForwardNavigatedToChildren(IView element)
		{
			List<IView> children = new List<IView>();
			CollectChildViews(element, children);
			for (int index = 0; index < children.Count; index++)
			{
				ThumpView childView = children[index] as ThumpView;
				if (childView != null)
				{
					childView.OnNavigatedTo();
				}
				else
				{
					ForwardNavigatedToChildren(children[index]);
				}
			}
		}

		protected void Sort<T>(QuietObservableCollection<T> collection, Comparison<T> comparison)
		{
			collection.BeginBatch();
			List<T> sorted = new List<T>(collection);
			sorted.Sort(comparison);

			for (int i = 0; i < sorted.Count; i++)
			{
				int current = -1;
				for (int j = i; j < collection.Count; j++)
				{
					if (ReferenceEquals(collection[j], sorted[i]))
					{
						current = j;
						break;
					}
				}

				if (current != i)
				{
					collection.Move(current, i);
				}
			}
			collection.EndBatch();
		}

		protected void SyncFrom<T>(QuietObservableCollection<T> collection, List<T> incoming) where T : PulseObject
		{
			SyncFrom<T>(collection, incoming, e => e.Id);
		}
		protected void SyncFrom<T>(QuietObservableCollection<T> collection, List<T> incoming, Func<T, string> getId)
		{
			collection.BeginBatch();
			HashSet<string> incomingIds = new HashSet<string>();
			for (int i = 0; i < incoming.Count; i++)
			{
				incomingIds.Add(getId(incoming[i]));
			}

			// Update existing / add new
			for (int i = 0; i < incoming.Count; i++)
			{
				string id = getId(incoming[i]);
				int existing = -1;
				for (int j = 0; j < collection.Count; j++)
				{
					if (getId(collection[j]) == id)
					{
						existing = j;
						break;
					}
				}

				if (existing >= 0)
				{
					if (!getId(collection[existing]).Equals(getId(incoming[i])))
					{
						collection[existing] = incoming[i];
					}
				}
				else
				{
					collection.Add(incoming[i]);
				}
			}

			// Remove stale
			for (int i = collection.Count - 1; i >= 0; i--)
			{
				if (!incomingIds.Contains(getId(collection[i])))
				{
					collection.RemoveAt(i);
				}
			}
			collection.EndBatch();
		}

		protected void SyncFromOrdered<T>(QuietObservableCollection<T> collection, List<T> incoming) where T : PulseObject
		{
			SyncFromOrdered<T>(collection, incoming, e => e.Id);
		}
		protected void SyncFromOrdered<T>(QuietObservableCollection<T> collection, List<T> incoming, Func<T, string> getId)
		{
			collection.BeginBatch();
			// Remove items no longer present.
			HashSet<string> incomingIds = new HashSet<string>();
			for (int i = 0; i < incoming.Count; i++)
			{
				incomingIds.Add(getId(incoming[i]));
			}
			for (int i = collection.Count - 1; i >= 0; i--)
			{
				if (!incomingIds.Contains(getId(collection[i])))
				{
					collection.RemoveAt(i);
				}
			}

			// Walk incoming order; reuse the existing instance for a matched Id
			// (moving it into position) or insert a new one, so the collection ends
			// up in exactly the incoming order with unchanged rows left intact.
			for (int i = 0; i < incoming.Count; i++)
			{
				string id = getId(incoming[i]);
				int found = -1;
				for (int j = i; j < collection.Count; j++)
				{
					if (getId(collection[j]) == id)
					{
						found = j;
						break;
					}
				}
				if (found < 0)
				{
					collection.Insert(i, incoming[i]);
				}
				else
				{
					if (found != i)
					{
						collection.Move(found, i);
					}
					if (!getId(collection[i]).Equals(getId(incoming[i])))
					{
						collection[i] = incoming[i];
					}
				}
			}
			collection.EndBatch();
		}

		private static void CollectChildViews(IView element, List<IView> children)
		{
			Layout layout = element as Layout;
			if (layout != null)
			{
				for (int index = 0; index < layout.Count; index++)
				{
					children.Add(layout[index]);
				}
				return;
			}
			ContentView contentView = element as ContentView;
			if (contentView != null)
			{
				if (contentView.Content != null)
				{
					children.Add(contentView.Content);
				}
				return;
			}
			Border border = element as Border;
			if (border != null)
			{
				if (border.Content != null)
				{
					children.Add(border.Content);
				}
				return;
			}
			ScrollView scrollView = element as ScrollView;
			if (scrollView != null)
			{
				if (scrollView.Content != null)
				{
					children.Add(scrollView.Content);
				}
				return;
			}
		}
    }
}
