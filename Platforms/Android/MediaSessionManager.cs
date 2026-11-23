using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
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
	private static LocalTrack? currentTrack;
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

	public static List<LocalTrack> FindLocalTracks(Context context, string? searchQuery)
	{
		if (!HasAudioPermission(context))
			return new List<LocalTrack>();

		var projection = new[]
		{
			MediaStore.Audio.Media.InterfaceConsts.Id,
			MediaStore.Audio.Media.InterfaceConsts.Title,
			MediaStore.Audio.Media.InterfaceConsts.Artist
		};
		var selection = $"{MediaStore.Audio.Media.InterfaceConsts.IsMusic} != 0";
		string[]? selectionArgs = null;
		if (!string.IsNullOrWhiteSpace(searchQuery))
		{
			selection += $" AND ({MediaStore.Audio.Media.InterfaceConsts.Title} LIKE ? " +
				$"OR {MediaStore.Audio.Media.InterfaceConsts.Artist} LIKE ? " +
				$"OR {MediaStore.Audio.Media.InterfaceConsts.Album} LIKE ?)";
			var pattern = $"%{searchQuery.Trim()}%";
			selectionArgs = new[] { pattern, pattern, pattern };
		}

		var audioCollection = MediaStore.Audio.Media.ExternalContentUri
			?? throw new InvalidOperationException("The device audio collection is unavailable.");
		var contentResolver = context.ContentResolver
			?? throw new InvalidOperationException("The device media provider is unavailable.");
		using ICursor? cursor = contentResolver.Query(
			audioCollection,
			projection,
			selection,
			selectionArgs,
			$"{MediaStore.Audio.Media.InterfaceConsts.Title} COLLATE NOCASE ASC");

		if (cursor is null)
			throw new InvalidOperationException("The device media provider returned no audio cursor.");

		var tracks = new List<LocalTrack>();
		var idColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Id);
		var titleColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Title);
		var artistColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Artist);

		while (cursor.MoveToNext())
		{
			var contentUri = ContentUris.WithAppendedId(
				audioCollection,
				cursor.GetLong(idColumn));
			tracks.Add(new LocalTrack(
				contentUri.ToString()!,
				cursor.GetString(titleColumn) ?? "Unknown title",
				cursor.GetString(artistColumn) ?? "Unknown artist"));
		}

		return tracks;
	}

	private static void PlayTrack(Context context, LocalTrack track)
	{
		currentTrack = track;
		var intent = new Intent(context, typeof(MediaPlaybackService));
		intent.SetAction(MediaPlaybackService.ActionPlay);
		intent.PutExtra(MediaPlaybackService.ExtraUri, track.Uri);
		intent.PutExtra(MediaPlaybackService.ExtraTitle, track.Title);
		intent.PutExtra(MediaPlaybackService.ExtraArtist, track.Artist);

		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			context.StartForegroundService(intent);
		else
			context.StartService(intent);
	}

	internal static void StartPlayback(string uri, string title, string artist)
	{
		currentTrack = new LocalTrack(uri, title, artist);
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
			StartPlayback(currentTrack.Uri, currentTrack.Title, currentTrack.Artist);
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
		public override void OnPlayFromSearch(string? query, Bundle? extras)
		{
			var track = FindLocalTracks(
				Android.App.Application.Context,
				string.IsNullOrWhiteSpace(query) ? null : query).FirstOrDefault();
			if (track is not null)
				PlayTrack(Android.App.Application.Context, track);
			else
				SetPlaybackState(PlaybackStateCompat.StateError);
		}

		public override void OnPlayFromMediaId(string? mediaId, Bundle? extras)
		{
			if (string.IsNullOrWhiteSpace(mediaId))
			{
				SetPlaybackState(PlaybackStateCompat.StateError);
				return;
			}

			var track = FindLocalTracks(Android.App.Application.Context, null)
				.FirstOrDefault(item => item.Uri == mediaId);
			if (track is not null)
				PlayTrack(Android.App.Application.Context, track);
			else
				SetPlaybackState(PlaybackStateCompat.StateError);
		}

		public override void OnPlay() => RequestResumePlayback();

		public override void OnPause() => PausePlayback();

		public override void OnStop() => StopPlayback();
	}
}

public sealed record LocalTrack(string Uri, string Title, string Artist);
