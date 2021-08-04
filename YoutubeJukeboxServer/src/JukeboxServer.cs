using System;
using System.Threading;
using System.Collections.Generic;
using YoutubeExplode;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using System.Diagnostics;
using YoutubeExplode.Videos;
using YoutubeExplode.Playlists;
using YoutubeExplode.Common;
using System.Text;
using System.Net.Sockets;

namespace YoutubeJukeboxServer
{
    public enum YTRequestType
    {
        Video,
        Playlist,
        Search
    }

    public struct YoutubeRequest
    {
        public bool successful;
        public YTRequestType type;
        public bool hiddenRequested;
        public List<string> ids;
    }

    public struct SongData
    {
        public string id;
        public string title;
        public int queueId;
        public int durationHours;
        public int durationMinutes;
        public int durationSeconds;
        public bool hiddenRequested;

        public static bool operator == (SongData a, SongData b)
        {
            return a.queueId == b.queueId;
        }
        public static bool operator != (SongData a, SongData b)
        {
            return a.queueId != b.queueId;
        }
    }


    class JukeboxServer : IDisposable
    {
        private ServerNetHandler _netHandler;
        private YoutubeAudioFetcher _audioFetcher;
        private List<SongData> _toPlay = new List<SongData>();
        private SongData? _currentSong;

        private bool _streamEnabled = true;
        private bool _shouldSkip = false;
        private bool _shouldRemoveSong = true;
        private bool _sentNullSongMsg = false;

        private int _currentSongTotalSecs = 0;
        private readonly int _bufferDurationMs;
        private readonly int _maxQueueSize;

        public JukeboxServer(ServerNetHandler netHandler, int bufferTimeMs, int maxQueueSize, int maxCacheSizeMb, int maxSongDurationSeconds, int requestTimeoutMs)
        {
            _netHandler = netHandler;
            _audioFetcher = new YoutubeAudioFetcher(maxCacheSizeMb, maxSongDurationSeconds, requestTimeoutMs);
            _bufferDurationMs = bufferTimeMs;
            _maxQueueSize = maxQueueSize;

            netHandler.OnUrlRequested(SongRequested);
            netHandler.OnSkipSongMsg(SkipSongRequested);
            netHandler.OnClientAuthenticated(OnClientConnected);
            netHandler.OnMoveSongUpMsg(MoveSongUp);
            netHandler.OnMoveSongDownMsg(MoveSongDown);
            netHandler.OnRemoveSongMsg(RemoveSong);
            ThreadPool.QueueUserWorkItem(ProcessSongQueue);
        }

        private void OnClientConnected(TcpClient client)
        {
            // Maybe wait for message that tells server the client is ready to receive messages?

            if (_currentSong != null)
            {
                _ =  DelayActionAsync(1000, () => // Delay must be shorter than buffer + network latency
                {
                    if (_currentSong != null)
                        _netHandler.Send(client, YTMessageType.SONG_PLAYING, _currentSong.Value);

                    _netHandler.Send(client, YTMessageType.WAIT_FOR_BUFFER, _bufferDurationMs);
                    _netHandler.Send(client, YTMessageType.SONG_TIME_UPDATE, _currentSongTotalSecs);
                    _netHandler.Send(client, YTMessageType.CONTROL_MSG, ControlMessage.PLAYBACK_START);
                    _netHandler.Send(client, YTMessageType.SONG_QUEUE_UPDATE, _toPlay);
                });   
            }
        }

        private SongData GetNullSong() => new SongData() { id = "NULL", title = "NULL", queueId = -1, durationHours = 0, durationMinutes = 0, durationSeconds = 0};

        private void SongRequested(string url) => _ = AddSongsToQueue(url);

        private void SkipSongRequested() => _shouldSkip = true;

        private void MoveSongUp(int queueId)
        {
            int index = _toPlay.IndexOf(_toPlay.FirstOrDefault(s => s.queueId == queueId));
            if (index == 0)
                return;

            SongData song = _toPlay[index];
            _toPlay[index] = _toPlay[index - 1];
            _toPlay[index - 1] = song;

            if (_currentSong != null && index == 1)
            {
                _shouldSkip = true;
                _shouldRemoveSong = false;
            }

            _netHandler.SendToAll(YTMessageType.SONG_QUEUE_UPDATE, _toPlay);
        }

        private void MoveSongDown(int queueId)
        {
            int index = _toPlay.IndexOf(_toPlay.FirstOrDefault(s => s.queueId == queueId));
            if (index == _toPlay.Count - 1)
                return;

            SongData song = _toPlay[index];
            _toPlay[index] = _toPlay[index + 1];
            _toPlay[index + 1] = song;

            if (_currentSong != null && index == 0)
            {
                _shouldSkip = true;
                _shouldRemoveSong = false;
            }

            _netHandler.SendToAll(YTMessageType.SONG_QUEUE_UPDATE, _toPlay);
        }

        private void RemoveSong(int queueId)
        {
            int index = _toPlay.IndexOf(_toPlay.FirstOrDefault(s => s.queueId == queueId));
            _toPlay.RemoveAt(index);

            if (_currentSong != null && index == 0)
            {
                _shouldSkip = true;
                _shouldRemoveSong = false;
            }

            _netHandler.SendToAll(YTMessageType.SONG_QUEUE_UPDATE, _toPlay);
        }

