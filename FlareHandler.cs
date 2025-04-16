using System;
using B3.PluginAPIKit;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Reflection;

namespace AILinguistic
{

    public class FlareHandler : IPlugin
    {
        private IHost _host;
        private bool _isActivated;
        private IRibbonTab _tab;
        private DockableWindow _dockableWindow;

        public bool IsActivated => _isActivated;

        public string GetVersion() => "1.0";
        public string GetAuthor() => "The BR Content Validation Tool";
        public string GetDescription() => "Provides real-time AI enhanced grammar and style improvements, saving review time, enhancing content quality, and standardizing communication for Broadridge";
        public string GetName() => "The BR Content Validation Tool";

        public void Initialize(IHost host)
        {
            try
            {
                if (host == null)
                {
                    throw new ArgumentNullException(nameof(host), "Host can not be null");
                }
                _host = host;
                AddRibbonButton();
            }
            catch (Exception ex)
            {
                // Use the full exception details for logging
                ExceptionHandling.LogError($"Initialization failed: {ex}");
            }

        }

        private void AddRibbonButton()
        {
            var navContext = _host.GetNavContext();
            var ribbon = navContext.GetRibbon();

            _tab = ribbon.AddNewRibbonTab("The BR Content Validation Tool", "AT");
            var group = _tab.AddNewRibbonGroup("Validation");

            var buttonData = new RibbonControlData
            {
                Label = "Open",
                LargeImage = LoadIcon(),
                // Use the embedded RelayCommand
                Command = new RelayCommand(OpenDockWindow)
            };

            group.AddRibbonButton(buttonData);
        }

        private BitmapImage LoadIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                // Resource name: DefaultNamespace.FolderName.FileName
                // Ensure 'Resources' folder exists and 'app.png' Build Action is 'Embedded Resource'
                string resourceName = "AILinguistic.Resources.app.png"; 

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        // Make warning more specific
                        ExceptionHandling.LogWarning($"Icon resource stream not found for: {resourceName}. Ensure the path is correct and Build Action is Embedded Resource.");
                        return new BitmapImage(); // Return empty image on failure
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; 
                    bitmap.EndInit();
                    bitmap.Freeze(); // Make thread-safe for UI
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                // Log the full exception for debugging
                ExceptionHandling.LogError($"Failed to load icon resource '{resourceName}': {ex}");
                return new BitmapImage(); // Return empty image on error
            }
        }

        private void OpenDockWindow()
        {
            if (_dockableWindow == null)
            {
                _dockableWindow = new DockableWindow(_host); 
                _dockableWindow.Closed += (s, e) => _dockableWindow = null;
            }
            _dockableWindow.Show();
            _dockableWindow.Activate(); // Ensure the window gets focus
        }

        public void Execute()
        {
            _isActivated = true;
            // Consider if showing the window here is needed depending on Flare's behavior
            // OpenDockWindow(); 
        }


        public void Stop()
        {
            // Check if IHost is IDisposable if API docs are available, otherwise remove Dispose call.
            // Assuming it's not IDisposable for now.
            // _host?.Dispose(); 
            _dockableWindow?.Close(); // Close the window if open
            _isActivated = false;
        }

        // Standard RelayCommand implementation 
        public class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;

            public RelayCommand(Action execute, Func<bool> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            // Use CommandManager for automatic UI updates
            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }

            public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
            public void Execute(object parameter) => _execute();

            // Optional: Method to manually trigger CanExecuteChanged check if needed
            public void RaiseCanExecuteChanged()
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
