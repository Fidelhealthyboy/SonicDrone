﻿using DroneMonitor.Infrastructure.Contracts;
using DroneMonitor.Infrastructure.Events;
using Microsoft.Practices.ServiceLocation;
using Microsoft.Win32;
using Prism.Events;
using Prism.Regions;
using System;
using System.Windows;
using System.Windows.Controls;
using static DroneMonitor.Infrastructure.Utils.RegionNames;

namespace DroneMonitor.Infrastructure.Services {
    public class WindowService : IWindowService {

        public T Launch<T>() where T : ContentControl {
            var viewName = typeof(T).Name;

            var window = RegionManager.Regions[ContentRegion].GetView(viewName);
            if (window == null) {
                window = Activator.CreateInstance(typeof(T));
                RegionManager.Regions[ContentRegion].Add(window, viewName)
                             .Regions[ContentRegion].Activate(window);
            }
            else
            {
                RegionManager.Regions[ContentRegion].Remove(window);
                RegionManager.Regions[ContentRegion].Add(window, viewName)
                             .Regions[ContentRegion].Activate(window);
            }
            _activeWindow = (T)window;
            EventAggregator.GetEvent<ViewChangedEvent>().Publish();
            return (T)window;
        }

        public T Launch<T>(bool createNew) where T : ContentControl {
            if (!createNew) {
                return Launch<T>();
            }
            var viewName = typeof(T).Name;

            var window = RegionManager.Regions[ContentRegion].GetView(viewName);
            if (window == null) {
                window = Activator.CreateInstance(typeof(T));
                RegionManager.Regions[ContentRegion].Add(window, viewName)
                             .Regions[ContentRegion].Activate(window);
            }
            else {
                RegionManager.Regions[ContentRegion].Remove(window);
                window = Activator.CreateInstance(typeof(T));
                RegionManager.Regions[ContentRegion].Add(window, viewName)
                             .Regions[ContentRegion].Activate(window);
            }
            _activeWindow = (T)window;
            EventAggregator.GetEvent<ViewChangedEvent>().Publish();
            return (T)window;
        }

        public T LaunchDialog<T>() where T : Window {
            var window = Activator.CreateInstance(typeof(T));
            var win = (T)window;
            _activeDialog = win;
            win.ShowDialog();
            return win;
        }

        public void CloseDialog() {
            var isVisible = _activeDialog?.IsVisible;
            if (isVisible == true) {
                _activeDialog.Close();
            }
        }

        public ContentControl GetActiveWindow() {
            return _activeWindow;
        }

        public string LaunchFileDialog() {
            var dialog = new OpenFileDialog {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Filter = "Excel files (*.xlsx*)|*.xlsx*|Text files (*.txt*)|*.txt*|All files (*.*)|*.*",
                // "Image files (*.bmp, *.jpg)|*.bmp;*.jpg|All files (*.*)|*.*"'
                Title = "Select input data stored in text file"
            };
            dialog.ShowDialog(Application.Current.MainWindow);
           return dialog.FileName;
        }

        public bool? DisplayAlert(string title, string message, bool yesNo = false) {
            MessageBoxResult result;
            if (yesNo) {
                result = MessageBox.Show(message, title,MessageBoxButton.YesNo);
            }
            else {
                result = MessageBox.Show(message, title);
            }
            return MessageBoxResult.Yes == result ? true : false;
        }

        private IRegionManager RegionManager => ServiceLocator.Current.GetInstance<IRegionManager>();

        private IEventAggregator EventAggregator => ServiceLocator.Current.GetInstance<IEventAggregator>();

        private ContentControl _activeWindow;
        private Window _activeDialog;
    }
}
