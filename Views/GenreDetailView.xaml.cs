using System;
using System.Collections.Generic;
using Thump.Pulse;

namespace Thump.Views
{
	public partial class GenreDetailView : ThumpView
	{
		private PulseGenre m_genre;
		private List<PulseTrack> m_tracks;

		public GenreDetailView(MainView mainView, PulseGenre genre) : base(mainView)
		{
			InitializeComponent();
			m_genre = genre;
		}

		public override void Initialize()
		{
			base.Initialize();
			m_titleLabel.Text = m_genre.Name;
			m_metaLabel.Text = m_genre.SongCount + " songs  ·  " + m_genre.AlbumCount + " albums";
			MainView.Data.GetTracksForGenre(m_genre, OnTracksLoaded);
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
