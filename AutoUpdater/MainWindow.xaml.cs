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
using System.Web;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Globalization;
using System.Collections.Concurrent;

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
		private bool mustInstallSilently = false;

		public MainWindow()
		{
			InitializeComponent();
		}

		public MainWindow(string currentVersion, DateTime? currentInstalledDate, PublishDetails newerversionDetails, bool installSilently)
		{
			InitializeComponent();

			mustInstallSilently = installSilently;

			System.Windows.Forms.Application.EnableVisualStyles();
			this.newerversionDetails = newerversionDetails;
			this.Title = "Update available for " + newerversionDetails.ApplicationName;

			if (currentVersion == null)
			{
				this.labelMessage.Content = "Download latest version of " + newerversionDetails.ApplicationName;
				this.labelCurrentVersion.Visibility = System.Windows.Visibility.Collapsed;
				this.labelCurrentVersionDate.Visibility = System.Windows.Visibility.Collapsed;
				this.labelNewVersion.Content = string.Format(
					"Newest version online is {0} ({1} kBs to be downloaded)",
					newerversionDetails.ApplicationVersion,
					GetKilobytesFromBytes(newerversionDetails.SetupSize));
				this.labelNewVersionDate.Content = "Published date is " + newerversionDetails.PublishedDate.ToString("yyyy-MM-dd HH:mm:ss");
			}
			else
			{
				this.labelMessage.Content = "Update available for " + newerversionDetails.ApplicationName;
				this.labelCurrentVersion.Content = "Current version is " + currentVersion;
				this.labelCurrentVersionDate.Content = "Date is " + (currentInstalledDate ?? DateTime.MinValue).ToString("yyyy-MM-dd HH:mm:ss");
				this.labelNewVersion.Content = string.Format(
					"Newest version online is {0} ({1} kBs to be downloaded)",
					newerversionDetails.ApplicationVersion,
					GetKilobytesFromBytes(newerversionDetails.SetupSize));
				this.labelNewVersionDate.Content = "Published date is " + newerversionDetails.PublishedDate.ToString("yyyy-MM-dd HH:mm:ss");
			}

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

		private enum VersionComparison { OnlineNewer, InstalledNewer, SameVersion, FailedToCheck };
		private static VersionComparison IsOnlineVersionNewer(string installedVersion, string onlineVersion, out string errorIfNullBecauseCannotCompare)
		{
			string versionsConcatenated = string.Format("InstalledVersion = {0}, OnlineVersion = {1}", installedVersion ?? "", onlineVersion ?? "");
			if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(onlineVersion))
			{
				errorIfNullBecauseCannotCompare = "InstalledVersion AND/OR OnlineVersion is empty: " + versionsConcatenated;
				return VersionComparison.FailedToCheck;
			}

			string[] installedSplitted = installedVersion.Split('.');
			string[] onlineSplitted = onlineVersion.Split('.');
			if (installedSplitted.Length != onlineSplitted.Length)
			{
				errorIfNullBecauseCannotCompare = "InstalledVersion and OnlineVersion not in same format: " + versionsConcatenated;
				return VersionComparison.FailedToCheck;
			}

			int tmpint;
			bool fail = false;
			installedSplitted.ToList().ForEach((s) => { if (!int.TryParse(s, out tmpint)) fail = true; });
			onlineSplitted.ToList().ForEach((s) => { if (!int.TryParse(s, out tmpint)) fail = true; });
			if (fail)
			{
				errorIfNullBecauseCannotCompare = "InstalledVersion and OnlineVersion must have integers between dots: " + versionsConcatenated;
				return VersionComparison.FailedToCheck;
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
					errorIfNullBecauseCannotCompare = null;
					return VersionComparison.InstalledNewer;
				}
				else
				{
					errorIfNullBecauseCannotCompare = null;
					return VersionComparison.OnlineNewer;
				}
			}
			errorIfNullBecauseCannotCompare = null;
			return VersionComparison.SameVersion;
		}

		public static bool? IsApplicationUpToDate(string ApplicationName, string installedVersion, out string errorIfNull, out PublishDetails detailsIfNewer)
		{
			//detailsIfNewer = null;//Only details if newer version available
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

				string errIfCannotCompare;
				var versionsComparison = IsOnlineVersionNewer(installedVersion, onlineVersion, out errIfCannotCompare);
				switch (versionsComparison)
				{
					case VersionComparison.OnlineNewer:
						detailsIfNewer = onlineAppDetails;
						errorIfNull = null;
						return false;
					case VersionComparison.InstalledNewer:
						detailsIfNewer = onlineAppDetails;
						errorIfNull = "InstalledVersion is newer than OnlineVersion: " + versionsConcatenated; ;
						return null;
					case VersionComparison.SameVersion:
						detailsIfNewer = null;
						errorIfNull = null;
						return true;
					case VersionComparison.FailedToCheck:
						detailsIfNewer = null;
						errorIfNull = errIfCannotCompare;
						return null;
					default:
						//This should never occur, unless the enum has an extra item
						detailsIfNewer = null;
						errorIfNull = "Error text not implemented for this enum value of VersionComparison = " + versionsComparison;
						return null;
				}

				//string versionsConcatenated = string.Format("InstalledVersion = {0}, OnlineVersion = {1}", installedVersion ?? "", onlineVersion ?? "");
				//if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(onlineVersion))
				//{
				//    errorIfNull = "InstalledVersion AND/OR OnlineVersion is empty: " + versionsConcatenated;
				//    return null;
				//}
				//string[] installedSplitted = installedVersion.Split('.');
				//string[] onlineSplitted = onlineVersion.Split('.');
				//if (installedSplitted.Length != onlineSplitted.Length)
				//{
				//    errorIfNull = "InstalledVersion and OnlineVersion not in same format: " + versionsConcatenated;
				//    return null;
				//}

				//int tmpint;
				//bool fail = false;
				//installedSplitted.ToList().ForEach((s) => { if (!int.TryParse(s, out tmpint)) fail = true; });
				//onlineSplitted.ToList().ForEach((s) => { if (!int.TryParse(s, out tmpint)) fail = true; });
				//if (fail)
				//{
				//    errorIfNull = "InstalledVersion and OnlineVersion must have integers between dots: " + versionsConcatenated;
				//    return null;
				//}

				////if (installedAppVersion.Equals(onlineVersion, StringComparison.InvariantCultureIgnoreCase))
				////    return VersionComparison.UpToDate;

				//for (int i = 0; i < installedSplitted.Length; i++)
				//{
				//    int tmpInstalledInt;
				//    int tmpOnlineInt;
				//    tmpInstalledInt = int.Parse(installedSplitted[i]);
				//    tmpOnlineInt = int.Parse(onlineSplitted[i]);

				//    if (tmpInstalledInt == tmpOnlineInt)
				//        continue;
				//    if (tmpInstalledInt > tmpOnlineInt)
				//    {
				//        detailsIfNewer = onlineAppDetails;
				//        errorIfNull = "InstalledVersion is newer than OnlineVersion: " + versionsConcatenated;
				//        return null;
				//    }
				//    else
				//    {
				//        errorIfNull = null;
				//        detailsIfNewer = onlineAppDetails;
				//        return false;
				//    }
				//}
				//errorIfNull = null;
				//return true;
			}
			else
			{
				if (errIfFail == WebInterop.cErrorIfNotFoundOnline)
					errorIfNull = "Update information not stored online yet for " + ApplicationName + ".";
				else
					errorIfNull = errIfFail;
				detailsIfNewer = null;
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
							tmpform = new MainWindow(InstalledVersion, File.GetCreationTime(applicationExePath), detailsIfNewer, false);
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

		public static void InstallLatest(string applicationName, bool installSilently)
		{
			PublishDetails onlineAppDetails = new PublishDetails();
			string errIfFail;
			bool populatesuccess = WebInterop.PopulateObjectFromOnline(
				PublishDetails.OnlineJsonCategory,
				applicationName + PublishDetails.LastestVersionJsonNamePostfix,
				onlineAppDetails,
				out errIfFail,
				TimeSpan.FromSeconds(10));

			Application.Current.Dispatcher.Invoke((Action)delegate
			{
				tmpform = new MainWindow(null, null, onlineAppDetails, installSilently);
				//tmpform.imageAppIcon.Source = IconsInterop.IconExtractor.Extract(applicationExePath).IconToImageSource();
				//MainWindow thisform = frm as MainWindow;
				try
				{
					//tmpform.Dispatcher
					//    .Invoke((Action)delegate
					//    {
					if (installSilently)
					{
						tmpform.Show();// Dialog();
						if (installSilently)//We will not show the form, was only created now
							tmpform.DownloadNow();
					}
					else
						tmpform.ShowDialog();
					//});
				}
				catch
				{
				}
			});
		}

		private const string cDateFormatForLastTimeCheckAllForUpdates = "yyyy_MM_dd__HH_mm_ss";
		private static string GetFilePathToStoreLastTimeCheckedAllForUpdates()
		{
			return SettingsInterop.GetFullFilePathInLocalAppdata("LastTimeCheckAllForUpdates.fjset", "AutoUpdater");
		}
		private static DateTime GetLastTimeCheckedAllForUpdates()
		{
			string filepath = GetFilePathToStoreLastTimeCheckedAllForUpdates();
			DateTime tmpParsedDate;
			if (!File.Exists(filepath)
				|| !DateTime.TryParseExact(File.ReadAllText(filepath).Trim(), cDateFormatForLastTimeCheckAllForUpdates, CultureInfo.InvariantCulture, DateTimeStyles.None, out tmpParsedDate))
				return DateTime.MinValue;
			return tmpParsedDate;
		}
		private static void SetLastTimeCheckedAllForUpdates(DateTime time, Action<string> onError)
		{
			try
			{
				File.WriteAllText(GetFilePathToStoreLastTimeCheckedAllForUpdates(), time.ToString(cDateFormatForLastTimeCheckAllForUpdates));
			}
			catch (Exception exc)
			{
				onError(exc.Message);
			}
		}

		private static Dictionary<string, string> GetListOfInstalledApplications()
		{
			Dictionary<string, string> tmpdict = new Dictionary<string, string>();
			using (var uninstallRootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryInterop.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32)
				.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
			{
				if (null == uninstallRootKey)
					return null;
				var appKeys = uninstallRootKey.GetSubKeyNames().ToArray();
				foreach (string appkeyname in appKeys)
				{
					try
					{
						using (RegistryKey appkey = uninstallRootKey.OpenSubKey(appkeyname))
						{
							object urlInfoValue = appkey.GetValue("URLInfoAbout");
							if (urlInfoValue == null)
								continue;//The value must exist for URLInfoAbout
							if (!urlInfoValue.ToString().StartsWith(SettingsSimple.HomePcUrls.Instance.AppsPublishingRoot, StringComparison.InvariantCultureIgnoreCase))
								continue;//The URLInfoAbout value must start with our AppsPublishingRoot
							//If we reached this point in the foreach loop, this application is one of our own, now make sure the EXE also exists
							object displayIcon = appkey.GetValue("DisplayIcon");
							//TODO: For now we use the DisplayIcon, is this the best way, what if DisplayIcon is different from EXE
							if (displayIcon == null)
								continue;//We need the DisplayIcon value, it contains the full path of the EXE
							if (!File.Exists(displayIcon.ToString()))
								continue;//The application is probably not installed
							//At this point we know the registry entry is our own application and it is actaully installed (file exists)
							string exePath = displayIcon.ToString();
							string appname = Path.GetFileNameWithoutExtension(exePath);
							if (!tmpdict.ContainsKey(appname))
								tmpdict.Add(appname, exePath);
						}
					}
					catch { }
				}
			}
			return tmpdict;
		}
		public static void CheckAndUpdateAllApplicationsToLatestVersion(Action<string> onError)
		{
			DateTime lastCheck = GetLastTimeCheckedAllForUpdates();
			if (DateTime.Now.Subtract(lastCheck).TotalMinutes < 10)//Do no check more than every 10 minutes
				return;
			SetLastTimeCheckedAllForUpdates(DateTime.Now, onError);

			if (onError == null) onError = delegate { };

			ConcurrentDictionary<string, PublishDetails> appsToBeUpdated = new ConcurrentDictionary<string, PublishDetails>();

			var installedApps = GetListOfInstalledApplications();
			Parallel.ForEach<KeyValuePair<string, string>>(installedApps,
				(appnameAndExepath) =>
				{
					string installedVersion = FileVersionInfo.GetVersionInfo(appnameAndExepath.Value).FileVersion;

					string errIfNull;
					PublishDetails pubdetails;
					bool? result = IsApplicationUpToDate(
						appnameAndExepath.Key,
						installedVersion,
						out errIfNull,
						out pubdetails);
					if (result == null)//Error occurred
					{
						ShowNoCallbackNotificationInterop.Notify(onError, string.Format("Could not check for application '{0}' updates: {1}", appnameAndExepath.Key, errIfNull), "Error", ShowNoCallbackNotificationInterop.NotificationTypes.Error);
						return;
					}
					if (result == false)//We have updates available
					{
						//InstallLatest(appnameAndExepath.Key, false);
						while (!appsToBeUpdated.TryAdd(appnameAndExepath.Key, pubdetails))
							Thread.Sleep(100);
					}
				});

			if (appsToBeUpdated.Count > 0)
			{
				//The applications (key=appname, value=exepath) for applications which require updates
				var tmpwin = new UpdatingApplicationsWindow(appsToBeUpdated);
				tmpwin.ShowDialog();
			}
		}

		WebClient client;
		//DateTime startTime;
		string localFileTempPath;
		private void downloadButton_Click(object sender, RoutedEventArgs e)
		{
			if (isSmall)
				return;

			DownloadNow();
		}

		private bool isBusyDownloading = false;
		private void DownloadNow()
		{
			if (isBusyDownloading)
			{
				UserMessages.ShowWarningMessage("Already busy downloading please be patient");
				return;
			}

			isBusyDownloading = true;
			/*WebInterop.EnsureHttpsTrustAll();*/
			labelStatus.Visibility = System.Windows.Visibility.Visible;
			labelStatus.Content = "Please wait, downloading...";
			labelStatus.UpdateLayout();
			progressBar1.Value = 0;
			progressBar1.Minimum = 0;
			progressBar1.Visibility = System.Windows.Visibility.Visible;
			progressBar1.Maximum = 100;
			progressBar1.UpdateLayout();

			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				try
				{
					//https://fjh.dyndns.org/downloadownapps.php?relativepath=minipopuptasks/Setup_MiniPopupTasks_1_0_0_52.exe
					localFileTempPath = Path.GetTempPath().TrimEnd('\\') + "\\Setup_Newest_" + newerversionDetails.ApplicationName + ".exe";

					if (!newerversionDetails.FtpUrl.Contains('?'))
					{
						UserMessages.ShowWarningMessage("Cannot obtain relative path from download URL, no ? found in url: " + newerversionDetails.FtpUrl);
						return;
					}

					int indexOfQuestionMark = newerversionDetails.FtpUrl.IndexOf('?');
					var parsed = HttpUtility.ParseQueryString(newerversionDetails.FtpUrl.Substring(indexOfQuestionMark + 1));
					var relativePaths = parsed.GetValues("relativepath");
					if (relativePaths == null || relativePaths.Length == 0)
					{
						UserMessages.ShowWarningMessage("Cannot obtain relative path from download URL, unable to find relativepath value in url:" + newerversionDetails.FtpUrl);
						return;
					}
					else if (relativePaths.Length > 1)
					{
						UserMessages.ShowWarningMessage("Cannot obtain relative path, multiple relative path values found in url: " + newerversionDetails.FtpUrl);
						return;
					}

					string err;
					bool? downloadResult = PhpDownloadingInterop.PhpDownloadFile(
						relativePaths[0],//We already checked for multiple items above
						localFileTempPath,
						null,//Download complete file at this stage
						out err,
						(progperc, bytespersec) =>
						{
							UpdateProgress(progperc, true);
							UpdateStatus(string.Format("Download speed = {0:0.###} kB/s", bytespersec / 1024D), true);
						},
						delegate { return true; });//TODO: Validate all HTTPS/SSL certificates

					CallFromSeparateThread(delegate
					{
						progressBar1.Visibility = System.Windows.Visibility.Collapsed;
						labelStatus.Visibility = System.Windows.Visibility.Collapsed;
					});

					if (downloadResult != true)//Error occurred, client or server side
					{
						if (UserMessages.Confirm("The downloaded was unsuccessful due to the following error, download it again?"
							+ Environment.NewLine + Environment.NewLine + err))
						{
							DownloadNow();
							return;
						}
					}
					else if (mustInstallSilently
						|| UserMessages.Confirm("The download is complete, do you want to close this application and install new version?"))
					{
						CallFromSeparateThread(delegate
						{
							labelMessage.Content = "Please be patient, busy closing application to install download...";
							labelCurrentVersion.Visibility = System.Windows.Visibility.Collapsed;
							labelNewVersion.Visibility = System.Windows.Visibility.Collapsed;
							clickHereToDownloadButton.Visibility = System.Windows.Visibility.Collapsed;
							progressBar1.Visibility = System.Windows.Visibility.Collapsed;
							labelStatus.Visibility = System.Windows.Visibility.Collapsed;
							if (mustInstallSilently)
							{
								bool allOpenProcsKilled = ProcessesInterop.KillProcess(
									newerversionDetails.ApplicationName,
									delegate { });//Dont need to do anything with the actionOnMessage)

								if (allOpenProcsKilled)
								{
									var proc = Process.Start(localFileTempPath, "/S");
									proc.WaitForExit();
									Process.Start(PublishInterop.GetApplicationExePathFromApplicationName(newerversionDetails.ApplicationName));
								}
								else//Just start the setup in non-silent mode if some processes with the same name are still open
									Process.Start(localFileTempPath);
								this.Close();
							}
							else
							{
								Process.Start(localFileTempPath);
								this.Close();
							}
						});
					}
					else
					{
						Process.Start("explorer", "/select,\"" + localFileTempPath + "\"");
					}
				}
				finally
				{
					isBusyDownloading = false;
				}
			},
			false);

			/*startTime = DateTime.Now;
			if (client == null)
			{
				client = new WebClient();
				int usingHttpsNowInsteadOfFtpFollowingCommented;
				//client.Credentials = new System.Net.NetworkCredential(
				//    ftpUsername,
				//    ftpPassword);
				client.DownloadFileCompleted += new System.ComponentModel.AsyncCompletedEventHandler(client_DownloadFileCompleted);
				client.DownloadProgressChanged += new System.Net.DownloadProgressChangedEventHandler(client_DownloadProgressChanged);
			}
			downloadFilename = Path.GetTempPath().TrimEnd('\\') + "\\Setup_Newest_" + newerversionDetails.ApplicationName + ".exe";

			int todoFindBetterWayForNextLine;
			string url =
				!newerversionDetails.FtpUrl.StartsWith("https:", StringComparison.InvariantCultureIgnoreCase)
				? "https" + newerversionDetails.FtpUrl.Substring(newerversionDetails.FtpUrl.IndexOf(":"))
				: newerversionDetails.FtpUrl;
			string strToRemove = "https://fjh.dyndns.org/francois/websites/firepuma/";
			if (url.StartsWith(strToRemove, StringComparison.InvariantCultureIgnoreCase))
				url = "https://fjh.dyndns.org/" + url.Substring(strToRemove.Length);
			//client.DownloadFileAsync(new Uri(newerversionDetails.FtpUrl), downloadFilename);
			client.DownloadFileAsync(new Uri(url), downloadFilename);*/
		}

		private void UpdateStatus(string msg, bool fromSeparateThread = true)
		{
			Action<string> act = (message) =>
			{
				labelStatus.Content = message;
				labelStatus.UpdateLayout();
			};
			if (fromSeparateThread)
				this.Dispatcher.Invoke(act, msg);
			else
				act(msg);
		}

		private void UpdateProgress(int progperc, bool fromSeparateThread = true)
		{
			Action<int> act = (prog) =>
			{
				progressBar1.Value = prog;
				progressBar1.UpdateLayout();
			};
			if (fromSeparateThread)
				this.Dispatcher.Invoke(act, progperc);
			else
				act(progperc);
		}

		private void CallFromSeparateThread(Action action)
		{
			this.Dispatcher.Invoke(action);
		}

		private void CallFromSeparateThread<T>(Action<T> action, T arg1)
		{
			this.Dispatcher.Invoke(action, arg1);
		}

		/*void client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
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
			if (localFileTempPath.FileToMD5Hash() != newerversionDetails.MD5Hash)
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
				Process.Start(localFileTempPath)
					;//.WaitForExit();
				//if (exitApplicationAction == null)
				//    Application.Exit();
				//else
				//    exitApplicationAction();
				this.Close();
			}
			else
				Process.Start("explorer", "/select,\"" + localFileTempPath + "\"");
			//}

			//if (restartDownloadRequired)
			//    goto restartDownload;
		}*/

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

	//public class PublishDetails
	//{
	//    public const string OnlineJsonCategory = "Own Applications";
	//    public const string LastestVersionJsonNamePostfix = " - latest";

	//    public string ApplicationName;
	//    public string ApplicationVersion;
	//    public long SetupSize;
	//    public string MD5Hash;
	//    public DateTime PublishedDate;
	//    public string FtpUrl;
	//    //TODO: May want to add TracUrl here
	//    public PublishDetails() { }
	//    public PublishDetails(string ApplicationName, string ApplicationVersion, long SetupSize, string MD5Hash, DateTime PublishedDate, string FtpUrl)
	//    {
	//        this.ApplicationName = ApplicationName;
	//        this.ApplicationVersion = ApplicationVersion;
	//        this.SetupSize = SetupSize;
	//        this.MD5Hash = MD5Hash;
	//        this.PublishedDate = PublishedDate;
	//        this.FtpUrl = FtpUrl;
	//    }
	//    public string GetJsonString() { return WebInterop.GetJsonStringFromObject(this, true); }
	//}

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
