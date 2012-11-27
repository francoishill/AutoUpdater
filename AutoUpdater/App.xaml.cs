using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;
using SharedClasses;

namespace AutoUpdater
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private bool WasAlreadyCalledByIttself()
		{
			var args = Environment.GetCommandLineArgs();
			//return args.Length >= 4 && args[3].Equals(SharedClasses.AutoUpdating.cCalledItsselfThirdParameter, StringComparison.InvariantCultureIgnoreCase);
			return args.Contains(SharedClasses.AutoUpdating.cCalledItsselfThirdParameter, StringComparer.InvariantCultureIgnoreCase);
		}

		protected override void OnStartup(StartupEventArgs e)
		{
			//TODO: Do not use SingleInstance otherwise how will other application know if exit code is sent?
			//TODO: Maybe look at placing the WpfNotificationWindow in its own app

			if (!WasAlreadyCalledByIttself())//Check first if already called otherwise endless loop
				SharedClasses.AutoUpdating.CheckForUpdates(null, null);

			bool mustExit = false;

			var args = Environment.GetCommandLineArgs();
			//int remove;
			//args = new string[] { "", "checkforupdates", @"c:\windows\notepad.exe" };
			if (args.Length < 3)
			{
				UserMessages.ShowWarningMessage("Not enough command-line arguments, must be: command \"c:\\my\\path\\to\\exe\"");
				mustExit = true;
			}
			else if (args[1].Equals("checkforupdates", StringComparison.InvariantCultureIgnoreCase))
			{
				string exePathToCheckForUpdates = args[2];
				if (exePathToCheckForUpdates.EndsWith(".vshost.exe", StringComparison.InvariantCultureIgnoreCase))
					Environment.Exit((int)SharedClasses.AutoUpdating.ExitCodes.SkippingBecauseIsDebugEndingWithVshostExe);
				if (!File.Exists(exePathToCheckForUpdates))
				{
					UserMessages.ShowWarningMessage("Cannot find passed file path: " + exePathToCheckForUpdates);
					mustExit = true;
				}
				else
					AutoUpdater.MainWindow.CheckForUpdates(exePathToCheckForUpdates);
			}
			else if (args[1].Equals("checkforupdatesilently", StringComparison.InvariantCultureIgnoreCase))
			{
				string applicationName = args[2];
				if (File.Exists(applicationName))
					applicationName = Path.GetFileNameWithoutExtension(applicationName);
				string installedVersion = args[3];
				string errorIfNull;
				PublishDetails onlineVersionDetails;
				bool? checkSuccess =
					AutoUpdater.MainWindow.IsApplicationUpToDate(applicationName, installedVersion, out errorIfNull, out onlineVersionDetails);
				AutoUpdating.ExitCodes exitCode = AutoUpdating.ExitCodes.UpToDateExitCode;
				if (!checkSuccess.HasValue)
				{
					if (onlineVersionDetails == null)//Unknown erro
						exitCode = AutoUpdating.ExitCodes.UnableToCheckForUpdatesErrorCode;
					else//We have online details so the installed version is newer than online
						exitCode = AutoUpdating.ExitCodes.InstalledVersionNewerThanOnline;
					Console.Error.Write(errorIfNull);
				}
				else if (checkSuccess.Value == false)
				{
					exitCode = AutoUpdating.ExitCodes.NewVersionAvailableExitCode;
					JSON.SetDefaultJsonInstanceSettings();
					string jsonStr = JSON.Instance.ToJSON(onlineVersionDetails, false);
					Console.Out.Write(jsonStr);
				}
				ShutDownThisApplication((int)exitCode);
			}
			else if (args[1].Equals("installlatest", StringComparison.InvariantCultureIgnoreCase)
				|| args[1].Equals("installlatestsilently", StringComparison.InvariantCultureIgnoreCase))
			{
				bool installSilently = args[1] == "installlatestsilently";
				string applicationName = args[2];
				if (File.Exists(applicationName))
					applicationName = Path.GetFileNameWithoutExtension(applicationName);
				AutoUpdater.MainWindow.InstallLatest(applicationName, installSilently);
			}
			else
				UserMessages.ShowWarningMessage("AutoUpdater does not recognize command (from arguments): " + args[1]);

			if (mustExit)
				//Application.Current.Shutdown((int)SharedClasses.AutoUpdating.ExitCodes.UnableToCheckForUpdatesErrorCode);
				ShutDownThisApplication((int)SharedClasses.AutoUpdating.ExitCodes.UnableToCheckForUpdatesErrorCode);

			//base.OnStartup(e);
		}

		public static void ShutDownThisApplication(int? exitCodeOverride = null)
		{
			if (exitCodeOverride.HasValue)
				App.CurrentExitCode = exitCodeOverride.Value;
			try
			{
				Application.Current.Shutdown((int)App.CurrentExitCode);
			}
			catch
			{
				try
				{
					Application.Current.Dispatcher.InvokeShutdown();
				}
				catch (Exception exc)
				{
					MessageBox.Show("Unable to shutdown AutoUpdater: " + exc.Message);
				}
			}
		}

		public static int CurrentExitCode = 0;
		protected override void OnExit(ExitEventArgs e)
		{
			if (AutoUpdater.MainWindow.checkForUpdatesThread != null)
				AutoUpdater.MainWindow.checkForUpdatesThread.Abort();
			e.ApplicationExitCode = CurrentExitCode;
			base.OnExit(e);
		}
	}
}
