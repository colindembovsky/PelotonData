﻿using Microsoft.WindowsAPICodePack.Dialogs;
using PelotonData;
using PelotonData.JSONClasses;
using PelotonData.JSONClasses.WorkoutList;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Reflection;

namespace PelotonDataGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, ILogger
    {
        ILogger Logger;
        public MainWindow()
        {
            InitializeComponent();
            Logger = this;
            Logger.Log("Starting up");
            Assembly assembly = Assembly.GetExecutingAssembly();
            Logger.Log($"Using assembly: {assembly.GetName().Name} version {assembly.GetName().Version}");
            Assembly dataAssembly = Assembly.GetAssembly(typeof(PelotonDataDownloader));
            Logger.Log($"Using assembly: {dataAssembly.GetName().Name} version {dataAssembly.GetName().Version}");
        }

        public void Log(LogEntry entry)
        {
            string logMessage = "";
            if (entry.Severity != LoggingEventType.Information) logMessage += entry.Severity.ToString() + ": ";
            if (entry.Message != null) logMessage += entry.Message;
            if (entry.Exception != null) logMessage += "\nException: " + entry.Exception.ToString();

            string text = LogTextBox.Text;
            text += logMessage + "\n";
            LogTextBox.Text = text;
            LogTextBox.ScrollToEnd();
        }

        AuthResponse auth;

        private void HandleException(Exception ex)
        {
            if (ex is WebException)
            {
                var wex = ex as WebException;
                if (wex.Status == WebExceptionStatus.ProtocolError)
                {
                    var statusCode = ((HttpWebResponse)wex.Response).StatusCode;
                    var statusDescription = ((HttpWebResponse)wex.Response).StatusDescription;
                    if (statusCode == HttpStatusCode.Unauthorized)
                    {
                        Logger.LogError("Received response \"Unauthorized\" from Peloton Server.  Please check you username and password");
                    }
                    else
                    {
                        Logger.LogError($"Received protocol error from Peloton Server: {statusCode} \"{statusDescription}\"");
                    }
                }
                else
                {
                    Logger.LogError($"Received Web Exception from Peloton Server", ex);
                }
            } else
            {
                Logger.LogError($"Error during execution", ex);
            }

        }

        private async Task SetProgress(double progress)
        {
            ProgressBar.Value = progress;
            await Task.Delay(100);
        }

        private async void GetDataButton_Click(object sender, RoutedEventArgs e)
        {
            double progress = 0.0;

            var p = new PelotonDataDownloader();

            await SetProgress(0);

            string outputDirectory = OutputDirectoryTextBox.Text;

            if (!Directory.Exists(outputDirectory))
            {
                Logger.LogError($"The output directory does not exist: {outputDirectory}\nAborting.");
                return;
            }
            Logger.Log("Authenticating");
            var authTask = p.AuthenticateAsync(UsernameTextBox.Text, PasswordTextBox.Password, this, Path.Combine(outputDirectory, "authentication_response.json"));
            try
            {
                auth = await authTask;
            }
            catch (Exception ex)
            {
                HandleException(ex);
                Logger.Log("Aborting...");
                return;
            }

            progress += 5.0;
            await SetProgress(progress);
          
            Logger.Log("Success!");

            List<RideDatum> workoutList;
            Logger.Log("Getting list of workouts");
            try
            {
                workoutList = await p.GetWorkoutListAsync(auth, this, outputDirectory);
            } catch (Exception ex)
            {
                HandleException(ex);
                Logger.Log("Aborting...");
                return;
            }

            Logger.Log($"Received {workoutList.Count} workouts");

            progress += 5.0;
            await SetProgress(progress);

            double progressStep = (100 - progress) / workoutList.Count;
            var jsonRoot = Path.Combine(OutputDirectoryTextBox.Text, "json");
            if (!Directory.Exists(jsonRoot))
            {
                Directory.CreateDirectory(jsonRoot);
            }

            Logger.Log("Downloading data for each workout");
            bool redownloadWorkoutDetails = RedownloadCheckbox.IsChecked.HasValue && RedownloadCheckbox.IsChecked.Value;
            
            var jsonPath = Path.Combine(OutputDirectoryTextBox.Text, "json");
            if (!Directory.Exists(jsonPath)) Directory.CreateDirectory(jsonPath);
            var metricsPath = Path.Combine(OutputDirectoryTextBox.Text, "metrics");
            if (!Directory.Exists(metricsPath)) Directory.CreateDirectory(metricsPath);
            var detailsPath = Path.Combine(OutputDirectoryTextBox.Text, "details");
            if (!Directory.Exists(detailsPath)) Directory.CreateDirectory(detailsPath);

            foreach (var ride in workoutList)
            {
                try
                {
                    var fileName = p.GetFileNameBaseFromRideDatum(ride);
                    string metricsFilename = Path.Combine(metricsPath, fileName + "_Metrics.csv");
                    string detailsJson = Path.Combine(jsonPath, fileName + "_UserWorkoutDetails.json");
                    string detailsFilename = Path.Combine(detailsPath, fileName + "_UserWorkoutDetails.csv");

                    if (File.Exists(metricsFilename))
                    {
                        Logger.Log($"Already of workout metrics, skipping: {ride.ride.title} on {Util.DateTimeFromEpochSeconds(ride.device_time_created_at).ToShortDateString()}");
                    }
                    else
                    {
                        var data = await p.GetWorkoutMetricsAsync(ride, this, metricsFilename);
                        Logger.Log($"Writing data to {metricsFilename}");
                        p.OutputRideCSV(data, metricsFilename);
                        //var details = await p.GetWorkoutEventDetails(ride, this, filenameBase + "_EventDetails.json");
                    }

                    if (File.Exists(detailsFilename) && !redownloadWorkoutDetails)
                    {
                        Logger.Log($"Already have user details for workout, skipping: {ride.ride.title} on {Util.DateTimeFromEpochSeconds(ride.device_time_created_at).ToShortDateString()}");
                    } 
                    else
                    {
                        var userDetails = await p.GetWorkoutUserDetails(ride, this, detailsJson);
                        var propertyDict = Util.GetObjectPropertiesAsDictionaryRecursive(userDetails);
                        Util.WriteDictionaryAsCSV(propertyDict, detailsFilename);
                    }
                } catch (Exception ex)
                {
                    HandleException(ex);
                    Logger.Log("Skipping workout");
                }
                progress += progressStep;
                await SetProgress(progress);
            }
            await SetProgress(100);
            Logger.Log("Successfully Completed Data Download!");
        }

        private void OutputDirectoryBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CommonOpenFileDialog();
            dlg.Title = "Output Directory";
            dlg.IsFolderPicker = true;
            if (String.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text))
            {
                dlg.InitialDirectory = OutputDirectoryTextBox.Text;
            }

            dlg.AddToMostRecentlyUsedList = false;
            dlg.AllowNonFileSystemItems = false;
            //dlg.DefaultDirectory = currentDirectory;
            dlg.EnsureFileExists = true;
            dlg.EnsurePathExists = true;
            dlg.EnsureReadOnly = false;
            dlg.EnsureValidNames = true;
            dlg.Multiselect = false;
            dlg.ShowPlacesList = true;

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputDirectoryTextBox.Text = dlg.FileName;
            }
        }
    }
}
