using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.IO;
using System.Diagnostics;
using SharedClasses;
using System.Threading;
using System.Windows.Threading;
using System.Net;
using System.ComponentModel;

namespace AutoUpdater
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private PublishDetails newerversionDetails;
		private const string ftpUsername = "ownapps";
		private const string ftpPassword = "ownappsverylongpassword";
		ScaleTransform originalScale;
		ScaleTransform smallScale = new ScaleTransform(0.1, 0.1);

		public MainWindow()
		{
			InitializeComponent();
		}

		public MainWindow(string currentVersion, PublishDetails newerversionDetails)
		{
			InitializeComponent();

			System.Windows.Forms.Application.EnableVisualStyles();
			this.newerversionDetails = newerversionDetails;
			this.Title = "Update available for " + newerversionDetails.ApplicationName;

			this.labelMessage.Content = "Update available for " + newerversionDetails.ApplicationName;
			this.labelCurrentVersion.Content = "Current version is " + currentVersion;
			this.labelNewVersion.Content = string.Format(
				"Newest version online is {0} ({1} kBs to be downloaded)",
				newerversionDetails.ApplicationVersion,
				GetKilobytesFromBytes(newerversionDetails.SetupSize));

			originalScale = mainBorder.LayoutTransform as ScaleTransform;
			if (originalScale == null)
				UserMessages.ShowErrorMessage("ScaleTransform is null");
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			WpfNotificationWindow.CloseNotificationWindow();
			//Application.Current.Dispatcher.InvokeShutdown();
		}

		private static double GetKilobytesFromBytes(long bytes, int decimals = 3)
		{
			return Math.Round((double)bytes / (double)1024, decimals);
		}

		public static bool? IsApplicationUpToDate(string ApplicationName, string installedVersion, out string errorIfNull, out PublishDetails detailsIfNewer)
		{
			detailsIfNewer = null;//Only details if newer version available
			PublishDetails onlineAppDetails = new PublishDetails();
			string errIfFail;
			bool populatesuccess = WebInterop.PopulateObjectFromOnline(
				PublishDetails.OnlineJsonCategory,
				ApplicationName + PublishDetails.LastestVersionJsonNamePostfix,
				onlineAppDetails,
				out errIfFail,
				TimeSpan.FromSeconds(10));
			if (populatesuccess)
			{
				//return CompareVersions(installedVersion, onlineAppDetails.ApplicationVersion);
				string onlineVersion = onlineAppDetails.ApplicationVersion;
				string versionsConcatenated = string.Format("InstalledVersion = {0}, OnlineVersion = {1}", installedVersion ?? "", onlineVersion ?? "");
				if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(onlineVersion))
				{
					errorIfNull = "InstalledVersion AND/OR OnlineVersion is empty: " + versionsConcatenated;
					return null;
				}
				string[] installedSplitted = installedVersion.Split('.');
				string[] onlineSplitted = onlineVersion.Split('.');
				if (installedSplitted.Length != onlineSplitted.Length)
				{
					errorIfNull = "InstalledVersion and OnlineVersion not in same format: " + versionsConcatenated;
					return null;
				}

				int tmpint;
				bool fail = false;
				installedSplitted.ToList().ForEach((s) => { if (!int.TryParse(s, out tmpint)) fail = true; });
				onlineSplitted.ToList().ForEach((s) => { if (!int.TryParse(s, out tmpint)) fail = true; });
				if (fail)
				{
					errorIfNull = "InstalledVersion and OnlineVersion must have integers between dots: " + versionsConcatenated;
					return null;
				}

				//if (installedAppVersion.Equals(onlineVersion, StringComparison.InvariantCultureIgnoreCase))
				//    return VersionComparison.UpToDate;

				for (int i = 0; i < installedSplitted.Length; i++)
				{
					int tmpInstalledInt;
					int tmpOnlineInt;
					tmpInstalledInt = int.Parse(installedSplitted[i]);
					tmpOnlineInt = int.Parse(onlineSplitted[i]);

					if (tmpInstalledInt == tmpOnlineInt)
						continue;
					if (tmpInstalledInt > tmpOnlineInt)
					{
						errorIfNull = "InstalledVersion is newer than OnlineVersion: " + versionsConcatenated;
						return null;
					}
					else
					{
						errorIfNull = null;
						detailsIfNewer = onlineAppDetails;
						return false;
					}
				}
				errorIfNull = null;
				return true;
			}
			else
			{
				if (errIfFail == WebInterop.cErrorIfNotFoundOnline)
					errorIfNull = "Update information not stored online yet for " + ApplicationName + ".";
				else
					errorIfNull = errIfFail;
				return null;
			}
		}

		public static Thread checkForUpdatesThread;
		private static MainWindow tmpform;
		public static void CheckForUpdates(string applicationExePath)
		{
			string ApplicationName = FileVersionInfo.GetVersionInfo(applicationExePath).ProductName;
			var InstalledVersion = FileVersionInfo.GetVersionInfo(applicationExePath).FileVersion;//File.Exists(versionfileFullpath) ? File.ReadAllText(versionfileFullpath).Trim() : "";

			//int remove;
			////ApplicationName = "GenericTextFunctions";
			//ApplicationName = "GenericTextFunctions.vshost";
			//InstalledVersion = "0.0.0.0";

			PublishDetails detailsIfNewer;
			string errIfFail;
			bool? uptodate = IsApplicationUpToDate(ApplicationName, InstalledVersion, out errIfFail, out detailsIfNewer);
			if (uptodate == null)//Failed to check
			{
				App.CurrentExitCode = (int)AutoUpdating.ExitCodes.UnableToCheckForUpdatesErrorCode;

				Console.Error.WriteLine("Unable to check for updates: " + errIfFail);
				WpfNotificationWindow.ShowNotification("Cannot check for updates of application " + ApplicationName + ": " + errIfFail,
				notificationType: ShowNoCallbackNotificationInterop.NotificationTypes.Warning,
				onCloseCallback_WasClickedToCallback: (closeobj, wascallbackclicked) =>
				{
					WpfNotificationWindow.CloseNotificationWindow();
					//Application.Current.Dispatcher.InvokeShutdown();
					//if (!wascallbackclicked)Should always close on click too, was unable to check for updates, cannot do anything else
					App.ShutDownThisApplication();
				},
				timeout: null);
			}
			else if (uptodate == false)//Newer version available
			{
				App.CurrentExitCode = (int)AutoUpdating.ExitCodes.NewVersionAvailableExitCode;

				WpfNotificationWindow.ShowNotification(
					string.Format("{0} has an update available (from {1} to {2}), click to update", ApplicationName, InstalledVersion, detailsIfNewer.ApplicationVersion),
					ShowNoCallbackNotificationInterop.NotificationTypes.Success,
					null,
					leftClickCallback: (obj) =>//frm) =>
					{
						Application.Current.Dispatcher.Invoke((Action)delegate
						{
							tmpform = new MainWindow(InstalledVersion, detailsIfNewer);
							tmpform.imageAppIcon.Source = IconsInterop.IconExtractor.Extract(applicationExePath).IconToImageSource();
							//MainWindow thisform = frm as MainWindow;
							try
							{
								//tmpform.Dispatcher
								//    .Invoke((Action)delegate
								//    {
								tmpform.Show();// Dialog();
								//});
							}
							catch
							{
							}
						});
					},
					//leftClickCallbackArgument: tmpform,
					onCloseCallback_WasClickedToCallback: (closeobj, wascallbackclicked) =>
					{
						WpfNotificationWindow.CloseNotificationWindow();
						//Application.Current.Dispatcher.InvokeShutdown();
						if (!wascallbackclicked)
							App.ShutDownThisApplication();
					});
			}
			else//Up to date
			{
				App.CurrentExitCode = (int)AutoUpdating.ExitCodes.UpToDateExitCode;
				//Application.Current.Dispatcher.InvokeShutdown();
				App.ShutDownThisApplication();
			}
		}

		WebClient client;
		DateTime startTime;
		string downloadFilename;
		private void Button_Click(object sender, RoutedEventArgs e)
		{
			if (isSmall)
				return;

			DownloadNow();
		}

		private void DownloadNow()
		{
			labelStatus.Visibility = System.Windows.Visibility.Visible;
			labelStatus.Content = "Please wait, downloading...";
			progressBar1.Value = 0;
			progressBar1.Minimum = 0;
			progressBar1.Visibility = System.Windows.Visibility.Visible;
			progressBar1.Maximum = 100;

			startTime = DateTime.Now;
			if (client == null)
			{
				client = new WebClient();
				client.Credentials = new System.Net.NetworkCredential(
					ftpUsername,
					ftpPassword);
				client.DownloadFileCompleted += new System.ComponentModel.AsyncCompletedEventHandler(client_DownloadFileCompleted);
				client.DownloadProgressChanged += new System.Net.DownloadProgressChangedEventHandler(client_DownloadProgressChanged);
			}
			downloadFilename = Path.GetTempPath().TrimEnd('\\') + "\\Setup_Newest_" + newerversionDetails.ApplicationName + ".exe";
			client.DownloadFileAsync(new Uri(newerversionDetails.FtpUrl), downloadFilename);
		}

		void client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
		{
			int progressPercentage = (int)Math.Round((double)100 * (double)e.BytesReceived / (double)newerversionDetails.SetupSize);//ev.ProgressPercentage;
			double kiloBytesPerSecond = Math.Round(GetKilobytesFromBytes(e.BytesReceived) / DateTime.Now.Subtract(startTime).TotalSeconds, 3);
			string statusMessage = string.Format(
						"Downloading {0}/{1} at {2} kB/s",
						e.BytesReceived,
						newerversionDetails.SetupSize, //ev.TotalBytesToReceive,
						kiloBytesPerSecond);
			progressBar1.Value = progressPercentage;
			labelStatus.Content = statusMessage;

			if (e.BytesReceived == newerversionDetails.SetupSize)
			{
				labelStatus.Content = string.Format("Download complete ({0} bytes)", e.BytesReceived);
				progressBar1.Visibility = System.Windows.Visibility.Collapsed;
			}
			progressBar1.Refresh();
			labelStatus.Refresh();
			this.Refresh();
			//TODO: FFS!!! Progress updates does not fire at work, could be caching, firewall, etc???
		}

		void client_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
		{
			if (downloadFilename.FileToMD5Hash() != newerversionDetails.MD5Hash)
			{
				if (UserMessages.Confirm("The downloaded file is corrupt (different MD5Hash), download it again?"))
				{
					DownloadNow();
					return;
				}
			}
			else if (UserMessages.Confirm("The download is complete, do you want to close this application and install new version?"))
			{
				//InvokeDispatcherAction(delegate
				//{
				labelMessage.Content = "Please be patient, busy closing application to install download...";
				labelCurrentVersion.Visibility = System.Windows.Visibility.Collapsed;
				labelNewVersion.Visibility = System.Windows.Visibility.Collapsed;
				clickHereToDownloadButton.Visibility = System.Windows.Visibility.Collapsed;
				progressBar1.Visibility = System.Windows.Visibility.Collapsed;
				labelStatus.Visibility = System.Windows.Visibility.Collapsed;
				//});
				Process.Start(downloadFilename)
					;//.WaitForExit();
				//if (exitApplicationAction == null)
				//    Application.Exit();
				//else
				//    exitApplicationAction();
				this.Close();
			}
			else
				Process.Start("explorer", "/select,\"" + downloadFilename + "\"");
			//}

			//if (restartDownloadRequired)
			//    goto restartDownload;
		}

		private Point startPoint;
		private void StackPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			startPoint = e.GetPosition(this);
			//DragMove();
			//e.Handled = true;
		}

		private void mainBorder_PreviewMouseMove(object sender, MouseEventArgs e)
		{
			var currentPoint = e.GetPosition(this);
			if (e.LeftButton == MouseButtonState.Pressed &&
				//this.IsMouseCaptured &&
				(Math.Abs(currentPoint.X - startPoint.X) >
					SystemParameters.MinimumHorizontalDragDistance ||
				Math.Abs(currentPoint.Y - startPoint.Y) >
					SystemParameters.MinimumVerticalDragDistance))
			{
				// Prevent Click from firing
				this.ReleaseMouseCapture();
				DragMove();
			}
		}

		private void Button_Click_1(object sender, RoutedEventArgs e)
		{
			MakeSmall();
		}

		bool isSmall = false;
		Point? lastNotSmallPos = null;
		private void MakeSmall()
		{
			if (isSmall)
				return;
			lastNotSmallPos = new Point(this.Left, this.Top);
			this.mainBorder.LayoutTransform = smallScale;
			sliderOpacity.IsEnabled = false;
			this.Left = SystemParameters.WorkArea.Right - (this.Width * smallScale.ScaleX);
			this.Top = SystemParameters.WorkArea.Bottom - (this.Height * smallScale.ScaleY);
			this.UpdateLayout();
			isSmall = true;
		}

		private void thisMainWindow_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			if (!isSmall)
				MakeSmall();
			else
				MakeNormalSize();
			e.Handled = true;
		}

		private void MakeNormalSize()
		{
			this.mainBorder.LayoutTransform = originalScale;
			sliderOpacity.IsEnabled = true;
			if (lastNotSmallPos.HasValue)
			{
				this.Left = lastNotSmallPos.Value.X;
				this.Top = lastNotSmallPos.Value.Y;
			}
			lastNotSmallPos = null;
			this.UpdateLayout();
			isSmall = false;
		}

		private void labelMessage_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			this.ReleaseMouseCapture();
		}

		private void Button_Click_2(object sender, RoutedEventArgs e)
		{
			this.Close();
		}

		private void aboutLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			new AboutWindow2(new System.Collections.ObjectModel.ObservableCollection<DisplayItem>()
			{
				new DisplayItem("Author", "Francois Hill"),
				new DisplayItem("Icon obtained from", "http://www.visualpharm.com", "http://www.visualpharm.com")
			})
			.ShowDialog();
		}
	}

	public class PublishDetails
	{
		public const string OnlineJsonCategory = "Own Applications";
		public const string LastestVersionJsonNamePostfix = " - latest";

		public string ApplicationName;
		public string ApplicationVersion;
		public long SetupSize;
		public string MD5Hash;
		public DateTime PublishedDate;
		public string FtpUrl;
		//TODO: May want to add TracUrl here
		public PublishDetails() { }
		public PublishDetails(string ApplicationName, string ApplicationVersion, long SetupSize, string MD5Hash, DateTime PublishedDate, string FtpUrl)
		{
			this.ApplicationName = ApplicationName;
			this.ApplicationVersion = ApplicationVersion;
			this.SetupSize = SetupSize;
			this.MD5Hash = MD5Hash;
			this.PublishedDate = PublishedDate;
			this.FtpUrl = FtpUrl;
		}
		public string GetJsonString() { return WebInterop.GetJsonStringFromObject(this, true); }
	}

	public static class ExtensionMethods
	{
		private static Action EmptyDelegate = delegate() { };

		public static void Refresh(this UIElement uiElement)
		{
			uiElement.Dispatcher.Invoke(DispatcherPriority.Render, EmptyDelegate);
		}

		public static void InvokeOnDispatcher(this UIElement uiElement, Action action)
		{
			uiElement.Dispatcher.Invoke(DispatcherPriority.Send, action);
		}
	}
}
