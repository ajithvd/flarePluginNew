using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using B3.PluginAPIKit;
using System.Diagnostics;
using System.Xml;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;
using static AILinguistic.FlareHandler; // Consider removing static using if RelayCommand is moved
using System.Text;
using System.Linq;
// using static System.Net.WebRequestMethods; // Unused using
using System.Xml.Linq;
//using System.Text.RegularExpressions; // Unused using

namespace AILinguistic
{
    public class DockableWindow : Window
    {
        // Configuration Constant
        private const string ApiValidationUrl = "http://JSQPVWAIW01:2122/validate";
        // Consider moving rules options to constants as well
        private const string LinguisticRulesOption = "rulesDoc1";
        private const string TerminologyRulesOption = "rulesDoc2";


        private IntPtr _flareWindowHandle;
        private readonly IHost _host;
        private List<SuggestionItem> _suggestions = new List<SuggestionItem>();
        private ComboBox _checkTypeComboBox;
        private ScrollViewer _mainScrollViewer;
        private string _selectedCheckType = "Linguistic check"; // Default value
        private string _lastCheckedDocUrl;



        public DockableWindow(IHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host)); // Add null check
            InitializeWindow();
            LogInformations.InitializeLogFile(); // Ensure log file is ready
            InitializeContent();
            HookFlareWindow(); // Set Flare as owner window
        }
        private void InitializeContent()
        {
            Content = CreateMainContent();
        }
        private void InitializeWindow()
        {
            Title = "BR Content Validation Tool";
            Width = 300;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Position window top-right (consider screen size variations)
            try {
                 Left = SystemParameters.PrimaryScreenWidth - Width;
                 Top = 0;
            } catch(Exception ex) {
                 // Fallback if screen info not available
                 Left = 100;
                 Top = 100;
                 ExceptionHandling.LogError($"Failed to get screen width: {ex.Message}");
            }

            ResizeMode = ResizeMode.CanResize;
            ShowActivated = false; // Prevent stealing focus initially
            // Consider setting Topmost = true/false depending on desired behavior
        }



        private UIElement CreateMainContent()
        {
            var mainStackPanel = new StackPanel { Orientation = Orientation.Vertical };

            // --- Header ---
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)), // Light gray background
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(5),
                Padding = new Thickness(8),
                Child = CreateHeaderControls() // Delegate header creation
            };

             // --- Suggestions Area ---
            _mainScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateItemList(), // Populate with current suggestions
                Height = 500 // Consider making this dynamic or percentage-based
                // Background = Brushes.White // Set background if needed
            };

            mainStackPanel.Children.Add(headerBorder);
            mainStackPanel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 5) }); // Visual separator
            mainStackPanel.Children.Add(_mainScrollViewer);

            return mainStackPanel;
        }

        // Helper method to create header controls
        private UIElement CreateHeaderControls()
        {
             var headerStack = new StackPanel { Orientation = Orientation.Horizontal };

             // Check Type Dropdown
            _checkTypeComboBox = new ComboBox
            {
                Width = 180,
                Margin = new Thickness(0, 0, 10, 0),
                ItemsSource = new List<string> { "Linguistic check", "Terminology check" }, // Consider making these constants
                SelectedItem = _selectedCheckType,
                FontSize = 14,
                Foreground = Brushes.Navy
            };
            _checkTypeComboBox.SelectionChanged += CheckTypeComboBox_SelectionChanged; // Use named handler

            // Check Button
            var checkButton = new Button
            {
                Content = "Check",
                Background = new SolidColorBrush(Color.FromRgb(30, 144, 255)), // Dodger Blue
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 5, 12, 5),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 5, 0),
                // Assuming RelayCommand is accessible (either static using or moved)
                Command = new RelayCommand(async () => await ExecuteCheckCommand())
            };

            headerStack.Children.Add(_checkTypeComboBox);
            headerStack.Children.Add(checkButton);

            return headerStack;
        }

         // Event handler for ComboBox selection change
        private void CheckTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedCheckType = _checkTypeComboBox.SelectedItem as string;
        }


        // Main logic for performing the check
        private async Task ExecuteCheckCommand()
        {
            string selectedCheck = "";
            string documentText = "";
            string rulesOption = "";
            IDocument activeDoc = null;

            // --- 1. Get Context and Text (on UI thread initially) ---
            try
            {
                 selectedCheck = _selectedCheckType;
                if (string.IsNullOrEmpty(selectedCheck))
                {
                    MessageBox.Show("Please select a check type first!", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var editorContext = _host?.GetEditorContext();
                if (editorContext == null)
                {
                    MessageBox.Show("Unable to access editor context.", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                activeDoc = editorContext.GetActiveDocument();
                if (activeDoc == null)
                {
                    MessageBox.Show("No active document found.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                 _lastCheckedDocUrl = activeDoc.GetSourceUrl(); // Store URL for later checks

                var xmlDoc = activeDoc.GetXmlDocument();
                if (xmlDoc == null)
                {
                    MessageBox.Show("Could not retrieve document content.", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                documentText = ExtractAllTextFromXml(xmlDoc);
                if (string.IsNullOrWhiteSpace(documentText)) {
                    MessageBox.Show("No text found in the document paragraphs to check.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return; // Nothing to check
                }

                // Determine rules based on selection
                rulesOption = selectedCheck == "Linguistic check" ? LinguisticRulesOption : TerminologyRulesOption;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error preparing check: {ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                ExceptionHandling.LogError($"Error in ExecuteCheckCommand (preparation): {ex}");
                return; // Don't proceed if preparation failed
            }


            // --- 2. Show Loading Indicator ---
            ShowLoadingIndicator("Analyzing document content...");
            _suggestions.Clear(); // Clear previous suggestions


             // --- 3. Perform API Call (background thread) ---
            try
            {
                using (var client = new HttpClient())
                {
                    // Consider adding a timeout to the client
                    // client.Timeout = TimeSpan.FromSeconds(60);

                    var payload = new { rulesOption, text = documentText };
                    var jsonPayload = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // Log payload for debugging (consider logging level or conditional logging)
                    ExceptionHandling.LogError($"API Payload ({ApiValidationUrl}): {jsonPayload}");

                    var response = await client.PostAsync(ApiValidationUrl, content); // Use constant URL
                    var responseContent = await response.Content.ReadAsStringAsync();

                    // Log response for debugging
                    ExceptionHandling.LogError($"API Response ({(int)response.StatusCode} {response.StatusCode}): {responseContent}");


                    // --- 4. Process API Response (back on UI thread) ---
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            try
                            {
                                var apiResponse = JsonConvert.DeserializeObject<ApiResponse>(responseContent);
                                _suggestions = apiResponse?.Data ?? new List<SuggestionItem>();

                                if (!_suggestions.Any())
                                {
                                    MessageBox.Show("No issues found! Great work!", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                                // No 'else' needed, UI will be updated below
                            }
                            catch (JsonException jsonEx)
                            {
                                MessageBox.Show("Failed to parse API response.", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                                ExceptionHandling.LogError($"Failed to parse API response: {jsonEx}");
                                _suggestions.Clear(); // Ensure suggestions are empty on error
                            }
                        }
                        else
                        {
                            MessageBox.Show($"API request failed: {response.StatusCode}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                            // Error already logged with content
                             _suggestions.Clear(); // Ensure suggestions are empty on error
                        }
                    });
                }
            }
            catch (HttpRequestException httpEx)
            {
                 await Application.Current.Dispatcher.InvokeAsync(() => {
                    MessageBox.Show($"Network error connecting to API: {httpEx.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    ExceptionHandling.LogError($"Network error: {httpEx}");
                 });
            }
            catch (TaskCanceledException) // Handles HttpClient timeout
            {
                 await Application.Current.Dispatcher.InvokeAsync(() => {
                    MessageBox.Show("The request timed out. Please check the connection or try again.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    ExceptionHandling.LogWarning("API request timed out.");
                 });
            }
             catch (Exception ex) // Catch-all for unexpected errors during API call/processing
            {
                 await Application.Current.Dispatcher.InvokeAsync(() => {
                    MessageBox.Show($"An unexpected error occurred during the check: {ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    ExceptionHandling.LogError($"Error in ExecuteCheckCommand (API call/processing): {ex}");
                 });
            }
            finally // Ensure UI is always restored
            {
                 // --- 5. Restore Main UI (on UI thread) ---
                 await Application.Current.Dispatcher.InvokeAsync(() =>
                 {
                    InitializeContent(); // Rebuild the main content with potentially new suggestions
                    _mainScrollViewer?.ScrollToTop(); // Scroll to top after loading
                 });
            }
        }

        // Helper to show a simple loading message
        private void ShowLoadingIndicator(string message)
        {
            // Replace window content with a simple loading text block
             Content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 16,
                        Foreground = Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap
                    },
                    // Optional: Add a progress ring or animation here
                }
            };
        }


        // Extracts text content primarily from <p> tags
        private string ExtractAllTextFromXml(XmlDocument xmlDoc)
        {
            // Consider making the XPath configurable or more robust if needed
            const string paragraphXPath = "//p";
            var sb = new StringBuilder();

            try
            {
                var navigator = xmlDoc.CreateNavigator();
                var expr = navigator.Compile(paragraphXPath);
                var iterator = navigator.Select(expr);

                while (iterator.MoveNext())
                {
                    // Get text content, replace newlines with spaces, trim whitespace
                    string text = iterator.Current.Value?
                        .Replace("
", " ")
                        .Replace("", "") // Also remove carriage returns
                        .Trim();

                     // Simple whitespace normalization (replace multiple spaces with one)
                     if(text != null) {
                        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                     }


                    // Skip empty/whitespace-only paragraphs (e.g., <p>&#160;</p>)
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.Append(text);
                        // Use a consistent line break recognized by the API if necessary, otherwise space might be enough
                        // sb.Append(""); // Using carriage return as separator - verify API expectation
                        sb.Append(" "); // Use space as separator, API might handle sentence splitting
                    }
                }

                // Remove trailing separator if exists
                //if (sb.Length > 0 && sb[sb.Length - 1] == '')
                //    sb.Length--;
                 if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                    sb.Length--;


                return sb.ToString();
            }
            catch (Exception ex)
            {
                 ExceptionHandling.LogError($"Failed to extract text from XML using XPath '{paragraphXPath}': {ex}");
                 // Optionally show message or return empty string?
                 MessageBox.Show($"Error reading document content: {ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                 return string.Empty;
            }
        }


        // Creates the visual list of suggestion items
        private StackPanel CreateItemList()
        {
            var stackPanel = new StackPanel { CanVerticallyScroll = true }; // Enable scrolling within stackpanel if needed

            if (_suggestions == null || !_suggestions.Any())
            {
                // Display a clearer message when no suggestions are loaded/found
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "No suggestions to display.", // Changed message
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10, 20, 10, 20), // Added horizontal margin
                    TextWrapping = TextWrapping.Wrap
                });
                return stackPanel;
            }

            // Create UI element for each suggestion item
            foreach (var item in _suggestions)
            {
                // Add visual separator between items for clarity
                if (stackPanel.Children.Count > 0) {
                    stackPanel.Children.Add(new Separator { Background = Brushes.Gainsboro, Margin = new Thickness(5, 0, 5, 0) });
                }
                 stackPanel.Children.Add(CreateListItem(item));
            }

            return stackPanel;
        }

        // Creates the UI for a single suggestion item
        private UIElement CreateListItem(SuggestionItem item)
        {
            if (item == null) return new TextBlock { Text = "Invalid suggestion item data.", Foreground = Brushes.Red, Margin = new Thickness(5) };

            var itemBorder = new Border // Use Border for background and padding
            {
                 Margin = new Thickness(5),
                 Background = Brushes.WhiteSmoke, // Slightly off-white background
                 CornerRadius = new CornerRadius(3),
                 Padding = new Thickness(8)
            };

            var itemStackPanel = new StackPanel { Orientation = Orientation.Vertical };

            // --- Original/Corrected Text Area (Clickable) ---
            var textBorder = new Border
            {
                Background = Brushes.Transparent, // Allows parent background to show
                Cursor = Cursors.Hand,
                ToolTip = "Click to highlight original text in document",
                Child = CreateSuggestionTextBlock(item) // Delegate text block creation
            };
            textBorder.MouseDown += (s, e) => HighlightOriginalText(item.Original); // Attach click handler
             // Add hover effect directly to the border
            textBorder.MouseEnter += (s, e) => ((Border)s).Background = Brushes.LightGray; // Use LightGray for hover
            textBorder.MouseLeave += (s, e) => ((Border)s).Background = Brushes.Transparent;


             // --- Buttons Panel ---
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, // Align buttons to the right
                Margin = new Thickness(0, 8, 0, 0) // Add space above buttons
            };

            // Approve Button
            var approveButton = new Button
            {
                Content = "Approve",
                Background = new SolidColorBrush(Color.FromRgb(40, 167, 69)), // Green
                Foreground = Brushes.White,
                Width = 80,
                Margin = new Thickness(5),
                Tag = item, // Store item data in Tag
                ToolTip = "Replace original text with the correction in the document"
            };
            approveButton.Click += ApproveButton_Click; // Use named handler

            // Reject Button
            var rejectButton = new Button
            {
                Content = "Reject",
                Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)), // Red
                Foreground = Brushes.White,
                Width = 80,
                Margin = new Thickness(5),
                Tag = item, // Store item data in Tag
                ToolTip = "Dismiss this suggestion"
            };
             rejectButton.Click += RejectButton_Click; // Use named handler


            buttonPanel.Children.Add(approveButton);
            buttonPanel.Children.Add(rejectButton);

            // Assemble the item UI
            itemStackPanel.Children.Add(textBorder);
            // itemStackPanel.Children.Add(new Separator { Background = Brushes.LightGray, Margin = new Thickness(0, 5, 0, 5) }); // Separator might be excessive with Border
            itemStackPanel.Children.Add(buttonPanel);

            itemBorder.Child = itemStackPanel; // Set content of the border
            return itemBorder; // Return the encompassing border
        }

         // Helper method to create the text block part of a suggestion item
        private UIElement CreateSuggestionTextBlock(SuggestionItem item) {
             var textStack = new StackPanel();

             // Original Text
             textStack.Children.Add(new TextBlock
            {
                // Use Run elements for better control if needed, but TextBlock is fine here
                Inlines = {
                    new System.Windows.Documents.Run("Original: ") { FontWeight = FontWeights.SemiBold },
                    new System.Windows.Documents.Run(item.Original ?? "N/A") { FontStyle = FontStyles.Italic, Foreground = Brushes.DimGray }
                 },
                TextWrapping = TextWrapping.Wrap
            });

            // Corrected Text
            textStack.Children.Add(new TextBlock
            {
                 Inlines = {
                    new System.Windows.Documents.Run("Corrected: ") { FontWeight = FontWeights.SemiBold },
                    new System.Windows.Documents.Run(item.Corrected ?? "N/A") { FontWeight = FontWeights.Bold, Foreground = Brushes.DarkGreen }
                 },
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0) // Add space between original and corrected
            });

            return textStack;
        }

        // --- Button Click Handlers ---

        private void ApproveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is SuggestionItem approvedItem)) return;

            var activeDoc = _host.GetEditorContext()?.GetActiveDocument();
            if (!IsDocumentStillValid(activeDoc)) return; // Check if doc changed

            if (string.IsNullOrWhiteSpace(approvedItem.Original) || approvedItem.Corrected == null) // Corrected can be empty string ""
            {
                MessageBox.Show("Invalid replacement values (original or corrected text missing).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                 // Attempt the replacement
                bool replacementSuccessful = ReplaceText(approvedItem.Original, approvedItem.Corrected);

                if (replacementSuccessful)
                {
                    _suggestions.Remove(approvedItem); // Remove from list
                    LogInformations.LogAction("Approved", approvedItem.Original, approvedItem.Corrected); // Log action
                    RefreshSuggestionsList(); // Update UI efficiently
                }
                else
                {
                    // ReplaceText should ideally log specifics, show generic message here
                    MessageBox.Show("Unable to find or replace the original text in the document. It might have been modified.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                     // Consider automatically re-running the check if replacement fails?
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during replacement: {ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                ExceptionHandling.LogError($"Approval replacement failed: {ex}");
            }
        }

        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
             if (!(sender is Button button) || !(button.Tag is SuggestionItem rejectedItem)) return;

             var activeDoc = _host.GetEditorContext()?.GetActiveDocument();
             if (!IsDocumentStillValid(activeDoc)) return; // Check if doc changed (optional for reject, but good practice)

            _suggestions.Remove(rejectedItem); // Remove from list
            LogInformations.LogAction("Rejected", rejectedItem.Original, rejectedItem.Corrected); // Log action
            RefreshSuggestionsList(); // Update UI efficiently
        }


        // Helper to refresh the content of the suggestions ScrollViewer
        private void RefreshSuggestionsList() {
            if(_mainScrollViewer != null) {
                 _mainScrollViewer.Content = CreateItemList();
            } else {
                // Fallback if somehow scrollviewer is null (shouldn't happen after init)
                InitializeContent();
            }
        }

        // Helper to check if the document context is still valid for operations
        private bool IsDocumentStillValid(IDocument activeDoc) {
             if (activeDoc == null || activeDoc.GetSourceUrl() != _lastCheckedDocUrl)
            {
                MessageBox.Show("The active document has changed or is no longer available. Please re-run the check.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                _suggestions.Clear();
                RefreshSuggestionsList(); // Clear the UI list too
                return false;
            }
            return true;
        }


        // Highlights the original text in the Flare editor
        private void HighlightOriginalText(string originalText)
        {
            if (string.IsNullOrEmpty(originalText)) return;

            var editorContext = _host?.GetEditorContext();
            var activeDoc = editorContext?.GetActiveDocument();

             if (!IsDocumentStillValid(activeDoc)) return; // Check if doc changed

            try
            {
                activeDoc.StartOperation("Highlight Text"); // Use descriptive operation name
                
                // Call Select directly - it returns void
                activeDoc.Select(originalText); 
                
                // We can't reliably know if Select found the text here, 
                // so the message box warning about not finding text is removed.
                // The user will see if the text gets selected or not.

                // Optional: Change background color if API supports it and is desired
                // activeDoc.Selection?.ChangeBackColor(System.Drawing.Color.Yellow);
                activeDoc.UpdateView(); // Ensure selection is visible
            }
            catch (Exception ex) {
                MessageBox.Show($"Error highlighting text: {ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                ExceptionHandling.LogError($"HighlightOriginalText failed: {ex}");
            }
            finally
            {
                // Ensure EndOperation is always called if StartOperation was successful
                 try { activeDoc?.EndOperation(); } catch (Exception ex) { ExceptionHandling.LogError($"Failed to end highlight operation: {ex}"); }
            }
        }


        // Replaces text in the document (using potentially fragile XPath method)
        // WARNING: This XPath approach is fragile. If B3.PluginAPIKit offers a dedicated replacement method, use it instead.
        private bool ReplaceText(string original, string suggestion)
        {
            var editorContext = _host?.GetEditorContext();
            var activeDoc = editorContext?.GetActiveDocument();

            // Doc validity check already done in caller (ApproveButton_Click)
            // if (!IsDocumentStillValid(activeDoc)) return false;

            bool success = false;
            try
            {
                activeDoc?.StartOperation("Replace Text"); // Descriptive name

                var xmlDoc = activeDoc?.GetXmlDocument();
                if (xmlDoc == null)
                {
                     ExceptionHandling.LogError("ReplaceText failed: Could not get XmlDocument.");
                     return false;
                }

                // --- XPath Replacement Logic ---
                // This remains fragile. It might replace partial matches or fail on complex structures.
                // Sanitize input for XPath query to handle quotes (basic example)
                 string safeOriginal = original?.Replace("'", "&apos;"); // Handle single quotes for XPath 1.0

                if (string.IsNullOrEmpty(safeOriginal)) {
                     ExceptionHandling.LogError("ReplaceText failed: Original text is empty after sanitization.");
                     return false;
                }

                 // Using double quotes for XPath string, single quotes inside for the text value
                 var xpath = $"//text()[contains(., '{safeOriginal}')]";
                 ExceptionHandling.LogWarning($"Attempting replacement using XPath: {xpath}"); // Log the attempt

                XmlNodeList nodes = null;
                try {
                    nodes = xmlDoc.SelectNodes(xpath);
                } catch (System.Xml.XPath.XPathException xpathEx) {
                     ExceptionHandling.LogError($"ReplaceText failed: XPath selection error for '{xpath}': {xpathEx}");
                     MessageBox.Show($"Error finding text to replace (invalid characters?): {xpathEx.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                     return false; // Don't proceed if XPath fails
                }


                if (nodes == null || nodes.Count == 0)
                {
                    ExceptionHandling.LogWarning($"ReplaceText: No nodes found matching XPath: {xpath}");
                    return false; // Original text not found via this method
                }

                 ExceptionHandling.LogWarning($"XPath '{xpath}' found {nodes.Count} nodes. Attempting replacement.");
                 int replacementCount = 0;
                foreach (XmlNode node in nodes)
                {
                    try
                    {
                        // Direct string replacement - might replace multiple occurrences in a single node
                         if (node.Value != null && node.Value.Contains(original)) { // Check contains again before replacing
                            node.Value = node.Value.Replace(original, suggestion ?? ""); // Replace with suggestion or empty string if null
                            replacementCount++;
                         } else {
                             ExceptionHandling.LogWarning($"Node found by XPath '{xpath}' but value '{node.Value?.Substring(0, Math.Min(node.Value.Length, 50))}...' did not contain exact original '{original}'. Skipping.");
                         }
                    }
                    catch (Exception ex)
                    {
                        // Log error for specific node but continue trying others
                        ExceptionHandling.LogError($"Failed to replace text in node (Value='{node.Value?.Substring(0, Math.Min(node.Value.Length, 50))}...'): {ex}");
                        continue;
                    }
                }
                // --- End XPath Replacement Logic ---

                if (replacementCount > 0) {
                    activeDoc?.UpdateView(); // Update editor view only if changes were made
                    success = true;
                    ExceptionHandling.LogWarning($"Replacement successful for '{original}' -> '{suggestion}'. {replacementCount} node(s) modified.");
                } else {
                     ExceptionHandling.LogWarning($"Replacement attempted for '{original}', but no nodes were actually modified (text might not match exactly within nodes found by XPath).");
                     success = false; // No actual replacement occurred
                }

            }
            catch (Exception ex) // Catch broader errors during the operation
            {
                ExceptionHandling.LogError($"Text replacement operation failed: {ex}");
                MessageBox.Show($"Error during text replacement: {ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                success = false;
            }
            finally
            {
                 // Ensure EndOperation is always called
                 try { activeDoc?.EndOperation(); } catch (Exception ex) { ExceptionHandling.LogError($"Failed to end replacement operation: {ex}"); }
            }
            return success;
        }


        // --- Window Hooking (for Owner relationship with Flare) ---

        private void HookFlareWindow()
        {
            try
            {
                _flareWindowHandle = GetFlareMainWindow();
                if (_flareWindowHandle != IntPtr.Zero)
                {
                    var interopHelper = new WindowInteropHelper(this);
                    interopHelper.Owner = _flareWindowHandle;

                    // Optional: Hook window messages if needed (currently empty)
                    // var source = HwndSource.FromHwnd(_flareWindowHandle);
                    // source?.AddHook(WndProc);
                } else {
                     ExceptionHandling.LogWarning("Could not find Flare main window handle to set owner.");
                }
            } catch (Exception ex) {
                 ExceptionHandling.LogError($"Failed to hook Flare window: {ex}");
            }
        }

        // Empty Window Procedure Hook (can be removed if unused)
        // private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        // {
        //     // Handle specific Windows messages here if needed
        //     handled = false;
        //     return IntPtr.Zero;
        // }

        // Finds the main window handle of the Flare process
        private IntPtr GetFlareMainWindow()
        {
             try {
                 // Using LINQ for potentially cleaner lookup
                Process flareProcess = Process.GetProcessesByName("Flare")
                                            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                return flareProcess?.MainWindowHandle ?? IntPtr.Zero;
            } catch (Exception ex) {
                ExceptionHandling.LogError($"Error finding Flare process: {ex}");
                return IntPtr.Zero;
            }
        }
    }
}
