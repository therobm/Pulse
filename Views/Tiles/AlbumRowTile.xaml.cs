using System;
using Thump.Pulse;

namespace Thump.Views.Tiles
{
	public partial class AlbumRowTile : ThumpView
	{
		private PulseAlbum m_album;

		public AlbumRowTile() : base(MainView.Self)
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
			PulseAlbum album = BindingContext as PulseAlbum;
			if (album == null)
			{
				return;
			}
			m_album = album;
			m_nameLabel.Text = album.Name;
			m_subtitleLabel.Text = album.Artist;
			m_art.SetCoverArt(album.CoverArt);
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_album == null)
			{
				return;
			}
			m_mainView.OnAlbumSelected(m_album);
		}
	}
}
