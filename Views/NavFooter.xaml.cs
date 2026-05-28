using System;
using Microsoft.Maui.Graphics;

namespace Thump.Views
{
	public partial class NavFooter : ThumpView
	{
		private static readonly Color s_activeColor = Color.FromArgb("#3b82f6");
		private static readonly Color s_inactiveColor = Color.FromArgb("#555568");

		public NavFooter(MainView mainView) : base(mainView)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		public void SetActiveTab(eTab tab)
		{
			m_homeButton.TextColor = s_inactiveColor;
			m_libraryButton.TextColor = s_inactiveColor;
			m_searchButton.TextColor = s_inactiveColor;
			m_settingsButton.TextColor = s_inactiveColor;

			if (tab == eTab.Home)
			{
				m_homeButton.TextColor = s_activeColor;
			}
			else if (tab == eTab.Library)
			{
				m_libraryButton.TextColor = s_activeColor;
			}
			else if (tab == eTab.Search)
			{
				m_searchButton.TextColor = s_activeColor;
			}
			else if (tab == eTab.Settings)
			{
				m_settingsButton.TextColor = s_activeColor;
			}
		}

		private void OnHomeClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToHome();
		}

		private void OnLibraryClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToLibrary();
		}

		private void OnSearchClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToSearch();
		}

		private void OnSettingsClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToSettings();
		}
	}
}
