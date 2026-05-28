using System;
using Thump.Pulse;

namespace Thump.Views
{
	public partial class NowPlayingView : ThumpView
	{
		public NowPlayingView(MainView mainView) : base(mainView)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
			PulseTrack song = MainView.Self.GetCurrentTrack();
			SetTrack(song);
		}

		public void SetTrack(PulseTrack song)
		{
			if (song == null)
			{
				m_titleLabel.Text = "Nothing playing";
				m_artistLabel.Text = "";
				m_currentTimeLabel.Text = "0:00";
				m_totalTimeLabel.Text = "0:00";
				m_seekSlider.Value = 0;
				return;
			}
			m_titleLabel.Text = song.Title;
			m_artistLabel.Text = song.Artist;
			m_currentTimeLabel.Text = "0:00";
			m_totalTimeLabel.Text = FormatDuration(song.Duration);
			m_seekSlider.Value = 0;
			m_art.SetCoverArt(song.ImageID);
		}

		private static string FormatDuration(int totalSeconds)
		{
			int minutes = totalSeconds / 60;
			int seconds = totalSeconds % 60;
			string secondsText;
			if (seconds < 10)
			{
				secondsText = "0" + seconds;
			}
			else
			{
				secondsText = seconds.ToString();
			}
			return minutes + ":" + secondsText;
		}

		private void OnBackClicked(object sender, EventArgs e)
		{
			m_mainView.OnBackPressed();
		}

		private void OnPlayPauseClicked(object sender, EventArgs e)
		{
		}

		private void OnPrevClicked(object sender, EventArgs e)
		{
		}

		private void OnNextClicked(object sender, EventArgs e)
		{
		}

		private void OnShuffleClicked(object sender, EventArgs e)
		{
		}

		private void OnRepeatClicked(object sender, EventArgs e)
		{
		}

		private void OnFavoriteClicked(object sender, EventArgs e)
		{
		}

		private void OnQueueClicked(object sender, EventArgs e)
		{
		}
	}
}
