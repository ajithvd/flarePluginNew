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
                    throw new ArgumentNullException(nameof(host), "Host can not bel null");
                }
                _host = host;
                AddRibbonButton();
            }
            catch (Exception ex)
            {
                ExceptionHandling.LogError($"Initialization failed : {ex}");
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
                Command = new RelayCommand(OpenDockWindow, null)
            };

            group.AddRibbonButton(buttonData);
        }

        private BitmapImage LoadIcon()
        {
            try
            {
                // Get the assembly containing the embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                // Resource name: [Namespace].[Folder].[Filename]
                string resourceName = "AILinguistic.Resources.app.png";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        ExceptionHandling.LogWarning("Icon not found");
                        return new BitmapImage();
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                ExceptionHandling.LogError($"Initialization failed : {ex}");
                return new BitmapImage();
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
        }

        public void Execute()
        {
            _isActivated = true;

        }


        public void Stop()
        {
            // Clean up other resources
            _host?.Dispose();
            _dockableWindow?.Close();
            _isActivated = false;
        }

        public class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;

            public RelayCommand(Action execute, Func<bool> canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public event EventHandler CanExecuteChanged;

            public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
            public void Execute(object parameter) => _execute();
        }

    }

}
