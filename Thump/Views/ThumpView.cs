using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

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

		// Fired when this view becomes the active content (or its parent passes
		// the signal down). The base implementation forwards to child ThumpViews;
		// overrides that do their own work should call base to keep it flowing.
		public virtual void OnNavigatedTo()
		{
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
