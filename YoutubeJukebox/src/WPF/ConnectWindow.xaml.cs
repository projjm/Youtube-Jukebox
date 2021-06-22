using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using YoutubeJukebox.Network;

namespace YoutubeJukebox
{
    /// <summary>
    /// Interaction logic for ConnectWindow.xaml
    /// </summary>
    public partial class ConnectWindow : Window
    {
        private event Action<ClientNetHandler> onConnectionSuccess;

        public ConnectWindow(Action<ClientNetHandler> onConnectionSuccessAction, IYoutubeJukeboxSettings settings)
        {
            InitializeComponent();
            onConnectionSuccess = onConnectionSuccessAction;
            AutoFill(settings);
        }

        private void AutoFill(IYoutubeJukeboxSettings settings)
        {
            ipAddressText.Text = settings.ServerIpAddress;
            portText.Text = settings.ServerPort.ToString();
            passwordText.Password = settings.ServerPassword;
        }

        private void connectButton_Click(object sender, RoutedEventArgs e)
        {
            connectButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B8B8B"));

            string ipAddress = ipAddressText.Text;
            string password = passwordText.Password;
            int port;
            bool validPort = Int32.TryParse(portText.Text, out port);

            if (!validPort)
            {
                var result = MessageBox.Show("Invalid port number", "Invalid Port", MessageBoxButton.OK);
                connectButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDDDDDD"));
                return;
            }

            ClientNetHandler _netHandler = new ClientNetHandler(ipAddress, port, password, out ConnectionStatus status);

            switch (status)
            {
                case ConnectionStatus.FAILED_TO_CONNECT:
                    MessageBox.Show("Failed to connect to server", "Connection Failed", MessageBoxButton.OK);
                    connectButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDDDDDD"));
                    return;
                case ConnectionStatus.FAILED_TO_AUTHENTICATE:
                    MessageBox.Show("Password incorrect.", "Connection Failed", MessageBoxButton.OK);
                    connectButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDDDDDD"));
                    return;
            }

            onConnectionSuccess?.Invoke(_netHandler);
            Close();
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ipAddressText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                connectButton_Click(sender, e);
        }

        private void portText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                connectButton_Click(sender, e);
        }
    }
}
