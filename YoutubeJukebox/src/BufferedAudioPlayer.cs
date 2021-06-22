using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using YoutubeJukebox.Network;

namespace YoutubeJukebox
{
    class BufferedAudioPlayer : IDisposable
    {
        private enum PlayBackState
        {
            Playing,
            Paused,
            Buffering
        }

        private PlayBackState _playBackState = PlayBackState.Buffering;

        private readonly ClientNetHandler _netHandler;
        private readonly WaveOutEvent _waveOut;
        private BufferedWaveProvider _waveProvider;
        private VolumeWaveProvider16 _volumeWaveProvider;
        private MemoryStream _dataStream;

        private Mp3FrameDecompressorDmo _decompressor = null;
        private Mp3FrameYTC _mp3frame;
        private Mp3WaveFormat _streamWaveFormat;

        private bool _shouldPlay = false;
        private bool _waveProviderInitialised = false;

        private long _lastWritePosition = 0;
        private byte[] _buffer = new byte[16384 * 8];

        private double _waitForBuffer = 0.0;
        private float _currentVolume = 0.5f;

        private const float ReadyBufferMsgMs = 2000;

        private bool IsBufferNearlyFull
        {
            get
            {
                return _waveProvider != null &&
                       _waveProvider.BufferLength - _waveProvider.BufferedBytes
                       < _waveProvider.WaveFormat.AverageBytesPerSecond / 4;
            }
        }

        public BufferedAudioPlayer(ClientNetHandler netHandler) : this()
        {
            _netHandler = netHandler;
            _netHandler.OnAudioReceived(OnDataReceived);
            _netHandler.OnBufferClearMsg(OnBufferClear);
            _netHandler.OnPlaybackStartMsg(OnPlaybackStart);
            _netHandler.OnWaitForBuffer(OnWaitForBuffer);
        }

        public BufferedAudioPlayer()
        {
            _dataStream = new MemoryStream();
            _waveOut = new WaveOutEvent();
            ThreadPool.QueueUserWorkItem(PlayAudioBuffer);
        }

        public void SetVolume(float value)
        {
            _currentVolume = value;
            if (_waveProviderInitialised && _volumeWaveProvider != null)
            {
                _volumeWaveProvider.Volume = _currentVolume;
            }
        }

        public float GetVolume() => _volumeWaveProvider.Volume;

        public bool WaveOutInitialised() => _waveProviderInitialised;

        public int GetTotalSecondsPlayed() => (int)Math.Ceiling((_waveOut.GetPosition() / (float)_waveProvider.WaveFormat.AverageBytesPerSecond));

        private void PlayAudioBuffer(object state)
        {
            while (true)
            {
                if (!_waveProviderInitialised)
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (!_shouldPlay)
                {
                    Thread.Sleep(100);
                    continue;
                }

                double bufferedMs = _waveProvider.BufferedDuration.TotalMilliseconds;

                if (bufferedMs < _waitForBuffer)
                {
                    _waveOut.Pause();
                    Thread.Sleep(100);
                    continue;
                }
                else if (_waitForBuffer != 0.0)
                {
                    _waitForBuffer = 0.0;
                }

                if (_playBackState == PlayBackState.Buffering && bufferedMs >= ReadyBufferMsgMs)
                {
                    Console.WriteLine("Playing");
                    _waveOut.Play();
                    _playBackState = PlayBackState.Playing;
                }
                else if (_playBackState == PlayBackState.Playing && bufferedMs < 250)
                {
                    Console.WriteLine("Pausing");
                    _waveOut.Pause();
                    _playBackState = PlayBackState.Buffering;
                }

                Thread.Sleep(100);
            }
        }


        private void ProcessAudioBuffer()
        { 
            while ((_mp3frame = Mp3FrameYTC.LoadFromStream(_dataStream)) != null)
            {
                if (_decompressor == null)
                {
                    _decompressor = CreateFrameDecompressor(_mp3frame);

                    _streamWaveFormat = new Mp3WaveFormat(_mp3frame.SampleRate, _mp3frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                _mp3frame.FrameLength, _mp3frame.BitRate);

                    _waveOut.Stop();

                    _waveProvider = new BufferedWaveProvider(_decompressor.OutputFormat);
                    _waveProvider.BufferDuration = TimeSpan.FromSeconds(60);
                    _waveProvider.DiscardOnBufferOverflow = false;

                    _volumeWaveProvider = new VolumeWaveProvider16(_waveProvider);
                    _waveOut.Init(_volumeWaveProvider);
                    _volumeWaveProvider.Volume = _currentVolume;
                    _waveProviderInitialised = true;
                }

                int decompressed = _decompressor.DecompressFrame(_mp3frame, _buffer, 0);
                _waveProvider.AddSamples(_buffer, 0, decompressed);
            }     
        }

        private bool ShouldBufferFormatChange(Mp3FrameYTC newFrame)
        {
            int channels = newFrame.ChannelMode == ChannelMode.Mono ? 1 : 2;
            return (_streamWaveFormat.SampleRate != newFrame.SampleRate) || (_streamWaveFormat.Channels != channels)
                || (_streamWaveFormat.blockSize != newFrame.FrameLength) || ((_streamWaveFormat.AverageBytesPerSecond * 8) != newFrame.BitRate);
        }

        private void WriteToEndOfDataStream(byte[] data)
        {
            long currentReadPos = _dataStream.Position;
            _dataStream.Position = _lastWritePosition;
            _dataStream.Write(data, 0, data.Length);
            _lastWritePosition = _dataStream.Position;
            _dataStream.Flush();
            _dataStream.Position = currentReadPos;
        }

        public void ForceBufferClear() => OnBufferClear();

        private void OnDataReceived(byte[] data)
        {
            WriteToEndOfDataStream(data);
            ProcessAudioBuffer();
        }

        public void OnPlaybackStart() => _shouldPlay = true;

        public void OnWaitForBuffer(double duration) => _waitForBuffer = duration;

        public void OnBufferClear()
        {
            _waveOut.Stop();

            if (_waveProvider != null)
                _waveProvider.ClearBuffer();

            _waveProvider = null;
            _volumeWaveProvider = null;
            _waveProviderInitialised = false;

            _dataStream.Dispose();
            _dataStream = new MemoryStream();

            if (_decompressor != null)
                _decompressor.Dispose();
            _decompressor = null;

            _lastWritePosition = 0;
            _playBackState = PlayBackState.Buffering;
            _shouldPlay = false;

        }

        private static Mp3FrameDecompressorDmo CreateFrameDecompressor(Mp3FrameYTC frame)
        {
            WaveFormat waveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);

            return new Mp3FrameDecompressorDmo(waveFormat);
        }

        public void Dispose()
        {
            _netHandler.Dispose();
            _waveOut?.Dispose();
            _dataStream?.Dispose();
        }

    }
}