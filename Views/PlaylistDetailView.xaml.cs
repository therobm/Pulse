using System;
using System.Collections.Generic;
using Thump.Pulse;

namespace Thump.Views
{
	public partial class PlaylistDetailView : ThumpView
	{
		private PulsePlaylist m_playlist;
		private List<PulseTrack> m_tracks;

		public PlaylistDetailView(MainView mainView, PulsePlaylist playlist) : base(mainView)
		{
			InitializeComponent();
			m_playlist = playlist;
		}

		public override void Initialize()
		{
			base.Initialize();
			m_titleLabel.Text = m_playlist.Name;
			m_metaLabel.Text = m_playlist.SongCount + " tracks";
			m_art.SetCoverArt(m_playlist.CoverArt);

			
			m_tracks = m_playlist.Songs;
			m_trackList.ItemsSource = m_tracks;

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
