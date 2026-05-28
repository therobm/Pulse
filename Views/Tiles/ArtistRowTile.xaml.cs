using System;
using Thump.Pulse;

namespace Thump.Views.Tiles
{
	public partial class ArtistRowTile : ThumpView
	{
		private PulseArtist m_artist;

		public ArtistRowTile() : base(MainView.Self)
		{
			InitializeComponent();
			m_art.MakeCircular();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulseArtist artist = BindingContext as PulseArtist;
			if (artist == null)
			{
				return;
			}
			m_artist = artist;
			m_nameLabel.Text = artist.Name;
			m_subtitleLabel.Text = artist.AlbumCount + " albums";
			m_art.SetCoverArt(artist.CoverArt);
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_artist == null)
			{
				return;
			}
			m_mainView.OnArtistSelected(m_artist);
		}
	}
}
