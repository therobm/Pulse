using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Thump.Data;
using Thump.Pulse;
using Thump.Utility;

namespace Thump.Views
{
	public class SettingsView : ThumpView
	{
		private static readonly Color s_successColor = Color.FromArgb("#3ddc84");
		private static readonly Color s_failColor = Color.FromArgb("#ef4444");

		private Button m_normalizeOff;
		private Button m_normalizePerTrack;
		private Button m_normalizePerAlbum;
		private eNormalizeVolume m_normalize = eNormalizeVolume.Off;

		private Slider m_prefetchSlider;
		private Label m_prefetchValueLabel;
		private Slider m_cacheSizeSlider;
		private Label m_cacheSizeValueLabel;
		private ProgressBar m_usageBar;
		private Label m_usageLabel;
		private Label m_entriesLabel;
		private Label m_oldestLabel;
		private Button m_refreshCacheButton;
		private Button m_clearCacheButton;

		private Entry m_serverIpEntry;
		private Entry m_serverPortEntry;
		private Entry m_usernameEntry;
		private Entry m_passwordEntry;
		private Entry m_tokenEntry;
		private Button m_serverSubsonic;
		private Button m_serverPulse;
		private Button m_httpsHttps;
		private Button m_httpsHttp;
		private bool m_useHttps = true;
		private Label m_connectStatusLabel;

		public SettingsView(MainView mainView) : base(mainView)
		{

		}

		protected override void BuildLayout()
		{
			Stopwatch stopwatch = Stopwatch.StartNew();

			BackgroundColor = ThumpColors.Background;

			ScrollView scroll = new ScrollView();

			VerticalStackLayout stack = new VerticalStackLayout();
			stack.Spacing = 20;
			stack.Padding = new Thickness(16, 12);

			stack.Children.Add(BuildTitle());
			stack.Children.Add(BuildPlaybackSection());
			stack.Children.Add(BuildCachingSection());
			stack.Children.Add(BuildDiagnosticsSection());
			stack.Children.Add(BuildLoginSection());

			scroll.Content = stack;
			Content = scroll;

			Log.Perf("SettingsView.BuildLayout " + stopwatch.ElapsedMilliseconds + "ms");
		}

		private View BuildTitle()
		{
			Label header = new Label();
			header.Text = "Settings";
			header.FontSize = 24;
			header.FontAttributes = FontAttributes.Bold;
			header.TextColor = ThumpColors.OnBackground;
			return header;
		}

		private Label BuildSectionHeader(string text)
		{
			Label header = new Label();
			header.Text = text;
			header.FontSize = 13;
			header.FontAttributes = FontAttributes.Bold;
			header.TextColor = ThumpColors.Accent;
			header.Margin = new Thickness(0, 8, 0, 0);
			return header;
		}

		private Label BuildFieldLabel(string text)
		{
			Label label = new Label();
			label.Text = text;
			label.FontSize = 13;
			label.TextColor = ThumpColors.TextSecondary;
			return label;
		}

		private Entry BuildEntry()
		{
			Entry entry = new Entry();
			entry.TextColor = ThumpColors.OnBackground;
			entry.BackgroundColor = ThumpColors.Surface;
			entry.FontSize = 15;
			return entry;
		}

		private Button BuildSegmentButton(string text)
		{
			Button button = new Button();
			button.Text = text;
			button.TextColor = ThumpColors.OnBackground;
			button.BackgroundColor = ThumpColors.Surface;
			button.CornerRadius = 8;
			button.FontSize = 13;
			button.Padding = new Thickness(14, 4);
			button.HeightRequest = 36;
			return button;
		}

