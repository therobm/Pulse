using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PulseAPI.CSharp;
using Thump.Pulse;
using Thump.Views.Tiles;

namespace Thump.Views
{
	public class PodcastDetailView : ThumpView
	{
		private ArtImage m_art;
		private Label m_titleLabel;
		private Label m_metaLabel;
		private Button m_subscribeButton;
		private Picker m_retentionPicker;
		private Entry m_retentionValueEntry;
		private Label m_settingsStatusLabel;
		private CollectionView m_episodeList;
		// Retention policy names in the order they appear in m_retentionPicker;
		// the picker's SelectedIndex maps to one of these wire-side enum names.
		private static readonly string[] s_retentionPolicies = new string[] { "KeepAll", "KeepN", "KeepDays" };
		private PulsePodcast m_podcast;
		// Bound to m_episodeList once; OnPodcastLoaded reconciles it in place then
		// sorts newest-first, so a back-nav refresh only touches changed rows.
		private ObservableCollection<PulsePodcastEpisode> m_episodes = new ObservableCollection<PulsePodcastEpisode>();
		// Episodes adapted to PulseTrack so the existing playback path can
		// queue them. Rebuilt from m_episodes after each sync so SelectionChanged's
		// index maps cleanly between the two lists.
		private List<PulseTrack> m_episodeTracks;

		public PodcastDetailView(MainView mainView, PulsePodcast podcast) : base(mainView)
		{
			// The list endpoints hand back a summary PulsePodcast; wrap it so the
			// header can render immediately. Initialize() then fetches the full
			// PulsePodcastDetails (with episodes) via GetPodcast.
			m_podcast = podcast;
		}

		protected override void BuildLayout()
		{
			BackgroundColor = ThumpColors.Background;

			Grid grid = new Grid();

			RowDefinition headerRow = new RowDefinition();
			headerRow.Height = GridLength.Auto;
			RowDefinition artRow = new RowDefinition();
			artRow.Height = GridLength.Auto;
			RowDefinition titleRow = new RowDefinition();
			titleRow.Height = GridLength.Auto;
			RowDefinition buttonRow = new RowDefinition();
			buttonRow.Height = GridLength.Auto;
			RowDefinition settingsRow = new RowDefinition();
			settingsRow.Height = GridLength.Auto;
			RowDefinition listRow = new RowDefinition();
			listRow.Height = GridLength.Star;
			grid.RowDefinitions.Add(headerRow);
			grid.RowDefinitions.Add(artRow);
			grid.RowDefinitions.Add(titleRow);
			grid.RowDefinitions.Add(buttonRow);
			grid.RowDefinitions.Add(settingsRow);
			grid.RowDefinitions.Add(listRow);

			grid.Children.Add(BuildHeader());
			grid.Children.Add(BuildArt());
			grid.Children.Add(BuildTitle());
			grid.Children.Add(BuildButtons());
			grid.Children.Add(BuildSettings());
			grid.Children.Add(BuildEpisodeList());

			Content = grid;
		}

		private View BuildHeader()
		{
			HorizontalStackLayout headerStack = new HorizontalStackLayout();
			headerStack.Padding = new Thickness(8, 8, 8, 0);

			Button backButton = new Button();
			backButton.Text = "‹";
			backButton.FontSize = 22;
			backButton.TextColor = ThumpColors.OnBackground;
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
			m_art = new ArtImage();
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
			m_titleLabel.Text = "Podcast title";
			m_titleLabel.FontSize = 22;
			m_titleLabel.TextColor = ThumpColors.OnBackground;
			m_titleLabel.HorizontalOptions = LayoutOptions.Center;
			titleStack.Children.Add(m_titleLabel);

			m_metaLabel = new Label();
			m_metaLabel.Text = "";
			m_metaLabel.FontSize = 12;
			m_metaLabel.TextColor = ThumpColors.TextDim;
			m_metaLabel.HorizontalOptions = LayoutOptions.Center;
			titleStack.Children.Add(m_metaLabel);

			Grid.SetRow(titleStack, 2);
			return titleStack;
		}

