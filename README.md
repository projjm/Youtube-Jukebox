![alt text](https://i.imgur.com/mRdw746.png "Youtube Jukebox Logo")
# Youtube Jukebox
A simple standalone app to listen to music in sync with your friends via Youtube.

![alt text](https://i.imgur.com/A5iqojH.png "Youtube Jukebox Screenshot")

# Features
* Queue songs from URLs or direct searches
* Supports videos and playlist links
* Automatically caches audio for future listens
* Configurable server resources parameters
* Easy to use!

# About
Youtube Jukebox is a server-client based app that streams audio to all listeners in realtime.
Song requests are downloaded, cached and then queued by the server, the server will convert audio files into an MP3 format, MP3 frames are then sent in realtime to assure a synchronized listening experience.

# Setting up
1. Ensure a Youtube Jukebox server instance is running.
2. If listening across the internet, make sure the server host is port-fowarded on the configured port (**525** by default).
3. Connect to the server via **Settings -> Connect**. If the server is configured to use a password, enter it here.
4. Once connected, type a **Youtube URL** or **search query** into the suggestion box and press enter to queue!

# Config
The client application does not require configuration, it will however cache your connection preferences after succesfully connecting to a server.
The server application has a number of config options, these can be found in the **config.ini** file:

**Key : Default Value**
* **IPAddress=127.0.0.1**   - The IP address to use when hosting the server.
* **Port=525**              - The port to use when hosting the server.
* **BufferTimeMS=4000** - The amount of time in ms to buffer ahead of the current stream position.
* **MaxQueueSize=-1** - Max number of songs in the queue (-1 means no maximum)
* **ServerPassword=** - Server password, leave blank to use no password.
* **MaxCacheSizeMb=-1** - Max cache directory size in Mb (-1 means no maximum)
* **MaxSongDurationMinutes=-1** - Max song duration to be queued (-1 means no maximum)
* **RequestTimeoutMs=60000** - Maximum time in ms to wait for data requests (-1 means no maximum)