		private View BuildPlaybackSection()
		{
			VerticalStackLayout section = new VerticalStackLayout();
			section.Spacing = 12;
			section.Children.Add(BuildSectionHeader("Playback"));

			section.Children.Add(BuildFieldLabel("Normalize Volume"));

			HorizontalStackLayout normalizeRow = new HorizontalStackLayout();
			normalizeRow.Spacing = 8;

			m_normalizeOff = BuildSegmentButton("Off");
			m_normalizeOff.Clicked += OnNormalizeOffClicked;
			normalizeRow.Children.Add(m_normalizeOff);

			m_normalizePerTrack = BuildSegmentButton("Per Track");
			m_normalizePerTrack.Clicked += OnNormalizePerTrackClicked;
			normalizeRow.Children.Add(m_normalizePerTrack);

			m_normalizePerAlbum = BuildSegmentButton("Per Album");
			m_normalizePerAlbum.Clicked += OnNormalizePerAlbumClicked;
			normalizeRow.Children.Add(m_normalizePerAlbum);

			section.Children.Add(normalizeRow);
			return section;
		}

		private View BuildCachingSection()
		{
			VerticalStackLayout section = new VerticalStackLayout();
			section.Spacing = 12;
			section.Children.Add(BuildSectionHeader("Caching"));

			m_prefetchValueLabel = BuildFieldLabel("Prefetch Tracks: 10");
			section.Children.Add(m_prefetchValueLabel);

			m_prefetchSlider = new Slider();
			m_prefetchSlider.Minimum = 0;
			m_prefetchSlider.Maximum = 30;
			m_prefetchSlider.ValueChanged += OnPrefetchChanged;
			section.Children.Add(m_prefetchSlider);

			m_cacheSizeValueLabel = BuildFieldLabel("Cache Size: 500 MB");
			section.Children.Add(m_cacheSizeValueLabel);

			m_cacheSizeSlider = new Slider();
			m_cacheSizeSlider.Minimum = 0;
			m_cacheSizeSlider.Maximum = 5120;
			m_cacheSizeSlider.ValueChanged += OnCacheSizeChanged;
			section.Children.Add(m_cacheSizeSlider);

			m_usageLabel = BuildFieldLabel("Cache usage: 0 B / 500 MB");
			section.Children.Add(m_usageLabel);

			m_usageBar = new ProgressBar();
			m_usageBar.ProgressColor = ThumpColors.Accent;
			m_usageBar.Progress = 0;
			section.Children.Add(m_usageBar);

			m_entriesLabel = BuildFieldLabel("Cached Entries: 0");
			section.Children.Add(m_entriesLabel);

			m_oldestLabel = BuildFieldLabel("Oldest Cached Object: —");
			section.Children.Add(m_oldestLabel);

			Grid cacheButtonRow = new Grid();
			cacheButtonRow.Margin = new Thickness(0, 4, 0, 0);
			cacheButtonRow.ColumnSpacing = 8;
			ColumnDefinition cacheRefreshColumn = new ColumnDefinition();
			cacheRefreshColumn.Width = GridLength.Star;
			ColumnDefinition cacheClearColumn = new ColumnDefinition();
			cacheClearColumn.Width = GridLength.Star;
			cacheButtonRow.ColumnDefinitions.Add(cacheRefreshColumn);
			cacheButtonRow.ColumnDefinitions.Add(cacheClearColumn);

			m_refreshCacheButton = new Button();
			m_refreshCacheButton.Text = "Refresh";
			m_refreshCacheButton.TextColor = ThumpColors.OnBackground;
			m_refreshCacheButton.BackgroundColor = ThumpColors.Surface;
			m_refreshCacheButton.CornerRadius = 8;
			m_refreshCacheButton.FontSize = 15;
			m_refreshCacheButton.HeightRequest = 44;
			m_refreshCacheButton.Clicked += OnRefreshCacheClicked;
			Grid.SetColumn(m_refreshCacheButton, 0);
			cacheButtonRow.Children.Add(m_refreshCacheButton);

			m_clearCacheButton = new Button();
			m_clearCacheButton.Text = "Clear Cache";
			m_clearCacheButton.TextColor = ThumpColors.OnBackground;
			m_clearCacheButton.BackgroundColor = ThumpColors.Surface;
			m_clearCacheButton.CornerRadius = 8;
			m_clearCacheButton.FontSize = 15;
			m_clearCacheButton.HeightRequest = 44;
			m_clearCacheButton.Clicked += OnClearCacheClicked;
			Grid.SetColumn(m_clearCacheButton, 1);
			cacheButtonRow.Children.Add(m_clearCacheButton);

			section.Children.Add(cacheButtonRow);

			return section;
		}