		private View BuildButtons()
		{
			HorizontalStackLayout buttonStack = new HorizontalStackLayout();
			buttonStack.Spacing = 12;
			buttonStack.Padding = new Thickness(16, 0, 16, 12);

			Button playButton = new Button();
			playButton.Text = "▶  Play";
			playButton.TextColor = ThumpColors.Background;
			playButton.BackgroundColor = ThumpColors.Accent;
			playButton.CornerRadius = 8;
			playButton.FontSize = 14;
			playButton.Padding = new Thickness(20, 8);
			playButton.Clicked += OnPlayClicked;
			buttonStack.Children.Add(playButton);

			m_subscribeButton = new Button();
			m_subscribeButton.TextColor = ThumpColors.OnBackground;
			m_subscribeButton.BackgroundColor = ThumpColors.Surface;
			m_subscribeButton.CornerRadius = 8;
			m_subscribeButton.FontSize = 14;
			m_subscribeButton.Padding = new Thickness(20, 8);
			m_subscribeButton.Clicked += OnSubscribeClicked;
			buttonStack.Children.Add(m_subscribeButton);

			ScrollView scroller = new ScrollView();
			scroller.Orientation = ScrollOrientation.Horizontal;
			scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Never;
			scroller.Content = buttonStack;

			Grid.SetRow(scroller, 4);
			return scroller;
		}

		private View BuildEpisodeList()
		{
			m_episodeList = new CollectionView();
			m_episodeList.ItemTemplate = new DataTemplate(typeof(EpisodeRowTile));
			m_episodeList.SelectionMode = SelectionMode.Single;
			m_episodeList.SelectionChanged += OnEpisodeSelectionChanged;
			m_episodeList.ItemsSource = m_episodes;

			Grid.SetRow(m_episodeList, 5);
			return m_episodeList;
		}

		// Backlog (retention) settings: the retention policy and its count sit
		// side by side above the play/unsubscribe buttons. Mirrors the web
		// podcast detail's "Backlog" controls (TMP147).
		private View BuildSettings()
		{
			StackLayout settingsStack = new StackLayout();
			settingsStack.Spacing = 8;
			settingsStack.Padding = new Thickness(16, 0, 16, 12);

			Label heading = new Label();
			heading.Text = "Backlog";
			heading.FontSize = 12;
			heading.TextColor = ThumpColors.TextDim;
			settingsStack.Children.Add(heading);

			// Policy picker (fills the row) and its count (fixed, right) side by side.
			Grid optionsRow = new Grid();
			optionsRow.ColumnSpacing = 8;
			ColumnDefinition pickerColumn = new ColumnDefinition();
			pickerColumn.Width = GridLength.Star;
			ColumnDefinition valueColumn = new ColumnDefinition();
			valueColumn.Width = GridLength.Auto;
			optionsRow.ColumnDefinitions.Add(pickerColumn);
			optionsRow.ColumnDefinitions.Add(valueColumn);

			m_retentionPicker = new Picker();
			m_retentionPicker.TextColor = ThumpColors.OnBackground;
			m_retentionPicker.TitleColor = ThumpColors.TextDim;
			m_retentionPicker.Items.Add("Keep all episodes");
			m_retentionPicker.Items.Add("Keep newest");
			m_retentionPicker.Items.Add("Keep last (days)");
			m_retentionPicker.SelectedIndex = 0;
			m_retentionPicker.SelectedIndexChanged += OnRetentionChanged;
			Grid.SetColumn(m_retentionPicker, 0);
			optionsRow.Children.Add(m_retentionPicker);

			m_retentionValueEntry = new Entry();
			m_retentionValueEntry.Keyboard = Keyboard.Numeric;
			m_retentionValueEntry.Placeholder = "Count";
			m_retentionValueEntry.WidthRequest = 80;
			m_retentionValueEntry.TextColor = ThumpColors.OnBackground;
			m_retentionValueEntry.PlaceholderColor = ThumpColors.TextDim;
			m_retentionValueEntry.IsVisible = false;
			Grid.SetColumn(m_retentionValueEntry, 1);
			optionsRow.Children.Add(m_retentionValueEntry);

			settingsStack.Children.Add(optionsRow);

			HorizontalStackLayout saveRow = new HorizontalStackLayout();
			saveRow.Spacing = 12;
			Button saveButton = new Button();
			saveButton.Text = "Save";
			saveButton.TextColor = ThumpColors.OnBackground;
			saveButton.BackgroundColor = ThumpColors.Surface;
			saveButton.CornerRadius = 8;
			saveButton.FontSize = 14;
			saveButton.Padding = new Thickness(20, 8);
			saveButton.Clicked += OnSaveSettingsClicked;
			saveRow.Children.Add(saveButton);

			m_settingsStatusLabel = new Label();
			m_settingsStatusLabel.Text = "";
			m_settingsStatusLabel.FontSize = 12;
			m_settingsStatusLabel.TextColor = ThumpColors.TextDim;
			m_settingsStatusLabel.VerticalOptions = LayoutOptions.Center;
			saveRow.Children.Add(m_settingsStatusLabel);
			settingsStack.Children.Add(saveRow);

			Grid.SetRow(settingsStack, 3);
			return settingsStack;
		}

