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
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			listboxApps.ItemsSource = applicationList;
		}

		private Dictionary<ApplicationBeingUpdated, Button> busyDownloadsWithButtons = new Dictionary<ApplicationBeingUpdated, Button>();
		private Dictionary<ApplicationBeingUpdated, bool> successfullyInstalledUpdate = new Dictionary<ApplicationBeingUpdated, bool>();
		private void buttonUpdate_Click(object sender, RoutedEventArgs e)
		{
			Button but = sender as Button;
			if (but == null) return;
			ApplicationBeingUpdated app = but.DataContext as ApplicationBeingUpdated;
			if (app == null) return;
			if (busyDownloadsWithButtons.ContainsKey(app))
			{
				UserMessages.ShowWarningMessage("Download already busy for application, please wait.");
				return;
			}
			but.IsEnabled = false;
			busyDownloadsWithButtons.Add(app, but);
			successfullyInstalledUpdate.Add(app, false);
			
			but.Content = "Downloading...";
			app.StatusMessage = "Downloading latest version, please be patient...";

			ThreadingInterop.PerformOneArgFunctionSeperateThread<ApplicationBeingUpdated>(
			(apptodownload) =>
			{
				Button tmpbut = busyDownloadsWithButtons[apptodownload];
				bool updateSuccess = apptodownload.DownloadLatestVersionAndInstall(
					(appAfterDownloadBeforeInstalling) =>
					{
						Button tmpbut2 = busyDownloadsWithButtons[appAfterDownloadBeforeInstalling];
						tmpbut2.Dispatcher.Invoke((Action)delegate
						{
							tmpbut2.Content = "Installing...";
							appAfterDownloadBeforeInstalling.StatusMessage = "Installing downloaded setup, please wait...";
						});
					});
				if (!updateSuccess)//If we failed to update, enable the button again
					tmpbut.Dispatcher.Invoke(
						(Action)delegate
						{
							tmpbut.IsEnabled = true;
						});
				else
				{
					tmpbut.Dispatcher.Invoke(
						(Action)delegate
						{
							if (apptodownload.HasBeenUpdated)
							{
								tmpbut.Content = "Up to date";
								apptodownload.StatusMessage = "Application successfully updated.";
							}
							else
							{
								tmpbut.Content = "Update";
								tmpbut.IsEnabled = true;
								apptodownload.StatusMessage = "Setup did not successfully update the application.";
							}
						});
				}
				busyDownloadsWithButtons.Remove(apptodownload);
				successfullyInstalledUpdate.Remove(apptodownload);
			},
			app,
			false);
		}

		private void listboxApps_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			listboxApps.SelectedItem = null;
		}
	}

	public class ApplicationBeingUpdated : INotifyPropertyChanged
	{
		public string ApplicationName { get; private set; }
		private PublishDetails NewerversionDetails;
		public bool HasBeenUpdated;

		private int _progresspercentage;
		public int ProgressPercentage { get { return _progresspercentage; } set { _progresspercentage = value; OnPropertyChanged("ProgressPercentage", "ProgressVisible"); } }
		public bool ProgressVisible { get { return this.ProgressPercentage != 0 && this.ProgressPercentage != 100; } }
		private string _statusmessage;
		public string StatusMessage { get { return _statusmessage; } set { _statusmessage = value; OnPropertyChanged("StatusMessage"); } }

		public ApplicationBeingUpdated(string ApplicationName, PublishDetails NewerversionDetails)
		{
			this.ApplicationName = ApplicationName;
			this.NewerversionDetails = NewerversionDetails;
			this.HasBeenUpdated = false;
		}

		public bool DownloadLatestVersionAndInstall(Action<ApplicationBeingUpdated> onDownloadCompleteBeforeRunningSetup)
		{
			if (onDownloadCompleteBeforeRunningSetup == null) onDownloadCompleteBeforeRunningSetup = delegate { };

			string localFileTempPath = Path.GetTempPath().TrimEnd('\\') + "\\Setup_Newest_" + NewerversionDetails.ApplicationName + ".exe";
			if (!NewerversionDetails.FtpUrl.Contains('?'))
			{
				UserMessages.ShowWarningMessage("Cannot obtain relative path from download URL, no ? found in url: " + NewerversionDetails.FtpUrl);
				return false;
			}
			int indexOfQuestionMark = NewerversionDetails.FtpUrl.IndexOf('?');
			var parsed = HttpUtility.ParseQueryString(NewerversionDetails.FtpUrl.Substring(indexOfQuestionMark + 1));
			var relativePaths = parsed.GetValues("relativepath");
			if (relativePaths == null || relativePaths.Length == 0)
			{
				UserMessages.ShowWarningMessage("Cannot obtain relative path from download URL, unable to find relativepath value in url:" + NewerversionDetails.FtpUrl);
				return false;
			}
			else if (relativePaths.Length > 1)
			{
				UserMessages.ShowWarningMessage("Cannot obtain relative path, multiple relative path values found in url: " + NewerversionDetails.FtpUrl);
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
				delegate { return true; });//TODO: Validate all HTTPS/SSL certificates

			if (downloadResult != true)//Error occurred, client or server side
			{
				if (UserMessages.Confirm("The downloaded was unsuccessful due to the following error, download it again?"
					+ Environment.NewLine + Environment.NewLine + err))
				{
					return DownloadLatestVersionAndInstall(onDownloadCompleteBeforeRunningSetup);
				}
			}

			onDownloadCompleteBeforeRunningSetup(this);
			//Can latest install automatically, look at the DownloadNow method, at the "else if (mustInstallSilently" section in MainWindow.xaml.cs of AutoUpdatr
			Process.Start(localFileTempPath).WaitForExit();
			//Check if the setup actually completed and the local version is now up to date
			string localVersion = FileVersionInfo.GetVersionInfo(PublishInterop.GetApplicationExePathFromApplicationName(ApplicationName)).FileVersion;
			if (localVersion == NewerversionDetails.ApplicationVersion)
				HasBeenUpdated = true;
			return true;
		}

		public event PropertyChangedEventHandler PropertyChanged = delegate { };
		public void OnPropertyChanged(params string[] propertyNames) { foreach (var pn in propertyNames) PropertyChanged(this, new PropertyChangedEventArgs(pn)); }
	}
}
