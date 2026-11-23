# AndriodAutoMediaApp

Android-only .NET MAUI media app that exposes local music to Android Auto through
`MediaBrowserServiceCompat` and `MediaSessionCompat`. Playback uses ExoPlayer
(Media3), and `OnPlayFromSearch` searches the device audio library through
`MediaStore`.

## Use

1. Install and launch the app on the phone.
2. Grant audio access from the app screen.
3. Connect to Android Auto and ask Google Assistant to play a song from the device.

The browser service also exposes the device's music tracks for browsing. On Android
13 and later the app requests `READ_MEDIA_AUDIO`; earlier Android versions use
`READ_EXTERNAL_STORAGE`. After permission is granted, the app indexes all audio
entries exposed by `MediaStore.Audio.Media`, including title, artist, album,
duration, and content URI. Android Auto browsing and Assistant searches use this
local index. `IMusicSearchService` uses Levenshtein similarity to resolve song
titles, titles with an artist, artist requests (for example, "songs by Metallica"),
and album requests (for example, "album Fear Inoculum"). Artist and album matches
are queued for playback.

The media session advertises play, pause, stop, seek, and available skip actions
to Android Auto. Previous-track control restarts the current track when it has
played for more than three seconds; otherwise it moves to the previous queued track.

## Build

```text
dotnet build AndroidAutoMediaApp.csproj
```
