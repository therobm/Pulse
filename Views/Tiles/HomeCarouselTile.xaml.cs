using System;
using Thump.Data;
using Thump.Pulse;

namespace Thump.Views.Tiles
{
	public partial class HomeCarouselTile : ThumpView
	{
		private ThumpDataOb m_item;

		public HomeCarouselTile() : base(MainView.Self)
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
			ThumpDataOb item = BindingContext as ThumpDataOb;
			if (item == null)
			{
				return;
			}
			m_item = item;

			if (item.Kind == eDataType.Track)
			{
				PulseTrack song = item as PulseTrack;
				if (song != null)
				{
					m_titleLabel.Text = song.Title;
					m_subtitleLabel.Text = song.Artist;
					m_art.SetCoverArt(song.ImageID);
				}
			}
			else if (item.Kind == eDataType.Album)
			{
				PulseAlbum album = item as PulseAlbum;
				if (album != null)
				{
					m_titleLabel.Text = album.Name;
					m_subtitleLabel.Text = album.Artist;
					m_art.SetCoverArt(album.CoverArt);
				}
			}
			else if (item.Kind == eDataType.Playlist)
			{
				PulsePlaylist playlist = item as PulsePlaylist;
				if (playlist != null)
				{
					m_titleLabel.Text = playlist.Name;
					m_subtitleLabel.Text = playlist.SongCount + " tracks";
					m_art.SetCoverArt(playlist.CoverArt);
				}
			}
			else if (item.Kind == eDataType.Artist)
			{
				PulseArtist artist = item as PulseArtist;
				if (artist != null)
				{
					m_titleLabel.Text = artist.Name;
					m_subtitleLabel.Text = artist.AlbumCount + " albums";
					m_art.SetCoverArt(artist.CoverArt);
				}
			}
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_item == null)
			{
				return;
			}
			m_mainView.OnHomeItemSelected(m_item);
		}
	}
}
