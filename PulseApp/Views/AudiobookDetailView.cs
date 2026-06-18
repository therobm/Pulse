using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PulseAPI.CSharp;
using PulseApp.Pulse;
using PulseApp.Utility;
using PulseApp.Views.Tiles;

namespace PulseApp.Views
{
	public class AudiobookDetailView : PulseAppView
	{
		private ArtImage m_art;
		private Label m_titleLabel;
		private Label m_metaLabel;
		private CollectionView m_chapterList;
		private PulseAudiobook m_audiobook;
		// Bound to m_chapterList once; OnAudiobookLoaded reconciles in place.
		private QuietObservableCollection<PulseChapter> m_chapters = new QuietObservableCollection<PulseChapter>();
		// Chapters adapted to PulseTrack so the existing playback path can queue
		// them. Rebuilt from m_chapters after each sync so SelectionChanged's
		// index maps cleanly between the two lists.
		private List<PulseTrack> m_chapterTracks;

		public AudiobookDetailView(MainView mainView, PulseAudiobook audiobook) : base(mainView)
		{
			m_audiobook = audiobook;
		}

		protected override void BuildLayout()
		{
			BackgroundColor = PulseAppColors.Background;

			Grid grid = new Grid();

			RowDefinition headerRow = new RowDefinition();
			headerRow.Height = GridLength.Auto;
			RowDefinition artRow = new RowDefinition();
			artRow.Height = GridLength.Auto;
			RowDefinition titleRow = new RowDefinition();
			titleRow.Height = GridLength.Auto;
			RowDefinition buttonRow = new RowDefinition();
			buttonRow.Height = GridLength.Auto;
			RowDefinition listRow = new RowDefinition();
			listRow.Height = GridLength.Star;
			grid.RowDefinitions.Add(headerRow);
			grid.RowDefinitions.Add(artRow);
			grid.RowDefinitions.Add(titleRow);
			grid.RowDefinitions.Add(buttonRow);
			grid.RowDefinitions.Add(listRow);

			grid.Children.Add(BuildHeader());
			grid.Children.Add(BuildArt());
			grid.Children.Add(BuildTitle());
			grid.Children.Add(BuildButtons());
			grid.Children.Add(BuildChapterList());

			Content = grid;
		}

		private View BuildHeader()
		{
			HorizontalStackLayout headerStack = new HorizontalStackLayout();
			headerStack.Padding = new Thickness(8, 8, 8, 0);

			Button backButton = new Button();
			backButton.Text = "‹";
			backButton.FontSize = 22;
			backButton.TextColor = PulseAppColors.OnBackground;
			backButton.BackgroundColor = Colors.Transparent;
			backButton.WidthRequest = 44;
			backButton.HeightRequest = 44;
			backButton.Clicked += OnBackClicked;
			headerStack.Children.Add(backButton);

			Grid.SetRow(headerStack, 0);
			return headerStack;
		}

		private View BuildArt()
		{
			m_art = new ArtImage(220);
			m_art.HeightRequest = 220;
			m_art.WidthRequest = 220;
			m_art.HorizontalOptions = LayoutOptions.Center;
			m_art.Margin = new Thickness(0, 12, 0, 16);

			Grid.SetRow(m_art, 1);
			return m_art;
		}

		private View BuildTitle()
		{
			StackLayout titleStack = new StackLayout();
			titleStack.Spacing = 4;
			titleStack.Padding = new Thickness(16, 0, 16, 12);

			m_titleLabel = new Label();
			m_titleLabel.Text = "Audiobook title";
			m_titleLabel.FontSize = 22;
			m_titleLabel.TextColor = PulseAppColors.OnBackground;
			m_titleLabel.HorizontalOptions = LayoutOptions.Center;
			m_titleLabel.HorizontalTextAlignment = TextAlignment.Center;
			titleStack.Children.Add(m_titleLabel);

			m_metaLabel = new Label();
			m_metaLabel.Text = "";
			m_metaLabel.FontSize = 12;
			m_metaLabel.TextColor = PulseAppColors.TextDim;
			m_metaLabel.HorizontalOptions = LayoutOptions.Center;
			m_metaLabel.HorizontalTextAlignment = TextAlignment.Center;
			titleStack.Children.Add(m_metaLabel);

			Grid.SetRow(titleStack, 2);
			return titleStack;
		}

		private View BuildButtons()
		{
			HorizontalStackLayout buttonStack = new HorizontalStackLayout();
			buttonStack.Spacing = 12;
			buttonStack.Padding = new Thickness(16, 0, 16, 12);
			buttonStack.HorizontalOptions = LayoutOptions.Center;

			Button playButton = new Button();
			playButton.Text = "▶  Play";
			playButton.TextColor = PulseAppColors.Background;
			playButton.BackgroundColor = PulseAppColors.Accent;
			playButton.CornerRadius = 8;
			playButton.FontSize = 14;
			playButton.Padding = new Thickness(24, 8);
			playButton.Clicked += OnPlayClicked;
			buttonStack.Children.Add(playButton);

			Grid.SetRow(buttonStack, 3);
			return buttonStack;
		}

