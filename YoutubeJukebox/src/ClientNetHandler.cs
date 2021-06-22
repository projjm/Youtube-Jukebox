using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ProjjSerializer;

namespace YoutubeJukebox.Network
{

    public enum ControlMessage
    {
        CLEAR_BUFFER,
        PLAYBACK_START,
        PLAYBACK_PAUSE,
        SKIP_SONG
    }

    public enum YTMessageType
    {
        AUDIO_BUFFER,
        CONTROL_MSG,
        URL_REQUEST,
        GUI_MESSAGE,
        WAIT_FOR_BUFFER,
        SONG_PLAYING,
        SONG_TIME_UPDATE,
        MOVE_SONG_DOWN,
        MOVE_SONG_UP,
        REMOVE_SONG,
        SONG_QUEUE_UPDATE,
        AUTHENTICATION,
        AUTH_SUCCESSFUL
    }

    public enum ConnectionStatus
    {
        FAILED_TO_CONNECT,
        FAILED_TO_AUTHENTICATE,
        CONNECTION_SUCCESSFUL
    }

    public class ClientNetHandler
    {
        private string _hostName;
        private int _port;
        private TcpClient _client;
        private NetworkStream _networkStream;
        private bool _listening;

        private event Action onDisconnectedFromServer;
        private event Action<byte[]> onAudioReceived;
        private event Action onBufferClearMsg;
        private event Action onPlaybackStartMsg;
        private event Action<double> onWaitForBuffer;
        private event Action<List<SongData>> onSongQueueUpdated;
        private event Action<SongData> onSongPlaying;
        private event Action<int> onSongTimeUpdated;

        private const int connectionTimeout = 5000;
        private bool _shouldNotifyDisconnect = true;
        private bool _authenticated = false;
        private string _authPass;

        private ProjjSerializer<YTMessageType> _dataHandler;

        public ClientNetHandler(string hostName, int port, string password, out ConnectionStatus status, int timeout = connectionTimeout)
        {
            _dataHandler = new ProjjSerializer<YTMessageType>();
            RegisterMessageTypes();

            _hostName = hostName;
            _port = port;
            bool connected = TryConnectToServer(hostName, port, timeout);
            if (!connected)
            {
                status = ConnectionStatus.FAILED_TO_CONNECT;
                return;
            }
  
            _listening = true;
            _networkStream = _client.GetStream();
            ThreadPool.QueueUserWorkItem(ListenerThread);

            bool authSuccesful = TryAuthenticate(password, timeout);
            if (!authSuccesful)
            {
                DisconnectFromServer();
                status = ConnectionStatus.FAILED_TO_AUTHENTICATE;
                return;
            }
            _authPass = password;
            status = ConnectionStatus.CONNECTION_SUCCESSFUL; 
        }

        private void RegisterMessageTypes()
        {
            _dataHandler.BindMessageType<byte[]>(YTMessageType.AUDIO_BUFFER, (data) => onAudioReceived?.Invoke(data));
            _dataHandler.BindMessageType<int>(YTMessageType.WAIT_FOR_BUFFER, (data) => onWaitForBuffer?.Invoke(data));
            _dataHandler.BindMessageType<SongData>(YTMessageType.SONG_PLAYING, (data) => onSongPlaying?.Invoke(data));
            _dataHandler.BindMessageType<int>(YTMessageType.SONG_TIME_UPDATE, (data) => onSongTimeUpdated?.Invoke(data));
            _dataHandler.BindMessageType<string>(YTMessageType.GUI_MESSAGE, ShowGUIMessage);
            _dataHandler.BindMessageType<ControlMessage>(YTMessageType.CONTROL_MSG, HandleControlMessage);
            _dataHandler.BindMessageType<string>(YTMessageType.URL_REQUEST);
            _dataHandler.BindMessageType<int>(YTMessageType.MOVE_SONG_UP);
            _dataHandler.BindMessageType<int>(YTMessageType.MOVE_SONG_DOWN);
            _dataHandler.BindMessageType<int>(YTMessageType.REMOVE_SONG);
            _dataHandler.BindMessageType<List<SongData>>(YTMessageType.SONG_QUEUE_UPDATE, (data) => onSongQueueUpdated?.Invoke(data));
            _dataHandler.BindMessageType<string>(YTMessageType.AUTHENTICATION);
            _dataHandler.BindMessageType<bool>(YTMessageType.AUTH_SUCCESSFUL, (authSuccess) => _authenticated = authSuccess);

        }