		public override void Initialize()
		{
			m_titleLabel.Text = m_podcast.Title;
			m_metaLabel.Text = m_podcast.EpisodeCount + " episodes";
			m_art.SetCoverArt(m_podcast.CoverArt);
			UpdateSubscribeButtonLabel();
			base.Initialize();

		}
		protected override void RefreshData()
		{
			// The podcast from the list endpoint carries no episodes; fetch the
			// full podcast (GetPodcast) which includes its episodes.
			MainView.MediaClient.GetPodcast(m_podcast.Id, OnPodcastLoaded);
			base.RefreshData();
		}
		private void OnPodcastLoaded(PulsePodcastDetails podcastDetails)
		{
			if (podcastDetails == null)
			{
				return;
			}

			// Refresh the header from the authoritative detail payload when the
			// server returned a richer Series record than the list summary.
			if (podcastDetails.Series != null)
			{
				m_podcast = podcastDetails.Series;
				m_titleLabel.Text = m_podcast.Title;
				m_metaLabel.Text = m_podcast.EpisodeCount + " episodes";
				m_art.SetCoverArt(m_podcast.CoverArt);
				UpdateSubscribeButtonLabel();
				PopulateSettings();
			}

			// Reconcile the episode list in place, then sort newest-first (new
			// episodes arrive at the top, but SyncFrom appends them at the end).
			List<PulsePodcastEpisode> incoming = podcastDetails.Episodes;
			if (incoming == null)
			{
				incoming = new List<PulsePodcastEpisode>();
			}
			SyncFrom<PulsePodcastEpisode>(m_episodes, incoming);
			Sort<PulsePodcastEpisode>(m_episodes, CompareEpisodeByPublishedDescending);
			// Keep the parallel playback list aligned with the displayed order.
			m_episodeTracks = BuildEpisodeTracks(new List<PulsePodcastEpisode>(m_episodes));
		}

		private static int CompareEpisodeByPublishedDescending(PulsePodcastEpisode first, PulsePodcastEpisode second)
		{
			return string.CompareOrdinal(second.PublishedDate, first.PublishedDate);
		}

		private void UpdateSubscribeButtonLabel()
		{
			if (m_podcast.Subscribed)
			{
				m_subscribeButton.Text = "Unsubscribe";
			}
			else
			{
				m_subscribeButton.Text = "Subscribe";
			}
		}

		private void PopulateSettings()
		{
			int policyIndex = RetentionPolicyToIndex(m_podcast.RetentionPolicy);
			m_retentionPicker.SelectedIndex = policyIndex;
			if (m_podcast.RetentionValue > 0)
			{
				m_retentionValueEntry.Text = m_podcast.RetentionValue.ToString();
			}
			else
			{
				m_retentionValueEntry.Text = "";
			}
			m_settingsStatusLabel.Text = "";
			UpdateRetentionValueVisibility();
		}

		private int RetentionPolicyToIndex(string policy)
		{
			for (int index = 0; index < s_retentionPolicies.Length; index++)
			{
				if (s_retentionPolicies[index] == policy)
				{
					return index;
				}
			}
			return 0;
		}

		private string IndexToRetentionPolicy(int index)
		{
			if (index >= 0 && index < s_retentionPolicies.Length)
			{
				return s_retentionPolicies[index];
			}
			return "KeepAll";
		}

