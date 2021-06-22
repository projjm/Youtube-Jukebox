using System;
using System.Collections.Generic;
using System.Text;
using Config.Net;

namespace YoutubeJukeboxServer
{
    public interface IYoutubeJukeboxServerSettings
    {
        [Option(DefaultValue = "/")]
        public string IPAddress { get; set; }

        [Option(DefaultValue = -1)]
        public int Port { get; set; }

        [Option(DefaultValue = -1)]
        public int BufferTimeMS { get; set; }

        [Option(DefaultValue = 0)]
        public int MaxCacheSizeMb { get; set; }

        [Option(DefaultValue = 0)]
        public int MaxSongDurationMinutes { get; set; }

        [Option(DefaultValue = -2)]
        public int MaxQueueSize { get; set; }

        [Option(DefaultValue = "/")]
        public string ServerPassword { get; set; }
    }

    public static class ConfigHelper
    {
        public static void TryWriteDefaults(IYoutubeJukeboxServerSettings settings)
        {
            if (settings.IPAddress == "/")
                settings.IPAddress = "127.0.0.1";

            if (settings.Port == -1)
                settings.Port = 525;

            if (settings.BufferTimeMS == -1)
                settings.BufferTimeMS = 4000;

            if (settings.MaxQueueSize == -2)
                settings.MaxQueueSize = -1;

            if (settings.ServerPassword == "/")
                settings.ServerPassword = "";

            if (settings.MaxCacheSizeMb == 0)
                settings.MaxCacheSizeMb = -1;

            if (settings.MaxSongDurationMinutes == 0)
                settings.MaxSongDurationMinutes = -1;
        }
    }
}
