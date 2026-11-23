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
	private static IReadOnlyList<MusicTrack> currentQueue = Array.Empty<MusicTrack>();
	private static readonly IMusicSearchService musicSearchService =
		new MusicSearchService(AndroidMusicLibraryService.Instance);
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
		PlayTracks(context, new[] { track });
	}

	private static void PlayTracks(Context context, IReadOnlyList<MusicTrack> tracks)
	{
		if (tracks.Count == 0)
		{
			SetPlaybackState(PlaybackStateCompat.StateError);
			return;
		}

		currentQueue = tracks;
		currentTrack = tracks[0];
		var intent = new Intent(context, typeof(MediaPlaybackService));
		intent.SetAction(MediaPlaybackService.ActionPlay);
		intent.PutExtra(MediaPlaybackService.ExtraUri, currentTrack.ContentUri);
		intent.PutExtra(MediaPlaybackService.ExtraTitle, currentTrack.Title);
		intent.PutExtra(MediaPlaybackService.ExtraArtist, currentTrack.Artist);

		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			context.StartForegroundService(intent);
		else
			context.StartService(intent);
	}

	internal static void StartPlayback(
		string uri,
		string title,
		string artist,
		long startPosition = 0,
		bool playWhenReady = true)
	{
		if (currentQueue.Count == 0 || currentQueue[0].ContentUri != uri)
			currentQueue = new[] { new MusicTrack(title, artist, "Unknown album", 0, uri) };
		currentTrack = currentQueue[0];
		var playerBuilder = new ExoPlayerBuilder(Android.App.Application.Context);
		playerBuilder.SetWakeMode(C.WakeModeLocal);
		var activePlayer = player ??= playerBuilder.Build()
			?? throw new InvalidOperationException("Could not initialize the media player.");
		var audioAttributesBuilder = new AudioAttributes.Builder();
		audioAttributesBuilder.SetUsage(C.UsageMedia);
		audioAttributesBuilder.SetContentType(C.AudioContentTypeMusic);
		var audioAttributes = audioAttributesBuilder.Build()
			?? throw new InvalidOperationException("Could not configure audio focus attributes.");
		activePlayer.SetAudioAttributes(audioAttributes, true);
		activePlayer.SetMediaItem(MediaItem.FromUri(Android.Net.Uri.Parse(uri)!));
		foreach (var track in currentQueue.Skip(1))
			activePlayer.AddMediaItem(MediaItem.FromUri(Android.Net.Uri.Parse(track.ContentUri)!));
		activePlayer.Prepare();
		if (startPosition > 0)
			activePlayer.SeekTo(startPosition);
		if (playWhenReady)
			activePlayer.Play();
		playerNeedsPrepare = false;

		var metadata = new MediaMetadataCompat.Builder();
		metadata.PutString(MediaMetadataCompat.MetadataKeyTitle, title);
		metadata.PutString(MediaMetadataCompat.MetadataKeyArtist, artist);
		metadata.PutString(MediaMetadataCompat.MetadataKeyAlbum, currentTrack.Album);
		session?.SetMetadata(metadata.Build());
		SetPlaybackState(playWhenReady
			? PlaybackStateCompat.StatePlaying
			: PlaybackStateCompat.StatePaused);
		PersistPlaybackState();
	}

	internal static void RequestResumePlayback()
	{
		if (currentTrack is null)
			return;

		var intent = new Intent(Android.App.Application.Context, typeof(MediaPlaybackService));
		intent.SetAction(MediaPlaybackService.ActionResume);
		if (currentTrack is not null)
		{
			intent.PutExtra(MediaPlaybackService.ExtraUri, currentTrack.ContentUri);
			intent.PutExtra(MediaPlaybackService.ExtraTitle, currentTrack.Title);
			intent.PutExtra(MediaPlaybackService.ExtraArtist, currentTrack.Artist);
			intent.PutExtra(MediaPlaybackService.ExtraPosition, player?.CurrentPosition ?? 0);
		}
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			Android.App.Application.Context.StartForegroundService(intent);
		else
			Android.App.Application.Context.StartService(intent);
	}

	internal static void ResumePlayback()
	{
		var track = currentTrack;
		if (track is null)
			return;

		var activePlayer = player;
		if (activePlayer is null || playerNeedsPrepare)
		{
			var savedState = MediaPlaybackService.LoadPlaybackState();
			if (savedState is not null && savedState.Track.ContentUri == track.ContentUri)
				RestorePlayback(savedState with { IsPlaying = true });
			else
				StartPlayback(track.ContentUri, track.Title, track.Artist);
			return;
		}

		activePlayer.Play();
		SetPlaybackState(PlaybackStateCompat.StatePlaying);
		PersistPlaybackState();
		MediaPlaybackService.RefreshNotification();
	}

	internal static void PausePlayback()
	{
		player?.Pause();
		SetPlaybackState(PlaybackStateCompat.StatePaused);
		PersistPlaybackState();
		MediaPlaybackService.RefreshNotification();
	}

	internal static void SkipToNext()
	{
		if (player?.HasNextMediaItem != true)
			return;

		player.SeekToNextMediaItem();
		UpdateCurrentTrackFromQueue();
		SetPlaybackState(PlaybackStateCompat.StatePlaying);
		PersistPlaybackState();
		MediaPlaybackService.RefreshNotification();
	}

	internal static void SkipToPrevious()
	{
		if (player is null)
			return;

		if (player.CurrentPosition > 3000)
			player.SeekTo(0);
		else if (player.HasPreviousMediaItem)
			player.SeekToPreviousMediaItem();
		else
			return;

		UpdateCurrentTrackFromQueue();
		SetPlaybackState(PlaybackStateCompat.StatePlaying);
		PersistPlaybackState();
		MediaPlaybackService.RefreshNotification();
	}

	internal static void SeekTo(long position)
	{
		if (player is null)
			return;

		player.SeekTo(Math.Max(0, position));
		SetPlaybackState(player.IsPlaying
			? PlaybackStateCompat.StatePlaying
			: PlaybackStateCompat.StatePaused);
		PersistPlaybackState();
	}

	internal static void StopPlayback()
	{
		player?.Stop();
		playerNeedsPrepare = true;
		SetPlaybackState(PlaybackStateCompat.StateStopped);
		MediaPlaybackService.ClearPlaybackState();
		currentTrack = null;
		currentQueue = Array.Empty<MusicTrack>();
		Android.App.Application.Context.StopService(
			new Intent(Android.App.Application.Context, typeof(MediaPlaybackService)));
	}

	internal static PlaybackSnapshot? GetPlaybackSnapshot()
	{
		if (currentTrack is null)
			return null;

		return new PlaybackSnapshot(
			currentTrack,
			player?.CurrentPosition ?? 0,
			player?.IsPlaying ?? false);
	}

	internal static void RestorePlayback(PlaybackSnapshot snapshot)
	{
		currentTrack = snapshot.Track;
		currentQueue = new[] { snapshot.Track };
		StartPlayback(
			snapshot.Track.ContentUri,
			snapshot.Track.Title,
			snapshot.Track.Artist,
			snapshot.Position,
			snapshot.IsPlaying);
	}

	private static void PersistPlaybackState()
	{
		var snapshot = GetPlaybackSnapshot();
		if (snapshot is not null)
			MediaPlaybackService.SavePlaybackState(snapshot);
	}

	internal static void ReleasePlayer()
	{
		player?.Release();
		player = null;
		playerNeedsPrepare = true;
	}

	internal sealed record PlaybackSnapshot(MusicTrack Track, long Position, bool IsPlaying);

	private static void UpdateCurrentTrackFromQueue()
	{
		if (player is null)
			return;

		var index = player.CurrentMediaItemIndex;
		if (index < 0 || index >= currentQueue.Count)
			return;

		currentTrack = currentQueue[index];
		var metadata = new MediaMetadataCompat.Builder();
		metadata.PutString(MediaMetadataCompat.MetadataKeyTitle, currentTrack.Title);
		metadata.PutString(MediaMetadataCompat.MetadataKeyArtist, currentTrack.Artist);
		metadata.PutString(MediaMetadataCompat.MetadataKeyAlbum, currentTrack.Album);
		session?.SetMetadata(metadata.Build());
	}

	private static void SetPlaybackState(int state)
	{
		var actions = PlaybackStateCompat.ActionPlay |
			PlaybackStateCompat.ActionPause |
			PlaybackStateCompat.ActionPlayFromSearch |
			PlaybackStateCompat.ActionPlayFromMediaId |
			PlaybackStateCompat.ActionStop;
		if (player?.HasNextMediaItem == true)
			actions |= PlaybackStateCompat.ActionSkipToNext;
		if (player?.HasPreviousMediaItem == true || player?.CurrentPosition > 3000)
			actions |= PlaybackStateCompat.ActionSkipToPrevious;
		if (player is not null)
			actions |= PlaybackStateCompat.ActionSeekTo;

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
				var result = musicSearchService.Search(query ?? string.Empty);
				if (result.Tracks.Count > 0)
					PlayTracks(Android.App.Application.Context, result.Tracks);
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

		public override void OnSkipToNext() => SkipToNext();

		public override void OnSkipToPrevious() => SkipToPrevious();

		public override void OnSeekTo(long position) => SeekTo(position);
	}
}
