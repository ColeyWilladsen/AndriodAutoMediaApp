using Android.Content;
using Android.OS;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;

namespace AndroidAutoMediaApp;

public static class MediaSessionManager
{
	private static readonly object SyncRoot = new();
	private static MediaSessionCompat? session;
	private static IExoPlayer? player;
	private static MusicTrack? currentTrack;
	private static bool playerNeedsPrepare;

	public static MediaSessionCompat GetOrCreate(Context context)
	{
		lock (SyncRoot)
		{
			if (session is not null)
				return session;

			session = new MediaSessionCompat(context.ApplicationContext ?? context, "AndroidAutoMedia");
			session.SetFlags(
				MediaSessionCompat.FlagHandlesMediaButtons |
				MediaSessionCompat.FlagHandlesTransportControls);
			session.SetCallback(new PlaybackCallback());
			session.Active = true;
			SetPlaybackState(PlaybackStateCompat.StateNone);
			return session;
		}
	}

	public static bool HasAudioPermission(Context context)
	{
		var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
			? Android.Manifest.Permission.ReadMediaAudio
			: Android.Manifest.Permission.ReadExternalStorage;
		return context.CheckSelfPermission(permission) == Android.Content.PM.Permission.Granted;
	}

	private static void PlayTrack(Context context, MusicTrack track)
	{
		currentTrack = track;
		var intent = new Intent(context, typeof(MediaPlaybackService));
		intent.SetAction(MediaPlaybackService.ActionPlay);
		intent.PutExtra(MediaPlaybackService.ExtraUri, track.ContentUri);
		intent.PutExtra(MediaPlaybackService.ExtraTitle, track.Title);
		intent.PutExtra(MediaPlaybackService.ExtraArtist, track.Artist);

		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			context.StartForegroundService(intent);
		else
			context.StartService(intent);
	}

	internal static void StartPlayback(string uri, string title, string artist)
	{
		currentTrack ??= new MusicTrack(title, artist, "Unknown album", 0, uri);
		var activePlayer = player ??= new ExoPlayerBuilder(Android.App.Application.Context).Build()
			?? throw new InvalidOperationException("Could not initialize the media player.");
		var audioAttributesBuilder = new AudioAttributes.Builder();
		audioAttributesBuilder.SetUsage(C.UsageMedia);
		audioAttributesBuilder.SetContentType(C.AudioContentTypeMusic);
		activePlayer.SetAudioAttributes(audioAttributesBuilder.Build(), true);
		activePlayer.SetMediaItem(MediaItem.FromUri(Android.Net.Uri.Parse(uri)!));
		activePlayer.Prepare();
		activePlayer.Play();
		playerNeedsPrepare = false;

		var metadata = new MediaMetadataCompat.Builder();
		metadata.PutString(MediaMetadataCompat.MetadataKeyTitle, title);
		metadata.PutString(MediaMetadataCompat.MetadataKeyArtist, artist);
		metadata.PutString(MediaMetadataCompat.MetadataKeyAlbum, currentTrack.Album);
		session?.SetMetadata(metadata.Build());
		SetPlaybackState(PlaybackStateCompat.StatePlaying);
	}

	internal static void RequestResumePlayback()
	{
		if (currentTrack is null)
			return;

		var intent = new Intent(Android.App.Application.Context, typeof(MediaPlaybackService));
		intent.SetAction(MediaPlaybackService.ActionResume);
		intent.PutExtra(MediaPlaybackService.ExtraTitle, currentTrack.Title);
		intent.PutExtra(MediaPlaybackService.ExtraArtist, currentTrack.Artist);
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			Android.App.Application.Context.StartForegroundService(intent);
		else
			Android.App.Application.Context.StartService(intent);
	}

	internal static void ResumePlayback()
	{
		if (currentTrack is null)
			return;

		var activePlayer = player;
		if (activePlayer is null || playerNeedsPrepare)
		{
			StartPlayback(currentTrack.ContentUri, currentTrack.Title, currentTrack.Artist);
			return;
		}

		activePlayer.Play();
		SetPlaybackState(PlaybackStateCompat.StatePlaying);
	}

	internal static void PausePlayback()
	{
		player?.Pause();
		SetPlaybackState(PlaybackStateCompat.StatePaused);
	}

	internal static void StopPlayback()
	{
		player?.Stop();
		playerNeedsPrepare = true;
		SetPlaybackState(PlaybackStateCompat.StateStopped);
		Android.App.Application.Context.StopService(
			new Intent(Android.App.Application.Context, typeof(MediaPlaybackService)));
	}

	internal static void ReleasePlayer()
	{
		player?.Release();
		player = null;
	}

	private static void SetPlaybackState(int state)
	{
		var actions = PlaybackStateCompat.ActionPlay |
			PlaybackStateCompat.ActionPause |
			PlaybackStateCompat.ActionPlayFromSearch |
			PlaybackStateCompat.ActionPlayFromMediaId |
			PlaybackStateCompat.ActionStop;
		var playbackState = new PlaybackStateCompat.Builder();
		playbackState.SetActions((long)actions);
		playbackState.SetState(state, player?.CurrentPosition ?? 0, 1f);
		session?.SetPlaybackState(playbackState.Build());
	}

	private sealed class PlaybackCallback : MediaSessionCompat.Callback
	{
		public override async void OnPlayFromSearch(string? query, Bundle? extras)
		{
			try
			{
				var library = AndroidMusicLibraryService.Instance;
				await library.BuildIndexAsync();
				var track = library.Search(query).FirstOrDefault();
				if (track is not null)
					PlayTrack(Android.App.Application.Context, track);
				else
					SetPlaybackState(PlaybackStateCompat.StateError);
			}
			catch (Exception exception)
			{
				Android.Util.Log.Error("AndroidAutoMedia", $"Music search failed: {exception}");
				SetPlaybackState(PlaybackStateCompat.StateError);
			}
		}

		public override async void OnPlayFromMediaId(string? mediaId, Bundle? extras)
		{
			if (string.IsNullOrWhiteSpace(mediaId))
			{
				SetPlaybackState(PlaybackStateCompat.StateError);
				return;
			}

			try
			{
				var library = AndroidMusicLibraryService.Instance;
				await library.BuildIndexAsync();
				var track = library.FindByContentUri(mediaId);
				if (track is not null)
					PlayTrack(Android.App.Application.Context, track);
				else
					SetPlaybackState(PlaybackStateCompat.StateError);
			}
			catch (Exception exception)
			{
				Android.Util.Log.Error("AndroidAutoMedia", $"Media item lookup failed: {exception}");
				SetPlaybackState(PlaybackStateCompat.StateError);
			}
		}

		public override void OnPlay() => RequestResumePlayback();

		public override void OnPause() => PausePlayback();

		public override void OnStop() => StopPlayback();
	}
}
