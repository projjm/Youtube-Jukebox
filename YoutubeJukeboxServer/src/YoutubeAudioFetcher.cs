using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.MediaFoundation;
using NAudio.Wave;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YouTubeSearch;
using Newtonsoft;
using Newtonsoft.Json;
using ByteSizeLib;

namespace YoutubeJukeboxServer
{
    public struct CacheEntry
    {
        public string videoId;
        public int cacheIndex;
        public int sizeMb;

        public CacheEntry(string id, int index, int sizeMB)
        {
            videoId = id;
            cacheIndex = index;
            sizeMb = sizeMB;
        }
    }

    public class YoutubeCacheMetadata
    {
        public List<CacheEntry> entries = new List<CacheEntry>();
        public int totalSizeMb = 0;
        public int lastCacheIndex = 0;
    }

    class YoutubeAudioFetcher
    {
        private static bool _mediaFoundationStarted = false;

        private const string DownloadPath = "TempAudio/";
        private const string ConvertPath = "CachedAudio/";
        private const string CacheFileName = "CacheMetadata.json";
        private readonly int MaxCacheSizeMb;
        private readonly int SongMaxDurationMinutes;

        private int _totalSongsQueued;

        YoutubeClient _youtube = new YoutubeClient();

        public YoutubeAudioFetcher(int maxCacheSizeMb, int songMaxDurationMinutes)
        {
            MaxCacheSizeMb = maxCacheSizeMb;
            SongMaxDurationMinutes = songMaxDurationMinutes;

            if (!_mediaFoundationStarted)
            {
                MediaFoundationApi.Startup();
                _mediaFoundationStarted = true;
            }

            if (!Directory.Exists(DownloadPath))
                Directory.CreateDirectory(DownloadPath);

            if (!Directory.Exists(ConvertPath))
                Directory.CreateDirectory(ConvertPath);
        }


        public bool ConvertedFileExists(string fileId) => File.Exists(ConvertPath + fileId + ".mp3");

        public string GetConvertedFilePath(string fileId) => ConvertPath + fileId + ".mp3";

        public async Task<YoutubeRequest> ParseRequest(string requestString)
        {
            Console.WriteLine("Parsing Request");
            YoutubeRequest request = new YoutubeRequest();
            request.ids = new List<string>();
            request.successful = false;

            VideoId? vid;
            PlaylistId? pid;
            if ((vid = VideoId.TryParse(requestString)) != null)
            {
                request.ids.Add(vid.Value);
                request.type = YTRequestType.Video;
                request.successful = true;
                Console.WriteLine("Got video URL request");
            }
            else if ((pid = PlaylistId.TryParse(requestString)) != null)
            {
                Console.WriteLine("Got playlist URL request");
                var videos = await _youtube.Playlists.GetVideosAsync(pid.Value);
                if (videos == null)
                    return request;

                foreach (var v in videos)
                {
                    request.ids.Add(v.Id);
                }
                request.type = YTRequestType.Playlist;
                request.successful = true;
                Console.WriteLine("Got playlist ids");
            }
            else
            {
                Console.WriteLine("Trying to parse search string");
                string url = await GetSearchQueryUrl(requestString, 2);

                if (url == "")
                {
                    request.successful = false;
                    return request;
                }

                string id = VideoId.TryParse(url)?.Value;
                request.ids.Add(id);
                request.type = YTRequestType.Search;
                request.successful = true;
                Console.WriteLine("Got search result id");
            }

            return request;
        }

        private async Task<string> GetSearchQueryUrl(string searchString, int maxPages)
        {
            VideoSearch search = new VideoSearch();
            int pageNum = 1;
            while (pageNum < maxPages)
            {
                var results = await search.GetVideosPaged(searchString, 1);

                for (int i = 0; i < results.Count; i++)
                {
                    int durMinutes = DurationStringToMinutes(results[i].getDuration());
                    if (SongMaxDurationMinutes > 0 && durMinutes > SongMaxDurationMinutes)
                    {
                        continue;
                    }
                    else
                    {
                        return results[i].getUrl();
                    }
                }
            }
            return "";
        }

