using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PulseAPI.CSharp;
using PulseApp.Data;
using PulseApp.Pulse;
using PulseApp.Utility;

namespace PulseApp.Views
{
	/// <summary>
	/// Full-screen sign-in gate. Shown at boot while the client authenticates
	/// from stored credentials (covering the "signing in" time), and the redirect
	/// target whenever the app finds itself without a signed-in uid. On success it
	/// hands control back to MainView (Home); on failure it shows the form for a
	/// manual sign-in. Carries its own server address/port/scheme so a first run
	/// can be configured entirely from here.
	/// </summary>
	public class LoginView : PulseAppView
	{
		private static readonly Color s_successColor = Color.FromArgb("#3ddc84");
		private static readonly Color s_failColor = Color.FromArgb("#ef4444");

		private Entry m_serverIpEntry;
		private Entry m_serverPortEntry;
		private Switch m_httpsSwitch;
		private Entry m_usernameEntry;
		private Entry m_passwordEntry;
		private Button m_signInButton;
		private Label m_statusLabel;
		private Grid m_busyOverlay;
		private ActivityIndicator m_busyIndicator;

		public LoginView(MainView mainView) : base(mainView)
		{
		}

		protected override void BuildLayout()
		{
			BackgroundColor = PulseAppColors.Background;

			VerticalStackLayout form = new VerticalStackLayout();
			form.Spacing = 12;
			form.Padding = new Thickness(28);
			form.VerticalOptions = LayoutOptions.Center;
			form.HorizontalOptions = LayoutOptions.Fill;

			Label title = new Label();
			title.Text = "Sign in to Pulse";
			title.FontSize = 24;
			title.TextColor = PulseAppColors.OnBackground;
			title.HorizontalOptions = LayoutOptions.Center;
			form.Children.Add(title);

			form.Children.Add(BuildLabel("Server address"));
			m_serverIpEntry = BuildEntry();
			form.Children.Add(m_serverIpEntry);

			form.Children.Add(BuildLabel("Port"));
			m_serverPortEntry = BuildEntry();
			m_serverPortEntry.Keyboard = Keyboard.Numeric;
			form.Children.Add(m_serverPortEntry);

			HorizontalStackLayout httpsRow = new HorizontalStackLayout();
			httpsRow.Spacing = 10;
			m_httpsSwitch = new Switch();
			httpsRow.Children.Add(m_httpsSwitch);
			Label httpsLabel = BuildLabel("Use HTTPS");
			httpsLabel.VerticalOptions = LayoutOptions.Center;
			httpsRow.Children.Add(httpsLabel);
			form.Children.Add(httpsRow);

			form.Children.Add(BuildLabel("Username"));
			m_usernameEntry = BuildEntry();
			form.Children.Add(m_usernameEntry);

			form.Children.Add(BuildLabel("Password"));
			m_passwordEntry = BuildEntry();
			m_passwordEntry.IsPassword = true;
			form.Children.Add(m_passwordEntry);

			m_signInButton = new Button();
			m_signInButton.Text = "Sign In";
			m_signInButton.TextColor = PulseAppColors.OnBackground;
			m_signInButton.BackgroundColor = PulseAppColors.Surface;
			m_signInButton.Margin = new Thickness(0, 8, 0, 0);
			m_signInButton.Clicked += OnSignInClicked;
			form.Children.Add(m_signInButton);

			m_statusLabel = new Label();
			m_statusLabel.FontSize = 13;
			m_statusLabel.TextColor = PulseAppColors.TextSecondary;
			m_statusLabel.HorizontalOptions = LayoutOptions.Center;
			form.Children.Add(m_statusLabel);

			ScrollView scroll = new ScrollView();
			scroll.Content = form;

			Grid root = new Grid();
			root.Children.Add(scroll);
			root.Children.Add(BuildBusyOverlay());

			Content = root;
		}

		private Label BuildLabel(string text)
		{
			Label label = new Label();
			label.Text = text;
			label.FontSize = 13;
			label.TextColor = PulseAppColors.TextSecondary;
			return label;
		}

		private Entry BuildEntry()
		{
			Entry entry = new Entry();
			entry.TextColor = PulseAppColors.OnBackground;
			entry.BackgroundColor = PulseAppColors.Surface;
			entry.FontSize = 15;
			return entry;
		}

		private Grid BuildBusyOverlay()
		{
			m_busyOverlay = new Grid();
			m_busyOverlay.BackgroundColor = PulseAppColors.Background;
			m_busyOverlay.IsVisible = false;

			VerticalStackLayout column = new VerticalStackLayout();
			column.Spacing = 20;
			column.HorizontalOptions = LayoutOptions.Center;
			column.VerticalOptions = LayoutOptions.Center;

			Image logo = new Image();
			logo.Source = "pulse_logo.png";
			logo.Aspect = Aspect.AspectFit;
			logo.WidthRequest = 180;
			logo.HeightRequest = 180;
			logo.HorizontalOptions = LayoutOptions.Center;
			column.Children.Add(logo);

			m_busyIndicator = new ActivityIndicator();
			m_busyIndicator.Color = PulseAppColors.Accent;
			m_busyIndicator.WidthRequest = 36;
			m_busyIndicator.HeightRequest = 36;
			m_busyIndicator.HorizontalOptions = LayoutOptions.Center;
			column.Children.Add(m_busyIndicator);

			Label signingInLabel = new Label();
			signingInLabel.Text = "Signing in...";
			signingInLabel.FontSize = 15;
			signingInLabel.TextColor = PulseAppColors.TextSecondary;
			signingInLabel.HorizontalOptions = LayoutOptions.Center;
			column.Children.Add(signingInLabel);

			m_busyOverlay.Children.Add(column);
			return m_busyOverlay;
		}

