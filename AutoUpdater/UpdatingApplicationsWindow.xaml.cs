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
using System.Collections.ObjectModel;
using System.ComponentModel;
using SharedClasses;
using System.IO;
using System.Web;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AutoUpdater
{
	/// <summary>
	/// Interaction logic for UpdatingApplicationsWindow.xaml
	/// </summary>
	public partial class UpdatingApplicationsWindow : Window
	{
		ObservableCollection<ApplicationBeingUpdated> applicationList = new ObservableCollection<ApplicationBeingUpdated>();

		public UpdatingApplicationsWindow(IDictionary<string, PublishDetails> appListWithNewerDetails)
		{
			InitializeComponent();

			foreach (var appname in appListWithNewerDetails)
				applicationList.Add(new ApplicationBeingUpdated(appname.Key, appname.Value));

			buttonUpdateAndInstallAllSilently.IsEnabled = applicationList.Count > 0;
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			listboxApps.ItemsSource = applicationList;
		}

		private void buttonDownload_Click(object sender, RoutedEventArgs e)
		{
			WPFHelper.GetFromObjectSender<ApplicationBeingUpdated>(sender)
				.DownloadNewestVersionForApplication(true);
		}

		private void buttonInstall_Click(object sender, RoutedEventArgs e)
		{
			WPFHelper.GetFromObjectSender<ApplicationBeingUpdated>(sender)
				.InstallApplication(false, true);
		}

		private void buttonInstallSilently_Click(object sender, RoutedEventArgs e)
		{
			WPFHelper.GetFromObjectSender<ApplicationBeingUpdated>(sender)
				.InstallApplication(true, true);
		}

		private void buttonUpdateAndInstallAllSilently_Click(object sender, RoutedEventArgs e)
		{
			buttonUpdateAndInstallAllSilently.IsEnabled = false;
			ThreadingInterop.DoAction(
				delegate
				{
					Parallel.ForEach(
						applicationList,
						app => app.DownloadAndInstall(true, true));

					this.Dispatcher.Invoke((Action)delegate { buttonUpdateAndInstallAllSilently.IsEnabled = true; });
				},
				false);
		}

		private void listboxApps_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			listboxApps.SelectedItem = null;
		}
	}

	public class ApplicationBeingUpdated : INotifyPropertyChanged
	{
		public enum ApplicationState
		{
			NotDownloaded,
			BusyDownloading, FailedDownload, SuccessfullyDownloaded,
			BusyInstallingNonsilent, FailedInstallNonsilent, SuccessfullyInstalledNonsilent,
			BusyInstallingSilently, FailedInstallSilently, SuccessfullyInstalledSilently
		};

		public string ApplicationName { get; private set; }
		private PublishDetails NewerversionDetails;
		public string DownloadedFilePathIfSucceeded { get; private set; }
		private bool HasBeenUpdated;// { get; private set; }
		private ApplicationState _currentstate;
		public ApplicationState CurrentState { get { return _currentstate; } set { _currentstate = value; OnPropertyChanged("CurrentState"); } }


		private int _progresspercentage;
		public int ProgressPercentage { get { return _progresspercentage; } set { _progresspercentage = value; OnPropertyChanged("ProgressPercentage", "ProgressVisible"); } }
		public bool ProgressVisible { get { return this.ProgressPercentage != 0 && this.ProgressPercentage != 100; } }
		private string _statusmessage;
		public string StatusMessage { get { return _statusmessage; } set { _statusmessage = value; OnPropertyChanged("StatusMessage"); } }
		private ImageSource _iconimage;
		public ImageSource IconImage { get { return _iconimage; } set { _iconimage = value; OnPropertyChanged("IconImage"); } }

		public ApplicationBeingUpdated(string ApplicationName, PublishDetails NewerversionDetails)
		{
			this.ApplicationName = ApplicationName;
			this.NewerversionDetails = NewerversionDetails;
			this.CurrentState = ApplicationState.NotDownloaded;
			this.IconImage = IconsInterop.IconExtractor.Extract(GetExePath(), IconsInterop.IconExtractor.IconSize.Large).IconToImageSource();
			this.HasBeenUpdated = false;
		}

		private string GetExePath()
		{
			return PublishInterop.GetApplicationExePathFromApplicationName(ApplicationName);
		}

		private bool DownloadLatestVersion(out string errIfFailed)//Action<ApplicationBeingUpdated> onDownloadCompleteBeforeRunningSetup)
		{
			//if (onDownloadCompleteBeforeRunningSetup == null) onDownloadCompleteBeforeRunningSetup = delegate { };

			string localFileTempPath = Path.GetTempPath().TrimEnd('\\') + "\\Setup_Newest_" + NewerversionDetails.ApplicationName + ".exe";
			if (!NewerversionDetails.FtpUrl.Contains('?'))
			{
				errIfFailed = "Cannot obtain relative path from download URL, no ? found in url: " + NewerversionDetails.FtpUrl;
				return false;
			}
			int indexOfQuestionMark = NewerversionDetails.FtpUrl.IndexOf('?');
			var parsed = HttpUtility.ParseQueryString(NewerversionDetails.FtpUrl.Substring(indexOfQuestionMark + 1));
			var relativePaths = parsed.GetValues("relativepath");
			if (relativePaths == null || relativePaths.Length == 0)
			{
				errIfFailed = "Cannot obtain relative path from download URL, unable to find relativepath value in url:" + NewerversionDetails.FtpUrl;
				return false;
			}
			else if (relativePaths.Length > 1)
			{
				errIfFailed = "Cannot obtain relative path, multiple relative path values found in url: " + NewerversionDetails.FtpUrl;
				return false;
			}

			string err;
			bool? downloadResult = PhpDownloadingInterop.PhpDownloadFile(
				relativePaths[0],//We already checked for multiple items above
				localFileTempPath,
				null,//Download complete file at this stage
				out err,
				(progperc, bytespersec) =>
				{
					ProgressPercentage = progperc;
					StatusMessage = string.Format("Download speed = {0:0.###} kB/s", bytespersec / 1024D);
				},
				delegate { return true; });

			if (downloadResult != true)//Error occurred, client or server side
			{
				errIfFailed = err;
				return false;
				/*if (UserMessages.Confirm("The downloaded was unsuccessful due to the following error, download it again?"
					+ Environment.NewLine + Environment.NewLine + err))
				{
					return DownloadLatestVersion();//onDownloadCompleteBeforeRunningSetup);
				}*/
			}

			this.DownloadedFilePathIfSucceeded = localFileTempPath;
			//Do not install in the method, rather in separate install method
			/*onDownloadCompleteBeforeRunningSetup(this);
			 InstallLocallyDownloadedSetup()
			 */
			errIfFailed = null;
			return true;
		}

		private bool InstallLocallyDownloadedSetup(bool installSilently)
		{
			if (installSilently)
				Process.Start(this.DownloadedFilePathIfSucceeded, "/S")
				.WaitForExit();
			else
				Process.Start(this.DownloadedFilePathIfSucceeded)
					.WaitForExit();

			//Check if the setup actually completed and the local version is now up to date
			string localVersion = FileVersionInfo.GetVersionInfo(GetExePath()).FileVersion;
			if (localVersion == NewerversionDetails.ApplicationVersion)
				CurrentState =
					installSilently
					? ApplicationState.SuccessfullyInstalledSilently
					: ApplicationState.SuccessfullyInstalledNonsilent;

			bool successfullyInstalled = 
				CurrentState == ApplicationState.SuccessfullyInstalledSilently
				|| CurrentState == ApplicationState.SuccessfullyInstalledNonsilent;
			this.HasBeenUpdated = successfullyInstalled;

			return successfullyInstalled;
		}

		private void PerformActionOnApplication(Action<ApplicationBeingUpdated> actionOnApp, bool separateThread, bool waitUntilFinishIfSeparateThread)
		{
			if (!separateThread)
				actionOnApp(this);
			else
				ThreadingInterop.PerformOneArgFunctionSeperateThread<ApplicationBeingUpdated>(
					actionOnApp,
					this,
					waitUntilFinishIfSeparateThread);
		}

		public void DownloadNewestVersionForApplication(bool separateThread = true)
		{
			PerformActionOnApplication(
				(app) =>
				{
					if (app.CurrentState == ApplicationBeingUpdated.ApplicationState.BusyDownloading)
					{
						UserMessages.ShowWarningMessage("Download already busy for application, please wait.");
						return;
					}

					app.CurrentState = ApplicationBeingUpdated.ApplicationState.BusyDownloading;
					app.StatusMessage = "Downloading latest version, please be patient...";

					string errIfFailed;
					bool downloadSuccess = app.DownloadLatestVersion(out errIfFailed);
					if (downloadSuccess)
					{
						app.CurrentState = ApplicationBeingUpdated.ApplicationState.SuccessfullyDownloaded;
						app.StatusMessage = "Update downloaded, click button to install it.";
					}
					else
					{
						app.CurrentState = ApplicationBeingUpdated.ApplicationState.FailedDownload;
						app.StatusMessage = "Failed reason: " + errIfFailed;
					}
				},
				separateThread,
				!separateThread);
		}

		public void InstallApplication(bool installSilently, bool separateThread = true)
		{
			PerformActionOnApplication(
				(app) =>
				{
					if (app.CurrentState == ApplicationBeingUpdated.ApplicationState.BusyInstallingSilently
						|| app.CurrentState == ApplicationBeingUpdated.ApplicationState.BusyInstallingNonsilent)
					{
						UserMessages.ShowWarningMessage("Installation already busy for application, please wait.");
						return;
					}

					app.CurrentState =
						installSilently
						? ApplicationBeingUpdated.ApplicationState.BusyInstallingSilently
						: ApplicationBeingUpdated.ApplicationState.BusyInstallingNonsilent;
					app.StatusMessage = "Waiting for installation to complete...";

					bool installSuccess = app.InstallLocallyDownloadedSetup(installSilently);
					if (installSuccess)
					{
						app.CurrentState =
							installSilently
							? ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledSilently
							: ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledNonsilent;
						app.StatusMessage = "Application successfully updated.";
					}
					else
					{
						app.CurrentState =
							installSilently
							? ApplicationBeingUpdated.ApplicationState.FailedInstallSilently
							: ApplicationBeingUpdated.ApplicationState.FailedInstallNonsilent;
						app.StatusMessage = "Setup did not successfully update the application.";
					}
				},
				separateThread,
				!separateThread);
		}

		public void DownloadAndInstall(bool installSilently, bool separateThread = true)
		{
			if (this.HasBeenUpdated)
			{
				this.StatusMessage = "Application already up to date";
				return;
			}

			PerformActionOnApplication(
				(app) =>
				{
					app.DownloadNewestVersionForApplication(false);
					if (this.DownloadedFilePathIfSucceeded != null)
						app.InstallApplication(installSilently, false);
				},
				separateThread,
				!separateThread);
		}

		public event PropertyChangedEventHandler PropertyChanged = delegate { };
		public void OnPropertyChanged(params string[] propertyNames) { foreach (var pn in propertyNames) PropertyChanged(this, new PropertyChangedEventArgs(pn)); }
	}

	public class ApplicationStateToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			var visibilityResult = Visibility.Visible;
			if (!(value is ApplicationBeingUpdated.ApplicationState))
				visibilityResult = Visibility.Collapsed;
			//else if (parameter == null)
			//	visibilityResult = Visibility.Visible;
			else
			{
				var state = (ApplicationBeingUpdated.ApplicationState)value;
				if (parameter.ToString().Equals("DownloadButton", StringComparison.InvariantCultureIgnoreCase))
					visibilityResult =
						state == ApplicationBeingUpdated.ApplicationState.NotDownloaded
						|| state == ApplicationBeingUpdated.ApplicationState.BusyDownloading
						|| state == ApplicationBeingUpdated.ApplicationState.FailedDownload
						|| state == ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledSilently
						|| state == ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledNonsilent
						? Visibility.Visible
						: Visibility.Collapsed;
				else if (parameter.ToString().Equals("SilentInstallButton", StringComparison.InvariantCultureIgnoreCase)
					|| parameter.ToString().Equals("InstallButton", StringComparison.InvariantCultureIgnoreCase))
					visibilityResult =
						state != ApplicationBeingUpdated.ApplicationState.NotDownloaded
						&& state != ApplicationBeingUpdated.ApplicationState.BusyDownloading
						&& state != ApplicationBeingUpdated.ApplicationState.FailedDownload
						&& state != ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledSilently
						&& state != ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledNonsilent
						? Visibility.Visible
						: Visibility.Collapsed;
			}
			return visibilityResult;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class ApplicationStateToEnabledConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			bool isEnabled = true;
			if (!(value is ApplicationBeingUpdated.ApplicationState))
				isEnabled = true;
			else
			{
				var state = (ApplicationBeingUpdated.ApplicationState)value;
				if (parameter.ToString().Equals("DownloadButton", StringComparison.InvariantCultureIgnoreCase))
				{
					if (state == ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledSilently
						|| state == ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledNonsilent)
						isEnabled = false;
				}
			}
			return isEnabled;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class ApplicationStateToButtonTextConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			var buttonText = "Unknown";
			//if (!(value is ApplicationBeingUpdated.ApplicationState))
			//	buttonText = "Unknown";
			//else if (parameter == null)
			//	visibilityResult = Visibility.Visible;
			//else
			//{
			var state = (ApplicationBeingUpdated.ApplicationState)value;
			if (parameter.ToString().Equals("DownloadButton", StringComparison.InvariantCultureIgnoreCase))
			{
				if (state == ApplicationBeingUpdated.ApplicationState.NotDownloaded)
					buttonText = "Download";
				else if (state == ApplicationBeingUpdated.ApplicationState.BusyDownloading)
					buttonText = "Donwloading...";
				else if (state == ApplicationBeingUpdated.ApplicationState.FailedDownload)
					buttonText = "Failed, retry?";
				else if (state == ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledSilently
					|| state == ApplicationBeingUpdated.ApplicationState.SuccessfullyInstalledNonsilent)
					buttonText = "Successfully updated";
				else
					buttonText = "Unknown state for DownloadButton";
			}
			else if (parameter.ToString().Equals("InstallButton", StringComparison.InvariantCultureIgnoreCase))
			{
				if (state == ApplicationBeingUpdated.ApplicationState.SuccessfullyDownloaded
					|| state == ApplicationBeingUpdated.ApplicationState.BusyInstallingSilently
					|| state == ApplicationBeingUpdated.ApplicationState.FailedInstallSilently)
					buttonText = "Install now";
				else if (state == ApplicationBeingUpdated.ApplicationState.BusyInstallingNonsilent)
					buttonText = "Installing...";
				else if (state == ApplicationBeingUpdated.ApplicationState.FailedInstallNonsilent)
					buttonText = "Install failed, retry?";
				else
					buttonText = "Unknown state for InstallButton";
			}
			else if (parameter.ToString().Equals("SilentInstallButton", StringComparison.InvariantCultureIgnoreCase))
			{
				if (state == ApplicationBeingUpdated.ApplicationState.SuccessfullyDownloaded
					|| state == ApplicationBeingUpdated.ApplicationState.BusyInstallingNonsilent
					|| state == ApplicationBeingUpdated.ApplicationState.FailedInstallNonsilent)
					buttonText = "Install silently now";
				else if (state == ApplicationBeingUpdated.ApplicationState.BusyInstallingSilently)
					buttonText = "Installing silently...";
				else if (state == ApplicationBeingUpdated.ApplicationState.FailedInstallSilently)
					buttonText = "Install silently failed, retry?";
				else
					buttonText = "Unknown state for SilentInstallButton";
			}
			//}
			return buttonText;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