        private int DurationStringToMinutes(string duration)
        {
            TimeSpan timespan;
            string[] vals = duration.Split(':');

            if (vals.Length == 0 || vals.Length == 1)
                return 0;
            else if (vals.Length == 2)
                timespan = new TimeSpan(0, Int32.Parse(vals[0]), Int32.Parse(vals[1]));
            else if (vals.Length == 3)
                timespan = new TimeSpan(Int32.Parse(vals[0]), Int32.Parse(vals[1]), Int32.Parse(vals[2]));
            else
                return 0;

            return (int)timespan.TotalMinutes;
        }


        public async Task<SongData> DownloadAndConvertYoutubeAudio(string videoId, bool songDataOnly = false)
        {
            if (!songDataOnly)
                Console.WriteLine("Downloading audio...");
            else
                Console.WriteLine("Audio cached. Downloading metadata...");

            SongData songData = new SongData();
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId);
            var streamMetaData = await _youtube.Videos.GetAsync(videoId);
            var streamInfo = streamManifest.GetAudioOnlyStreams().OrderByDescending(s => s.Bitrate).First();

            if (SongMaxDurationMinutes > 0 && streamMetaData.Duration.Value.TotalMinutes > SongMaxDurationMinutes)
            {
                songData.queueId = -1;
                return songData;
            }

            songData.id = videoId;
            songData.title = streamMetaData.Title;
            songData.queueId = _totalSongsQueued++;
            songData.durationSeconds = streamMetaData.Duration.Value.Seconds;
            songData.durationHours = streamMetaData.Duration.Value.Hours;
            songData.durationMinutes = streamMetaData.Duration.Value.Minutes;

            if (songDataOnly)
                return songData;

            await _youtube.Videos.Streams.DownloadAsync(streamInfo, DownloadPath + videoId + '.' + streamInfo.Container);

            var audioFile = new MediaFoundationReader(DownloadPath + videoId + '.' + streamInfo.Container);
            MediaFoundationEncoder.EncodeToMp3(audioFile, ConvertPath + videoId + ".mp3", 44100);
            audioFile.Dispose();

            UpdateCache(videoId, streamInfo.Container.Name);
            return songData;
        }

        private void UpdateCache(string videoId, string container)
        {
            File.Delete(DownloadPath + videoId + '.' + container);
            FileInfo fileInfo = new FileInfo(ConvertPath + videoId + ".mp3");
            var fileSize = ByteSize.FromBytes(fileInfo.Length);

            YoutubeCacheMetadata cache;
            if (File.Exists(ConvertPath + CacheFileName))
                cache = JsonConvert.DeserializeObject<YoutubeCacheMetadata>(File.ReadAllText(ConvertPath + CacheFileName));
            else
                cache = new YoutubeCacheMetadata();

            CacheEntry newEntry = new CacheEntry(videoId,  cache.lastCacheIndex + 1, (int)fileSize.MegaBytes);
            cache.entries.Add(newEntry);
            cache.lastCacheIndex++;
            cache.totalSizeMb += newEntry.sizeMb;

            if (MaxCacheSizeMb > 0 && cache.totalSizeMb > MaxCacheSizeMb)
            {
                Console.WriteLine("Cache limit exceeded, disposing cached audio...");
                List<CacheEntry> orderedByIndex = cache.entries.OrderBy(e => e.cacheIndex).ToList();
                List<int> toRemove = new List<int>();
                for (int i = 0; i < cache.entries.Count; i++)
                {
                    cache.totalSizeMb -= orderedByIndex[i].sizeMb;
                    try
                    {
                        File.Delete(ConvertPath + orderedByIndex[i].videoId + ".mp3");
                    }
                    catch(IOException)
                    {
                        Console.WriteLine("Could not clear cached file - File is open in another application or is currently playing");
                        continue;
                    }
                    
                    toRemove.Add(i);

                    if (cache.totalSizeMb <= MaxCacheSizeMb)
                        break;
                }

                toRemove.ForEach(i => orderedByIndex.RemoveAt(i));
                cache.entries = orderedByIndex;
            }

            string json = JsonConvert.SerializeObject(cache, Formatting.Indented);
            File.WriteAllText(ConvertPath + CacheFileName, json);
        }

    }
}