		private void PrefillFields()
		{
			m_serverIpEntry.Text = PulseAppSettings.GetServerIp();
			m_serverPortEntry.Text = PulseAppSettings.GetServerPort();
			m_httpsSwitch.IsToggled = PulseAppSettings.GetUseHttps();
			m_usernameEntry.Text = PulseAppSettings.GetUsername();
			m_passwordEntry.Text = "";
		}

		/// <summary>
		/// Entry point from MainView at boot and on redirect. Prefills the form and,
		/// when a password is stored, attempts a sign-in immediately while showing a
		/// "Signing in..." state; otherwise it just shows the form for manual entry.
		/// </summary>
		public void BeginAutoLogin()
		{
			PrefillFields();
			string password = PulseAppSettings.GetPassword();
			if (string.IsNullOrEmpty(password))
			{
				SetBusy(false);
				m_statusLabel.Text = "Enter your credentials to sign in.";
				m_statusLabel.TextColor = PulseAppColors.TextSecondary;
				return;
			}
			SetBusy(true);
			m_statusLabel.Text = "Signing in...";
			m_statusLabel.TextColor = PulseAppColors.TextSecondary;
			AttemptLogin(PulseAppSettings.GetServerIp(), PulseAppSettings.GetServerPort(), PulseAppSettings.GetUseHttps(), PulseAppSettings.GetUsername(), password);
		}

		private void OnSignInClicked(object sender, EventArgs e)
		{
			string ip = m_serverIpEntry.Text;
			string port = m_serverPortEntry.Text;
			bool useHttps = m_httpsSwitch.IsToggled;
			string username = (m_usernameEntry.Text ?? "").Trim();
			string password = m_passwordEntry.Text ?? "";

			string validationError = ValidateServer(ip, port);
			if (!string.IsNullOrEmpty(validationError))
			{
				ShowError(validationError);
				return;
			}
			if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
			{
				ShowError("Username and password are required.");
				return;
			}
			SetBusy(true);
			m_statusLabel.Text = "Signing in...";
			m_statusLabel.TextColor = PulseAppColors.TextSecondary;
			AttemptLogin(ip, port, useHttps, username, password);
		}

		private string ValidateServer(string ip, string port)
		{
			if (string.IsNullOrWhiteSpace(ip))
			{
				return "Server address is required.";
			}
			int portNumber;
			if (!int.TryParse((port ?? "").Trim(), out portNumber) || portNumber < 1 || portNumber > 65535)
			{
				return "Port must be a number between 1 and 65535.";
			}
			return "";
		}

		/// <summary>
		/// Persists the server/credentials, clears stale cache + uid when the server
		/// changed, then logs in on a background thread. On success it stores the uid
		/// and hands back to MainView (Home); on failure it surfaces the reason and
		/// leaves the form up. A null result (no login response) is treated as a
		/// failure so we never advance into the app without a confirmed uid.
		/// </summary>
		private void AttemptLogin(string ip, string port, bool useHttps, string username, string password)
		{
			bool serverChanged = ip != PulseAppSettings.GetServerIp()
				|| port != PulseAppSettings.GetServerPort()
				|| useHttps != PulseAppSettings.GetUseHttps();
			bool credentialsChanged = username != PulseAppSettings.GetUsername()
				|| password != PulseAppSettings.GetPassword();
			if (serverChanged)
			{
				PulseAppSettings.SetUserID("");
				PulseAppSettings.SetToken("");
				PulseAppCache cache = MainView.Self.GetCache();
				cache.ExecuteAsync(() =>
				{
					cache.ClearCache();
				});
			}

			PulseAppSettings.SetServerIp(ip);
			PulseAppSettings.SetServerPort(port);
			PulseAppSettings.SetUsername(username);
			PulseAppSettings.SetPassword(password);
			PulseAppSettings.SetUseHttps(useHttps);

			MediaClient pulse = MainView.MediaClient;

			if (serverChanged || credentialsChanged)
			{
				pulse.SetServerParams(ip, port, username, password, useHttps);
			}

			Task.Run(() =>
			{
				pulse.Login(username, password, true, (loginResult) =>
				{
					bool ok = false;
					string message = "";
					
					if (loginResult == null)
					{
						message = "Could not reach the server.";
					}
					else if (loginResult.Outcome != eAuthOutcome.Ok)
					{
						message = loginResult.Outcome.ToString();
					}
					else
					{
						if (!string.IsNullOrEmpty(loginResult.Id))
						{
							PulseAppSettings.SetUserID(loginResult.Id);
							PulseAppSettings.SetToken(loginResult.Token);
							ok = true;
						}
						else
						{
							message = "login failed";
						}
					}
					MainThread.BeginInvokeOnMainThread(() =>
					{
						if (ok)
						{
							m_mainView.OnSignedIn();
						}
						else
						{
							ShowError(message);
						}
					});
				});
			});
		}

		private void ShowError(string message)
		{
			SetBusy(false);
			m_statusLabel.Text = message;
			m_statusLabel.TextColor = s_failColor;
		}

		private void SetBusy(bool busy)
		{
			m_signInButton.IsEnabled = !busy;
			m_busyOverlay.IsVisible = busy;
			m_busyIndicator.IsRunning = busy;
		}
	}
}
