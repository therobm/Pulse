using Microsoft.Maui.Controls;

namespace Thump.Views
{
	public class ThumpView : ContentView
	{
		protected MainView m_mainView;

		public ThumpView(MainView mainView)
		{
			m_mainView = mainView;
		}

		public virtual void Initialize()
		{
		}
	}
}
