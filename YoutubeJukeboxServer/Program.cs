using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Config.Net;

namespace YoutubeJukeboxServer
{
    class Program
    {
        private static IYoutubeJukeboxServerSettings _config;

        static void Main(string[] args)
        {
            _config = new ConfigurationBuilder<IYoutubeJukeboxServerSettings>().UseIniFile("config.ini").Build();
            ConfigHelper.TryWriteDefaults(_config);

            ServerNetHandler netHandler = new ServerNetHandler(_config.IPAddress, _config.Port, _config.ServerPassword);
            JukeboxServer ytRadioServer = new JukeboxServer(netHandler, _config.BufferTimeMS, _config.MaxQueueSize, _config.MaxCacheSizeMb, _config.MaxSongDurationMinutes, _config.RequestTimeoutMs);

            // Block application exit - is there a better way to do this?
            while(true)
            {
                Thread.Sleep(10000);
            }
        }
    }
}