        public string GetCurrentConnectionIP() => ((IPEndPoint)(_client.Client.RemoteEndPoint)).Address.ToString();

        public int GetCurrentConnectionPort() => ((IPEndPoint)(_client.Client.RemoteEndPoint)).Port;

        public string GetServerPassword() => _authPass;

        private bool TryConnectToServer(string hostName, int port, int timeoutMs)
        {
            Console.WriteLine("Connecting...");
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (_client == null)
            {
                if (sw.ElapsedMilliseconds >= timeoutMs)
                    return false;

                try
                {
                    _client = new TcpClient(hostName, port);
                }
                catch (SocketException)
                {
                    Thread.Sleep(250);
                }
            }

            return true;
        }

        private bool TryAuthenticate(string password, int timeoutMs)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (!_authenticated && sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!SendToServer(YTMessageType.AUTHENTICATION, password))
                    return false;

                Thread.Sleep(100);
            }

            return _authenticated;
        }

        public void OnDisconnected(Action onDisconnectedAction)
        {
            onDisconnectedFromServer = onDisconnectedAction;
        }

        public bool SendToServer(YTMessageType type, object payload)
        {
            byte[] packet = _dataHandler.GetSendBuffer(type, payload);

            try
            {
                _client.Client.Send(packet);
                return true;
            }
            catch (SocketException)
            {
                Console.WriteLine("Connection to server lost");
                _authenticated = false;
                if (_shouldNotifyDisconnect)
                    onDisconnectedFromServer?.Invoke();
                return false;
            }
        }

        private void ListenerThread(object state)
        {
            byte[] incomingBuffer = new byte[1024 * 16];
            while (_listening)
            {
                try
                {
                    int received = _client.Client.Receive(incomingBuffer);
                    byte[] b = new byte[received];
                    Buffer.BlockCopy(incomingBuffer, 0, b, 0, received);
                    _dataHandler.ReadIncomingData(b);
                }
                catch (Exception)
                {
                    Console.WriteLine("Connection to server lost");
                    if (_shouldNotifyDisconnect)
                        onDisconnectedFromServer?.Invoke();
                    break;
                }     
            }
        }

        private void HandleControlMessage(ControlMessage controlMessage)
        {
            Console.WriteLine("Got control message: " + controlMessage);
            switch (controlMessage)
            {
                case ControlMessage.CLEAR_BUFFER:
                    onBufferClearMsg?.Invoke();
                    break;
                case ControlMessage.PLAYBACK_START:
                    onPlaybackStartMsg?.Invoke();
                    break;
            }
        }

        private void ShowGUIMessage(string message)
        {
            //GUI implementation later
            Console.WriteLine(message);
        }

        public void OnWaitForBuffer(Action<double> onWaitForBufferAction)
        {
            onWaitForBuffer = onWaitForBufferAction;
        }

        public void OnAudioReceived(Action<byte[]> onAudioReceivedAction)
        {
            onAudioReceived = onAudioReceivedAction;
        }

        public void OnBufferClearMsg(Action onBufferClearMsgAction)
        {
            onBufferClearMsg = onBufferClearMsgAction;
        }

        public void OnPlaybackStartMsg(Action onPlaybackStartMsgAction)
        {
            onPlaybackStartMsg = onPlaybackStartMsgAction;
        }

        public void OnSongQueueUpdated(Action<List<SongData>> onSongQueueUpdatedAction)
        {
            onSongQueueUpdated = onSongQueueUpdatedAction;
        }


        public void OnSongPlaying(Action<SongData> onSongPlayingAction)
        {
            onSongPlaying = onSongPlayingAction;
        }

        public void OnSongTimeUpdated(Action<int> onSongTimeUpdatedAction)
        {
            onSongTimeUpdated = onSongTimeUpdatedAction;
        }

        public void DisconnectFromServer(bool notify = true)
        {
            _authenticated = false;
            if (_client != null && _client.Connected)
            {
                _shouldNotifyDisconnect = notify;
                _client.GetStream().Close();
                _client.Close();
            }
        }

        public void Dispose()
        {
            _listening = false;
            _client?.Close();
        }

    }
    public static class Extensions
    {
        public static T[] SubArray<T>(this T[] array, int offset, int length)
        {
            T[] result = new T[length];
            Array.Copy(array, offset, result, 0, length);
            return result;
        }
    }

}