		private View BuildDiagnosticsSection()
		{
			VerticalStackLayout section = new VerticalStackLayout();
			section.Spacing = 12;
			section.Children.Add(BuildSectionHeader("Diagnostics"));
			section.Children.Add(BuildFieldLabel("Export the app log file to share when reporting a problem, or reset it to drop old data before testing a new build."));

			Grid logButtonRow = new Grid();
			logButtonRow.Margin = new Thickness(0, 4, 0, 0);
			logButtonRow.ColumnSpacing = 8;
			ColumnDefinition logExportColumn = new ColumnDefinition();
			logExportColumn.Width = GridLength.Star;
			ColumnDefinition logResetColumn = new ColumnDefinition();
			logResetColumn.Width = GridLength.Star;
			logButtonRow.ColumnDefinitions.Add(logExportColumn);
			logButtonRow.ColumnDefinitions.Add(logResetColumn);

			Button exportButton = new Button();
			exportButton.Text = "Export Logs";
			exportButton.TextColor = ThumpColors.OnBackground;
			exportButton.BackgroundColor = ThumpColors.Surface;
			exportButton.CornerRadius = 8;
			exportButton.FontSize = 15;
			exportButton.HeightRequest = 44;
			exportButton.Clicked += OnExportLogsClicked;
			Grid.SetColumn(exportButton, 0);
			logButtonRow.Children.Add(exportButton);

			Button resetLogButton = new Button();
			resetLogButton.Text = "Reset Log";
			resetLogButton.TextColor = ThumpColors.OnBackground;
			resetLogButton.BackgroundColor = ThumpColors.Surface;
			resetLogButton.CornerRadius = 8;
			resetLogButton.FontSize = 15;
			resetLogButton.HeightRequest = 44;
			resetLogButton.Clicked += OnResetLogClicked;
			Grid.SetColumn(resetLogButton, 1);
			logButtonRow.Children.Add(resetLogButton);

			section.Children.Add(logButtonRow);

			return section;
		}

		private View BuildLoginSection()
		{
			VerticalStackLayout section = new VerticalStackLayout();
			section.Spacing = 12;
			section.Children.Add(BuildSectionHeader("Login"));

			section.Children.Add(BuildFieldLabel("Server IP"));
			m_serverIpEntry = BuildEntry();
			section.Children.Add(m_serverIpEntry);

			section.Children.Add(BuildFieldLabel("Server Port"));
			m_serverPortEntry = BuildEntry();
			m_serverPortEntry.Keyboard = Keyboard.Numeric;
			section.Children.Add(m_serverPortEntry);

			section.Children.Add(BuildFieldLabel("Username"));
			m_usernameEntry = BuildEntry();
			section.Children.Add(m_usernameEntry);

			section.Children.Add(BuildFieldLabel("Password"));
			m_passwordEntry = BuildEntry();
			m_passwordEntry.IsPassword = true;
			section.Children.Add(m_passwordEntry);

			section.Children.Add(BuildFieldLabel("Device Token"));
			m_tokenEntry = BuildEntry();
			section.Children.Add(m_tokenEntry);

			section.Children.Add(BuildFieldLabel("Connection"));
			HorizontalStackLayout httpsRow = new HorizontalStackLayout();
			httpsRow.Spacing = 8;

			m_httpsHttps = BuildSegmentButton("HTTPS");
			m_httpsHttps.Clicked += OnHttpsHttpsClicked;
			httpsRow.Children.Add(m_httpsHttps);

			m_httpsHttp = BuildSegmentButton("HTTP");
			m_httpsHttp.Clicked += OnHttpsHttpClicked;
			httpsRow.Children.Add(m_httpsHttp);
			section.Children.Add(httpsRow);

			Button connectButton = new Button();
			connectButton.Text = "Connect";
			connectButton.TextColor = ThumpColors.Background;
			connectButton.BackgroundColor = ThumpColors.Accent;
			connectButton.CornerRadius = 8;
			connectButton.FontSize = 15;
			connectButton.HeightRequest = 44;
			connectButton.Margin = new Thickness(0, 4, 0, 0);
			connectButton.Clicked += OnConnectClicked;
			section.Children.Add(connectButton);

			m_connectStatusLabel = new Label();
			m_connectStatusLabel.Text = "";
			m_connectStatusLabel.FontSize = 13;
			m_connectStatusLabel.TextColor = ThumpColors.TextSecondary;
			section.Children.Add(m_connectStatusLabel);

			return section;
		}

