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
using static AILinguistic.FlareHandler;
using System.Text;
using System.Linq;
using static System.Net.WebRequestMethods;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace AILinguistic
{
    public class DockableWindow : Window
    {

        private IntPtr _flareWindowHandle;
        private readonly IHost _host;
        private List<SuggestionItem> _suggestions = new List<SuggestionItem>();
        private ComboBox _checkTypeComboBox;
        private ScrollViewer _mainScrollViewer;
        private string _selectedCheckType = "Linguistic check";
        private string _lastCheckedDocUrl;



        public DockableWindow(IHost host)
        {
            _host = host;
            InitializeWindow();
            LogInformations.InitializeLogFile();
            InitializeContent();
            HookFlareWindow();
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
            Left = SystemParameters.PrimaryScreenWidth - Width;
            Top = 0;
            ResizeMode = ResizeMode.CanResize;
            ShowActivated = false;
        }



        private UIElement CreateMainContent()
        {
            var mainStackPanel = new StackPanel { Orientation = Orientation.Vertical };

            // Header with dropdown and button
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(5),
                Padding = new Thickness(8),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        (_checkTypeComboBox = new ComboBox
                        {
                            Width = 180,
                            Margin = new Thickness(0, 0, 10, 0),
                            ItemsSource = new List<string> { "Linguistic check", "Terminology check" },
                            SelectedItem = _selectedCheckType,
                            FontSize = 14,
                            Foreground = Brushes.Navy
                        }),
                        new Button
                        {
                            Content = "Check",
                            Background = new SolidColorBrush(Color.FromRgb(30, 144, 255)),
                            Foreground = Brushes.White,
                            FontWeight = FontWeights.SemiBold,
                            Padding = new Thickness(12, 5, 12, 5),
                            Cursor = Cursors.Hand,
                            Margin = new Thickness(0, 0, 5, 0),
                            Command = new RelayCommand(async () => await ExecuteCheckCommand())
                        }
                    }
                }
            };
            // Subscribe to selection changes
            _checkTypeComboBox.SelectionChanged += (s, e) =>
            {
                _selectedCheckType = _checkTypeComboBox.SelectedItem?.ToString();
            };

            // Suggestions area
            _mainScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = CreateItemList(),
                Height = 500
            };

            mainStackPanel.Children.Add(headerBorder);
            mainStackPanel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 5) });
            mainStackPanel.Children.Add(_mainScrollViewer);

            return mainStackPanel;
        }

        private async Task ExecuteCheckCommand()
        {
            try
            {
                string selectedCheck = "";
                string documentText = "";
                string rulesOption = "";
                IDocument activeDoc = null;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    selectedCheck = _selectedCheckType;
                    if (string.IsNullOrEmpty(selectedCheck))
                    {
                        MessageBox.Show("Please select a check type first!");
                        return;
                    }
                    var editorContext = _host?.GetEditorContext();
                    if (editorContext == null)
                    {
                        MessageBox.Show("Unable to access editor context");
                        return;
                    }

                    activeDoc = editorContext?.GetActiveDocument();
                    _lastCheckedDocUrl = activeDoc?.GetSourceUrl();
                    if (activeDoc == null)
                    {
                        MessageBox.Show("No active document found");
                        return;
                    }

                    var xmlDoc = activeDoc.GetXmlDocument();
                    if (xmlDoc == null)
                    {
                        MessageBox.Show("Could not retrieve document content");
                        return;
                    }
                    documentText = ExtractAllTextFromXml(xmlDoc);
                    //documentText = ExtractAllTextFromXml(xmlDoc)?.Replace("\r\n", "\\r").Replace("\r", "\\r").Replace("\n", "\\r");
                    rulesOption = selectedCheck == "Linguistic check" ? "rulesDoc1" : "rulesDoc2";
                });
                if (string.IsNullOrEmpty(documentText))
                {
                    return;
                }
                _suggestions.Clear();

                // Show loading indicator
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Content = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                {
                    new TextBlock
                    {
                        Text = "Analyzing document content...",
                        FontSize = 16,
                        Foreground = Brushes.Gray
                    }
                }
                    };
                });
                try
                {
                    using (var client = new HttpClient())
                    {
                        var payload = new { rulesOption, text = documentText };
                        var content = new StringContent(
                            JsonConvert.SerializeObject(payload),
                            Encoding.UTF8,
                            "application/json"
                        );
                        ExceptionHandling.LogError($"-------------------API payload for tesing purpose-------------: {JsonConvert.SerializeObject(payload)}");
                        var response = await client.PostAsync("http://JSQPVWAIW01:2122/validate", content);
                        var responseContent = await response.Content.ReadAsStringAsync();
                        ExceptionHandling.LogError($"-------------------API response for tesing purpose-----------------: {responseContent.ToString()}");

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                try
                                {
                                    var apiResponse = JsonConvert.DeserializeObject<ApiResponse>(responseContent);
                                    _suggestions = apiResponse?.Data ?? new List<SuggestionItem>();

                                    if (_suggestions.Any())
                                    {
                                        Content = CreateMainContent();
                                    }
                                    else
                                    {
                                        Content = CreateMainContent();
                                        MessageBox.Show("No issues found! Great work!");
                                    }
                                }
                                catch (Exception jsonEx)
                                {
                                    Content = CreateMainContent();
                                    MessageBox.Show($"Failed to parse API response");
                                    ExceptionHandling.LogError($"Failed to parse API response: {jsonEx.Message}");
                                }
                            }
                            else
                            {
                                Content = CreateMainContent();
                                MessageBox.Show($"API request failed");
                                ExceptionHandling.LogError($"API request failed: {(int)response.StatusCode} {response.StatusCode}): {responseContent}");
                            }
                        });
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Content = CreateMainContent();
                        MessageBox.Show($"Network error");
                        ExceptionHandling.LogError($"Network error: {httpEx.Message}");
                    });
                }
                catch (TaskCanceledException)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Content = CreateMainContent();
                        MessageBox.Show("The request timed out. Please try again.");
                    });
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Content = CreateMainContent();
                    _mainScrollViewer.ScrollToTop();
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Content = CreateMainContent();
                    MessageBox.Show($"Error");
                    ExceptionHandling.LogError($"Create Main Content error: {ex.Message}");
                });
            }
        }

        private string ExtractAllTextFromXml(XmlDocument xmlDoc)
        {

            var sb = new StringBuilder();
            var navigator = xmlDoc.CreateNavigator();

            // Select ONLY <p> tags and their content
            var expr = navigator.Compile("//p");
            var iterator = navigator.Select(expr);

            while (iterator.MoveNext())
            {
                // Get FULL text content including nested tags within <p>
                string text = iterator.Current.Value
                    .Replace("\n", " ")      // Replace newlines with spaces
                    .Replace("  ", " ")      // Collapse double spaces
                    .Trim();

                // Skip empty paragraphs (like <p>&#160;</p>)
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.Append(text);
                    sb.Append("\r"); // Add line break after each paragraph
                }
            }

            // Remove trailing \r if exists
            if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                sb.Length--;

            return sb.ToString();

            //var sb = new StringBuilder();
            //var navigator = xmlDoc.CreateNavigator();
            //// XPath to select block elements: <p>, <h1>, <td>, etc.
            //var expr = navigator.Compile("//p | //h1 | //td");
            //var iterator = navigator.Select(expr);

            //while (iterator.MoveNext())
            //{
            //    // Get the inner text of the block element (includes nested spans)
            //    string text = iterator.Current.Value.Trim();

            //    if (!string.IsNullOrEmpty(text))
            //    {
            //        sb.Append(text);
            //        sb.Append("\r"); // Add line break after each block
            //    }
            //}

            //// Remove the trailing \r if any
            //if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
            //    sb.Length--;

            //return sb.ToString();
        }

        private StackPanel CreateItemList()
        {
            var stackPanel = new StackPanel();

            if (_suggestions == null || !_suggestions.Any())
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "No suggestions found",
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return stackPanel;
            }

            foreach (var item in _suggestions)
            {
                stackPanel.Children.Add(CreateListItem(item));
            }

            return stackPanel;
        }

        private UIElement CreateListItem(SuggestionItem item)
        {

            if (item == null) return new TextBlock { Text = "Invalid suggestions" };

            var itemPanel = new StackPanel
            {
                Margin = new Thickness(5),
                Background = Brushes.White,
                Orientation = Orientation.Vertical
            };

            // Clickable text block
            var textBlock = new Border
            {
                Padding = new Thickness(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "Click to highlight original text in document",
                Child = new StackPanel
                {
                    Children =
            {
                new TextBlock
                {
                    Text = $"Original: {item.Original}",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = $"Corrected: {item.Corrected}",
                    Foreground = Brushes.DarkGreen,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                }
            }
                }
            };

            textBlock.MouseDown += (s, e) => HighlightOriginalText(item.Original);

            // Buttons panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };

            var approveButton = new Button
            {
                Content = "Approve",
                Background = Brushes.LimeGreen,
                Foreground = Brushes.White,
                Width = 80,
                Margin = new Thickness(5),
                Tag = item
            };

            var rejectButton = new Button
            {
                Content = "Reject",
                Background = Brushes.OrangeRed,
                Foreground = Brushes.White,
                Width = 80,
                Margin = new Thickness(5),
                Tag = item
            };

            approveButton.Click += (s, e) =>
            {
                var activeDoc = _host.GetEditorContext()?.GetActiveDocument();
                if (activeDoc == null || activeDoc.GetSourceUrl() != _lastCheckedDocUrl)
                {
                    MessageBox.Show(" Document has changed,please re-run thecheck");
                    _suggestions.Clear();
                    Content = CreateMainContent();
                    return;
                }

                var approvedItem = (SuggestionItem)((Button)s).Tag; // Get the item from Tag
                                                                    // New validation check
                if (string.IsNullOrWhiteSpace(approvedItem.Original) ||
                    string.IsNullOrWhiteSpace(approvedItem.Corrected))
                {
                    MessageBox.Show("Invalid replacement values");
                    return;
                }
                try
                {
                    bool replacementSuccessful = ReplaceText(approvedItem.Original, approvedItem.Corrected);

                    if (replacementSuccessful)
                    {
                        _suggestions.Remove(approvedItem);
                        LogInformations.LogAction("Approved", approvedItem.Original, approvedItem.Corrected);
                        Content = CreateMainContent();
                    }
                    else
                    {
                        MessageBox.Show("Unable to find the original text in the document.");
                    }
                }
                catch (Exception ex)
                {
                    ExceptionHandling.LogError($"Approval failed: {ex}");
                }
               // Content = CreateMainContent();
            };

            rejectButton.Click += (s, e) =>
            {
                var activeDoc = _host.GetEditorContext()?.GetActiveDocument();
                if (activeDoc == null || activeDoc.GetSourceUrl() != _lastCheckedDocUrl)
                {
                    MessageBox.Show(" Document has changed,please re-run thecheck");
                    _suggestions.Clear();
                    Content = CreateMainContent();
                    return;
                }
                var rejectedItem = (SuggestionItem)((Button)s).Tag; // Get the item from Tag

                // Remove from suggestions
                _suggestions.Remove(rejectedItem);

                // Log the rejection
                LogInformations.LogAction("Rejected", rejectedItem.Original, rejectedItem.Corrected);

                // Refresh UI
                Content = CreateMainContent();
            };

            // Add hover effect
            textBlock.MouseEnter += (s, e) =>
            {
                textBlock.Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
            };

            textBlock.MouseLeave += (s, e) =>
            {
                textBlock.Background = Brushes.Transparent;
            };

            buttonPanel.Children.Add(approveButton);
            buttonPanel.Children.Add(rejectButton);

            itemPanel.Children.Add(textBlock);
            itemPanel.Children.Add(new Separator { Background = Brushes.LightGray, Margin = new Thickness(0, 5, 0, 0) });
            itemPanel.Children.Add(buttonPanel);

            return itemPanel;
        }

        private void HighlightOriginalText(string originalText)
        {
            var editorContext = _host?.GetEditorContext();
            var activeDoc = editorContext?.GetActiveDocument();

            if (activeDoc == null || activeDoc.GetSourceUrl() != _lastCheckedDocUrl)
            {
                MessageBox.Show(" Document has changed,please re-run thecheck");
                _suggestions.Clear();
                Content = CreateMainContent();
                return;
            }

            try
            {
                activeDoc.StartOperation("Highlight Operation");
                activeDoc.Select(originalText);
                //activeDoc.Selection.ChangeBackColor(System.Drawing.Color.Yellow);
                activeDoc.UpdateView();
            }
            finally
            {
                activeDoc.EndOperation();
            }
        }

        private bool ReplaceText(string original, string suggestion)
        {
            var editorContext = _host?.GetEditorContext();
            var activeDoc = editorContext?.GetActiveDocument();

            if (activeDoc == null || activeDoc?.GetSourceUrl() != _lastCheckedDocUrl)
            {
                MessageBox.Show(" Document has changed,please re-run thecheck");
                _suggestions.Clear();
                Content = CreateMainContent();
                return false;
            }

            try
            {
                activeDoc?.StartOperation("Text Replacement");

                var xmlDoc = activeDoc?.GetXmlDocument();
                if (xmlDoc == null) return false;

                string safeOriginal = original.Replace("\"", "'");
                var xpath = $"//text()[contains(., '{safeOriginal}')]";
                ExceptionHandling.LogError($"safer orginal: {safeOriginal}");

                var nodes = xmlDoc.SelectNodes(xpath);
                // Use double quotes for the XPath query and handle quotes safely inside with '&apos;'
                //var nodes = xmlDoc.SelectNodes($"//text()[contains(., \'{safeOriginal}\')]");

                ExceptionHandling.LogError($"nodessssssssss: {nodes}"); //remove 

                if (nodes == null || nodes.Count == 0)
                {
                    return false; // Original text not found
                }

                foreach (XmlNode node in nodes)
                {
                    try
                    {
                        ExceptionHandling.LogError($"node: {node}"); //remove 
                        node.Value = node.Value.Replace(original, suggestion);
                    }
                    catch (Exception ex)
                    {
                        ExceptionHandling.LogError($"Failed to replace text in node: {ex}");
                        continue;
                    }
                }

                activeDoc?.UpdateView();       
                return true;
            }
            catch (Exception ex)
            {
                ExceptionHandling.LogError($"Text replacement failed: {ex}");
                //MessageBox.Show("Failed to replace text in document");
                return false;
            }
            finally
            {
                try
                {
                    activeDoc?.EndOperation();
                }
                catch (Exception ex)
                {
                    ExceptionHandling.LogError($"Failed to end operation: {ex}");
                }
            }
        }

        private void HookFlareWindow()
        {
            _flareWindowHandle = GetFlareMainWindow();
            if (_flareWindowHandle != IntPtr.Zero)
            {
                var source = HwndSource.FromHwnd(_flareWindowHandle);
                source?.AddHook(WndProc);

                // Add these lines to set window owner
                var interopHelper = new WindowInteropHelper(this);
                interopHelper.Owner = _flareWindowHandle;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {

            return IntPtr.Zero;
        }

        private IntPtr GetFlareMainWindow()
        {
            foreach (Process proc in Process.GetProcessesByName("Flare"))
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    return proc.MainWindowHandle;
                }
            }
            return IntPtr.Zero;
        }



    }

}

