using System;
using Thump.Pulse;

namespace Thump.Views
{
	public partial class SearchView : ThumpView
	{
		public SearchView(MainView mainView) : base(mainView)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		private void OnSearchCompleted(object sender, EventArgs e)
		{
			string query = m_searchEntry.Text;
			if (string.IsNullOrWhiteSpace(query))
			{
				return;
			}
			MainView.Data.Search(query, OnSearchResults);
		}

		private void OnSearchResults(PulseSearchData results)
		{
			if (results == null)
			{
				m_artistResults.ItemsSource = null;
				m_albumResults.ItemsSource = null;
				m_songResults.ItemsSource = null;
				return;
			}
			m_artistResults.ItemsSource = results.Artists;
			m_albumResults.ItemsSource = results.Albums;
			m_songResults.ItemsSource = results.Songs;
		}
	}
}