		public override void Initialize()
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			Log.Perf("SettingsView.Initialize start");


			SetNormalize(ThumpSettings.GetNormalizeVolume());

			int prefetch = ThumpSettings.GetPrefetchCount();
			m_prefetchSlider.Value = prefetch;
			m_prefetchValueLabel.Text = "Prefetch Tracks: " + prefetch;

			long limitBytes = ThumpSettings.GetCacheLimitBytes();
			int limitMb = (int)(limitBytes / (1024L * 1024L));
			m_cacheSizeSlider.Value = limitMb;
			m_cacheSizeValueLabel.Text = "Cache Size: " + FormatBytes(limitBytes);
			MainView.Self.GetCache().SetSizeLimitBytes(limitBytes);

			m_serverIpEntry.Text = ThumpSettings.GetServerIp();
			m_serverPortEntry.Text = ThumpSettings.GetServerPort();
			m_usernameEntry.Text = ThumpSettings.GetUsername();
			m_passwordEntry.Text = ThumpSettings.GetPassword();
			m_tokenEntry.Text = ThumpSettings.GetToken();
			SetUseHttps(ThumpSettings.GetUseHttps());

			base.Initialize();
		}

		public override void OnNavigatedTo()
		{
			RefreshCacheStats();
			base.OnNavigatedTo();
		}

		private void OnNormalizeOffClicked(object sender, EventArgs e)
		{
			SetNormalize(eNormalizeVolume.Off);
			ThumpSettings.SetNormalizeVolume(m_normalize);
		}

		private void OnNormalizePerTrackClicked(object sender, EventArgs e)
		{
			SetNormalize(eNormalizeVolume.PerTrack);
			ThumpSettings.SetNormalizeVolume(m_normalize);
		}

		private void OnNormalizePerAlbumClicked(object sender, EventArgs e)
		{
			SetNormalize(eNormalizeVolume.PerAlbum);
			ThumpSettings.SetNormalizeVolume(m_normalize);
		}

		private void SetNormalize(eNormalizeVolume value)
		{
			m_normalize = value;
			StyleSegment(m_normalizeOff, value == eNormalizeVolume.Off);
			StyleSegment(m_normalizePerTrack, value == eNormalizeVolume.PerTrack);
			StyleSegment(m_normalizePerAlbum, value == eNormalizeVolume.PerAlbum);
		}

		private void OnHttpsHttpsClicked(object sender, EventArgs e)
		{
			SetUseHttps(true);
			ThumpSettings.SetUseHttps(m_useHttps);
		}

		private void OnHttpsHttpClicked(object sender, EventArgs e)
		{
			SetUseHttps(false);
			ThumpSettings.SetUseHttps(m_useHttps);
		}

		private void SetUseHttps(bool value)
		{
			m_useHttps = value;
			StyleSegment(m_httpsHttps, value == true);
			StyleSegment(m_httpsHttp, value == false);
		}

		private void StyleSegment(Button button, bool active)
		{
			if (active)
			{
				button.BackgroundColor = ThumpColors.Accent;
				button.TextColor = ThumpColors.Background;
			}
			else
			{
				button.BackgroundColor = ThumpColors.Surface;
				button.TextColor = ThumpColors.OnBackground;
			}
		}

		private void OnPrefetchChanged(object sender, ValueChangedEventArgs e)
		{
			int count = (int)Math.Round(e.NewValue);
			m_prefetchValueLabel.Text = "Prefetch Tracks: " + count;
			ThumpSettings.SetPrefetchCount(count);
		}

