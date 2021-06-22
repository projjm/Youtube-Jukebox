using System;
using System.Collections.Generic;
using System.Text;
using YoutubeJukebox.Network;

namespace YoutubeJukebox
{
    public struct SongData
    {
        public string id;
        public string title;
        public int queueId;
        public int durationHours;
        public int durationMinutes;
        public int durationSeconds;
    }

    class SongDataHandler
    {
        private ClientNetHandler _netHandler;
        private List<SongData> _songsQueued;
        private SongData _songPlaying;
        private MainWindow _mainWindow;

        public SongDataHandler(ClientNetHandler netHandler, MainWindow mainWindow)
        {
            _netHandler = netHandler;
            _mainWindow = mainWindow;
            _songsQueued = new List<SongData>();
            _netHandler.OnSongPlaying(OnSongPlaying);
            _netHandler.OnSongQueueUpdated(OnSongQueueUpdated);
            _netHandler.OnSongTimeUpdated(OnSongTimeUpdated);
        }

        public SongData GetCurrentSongPlaying() => _songPlaying;

        public int GetCurrentSongTotalSeconds() => (_songPlaying.durationHours * 60 * 60) + (_songPlaying.durationMinutes * 60) + _songPlaying.durationSeconds;

        private void OnSongQueueUpdated(List<SongData> songData)
        {
            _songsQueued = songData;
            _mainWindow.UpdateSongQueueList(_songsQueued);
        }

        private void OnSongPlaying(SongData songData)
        {
            _songPlaying = songData;
            _mainWindow.UpdateSongPlaying(_songPlaying);
            _mainWindow.UpdateSongQueueList(_songsQueued);
        }

        private void OnSongTimeUpdated(int totalSeconds)
        {
            _mainWindow.UpdateBaseTime(totalSeconds);
        }
    }
}