		// "Keep all" has no count; the value entry only applies to KeepN/KeepDays.
		private void UpdateRetentionValueVisibility()
		{
			m_retentionValueEntry.IsVisible = m_retentionPicker.SelectedIndex != 0;
		}

		private void OnRetentionChanged(object sender, EventArgs e)
		{
			UpdateRetentionValueVisibility();
		}

		private void OnSaveSettingsClicked(object sender, EventArgs e)
		{
			string policy = IndexToRetentionPolicy(m_retentionPicker.SelectedIndex);
			int retentionValue = 0;
			int.TryParse(m_retentionValueEntry.Text, out retentionValue);
			if (retentionValue < 0)
			{
				retentionValue = 0;
			}
			m_settingsStatusLabel.Text = "Saving…";
			// Auto-download is no longer surfaced in the UI; preserve whatever the
			// feed already had so saving the backlog doesn't change it.
			MainView.MediaClient.UpdatePodcast(m_podcast.Id, policy, retentionValue, m_podcast.AutoDownload, (updated) =>
			{
				if (updated == null)
				{
					m_settingsStatusLabel.Text = "Save failed";
					return;
				}
				m_podcast = updated;
				PopulateSettings();
				m_settingsStatusLabel.Text = "Saved";
			});
		}

		private List<PulseTrack> BuildEpisodeTracks(List<PulsePodcastEpisode> episodes)
		{
			List<PulseTrack> tracks = new List<PulseTrack>();
			if (episodes == null)
			{
				return tracks;
			}
			for (int index = 0; index < episodes.Count; index++)
			{
				tracks.Add(EpisodeToTrack(episodes[index]));
			}
			return tracks;
		}

		// Adapter from the episode wire-type to the PulseTrack the player pipeline
		// expects. The stream endpoint takes a single Id for either kind, so the
		// episode Id is the track Id. Series art is used when the episode itself
		// has no cover (the server already points episode.CoverArt at the series
		// art id, but the fallback covers older payloads).
		private PulseTrack EpisodeToTrack(PulsePodcastEpisode episode)
		{
			PulseTrack track = new PulseTrack();
			track.Id = episode.Id;
			track.Title = episode.Title;
			track.Artist = m_podcast.Title;
			track.Album = m_podcast.Title;
			if (string.IsNullOrEmpty(episode.CoverArt))
			{
				track.CoverArt = m_podcast.CoverArt;
			}
			else
			{
				track.CoverArt = episode.CoverArt;
			}
			track.Duration = episode.Duration;
			track.IsSeries = true;
			return track;
		}

		private void OnEpisodeSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			PulsePodcastEpisode episode = m_episodeList.SelectedItem as PulsePodcastEpisode;
			// SelectedItem = null re-enters this handler with a null selection; skip it.
			if (episode == null)
			{
				return;
			}
			int index = -1;
			if (m_episodes != null)
			{
				index = m_episodes.IndexOf(episode);
			}
			if (index >= 0 && m_episodeTracks != null && m_episodeTracks.Count > 0)
			{
				m_mainView.OnPlayTracks(m_episodeTracks, index, eQueueSource.Podcast, m_podcast.Id);
			}
			m_episodeList.SelectedItem = null;
		}

		private void OnBackClicked(object sender, EventArgs e)
		{
			m_mainView.OnBackPressed();
		}

		private void OnPlayClicked(object sender, EventArgs e)
		{
			if (m_episodeTracks == null || m_episodeTracks.Count == 0)
			{
				return;
			}
			m_mainView.OnPlayTracks(m_episodeTracks, 0, eQueueSource.Podcast, m_podcast.Id);
		}

		private void OnSubscribeClicked(object sender, EventArgs e)
		{
			if (m_podcast.Subscribed)
			{
				MainView.MediaClient.UnsubscribePodcast(m_podcast.Id, (ok) =>
				{
					if (ok)
					{
						m_podcast.Subscribed = false;
						UpdateSubscribeButtonLabel();
					}
				});
			}
			else
			{
				MainView.MediaClient.SubscribePodcast(m_podcast.Id, (ok) =>
				{
					if (ok)
					{
						m_podcast.Subscribed = true;
						UpdateSubscribeButtonLabel();
					}
				});
			}
		}
	}
}
