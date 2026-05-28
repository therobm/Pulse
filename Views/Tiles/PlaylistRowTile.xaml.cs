using System;
using Thump.Pulse;

namespace Thump.Views.Tiles
{
	public partial class PlaylistRowTile : ThumpView
	{
		private PulsePlaylist m_playlist;

		public PlaylistRowTile() : base(MainView.Self)
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
			PulsePlaylist playlist = BindingContext as PulsePlaylist;
			if (playlist == null)
			{
				return;
			}
			m_playlist = playlist;
			m_nameLabel.Text = playlist.Name;
			m_subtitleLabel.Text = playlist.SongCount + " tracks";
			m_art.SetCoverArt(playlist.CoverArt);
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_playlist == null)
			{
				return;
			}
			m_mainView.OnPlaylistSelected(m_playlist);
		}
	}
}
