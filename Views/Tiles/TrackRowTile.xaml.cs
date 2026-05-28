using System;
using Thump.Pulse;

namespace Thump.Views.Tiles
{
	public partial class TrackRowTile : ThumpView
	{
		private PulseTrack m_song;

		public TrackRowTile() : base(MainView.Self)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulseTrack song = BindingContext as PulseTrack;
			if (song == null)
			{
				return;
			}
			m_song = song;
			m_titleLabel.Text = song.Title;
			m_artistLabel.Text = song.Artist;
			m_durationLabel.Text = FormatDuration(song.Duration);
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

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_song == null)
			{
				return;
			}
			m_mainView.OnTrackSelected(m_song);
		}
	}
}
