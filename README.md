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
`READ_EXTERNAL_STORAGE`.

## Build

```text
dotnet build AndroidAutoMediaApp.csproj
```
