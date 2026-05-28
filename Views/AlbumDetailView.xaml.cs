using System;
using System.Collections.Generic;
using Thump.Pulse;

namespace Thump.Views
{
	public partial class AlbumDetailView : ThumpView
	{
		private PulseAlbum m_album;
		private List<PulseTrack> m_tracks;

		public AlbumDetailView(MainView mainView, PulseAlbum album) : base(mainView)
		{
			InitializeComponent();
			m_album = album;
		}

		public override void Initialize()
		{
			base.Initialize();
			m_titleLabel.Text = m_album.Name;
			m_artistLabel.Text = m_album.Artist;
			m_metaLabel.Text = m_album.Year + "  ·  " + m_album.SongCount + " tracks";
			m_art.SetCoverArt(m_album.CoverArt);
			MainView.Data.GetTracksForAlbum(m_album, OnTracksLoaded);
		}

		private void OnTracksLoaded(List<PulseTrack> tracks)
		{
			m_tracks = tracks;
			m_trackList.ItemsSource = tracks;
		}

		private void OnBackClicked(object sender, EventArgs e)
		{
			m_mainView.OnBackPressed();
		}

		private void OnPlayClicked(object sender, EventArgs e)
		{
			m_mainView.OnPlayTracks(m_tracks, 0);
		}

		private void OnShuffleClicked(object sender, EventArgs e)
		{
			m_mainView.OnPlayTracks(m_tracks, 0);
		}
	}
}
