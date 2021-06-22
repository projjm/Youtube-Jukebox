using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ProjjSerializer;

namespace YoutubeJukeboxServer
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

    class ServerNetHandler
    {
        private TcpListener _listener;
        private volatile List<TcpClient> _unauthenticated;
        private volatile List<TcpClient> _clients;
        private int _port;
        private bool _listeningForClients;
        private bool _listeningToClients;

        private event Action<TcpClient> onClientAuthenticated;
        private event Action<string> onUrlRequested;
        private event Action onSkipSongMsg;
        private event Action<int> onMoveSongDownMsg;
        private event Action<int> onMoveSongUpMsg;
        private event Action<int> onRemoveSongMsg;
        private ProjjSerializer<YTMessageType> _dataHandler;
        private string _serverPassword;

        private const int BufferSize = 1024 * 16;

        public ServerNetHandler(string ipAddress, int port, string password)
        {
            _clients = new List<TcpClient>();
            _unauthenticated = new List<TcpClient>();

            _dataHandler = new ProjjSerializer<YTMessageType>();
            RegisterMessageTypes();

            _listener = new TcpListener(IPAddress.Parse(ipAddress), port);
            
            _port = port;
            _serverPassword = password;
            _listeningForClients = true;
            _listeningToClients = true;

            _listener.Start();
            ThreadPool.QueueUserWorkItem(ClientConnectionListenerThread);
        }
         
        private void RegisterMessageTypes()
        {
            _dataHandler.BindMessageType<ControlMessage>(YTMessageType.CONTROL_MSG, HandleControlMessage);
            _dataHandler.BindMessageType<string>(YTMessageType.URL_REQUEST, HandleUrlRequest);
            _dataHandler.BindMessageType<int>(YTMessageType.MOVE_SONG_UP, (data) => onMoveSongUpMsg?.Invoke(data)); ;
            _dataHandler.BindMessageType<int>(YTMessageType.MOVE_SONG_DOWN, (data) => onMoveSongDownMsg?.Invoke(data));
            _dataHandler.BindMessageType<int>(YTMessageType.REMOVE_SONG, (data) => onRemoveSongMsg?.Invoke(data));
            _dataHandler.BindMessageType<byte[]>(YTMessageType.AUDIO_BUFFER);
            _dataHandler.BindMessageType<int>(YTMessageType.WAIT_FOR_BUFFER);
            _dataHandler.BindMessageType<SongData>(YTMessageType.SONG_PLAYING);
            _dataHandler.BindMessageType<int>(YTMessageType.SONG_TIME_UPDATE);
            _dataHandler.BindMessageType<string>(YTMessageType.GUI_MESSAGE);
            _dataHandler.BindMessageType<List<SongData>>(YTMessageType.SONG_QUEUE_UPDATE);
            _dataHandler.BindMessageType<string>(YTMessageType.AUTHENTICATION);
            _dataHandler.BindMessageType<bool>(YTMessageType.AUTH_SUCCESSFUL);
        }

        private void ClientConnectionListenerThread(object state)
        {
            Console.WriteLine("Listening for clients...");
            while (_listeningForClients)
            {
                TcpClient client = _listener.AcceptTcpClient();
                Console.WriteLine("Client connected");
                _unauthenticated.Add(client);
                ThreadPool.QueueUserWorkItem(state => ClientListenerThread(client));
            }
        }

        private void ClientListenerThread(TcpClient client)
        {
            byte[] incomingBuffer = new byte[BufferSize];
            while (_listeningToClients)
            {
                try
                {
                    int received = client.Client.Receive(incomingBuffer);
                    byte[] b = new byte[received];
                    Buffer.BlockCopy(incomingBuffer, 0, b, 0, received);

                    if (_unauthenticated.Contains(client))
                    {
                        _dataHandler.ReadIncomingData(b, out bool complete, out var parsed);

                        if (parsed.Count == 0)
                        {
                            try
                            {
                                _unauthenticated.Remove(client);
                                client.Close();
                                return;
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                return;
                            }
                        }

                        foreach (var packet in parsed)
                        {
                            if (packet.messageType != YTMessageType.AUTHENTICATION)
                            {
                                try
                                {
                                    _unauthenticated.Remove(client);
                                    client.Close();
                                    return;
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    return;
                                }
                            }

                            string pw = (string)packet.data;
                            if (_serverPassword == "" || pw == _serverPassword)
                            {
                                _unauthenticated.Remove(client);
                                _clients.Add(client);
                                onClientAuthenticated?.Invoke(client);
                                Send(client, YTMessageType.AUTH_SUCCESSFUL, true);
                                Console.WriteLine("Client authenticated");
                            }
                            else
                            {
                                try
                                {
                                    _unauthenticated.Remove(client);
                                    client.Close();
                                    return;
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    return;
                                }
                            }
                        }

                    }
                    else
                    {
                        _dataHandler.ReadIncomingData(b);
                    }     
                }
                catch (SocketException)
                {
                    Console.WriteLine("Connection to client lost");
                    try
                    {
                        _clients.Remove(client);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return;
                    }
                    break;
                }
            }
        }

        public void Send(TcpClient client, YTMessageType type, object payload)
        {
            byte[] packet = _dataHandler.GetSendBuffer(type, payload);

            try
            {
                client.Client.Send(packet);
            }
            catch (SocketException)
            {
                try
                {
                    _clients.Remove(client);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return;
                }
            }
        }

        public void SendToAll(YTMessageType type, object payload)
        {
            _clients.ForEachCopy(c => Send(c, type, payload));
        }

        private void HandleControlMessage(ControlMessage message)
        {
            ControlMessage cmessage = (ControlMessage)message;
            Console.WriteLine("Got control message: " + cmessage);
            switch (cmessage)
            {
                case ControlMessage.SKIP_SONG:
                    onSkipSongMsg?.Invoke();
                    break;
            }
        }

        private void HandleUrlRequest(string url)
        {
            Console.WriteLine("Handling url request");
            onUrlRequested?.Invoke(url);
        }

        public void OnMoveSongDownMsg(Action<int> onMoveSongDownAction)
        {
            onMoveSongDownMsg = onMoveSongDownAction;
        }

        public void OnMoveSongUpMsg(Action<int> onMoveSongUpAction)
        {
            onMoveSongUpMsg = onMoveSongUpAction;
        }

        public void OnRemoveSongMsg(Action<int> onRemoveSongAction)
        {
            onRemoveSongMsg = onRemoveSongAction;
        }

        public void OnSkipSongMsg(Action onSkipSongMsgAction)
        {
            onSkipSongMsg = onSkipSongMsgAction;
        }

        public void OnUrlRequested(Action<string> onUrlRequestedAction)
        {
            onUrlRequested = onUrlRequestedAction;
        }

        public void OnClientAuthenticated(Action<TcpClient> onClientAuthenticatedAction)
        {
            onClientAuthenticated = onClientAuthenticatedAction;
        }

        public void Dispose()
        {
            _listener?.Stop();
            _clients.ForEach(c =>
            {
                c?.GetStream().Close();
                c?.Close();
            });

            _listeningForClients = false;
            _listeningToClients = false;
        }

        public bool PollServer(int microseconds, SelectMode mode)
        {
            return _listener.Server.Poll(microseconds, mode);
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

        public static void ForEachCopy<T>(this List<T> list, Action<T> action)
        {
            List<T> copy = new List<T>(list);
            copy.ForEach(action);
        }
    }
}

