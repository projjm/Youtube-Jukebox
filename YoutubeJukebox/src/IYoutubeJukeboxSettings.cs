
using Config.Net;

namespace YoutubeJukebox
{
    public interface IYoutubeJukeboxSettings
    {
        [Option(DefaultValue = "/")]
        public string ServerIpAddress { get; set; }

        [Option(DefaultValue = -1)]
        public int ServerPort { get; set; }

        [Option(DefaultValue = "/")]
        public string ServerPassword { get; set; }
    }

    public static class ConfigHelper
    {
        public static void TryWriteDefaults(IYoutubeJukeboxSettings settings)
        {
            if (settings.ServerIpAddress == "/")
                settings.ServerIpAddress = "";

            if (settings.ServerPort == -1)
                settings.ServerPort = 525;

            if (settings.ServerPassword == "/")
                settings.ServerPassword = "";
        }
    }
}
