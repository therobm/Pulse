using System;
using Thump.Pulse;

namespace Thump.Views
{
	public partial class MiniPlayer : ThumpView
	{
		public MiniPlayer(MainView mainView) : base(mainView)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		public void SetTrack(PulseTrack song)
		{
			if (song == null)
			{
				m_titleLabel.Text = "Nothing playing";
				m_artistLabel.Text = "";
				return;
			}
			m_titleLabel.Text = song.Title;
			m_artistLabel.Text = song.Artist;
			m_art.SetCoverArt(song.ImageID);
		}

		private void OnPlayPauseClicked(object sender, EventArgs e)
		{
		}

		private void OnExpandTapped(object sender, EventArgs e)
		{
			m_mainView.OpenNowPlaying();
		}
	}
}