		private void OnCacheSizeChanged(object sender, ValueChangedEventArgs e)
		{
			int mb = (int)Math.Round(e.NewValue);
			long bytes = (long)mb * 1024L * 1024L;
			m_cacheSizeValueLabel.Text = "Cache Size: " + FormatBytes(bytes);
			ThumpSettings.SetCacheLimitBytes(bytes);
			MainView.Self.GetCache().SetSizeLimitBytes(bytes);
		}

		private void OnRefreshCacheClicked(object sender, EventArgs e)
		{
			SetCacheButtonsEnabled(false);
			m_refreshCacheButton.Text = "Refreshing...";
			ThumpCache cache = MainView.Self.GetCache();
			cache.ExecuteAsync(() =>
			{
				ThumpCacheStats stats = cache.GetCacheStats();
				MainThread.BeginInvokeOnMainThread(() =>
				{
					ApplyCacheStats(stats);
					m_refreshCacheButton.Text = "Refresh";
					SetCacheButtonsEnabled(true);
				});
			});
		}

		private void OnClearCacheClicked(object sender, EventArgs e)
		{
			SetCacheButtonsEnabled(false);
			m_clearCacheButton.Text = "Clearing...";
			ThumpCache cache = MainView.Self.GetCache();
			cache.ExecuteAsync(() =>
			{
				cache.ClearCache();
				ThumpCacheStats stats = cache.GetCacheStats();
				MainThread.BeginInvokeOnMainThread(() =>
				{
					ApplyCacheStats(stats);
					m_clearCacheButton.Text = "Clear Cache";
					SetCacheButtonsEnabled(true);
				});
			});
		}

		// Both cache buttons share a busy state: a click disables both until the
		// background cache op completes and the meter is refreshed, so a slow op
		// (lock contention behind ongoing cache writes) reads as in-progress
		// rather than ignored.
		private void SetCacheButtonsEnabled(bool enabled)
		{
			if (m_refreshCacheButton != null)
			{
				m_refreshCacheButton.IsEnabled = enabled;
			}
			if (m_clearCacheButton != null)
			{
				m_clearCacheButton.IsEnabled = enabled;
			}
		}

