using System;
using System.Collections.Generic;
using Thump.Pulse;

namespace Thump.Views
{
	public partial class ArtistDetailView : ThumpView
	{
		private PulseArtist m_artist;
		private List<PulseAlbum> m_albums;

		public ArtistDetailView(MainView mainView, PulseArtist artist) : base(mainView)
		{
			InitializeComponent();
			m_artist = artist;
			m_art.MakeCircular();
		}

		public override void Initialize()
		{
			base.Initialize();
			m_titleLabel.Text = m_artist.Name;
			m_metaLabel.Text = m_artist.AlbumCount + " albums";
			m_art.SetCoverArt(m_artist.CoverArt);
			MainView.Data.GetAlbumsForArtist(m_artist, OnAlbumsLoaded);
		}

		private void OnAlbumsLoaded(List<PulseAlbum> albums)
		{
			m_albums = albums;
			m_albumList.ItemsSource = albums;
		}

		private void OnBackClicked(object sender, EventArgs e)
		{
			m_mainView.OnBackPressed();
		}

		private void OnPlayClicked(object sender, EventArgs e)
		{
			m_mainView.OnPlayArtist(m_artist, false);
		}

		private void OnShuffleClicked(object sender, EventArgs e)
		{
			m_mainView.OnPlayArtist(m_artist, true);
		}
	}
}