        private async Task AddSongsToQueue(string requestString)
        {
            if (_maxQueueSize >= 0 && _toPlay.Count == _maxQueueSize)
                return;

            YoutubeRequest request = await _audioFetcher.ParseRequest(requestString);
            if (!request.successful)
                return;

            Console.WriteLine("Processing Ids...");
            foreach (string id in request.ids)
            {
                if (_maxQueueSize >= 0 && _toPlay.Count == _maxQueueSize)
                    return;

                SongData songData;

                if (!_audioFetcher.ConvertedFileExists(id))
                    songData = await _audioFetcher.DownloadAndConvertYoutubeAudio(id);
                else
                    songData = await _audioFetcher.DownloadAndConvertYoutubeAudio(id, true);

                songData.hiddenRequested = request.hiddenRequested;
                if (songData.queueId == -1)
                {
                    Console.WriteLine("Failed to queue song.");
                    return;
                }

                _toPlay.Add(songData);
                Console.WriteLine(songData.title + " queued!");

                _netHandler.SendToAll(YTMessageType.SONG_QUEUE_UPDATE, _toPlay);
                Thread.Sleep(1000);
            }
        }
        
        private void ProcessSongQueue(Object state)
        {
            while (_streamEnabled)
            {
                if (_toPlay.Count == 0)
                {
                    _currentSong = null;

                    if (!_sentNullSongMsg)
                    { 
                        _netHandler.SendToAll(YTMessageType.SONG_TIME_UPDATE, 0);
                        _netHandler.SendToAll(YTMessageType.SONG_PLAYING, GetNullSong());
                        _sentNullSongMsg = true;
                    }
                        
                    Thread.Sleep(500);
                    continue;
                }

                SongData nextSong = _toPlay[0];
                Console.WriteLine("Now playing: " + nextSong.title);

                _netHandler.SendToAll(YTMessageType.SONG_PLAYING, nextSong);
                _netHandler.SendToAll(YTMessageType.SONG_TIME_UPDATE, 0);

                _currentSong = nextSong;

                BufferAndSendAudioStream(nextSong.id);
                
                if (!_shouldSkip)
                {
                    int networkLatencySlackMs = 500;
                    Thread.Sleep(_bufferDurationMs + networkLatencySlackMs);
                }

                if (_shouldRemoveSong)
                    _toPlay.RemoveAt(0);

                _netHandler.SendToAll(YTMessageType.CONTROL_MSG, ControlMessage.CLEAR_BUFFER);
                _netHandler.SendToAll(YTMessageType.SONG_QUEUE_UPDATE, _toPlay);

                _shouldSkip = false;
                _sentNullSongMsg = false;
                _shouldRemoveSong = true;
            }
        }

        private void BufferAndSendAudioStream(string videoId)
        {
            string filePath = _audioFetcher.GetConvertedFilePath(videoId);

            if (!File.Exists(filePath))
            {
                _shouldSkip = true;
                return;
            }
                
            Mp3FileReader mp3Reader = new Mp3FileReader(filePath);
            Stopwatch sw = new Stopwatch();
            Queue<double> frameBuffer = new Queue<double>();

            int totalBytes = 0;
            long startTicks = Stopwatch.GetTimestamp();
            double ticksPerMs = (double)Stopwatch.Frequency / 1000.0;
            double frameBufferMaxTicks = ticksPerMs * _bufferDurationMs;
            double frameBufferTotalTicks = 0.0;
            double expectedTicks = 0.0; // The amount of ticks expected to have passed at this point
            bool sentInitialBuffer = false;

            Mp3Frame frame;
            while ((frame = mp3Reader.ReadNextFrame()) != null)
            {
                if (_shouldSkip)
                {
                    break;
                }

                byte[] frameRawData = frame.RawData;
                if (frameRawData == null)
                    break;

                totalBytes += frameRawData.Length;
                _netHandler.SendToAll(YTMessageType.AUDIO_BUFFER, frameRawData);

                _currentSongTotalSecs = (int)mp3Reader.CurrentTime.TotalSeconds;

                // Timing handling
                double expectedMs = mp3Reader.CurrentTime.TotalMilliseconds;
                double newExpectedTicks = expectedMs * ticksPerMs;
                double deltaTicks = newExpectedTicks - expectedTicks;
                expectedTicks = newExpectedTicks;

                frameBuffer.Enqueue(deltaTicks);
                frameBufferTotalTicks += deltaTicks;

                
                if (frameBufferTotalTicks < frameBufferMaxTicks)
                    continue; // Ticks not exceeded buffer length, Send the next frame

                if (!sentInitialBuffer)
                {
                    _netHandler.SendToAll(YTMessageType.CONTROL_MSG, ControlMessage.PLAYBACK_START);
                    sentInitialBuffer = true;
                }

                // Ticks have exceeded buffer length
                double frameDelayTicks = frameBuffer.Dequeue();
                frameBufferTotalTicks -= frameDelayTicks;

                // Wait for the frame delay (that we just dequeued) so we can send the next frame
                sw.Reset();
                sw.Start();
                int waitTime = (int)(frameDelayTicks / ticksPerMs);
                Thread.Sleep(waitTime);
                double elapsed = (double)sw.ElapsedTicks;

                // Check if we over-shot with the wait and adjust total frame buffer ticks
                if (elapsed > frameDelayTicks)
                {
                    frameBufferTotalTicks -= (elapsed - frameDelayTicks);
                }
                
            }

            double totalTicksElapsed = (double)Stopwatch.GetTimestamp() - startTicks;
            double totalMsElapsed = totalTicksElapsed / ticksPerMs;
            _currentSongTotalSecs = 0;

            Console.WriteLine("Total MS Elapsed: " + totalMsElapsed);
            Console.WriteLine("Total WaveStream Length (ms): " + mp3Reader.TotalTime.TotalMilliseconds);
            Console.WriteLine("Sent " + totalBytes + " total bytes");

            mp3Reader.Dispose();
        }

        private async Task DelayActionAsync(int delay, Action action)
        {
            await Task.Delay(delay);
            action?.Invoke();
        }

        public void Dispose()
        {
            _netHandler.Dispose();
        }
    }
}