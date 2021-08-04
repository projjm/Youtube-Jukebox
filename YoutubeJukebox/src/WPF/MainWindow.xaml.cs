using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Config.Net;
using YoutubeJukebox.Network;

namespace YoutubeJukebox
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IYoutubeJukeboxSettings _settings;
        private ClientNetHandler _netHandler;
        private BufferedAudioPlayer _audioPlayer;
        private SongDataHandler _songDataHandler;
        private DispatcherTimer _dispatcherTimer;

        private int _currentSongTotalSecsBase = 0;
        private float _volume;
        private float _volumeBeforeMute;
        private bool _isMuted;

        public MainWindow()
        {
            _settings = new ConfigurationBuilder<IYoutubeJukeboxSettings>().UseIniFile("config.ini").Build();
            ConfigHelper.TryWriteDefaults(_settings);

            InitializeComponent();
            TryConnectFromSettings();
            HideStartControls();
        }


        private void TryConnectFromSettings()
        {
            if (_settings.ServerIpAddress == "")
                return;

            int timeout = 1500;
            ClientNetHandler netHandler = new ClientNetHandler(_settings.ServerIpAddress, _settings.ServerPort,
                _settings.ServerPassword, out ConnectionStatus status, timeout);

            if (status != ConnectionStatus.CONNECTION_SUCCESSFUL)
                return;

            OnConnectedToServer(netHandler);
        }

        private void HideStartControls()
        {
            skipButton.IsEnabled = false;
        }

        private void OnConnectedToServer(ClientNetHandler netHandler)
        {
            if (_netHandler != null)
            {
                _netHandler.DisconnectFromServer(false);
                _netHandler.Dispose();
            }

            if (_audioPlayer != null)
                _audioPlayer.Dispose();

            _settings.ServerIpAddress = netHandler.GetCurrentConnectionIP();
            _settings.ServerPort = netHandler.GetCurrentConnectionPort();
            _settings.ServerPassword = netHandler.GetServerPassword();

            _netHandler = netHandler;
            _netHandler.OnDisconnected(OnDisconnectedFromServer);
            _audioPlayer = new BufferedAudioPlayer(_netHandler);
            _songDataHandler = new SongDataHandler(_netHandler, this);

            _dispatcherTimer = new DispatcherTimer();
            _dispatcherTimer.Tick += OnTimerTickSecond;
            _dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            _dispatcherTimer.Start();
            this.Dispatcher.Invoke(() =>
            {
                connectionStatusText.Text = "Connected";
                suggestionBox.IsEnabled = true;
                disconnectButton.IsEnabled = true;
            });
        }

        private void OnDisconnectedFromServer()
        {
            _netHandler.Dispose();
            _audioPlayer.Dispose();
            _songDataHandler = null;
            _dispatcherTimer = null;
            this.Dispatcher.Invoke(() =>
            {
                connectionStatusText.Text = "Disconnected";
                suggestionBox.IsEnabled = false;
                disconnectButton.IsEnabled = false;
                songDurationCurrent.Text = GetFormattedTime("0", "0");
                songDurationEnd.Text = GetFormattedTime("0", "0");
                songQueuePanel.Children.Clear();
                songInfoText.Text = "";
                skipButton.IsEnabled = false;
                songProgressBar.Value = 0;
            });

        }

        private void suggestionBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                string suggestion = suggestionBox.Text;
                _netHandler.SendToServer(YTMessageType.URL_REQUEST, suggestion);

                suggestionBox.Clear();
            }
        }

        private void OnTimerTickSecond(object sender, EventArgs e)
        {
            if (!_audioPlayer.WaveOutInitialised())
                return;

            if (_songDataHandler == null)
                return;

            this.Dispatcher.Invoke(() =>
            {
                int songTotalSeconds = _songDataHandler.GetCurrentSongTotalSeconds();
                int currentTotalSeconds = _currentSongTotalSecsBase + _audioPlayer.GetTotalSecondsPlayed(); // + 1 roughly compensates for packet delay

                if (songTotalSeconds == 0)
                    return;

                bool hasHours = ((currentTotalSeconds / 60) / 60) > 1.0;

                string hours = ((int)((currentTotalSeconds / 60) / 60)).ToString();
                string minutes = ((int)(currentTotalSeconds / 60)).ToString();
                string seconds = ((int)(currentTotalSeconds % 60)).ToString();

                if (hasHours)
                    songDurationCurrent.Text = GetFormattedTime(hours, minutes, seconds);
                else
                    songDurationCurrent.Text = GetFormattedTime(minutes, seconds);

                songProgressBar.Value = ((double)currentTotalSeconds / (double)songTotalSeconds) * 100.0;
            });
        }

        public void UpdateSongQueueList(List<SongData> queue)
        {
            this.Dispatcher.Invoke(() =>
            {
                songQueuePanel.Children.Clear();

                foreach (SongData song in queue)
                {
                    SolidColorBrush whiteBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#FFFFFFFF"));
                    SolidColorBrush purpleBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#FFB0234A"));

                    SongListing songListing = new SongListing();

                    songListing.moveSongDownButton.Click += new RoutedEventHandler((s, e) => OnMoveSongDownClicked(song));
                    songListing.moveSongUpButton.Click += new RoutedEventHandler((s, e) => OnMoveSongUpClicked(song));
                    songListing.removeSongButton.Click += new RoutedEventHandler((s, e) => OnRemoveSongClicked(song));

                    bool isCurrentSong = song.queueId == _songDataHandler.GetCurrentSongPlaying().queueId;

                    if (song.hiddenRequested)
                        songListing.songName.Text = "???";
                    else
                        songListing.songName.Text = song.title;

                    if (song.hiddenRequested)
                        songListing.songTime.Text = "???";
                    else
                        songListing.songTime.Text = GetFormattedTime(song.durationMinutes.ToString(), song.durationSeconds.ToString());

                    if (isCurrentSong)
                    {
                        songListing.songName.Foreground = whiteBrush;
                        songListing.songTime.Foreground = whiteBrush;
                        songListing.songGrid.Background = purpleBrush;
                        songListing.moveSongDownButton.Foreground = whiteBrush;
                        songListing.moveSongUpButton.Foreground = whiteBrush;
                        songListing.removeSongButton.Foreground = whiteBrush;
                    }

                    Border divider = new Border();
                    divider.Margin = new Thickness(0);
                    divider.Height = 1;
                    divider.BorderThickness = new Thickness(0, 1, 0, 0);

                    songQueuePanel.Children.Add(songListing);
                    songQueuePanel.Children.Add(divider);
                }
            });
        }

        private void OnMoveSongDownClicked(SongData song)
        {
            _netHandler.SendToServer(YTMessageType.MOVE_SONG_DOWN, song.queueId);
        }

        private void OnMoveSongUpClicked(SongData song)
        {
            _netHandler.SendToServer(YTMessageType.MOVE_SONG_UP, song.queueId);
        }

        private void OnRemoveSongClicked(SongData song)
        {
            _netHandler.SendToServer(YTMessageType.REMOVE_SONG, song.queueId);
        }

        public void UpdateSongPlaying(SongData songPlaying)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (songPlaying.id == "NULL")
                {
                    songInfoText.Text = "";
                    songDurationEnd.Text = "00:00";
                    songDurationCurrent.Text = "00:00";
                    songProgressBar.Value = 0;
                    skipButton.IsEnabled = false;
                    return;
                }

                bool hasHours = songPlaying.durationHours > 0;

                string durationHours = songPlaying.durationHours.ToString();
                string durationMins = songPlaying.durationMinutes.ToString();
                string durationSecs = (songPlaying.durationSeconds > 0) ? (songPlaying.durationSeconds - 1).ToString() : songPlaying.durationSeconds.ToString();

                if (songPlaying.hiddenRequested)
                    songInfoText.Text = "Playing: ???";
                else
                    songInfoText.Text = "Playing: " + songPlaying.title;

                if (songPlaying.hiddenRequested)
                    songDurationEnd.Text = "???";
                else if (hasHours)
                    songDurationEnd.Text = GetFormattedTime(durationHours, durationMins, durationSecs);
                else
                    songDurationEnd.Text = GetFormattedTime(durationMins, durationSecs);

                skipButton.IsEnabled = true;
                songProgressBar.Value = 0;
            });

        }

        private string GetFormattedTime(string mins, string secs)
        {
            return ForceDoubleDigitFormat(mins) + ":" + ForceDoubleDigitFormat(secs);
        }

        private string GetFormattedTime(string hours, string mins, string secs)
        {
            return ForceDoubleDigitFormat(hours) + ":" + ForceDoubleDigitFormat(mins) + ":" + ForceDoubleDigitFormat(secs);
        }

        private string ForceDoubleDigitFormat(string input)
        {
            return input.Length < 2 ? ("0" + input) : input;
        }

        public void UpdateBaseTime(int totalSeconds) => _currentSongTotalSecsBase = totalSeconds;

        private void connectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectWindow connectWindow = new ConnectWindow(OnConnectedToServer, _settings);
            connectWindow.Show();
        }

        private void disconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_netHandler != null)
                _netHandler.DisconnectFromServer();
        }

        private void exitButton_Click(object sender, RoutedEventArgs e)
        {
            _netHandler.DisconnectFromServer();
            _netHandler.Dispose();
            _audioPlayer.Dispose();
            Close();
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isMuted)
                Unmute(false);

            _volume = (float)e.NewValue / 10f;

            if (_audioPlayer != null)
                _audioPlayer.SetVolume(_volume);
        }

        private void skipButton_Click(object sender, RoutedEventArgs e)
        {
            _netHandler.SendToServer(YTMessageType.CONTROL_MSG, ControlMessage.SKIP_SONG);
        }

        private void AdjustWindowSize()
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void closeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void maximizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            AdjustWindowSize();
        }

        private void minimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void menuBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void muteButton_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;

            if (_isMuted)
                Mute();
            else
                Unmute();
        }

        private void Mute()
        {
            if (_audioPlayer != null)
                _audioPlayer.SetVolume(0f);

            _volumeBeforeMute = _volume;
            _volume = 0;
            volumeSlider.Value = 0;
            muteButtonImage.Source = new BitmapImage(new Uri("muted.png", UriKind.Relative));
        }

        private void Unmute(bool usePreviousVolume = true)
        {
            if (usePreviousVolume)
            {
                _volume = _volumeBeforeMute;
                if (_audioPlayer != null)
                    _audioPlayer.SetVolume(_volume);
                
                volumeSlider.Value = _volumeBeforeMute * 10.0;
            }
            
            muteButtonImage.Source = new BitmapImage(new Uri("unmuted.png", UriKind.Relative));
        }
    }
}