		private View BuildChapterList()
		{
			m_chapterList = new CollectionView();
			m_chapterList.ItemTemplate = new DataTemplate(typeof(ChapterRowTile));
			m_chapterList.SelectionMode = SelectionMode.Single;
			m_chapterList.SelectionChanged += OnChapterSelectionChanged;
			m_chapterList.ItemsSource = m_chapters;

			Grid.SetRow(m_chapterList, 4);
			return m_chapterList;
		}

		public override void Initialize()
		{
			m_titleLabel.Text = m_audiobook.Title;
			m_metaLabel.Text = BuildMeta(m_audiobook);
			m_art.SetCoverArt(m_audiobook.CoverArt);
			base.Initialize();
		}

		protected override void RefreshData()
		{
			// The list endpoint hands back a summary book with no chapters; fetch
			// the full audiobook (GetAudiobook) which includes its chapters.
			MainView.MediaClient.GetAudiobook(m_audiobook.Id, OnAudiobookLoaded);
			base.RefreshData();
		}

		private void OnAudiobookLoaded(PulseAudiobookDetails details)
		{
			if (details == null)
			{
				return;
			}
			if (details.Book != null)
			{
				m_audiobook = details.Book;
				m_titleLabel.Text = m_audiobook.Title;
				m_metaLabel.Text = BuildMeta(m_audiobook);
				m_art.SetCoverArt(m_audiobook.CoverArt);
			}

			List<PulseChapter> incoming = details.Chapters;
			if (incoming == null)
			{
				incoming = new List<PulseChapter>();
			}
			SyncFrom<PulseChapter>(m_chapters, incoming);
			Sort<PulseChapter>(m_chapters, CompareChapterByOrder);
			m_chapterTracks = BuildChapterTracks(new List<PulseChapter>(m_chapters));
		}

		private static int CompareChapterByOrder(PulseChapter first, PulseChapter second)
		{
			return first.OrderIndex.CompareTo(second.OrderIndex);
		}

		private static string BuildMeta(PulseAudiobook book)
		{
			string meta = book.ItemCount + " chapters";
			if (book.TotalDuration > 0)
			{
				meta = meta + "  ·  " + FormatTotalDuration(book.TotalDuration);
			}
			if (!string.IsNullOrEmpty(book.Author))
			{
				meta = book.Author + "  ·  " + meta;
			}
			return meta;
		}

		private static string FormatTotalDuration(int totalSeconds)
		{
			if (totalSeconds <= 0)
			{
				return "";
			}
			int hours = totalSeconds / 3600;
			int minutes = (totalSeconds % 3600) / 60;
			if (hours > 0)
			{
				return hours + "h " + minutes + "m";
			}
			return minutes + "m";
		}

		private List<PulseTrack> BuildChapterTracks(List<PulseChapter> chapters)
		{
			List<PulseTrack> tracks = new List<PulseTrack>();
			if (chapters == null)
			{
				return tracks;
			}
			for (int index = 0; index < chapters.Count; index++)
			{
				tracks.Add(ChapterToTrack(chapters[index]));
			}
			return tracks;
		}

		// Adapter from a chapter to the PulseTrack the player pipeline expects.
		// IsSeries = true gives the ±10s skip behaviour on the media buttons.
		private PulseTrack ChapterToTrack(PulseChapter chapter)
		{
			PulseTrack track = new PulseTrack();
			track.Id = chapter.Id;
			track.Title = chapter.Title;
			track.Artist = m_audiobook.Author;
			track.Album = m_audiobook.Title;
			if (string.IsNullOrEmpty(chapter.CoverArt))
			{
				track.CoverArt = m_audiobook.CoverArt;
			}
			else
			{
				track.CoverArt = chapter.CoverArt;
			}
			track.Duration = chapter.Duration;
			track.IsSeries = true;
			track.StartMs = chapter.StartMs;
			track.EndMs = chapter.EndMs;
			track.StreamId = chapter.StreamId;
			return track;
		}

		private void OnChapterSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			PulseChapter chapter = m_chapterList.SelectedItem as PulseChapter;
			// SelectedItem = null re-enters this handler with a null selection; skip.
			if (chapter == null)
			{
				return;
			}
			int index = -1;
			if (m_chapters != null)
			{
				index = m_chapters.IndexOf(chapter);
			}
			if (index >= 0 && m_chapterTracks != null && m_chapterTracks.Count > 0)
			{
				m_mainView.OnPlayTracks(m_chapterTracks, index, eQueueSource.Audiobook, m_audiobook.Id);
			}
			m_chapterList.SelectedItem = null;
		}

		private void OnPlayClicked(object sender, EventArgs e)
		{
			if (m_chapterTracks == null || m_chapterTracks.Count == 0)
			{
				return;
			}
			m_mainView.OnPlayTracks(m_chapterTracks, ResolveResumeIndex(), eQueueSource.Audiobook, m_audiobook.Id);
		}

		// Resume from the chapter the book last left off on, else the first.
		private int ResolveResumeIndex()
		{
			string lastItemId = m_audiobook.LastItemId;
			if (string.IsNullOrEmpty(lastItemId))
			{
				return 0;
			}
			for (int index = 0; index < m_chapters.Count; index++)
			{
				if (m_chapters[index].Id == lastItemId)
				{
					return index;
				}
			}
			return 0;
		}

		private void OnBackClicked(object sender, EventArgs e)
		{
			m_mainView.OnBackPressed();
		}
	}
}