		private async void OnExportLogsClicked(object sender, EventArgs e)
		{
			try
			{
				ShareFileRequest request = new ShareFileRequest();
				request.Title = "Thump logs";
				request.File = new ShareFile(Log.GetLogFilePath());
				await Share.Default.RequestAsync(request);
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		private void OnResetLogClicked(object sender, EventArgs e)
		{
			Log.Reset();
			Log.Info("Log reset by user.");
		}

		private string ValidateAndNormalizeServer(ref string ip, ref string port)
		{
			if (string.IsNullOrWhiteSpace(ip))
			{
				return "Server IP is required.";
			}
			if (string.IsNullOrWhiteSpace(port))
			{
				return "Server port is required.";
			}
			int portNumber;
			if (!int.TryParse(port.Trim(), out portNumber) || portNumber < 1 || portNumber > 65535)
			{
				return "Server port must be a number between 1 and 65535.";
			}
			/*
			Match match = Regex.Match(ip, @"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}");
			if (!match.Success)
			{
				return "Server IP is not a valid address.";
			}
			ip = match.Value;*/
			return "";
		}

		private void OnConnectClicked(object sender, EventArgs e)
		{
			string ip = m_serverIpEntry.Text;
			string port = m_serverPortEntry.Text;
			string user = m_usernameEntry.Text;
			string password = m_passwordEntry.Text;
			string token = m_tokenEntry.Text;

			string validationError = ValidateAndNormalizeServer(ref ip, ref port);
			if (!string.IsNullOrEmpty(validationError))
			{
				m_connectStatusLabel.Text = validationError;
				m_connectStatusLabel.TextColor = s_failColor;
				return;
			}

			ThumpSettings.SetServerIp(ip);
			ThumpSettings.SetServerPort(port);
			ThumpSettings.SetUsername(user);
			ThumpSettings.SetPassword(password);
			ThumpSettings.SetToken(token);
			ThumpSettings.SetUseHttps(m_useHttps);

			m_connectStatusLabel.Text = "Connecting…";
			m_connectStatusLabel.TextColor = ThumpColors.TextSecondary;

			MediaClient pulse = MainView.MediaClient;
			bool useHttps = m_useHttps;
			Task.Run(() =>
			{
				pulse.SetServerParams(ip, port, user, password,  useHttps);
				bool success = pulse.TestConnection(out JsonElement response);
				string message = "Unknown";
				if (!success && response.TryGetProperty("error", out JsonElement error))
				{
					message = JsonHelper.GetString(error, "message");
				}
				bool capturedSuccess = success;
				string capturedMessage = message;
				MainThread.BeginInvokeOnMainThread(() =>
				{
					if (capturedSuccess)
					{
						m_connectStatusLabel.Text = "Connected";
						m_connectStatusLabel.TextColor = s_successColor;
					}
					else
					{
						m_connectStatusLabel.Text = "Failed: " + capturedMessage;
						m_connectStatusLabel.TextColor = s_failColor;
					}
				});
			});
		}

		private void RefreshCacheStats()
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			Log.Perf("SettingsView.RefreshCacheStats dispatch (off UI thread via ExecuteAsync)");
			ThumpCache cache = MainView.Self.GetCache();
			cache.ExecuteAsync(() =>
			{
				Log.Perf("SettingsView.RefreshCacheStats sql lock acquired after " + stopwatch.ElapsedMilliseconds + "ms");
				Stopwatch queryStopwatch = Stopwatch.StartNew();
				ThumpCacheStats stats = cache.GetCacheStats();
				Log.Perf("SettingsView.RefreshCacheStats GetCacheStats query " + queryStopwatch.ElapsedMilliseconds + "ms (entries=" + stats.EntryCount + ", bytes=" + stats.BytesUsed + ")");
				MainThread.BeginInvokeOnMainThread(() =>
				{
					ApplyCacheStats(stats);
				});
			});
			Log.Perf("SettingsView.RefreshCacheStats returned to UI caller after " + stopwatch.ElapsedMilliseconds + "ms");
		}

		private void ApplyCacheStats(ThumpCacheStats stats)
		{
			long limitBytes = ThumpSettings.GetCacheLimitBytes();
			m_usageLabel.Text = "Cache usage: " + FormatBytes(stats.BytesUsed) + " / " + FormatBytes(limitBytes);
			if (limitBytes > 0)
			{
				double progress = (double)stats.BytesUsed / (double)limitBytes;
				if (progress > 1)
				{
					progress = 1;
				}
				m_usageBar.Progress = progress;
			}
			else
			{
				m_usageBar.Progress = 0;
			}
			m_entriesLabel.Text = "Cached Entries: " + stats.EntryCount;
			m_oldestLabel.Text = "Oldest Cached Object: " + FormatAge(stats.OldestFetchedUnix);
		}

		private static string FormatBytes(long bytes)
		{
			if (bytes >= 1024L * 1024L * 1024L)
			{
				double gb = (double)bytes / (1024.0 * 1024.0 * 1024.0);
				return gb.ToString("0.0") + " GB";
			}
			if (bytes >= 1024L * 1024L)
			{
				long mb = bytes / (1024L * 1024L);
				return mb + " MB";
			}
			if (bytes >= 1024L)
			{
				long kb = bytes / 1024L;
				return kb + " KB";
			}
			return bytes + " B";
		}

		private static string FormatAge(long fetchedUnix)
		{
			if (fetchedUnix <= 0)
			{
				return "—";
			}
			long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			long ageSeconds = nowUnix - fetchedUnix;
			if (ageSeconds < 0)
			{
				ageSeconds = 0;
			}
			long days = ageSeconds / 86400;
			long hours = (ageSeconds % 86400) / 3600;
			long minutes = (ageSeconds % 3600) / 60;
			return days + "d " + hours + "h " + minutes + "m";
		}
	}
}
