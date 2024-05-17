using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace GitStashManager
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<string> repositories;

        public MainWindow()
        {
            InitializeComponent();
			repositories = new ObservableCollection<string>();
            RepositoryDropdown.ItemsSource = repositories;

            LoadScanResults();
        }

        private void btnScan_Click(object sender, RoutedEventArgs e)
        {
            btnScan.IsEnabled = false;
            repositories.Clear();
            ScanForGitRepos();
        }

        private void SaveScanResults()
        {
            string filePath = "scanResults.txt"; // You can change the file path as needed
            try
            {
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    foreach (var repo in repositories)
                    {
                        writer.WriteLine(repo);
                    }
                }
                MessageBox.Show("Scan results saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving scan results: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadScanResults()
        {
            string filePath = "scanResults.txt"; // The path to the file where scan results are saved
            try
            {
                if (File.Exists(filePath))
                {
                    repositories.Clear();
                    using (StreamReader reader = new StreamReader(filePath))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            repositories.Add(line);
                        }
                    }
                    //MessageBox.Show("Scan results loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    btnScan.Content = "Scan";
                    Debug.Write("Scan results file not found.", "Error");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading scan results: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnAddDir_Click(object sender, RoutedEventArgs e)
        {
            repositories.Add(txtDirectory.Text);
        }

        private void btnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            // Open file dialog to select patch files
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Patch Files (*.patch;*.diff)|*.patch;*.diff|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var patchFiles = openFileDialog.FileNames;
                if (patchFiles.Length <= 0)
                {
                    MessageBox.Show($"No '.patch' files selected");
                }

                // Add found files to the ImportStashListBox
                ImportStashListBox.Items.Clear();
                foreach (string file in patchFiles)
                {
                    ImportStashListBox.Items.Add(file);
                }
                ImportStashListBox.SelectAll();
            }
        }

        private void btnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            // Open file dialog to select patch files
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Patch Files (*.patch;*.diff)|*.patch;*.diff|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var patchFile = openFileDialog.FileName;
                if (string.IsNullOrWhiteSpace(patchFile))
                {
                    MessageBox.Show($"No '.patch' file selected");
                }

                // Add file to the Import3WayListBox
                Import3WayListBox.Items.Clear();
                Import3WayListBox.Items.Add(patchFile);
                Import3WayListBox.SelectAll();
            }
        }

        private async void btnImport_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected repository path
            var selectedRepoPath = RepositoryDropdown.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedRepoPath))
            {
                MessageBox.Show("Please select a repository.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (ImportStashListBox.SelectedItems.Count <= 0)
            {
                MessageBox.Show("Please ensure that one or more items are selected from the list above.", "Invalid Selection!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnImport.IsEnabled = false;

            // Apply patch files using PowerShell
            await ApplyPatchesToRepository(selectedRepoPath, ImportStashListBox.SelectedItems);
        }

        private async void btnImport3Way_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected repository path
            var selectedRepoPath = RepositoryDropdown.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedRepoPath))
            {
                MessageBox.Show("Please select a repository.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (Import3WayListBox.SelectedItems.Count <= 0)
            {
                MessageBox.Show("Please ensure that one or more items are selected from the list above.", "Invalid Selection!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btn3WayImport.IsEnabled = false;

            // Apply patch files using PowerShell
            await ApplyPatchesToRepository(selectedRepoPath, Import3WayListBox.SelectedItems, true);
        }

        private async Task ApplyPatchesToRepository(string repoPath, IList patchFiles, bool ThreeWayMerge = false)
        {
            var hadErrors = false;
			var errorMessage = string.Empty;
			var sucessfullImports = new List<object?>();

            try
            {
                using PowerShell powerShell = PowerShell.Create();

                // Change directory to the repository path
                powerShell.AddScript($"cd {repoPath}");

                // Pull the latest changes from the remote repository
                powerShell.AddScript($"git fetch origin");

                // Execute the PowerShell script
                await powerShell.InvokeAsync();
                powerShell.Commands.Clear();

                foreach (var patchFile in patchFiles)
                {
                    var file = patchFile.ToString();
                    if (string.IsNullOrEmpty (file))
                    {
                        continue;
                    }

                    var branchName = Path.GetFileNameWithoutExtension(file).Replace("_", " ").Replace("^", "/");
					var stashName = Path.GetFileNameWithoutExtension(file).Replace("_", " ").Replace("^", "/");
                    var commitHash = string.Empty;
                    var index = stashName.LastIndexOf('-');
					if (index != -1)
					{
                        stashName = stashName.Substring(index + 1);
                        commitHash = branchName.Substring(0, index).Split("%")[0];
                        branchName = branchName.Substring(0, index).Split("%")[1];
                    }

                    // Define variables for branch name and commit hash
                    var cleanedBranchName = branchName.Replace(" ", "");
                    var shortCommitHash = commitHash.Substring(0, 12);
                    var newBranchName = $"{ cleanedBranchName }-{ shortCommitHash}";

                    // Check if the branch exists locally
                    powerShell.AddScript($@"git checkout {cleanedBranchName}");
                    await powerShell.InvokeAsync();
                    foreach (var error in powerShell.Streams.Error)
                    {
						// Handle errors
						if (!(error.Exception.Message.Contains("Already on") || error.Exception.Message.Contains("Checking patch") || error.Exception.Message.Contains("Applied patch") || error.Exception.Message.Contains("Switched to a new branch") || error.Exception.Message.Contains("whitespace") || error.Exception.Message.Contains("already exists") || error.Exception.Message.Contains("Checking patch") || error.Exception.Message.Contains("cleanly") || error.Exception.Message.Contains("Switched to branch")))
						{
							errorMessage += error.Exception.Message + "\n";
							hadErrors = true;
						}
                    }
                    powerShell.Commands.Clear();
                    powerShell.Streams.Error.Clear();

                    if (ThreeWayMerge)
                    {
                        // Apply the patch file using 3 way merge
                        powerShell.AddScript($"git apply --3way --ignore-space-change --ignore-whitespace \"{file}\"");
                    }
                    else
                    {
                        // Check if the patch can be applied cleanly
                        powerShell.AddScript($"git apply --check --ignore-space-change --ignore-whitespace \"{file}\"");
                        await powerShell.InvokeAsync();
                        powerShell.Commands.Clear();

                        // Check if the patch application failed
                        if (powerShell.HadErrors)
                        {
                            // Rollback changes and checkout an older version
                            powerShell.AddScript($"git reset --hard HEAD");
                            powerShell.AddScript($"git checkout -b {newBranchName} {commitHash}");

							await powerShell.InvokeAsync();
							
							// The branch already exists
							if (powerShell.HadErrors)
                            {
								powerShell.Commands.Clear();
								powerShell.Streams.Error.Clear();

								powerShell.AddScript($"git checkout {newBranchName}");
							}
							
                        }

                        // Apply the patch file
                        powerShell.AddScript($"git apply --reject --ignore-space-change --ignore-whitespace \"{file}\"");
                    }

                    // Stash the changes and name the stash after the patch file
                    powerShell.AddScript($"git stash push -m \"{stashName}\"");

                    // Execute the PowerShell script
                    await powerShell.InvokeAsync();
                    powerShell.Commands.Clear();


                    if (powerShell.HadErrors)
                    {
                        foreach (var error in powerShell.Streams.Error)
                        {
                            // Handle errors
                            if (!(error.Exception.Message.Contains("Already on") || error.Exception.Message.Contains("Checking patch") || error.Exception.Message.Contains("Applied patch") || error.Exception.Message.Contains("Switched to a new branch") || error.Exception.Message.Contains("whitespace") || error.Exception.Message.Contains("already exists") || error.Exception.Message.Contains("Checking patch") || error.Exception.Message.Contains("cleanly") || error.Exception.Message.Contains("Switched to branch") || error.Exception.Message.Contains("From")))
                            {
                                errorMessage += error.Exception.Message + "\n";
                                hadErrors = true;
                            }
                            else
                            {
								sucessfullImports.Add(patchFile);
							}
                        }
                    }
                }

                if(hadErrors)
                {
					MessageBox.Show($"{errorMessage}", "Output", MessageBoxButton.OK, MessageBoxImage.Warning);
					var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "./";
					string filePath = Path.Combine(root ,"errorLog.txt");

					try
					{
						using (StreamWriter writer = new StreamWriter(filePath))
						{
							foreach (var line in errorMessage.Split("\n"))
							{
								writer.WriteLine(line);
							}
						}
						MessageBox.Show("Errors written to log file!", "Error", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
					catch (Exception ex)
					{
						MessageBox.Show($"Error writing logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
					}
				}
                else
                {
					foreach (var item in sucessfullImports)
					{
						if (ThreeWayMerge)
						{
							Import3WayListBox.Items.Remove(item);
						}
						else
						{
							ImportStashListBox.Items.Remove(item);
						}
					}
				}
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            if (!hadErrors)
            {
                MessageBox.Show("Patches applied and changes stashed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            btnImport.IsEnabled = true;
            btn3WayImport.IsEnabled = true;
        }

        private async void btnExport_Click(object sender, RoutedEventArgs e)
        {
            var localRepoPath = RepositoryDropdown.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(localRepoPath))
            {
                MessageBox.Show("Please select a repository.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (ExportStashListBox.SelectedItems.Count <= 0)
            {
                MessageBox.Show("Please ensure that the repo has stashed changes, and one or more items are selected from the list above.", "Invalid Selection!", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var folderBrowser = new FolderBrowserDialog())
            {
                // Show the FolderBrowserDialog to the user
                if (folderBrowser.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
					btnExport.IsEnabled = false;
					var exportPath = folderBrowser.SelectedPath;

                    using (PowerShell powershell = PowerShell.Create())
                    {
                        // Change from the user folder that PowerShell starts up with to your git repository
                        powershell.AddScript($"cd {localRepoPath}");

                        foreach (var selectedItem in ExportStashListBox.SelectedItems)
                        {
                            try
                            {
                                var splitSelectedItem = selectedItem?.ToString()?.Replace("/", "^").Split(":");
                                var stashName = splitSelectedItem?[0];
                                var branchName = splitSelectedItem?[1].Replace(" On ", "");
                                var patchName = splitSelectedItem?[2].Substring(1).Replace(" ", "_");

                                patchName = patchName.Replace(".", "");

                                powershell.AddScript($@"git rev-parse '{stashName}^'");
                                var result = await powershell.InvokeAsync();
                                var commitHash = result[0];
                                powershell.Commands.Clear();

                                var patchFilePath = Path.Combine(exportPath, $"{commitHash}%{branchName}-{patchName}.patch");
                                powershell.AddScript($@"git stash show '{stashName}' -p > ""{patchFilePath}""");
								await powershell.InvokeAsync();
                                powershell.Commands.Clear();
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"An error occurred while listing stashes: {ex.Message}");
                            }
                        }

                        MessageBox.Show($"Backups saved to: {exportPath}", "Export Complete!", MessageBoxButton.OK, MessageBoxImage.Information);
                        btnExport.IsEnabled = true;
                    }
                }
            }
        }

		private async void btnPush_Click(object sender, RoutedEventArgs e)
		{
			var localRepoPath = RepositoryDropdown.SelectedItem?.ToString();
			if (string.IsNullOrWhiteSpace(localRepoPath))
			{
				MessageBox.Show("Please select a repository.", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			if (ExportStashListBox.SelectedItems.Count <= 0)
			{
				MessageBox.Show("Please ensure that the repo has stashed changes, and one or more items are selected from the list above.", "Invalid Selection!", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			btnPush.IsEnabled = false;

			using (PowerShell powershell = PowerShell.Create())
			{
				// Change from the user folder that PowerShell starts up with to your git repository
				powershell.AddScript($"cd {localRepoPath}");

				foreach (var selectedItem in ExportStashListBox.SelectedItems)
				{
					var splitSelectedItem = selectedItem?.ToString()?.Replace("/", "^").Split(":");
					var branchName = splitSelectedItem?[1].Replace(" On ", "");

					powershell.AddScript($@"git checkout {branchName}");
					powershell.AddScript(@"git push");
				}

				try
				{
					await powershell.InvokeAsync();
                    powershell.Commands.Clear();
				}
				catch (Exception ex)
				{
					MessageBox.Show($"An error occurred: {ex.Message}");
				}

				MessageBox.Show($"Branches pushed to remote.", "Sucess!", MessageBoxButton.OK, MessageBoxImage.Information);
				btnPush.IsEnabled = true;
			}
		}

		private async void ScanForGitRepos()
        {
            string rootPath = @"C:\";
            List<string> gitDirectories = await FindHiddenGitDirectoriesAsync(rootPath);

            foreach (string gitDirectory in gitDirectories)
            {
                repositories.Add(gitDirectory.Replace(".git", ""));
            }

            SaveScanResults();
            btnScan.Content = "Rescan";
            btnScan.IsEnabled = true;
        }

        static async Task<List<string>> FindHiddenGitDirectoriesAsync(string rootPath)
        {
            List<string> gitDirectories = new List<string>();
            await SearchDirectoriesAsync(rootPath, gitDirectories);
            return gitDirectories;
        }

        static async Task SearchDirectoriesAsync(string rootPath, List<string> gitDirectories)
        {
            try
            {
                var directories = Directory.GetDirectories(rootPath);
                var tasks = new List<Task>();

                foreach (var dir in directories)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var directoryInfo = new DirectoryInfo(dir);
                            if ((directoryInfo.Attributes & FileAttributes.Hidden) != 0 && directoryInfo.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                            {
                                lock (gitDirectories)
                                {
                                    gitDirectories.Add(directoryInfo.FullName);
                                }
                            }
                            // Recursively search within the current directory
                            await SearchDirectoriesAsync(dir, gitDirectories);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Silently continue if access to a directory is denied
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"An error occurred while accessing directory {dir}: {ex.Message}");
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }
            catch (UnauthorizedAccessException)
            {
                // Silently continue if access to the rootPath is denied
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while accessing root path {rootPath}: {ex.Message}");
            }
        }

        private async void RepositoryDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Clear the existing items in the ListBox
                ExportStashListBox.Items.Clear();

                if (((ComboBox)sender).SelectedItem == null)
                {
                    return;
                }

                // Get the selected repository path from the dropdown
                var selectedRepositoryPath = ((ComboBox)sender).SelectedItem as string;
                if (string.IsNullOrEmpty(selectedRepositoryPath))
                {
                    MessageBox.Show("Please select a valid repository.");
                    return;
                }

                // Run the git stash list command and capture the output
                var stashList = await GetGitStashList(selectedRepositoryPath);

                // Add each stash entry to the ListBox
                foreach (var stash in stashList)
                {
                    ExportStashListBox.Items.Add(stash);
                }
                ExportStashListBox.SelectAll();
            }
            catch(Exception ex)
            {

            }
        }

        private async Task<List<string>> GetGitStashList(string repositoryPath)
        {
            var stashList = new List<string>();

            try
            {
                using (PowerShell powershell = PowerShell.Create())
                {
                    // this changes from the user folder that PowerShell starts up with to your git repository
                    powershell.AddScript($"cd {repositoryPath}");
                    powershell.AddScript(@"git stash list");

                    var results = await powershell.InvokeAsync();
                    foreach (PSObject obj in results)
                    {
                        stashList.Add(obj.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while listing stashes: {ex.Message}");
            }

            return stashList;
        }
    }
}