using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Media.App;

namespace AndroidAutoMediaApp;

[Service(
	Name = "com.coley.androidautomediaapp.MediaPlaybackService",
	Exported = false,
	ForegroundServiceType = Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public sealed class MediaPlaybackService : Service
{
	internal const string ActionPlay = "com.coley.androidautomediaapp.action.PLAY";
	internal const string ActionResume = "com.coley.androidautomediaapp.action.RESUME";
	internal const string ActionPause = "com.coley.androidautomediaapp.action.PAUSE";
	internal const string ActionStop = "com.coley.androidautomediaapp.action.STOP";
	internal const string ActionNext = "com.coley.androidautomediaapp.action.NEXT";
	internal const string ActionPrevious = "com.coley.androidautomediaapp.action.PREVIOUS";
	internal const string ActionUpdateNotification = "com.coley.androidautomediaapp.action.UPDATE_NOTIFICATION";
	internal const string ExtraUri = "media_uri";
	internal const string ExtraTitle = "media_title";
	internal const string ExtraArtist = "media_artist";
	internal const string ExtraPosition = "media_position";
	private const string PreferencesName = "playback_state";
	private const string PreferenceUri = "uri";
	private const string PreferenceTitle = "title";
	private const string PreferenceArtist = "artist";
	private const string PreferencePosition = "position";
	private const string PreferenceIsPlaying = "is_playing";
	private const string ChannelId = "local_music_playback";
	private const int NotificationId = 1001;
	private const long StateSaveIntervalMilliseconds = 15000;
	private Handler? handler;
	private Action persistPlaybackAction = () => { };
	private bool isDestroying;

	public override void OnCreate()
	{
		base.OnCreate();
		MediaSessionManager.GetOrCreate(this);
		handler = new Handler(Looper.MainLooper
			?? throw new InvalidOperationException("The main looper is unavailable."));
		persistPlaybackAction = PersistPlaybackState;
		handler.PostDelayed(persistPlaybackAction, StateSaveIntervalMilliseconds);
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
		{
			var channel = new NotificationChannel(
				ChannelId,
				"Music playback",
				NotificationImportance.Low);
			channel.Description = "Playback controls and current track";
			channel.SetSound(null, null);
			if (GetSystemService(NotificationService) is NotificationManager manager)
				manager.CreateNotificationChannel(channel);
			else
				throw new InvalidOperationException("Notification service is unavailable.");
		}
	}

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		var savedState = LoadPlaybackState();
		var action = intent?.Action;
		if (intent is null && savedState is null)
		{
			StopSelf(startId);
			return StartCommandResult.NotSticky;
		}

		if (action == ActionUpdateNotification)
		{
			var updateState = MediaSessionManager.GetPlaybackSnapshot();
			if (updateState is not null)
				StartForeground(NotificationId, BuildNotification(updateState.Track.Title, updateState.Track.Artist, updateState.IsPlaying));
			return StartCommandResult.Sticky;
		}

		if (intent is null)
		{
			var restoreState = savedState!;
			StartForeground(NotificationId, BuildNotification(
				restoreState.Track.Title,
				restoreState.Track.Artist,
				restoreState.IsPlaying));
			MediaSessionManager.RestorePlayback(restoreState);
			return StartCommandResult.Sticky;
		}

		var title = intent.GetStringExtra(ExtraTitle) ?? savedState?.Track.Title ?? "Local music";
		var artist = intent.GetStringExtra(ExtraArtist) ?? savedState?.Track.Artist ?? "Playing from your device";
		StartForeground(NotificationId, BuildNotification(title, artist, action != ActionPause));

		var uri = intent.GetStringExtra(ExtraUri);
		if (action == ActionPlay && uri is not null)
		{
			MediaSessionManager.StartPlayback(uri, title, artist);
		}
		else if (action == ActionResume)
		{
			if (MediaSessionManager.GetPlaybackSnapshot() is { } currentState &&
				(intent.GetLongExtra(ExtraPosition, -1) < 0 || currentState.Track.ContentUri == uri))
				MediaSessionManager.ResumePlayback();
			else if (savedState is not null)
				MediaSessionManager.RestorePlayback(savedState with { IsPlaying = true });
			else if (uri is not null)
				MediaSessionManager.StartPlayback(uri, title, artist, intent.GetLongExtra(ExtraPosition, 0));
		}
		else if (action == ActionPause)
			MediaSessionManager.PausePlayback();
		else if (action == ActionStop)
		{
			MediaSessionManager.StopPlayback();
			return StartCommandResult.NotSticky;
		}
		else if (action == ActionNext)
			MediaSessionManager.SkipToNext();
		else if (action == ActionPrevious)
			MediaSessionManager.SkipToPrevious();

		var playbackState = MediaSessionManager.GetPlaybackSnapshot();
		if (playbackState is not null)
			StartForeground(NotificationId, BuildNotification(
				playbackState.Track.Title,
				playbackState.Track.Artist,
				playbackState.IsPlaying));
		return StartCommandResult.Sticky;
	}

	public override IBinder? OnBind(Intent? intent) => null;

	public override void OnDestroy()
	{
		isDestroying = true;
		PersistPlaybackState();
		handler?.RemoveCallbacks(persistPlaybackAction);
		MediaSessionManager.ReleasePlayer();
		StopForeground(StopForegroundFlags.Remove);
		base.OnDestroy();
	}

	internal static void RefreshNotification()
	{
		var context = Android.App.Application.Context;
		var intent = new Intent(context, typeof(MediaPlaybackService));
		intent.SetAction(ActionUpdateNotification);
		context.StartService(intent);
	}

	internal static void SavePlaybackState(MediaSessionManager.PlaybackSnapshot snapshot)
	{
		var editor = GetPlaybackPreferences().Edit()
			?? throw new InvalidOperationException("Could not open playback state preferences.");
		editor.PutString(PreferenceUri, snapshot.Track.ContentUri);
		editor.PutString(PreferenceTitle, snapshot.Track.Title);
		editor.PutString(PreferenceArtist, snapshot.Track.Artist);
		editor.PutLong(PreferencePosition, snapshot.Position);
		editor.PutBoolean(PreferenceIsPlaying, snapshot.IsPlaying);
		editor.Apply();
	}

	internal static MediaSessionManager.PlaybackSnapshot? LoadPlaybackState()
	{
		var preferences = GetPlaybackPreferences();
		var uri = preferences.GetString(PreferenceUri, null);
		if (string.IsNullOrWhiteSpace(uri))
			return null;

		var title = preferences.GetString(PreferenceTitle, null) ?? "Local music";
		var artist = preferences.GetString(PreferenceArtist, null) ?? "Unknown artist";
		var position = preferences.GetLong(PreferencePosition, 0);
		var isPlaying = preferences.GetBoolean(PreferenceIsPlaying, false);
		var track = new MusicTrack(title, artist, "Unknown album", 0, uri);
		return new MediaSessionManager.PlaybackSnapshot(track, position, isPlaying);
	}

	internal static void ClearPlaybackState()
	{
		var editor = GetPlaybackPreferences().Edit()
			?? throw new InvalidOperationException("Could not open playback state preferences.");
		editor.Clear();
		editor.Apply();
	}

	private static ISharedPreferences GetPlaybackPreferences() =>
		Android.App.Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
		?? throw new InvalidOperationException("Could not access playback state preferences.");

	private void PersistPlaybackState()
	{
		var snapshot = MediaSessionManager.GetPlaybackSnapshot();
		if (snapshot is not null)
			SavePlaybackState(snapshot);

		if (!isDestroying)
			handler?.PostDelayed(persistPlaybackAction, StateSaveIntervalMilliseconds);
	}

	private Notification BuildNotification(string title, string artist, bool isPlaying)
	{
		var playPauseAction = isPlaying ? ActionPause : ActionResume;
		var playPauseIcon = isPlaying
			? Android.Resource.Drawable.IcMediaPause
			: Android.Resource.Drawable.IcMediaPlay;
		var builder = new AndroidX.Core.App.NotificationCompat.Builder(this, ChannelId);
		builder.SetContentTitle(title);
		builder.SetContentText(artist);
		builder.SetSmallIcon(Resource.Mipmap.appicon);
		builder.SetCategory(Notification.CategoryTransport);
		builder.SetVisibility(AndroidX.Core.App.NotificationCompat.VisibilityPublic);
		builder.SetOngoing(true);
		builder.SetOnlyAlertOnce(true);
		builder.AddAction(Android.Resource.Drawable.IcMediaPrevious, "Previous", CreateActionIntent(ActionPrevious, 1));
		builder.AddAction(playPauseIcon, isPlaying ? "Pause" : "Play", CreateActionIntent(playPauseAction, 2));
		builder.AddAction(Android.Resource.Drawable.IcMediaNext, "Next", CreateActionIntent(ActionNext, 3));
		var mediaStyle = new AndroidX.Media.App.NotificationCompat.MediaStyle();
		mediaStyle.SetMediaSession(MediaSessionManager.GetOrCreate(this).SessionToken);
		mediaStyle.SetShowActionsInCompactView(0, 1, 2);
		builder.SetStyle(mediaStyle);

		var openAppIntent = new Intent(this, typeof(MainActivity));
		openAppIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
		var contentIntent = PendingIntent.GetActivity(
			this,
			0,
			openAppIntent,
			PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)
			?? throw new InvalidOperationException("Could not create the notification content intent.");
		builder.SetContentIntent(contentIntent);
		return builder.Build()
			?? throw new InvalidOperationException("Could not build the playback notification.");
	}

	private PendingIntent CreateActionIntent(string action, int requestCode)
	{
		var serviceIntent = new Intent(this, typeof(MediaPlaybackService));
		serviceIntent.SetAction(action);
		var snapshot = MediaSessionManager.GetPlaybackSnapshot() ?? LoadPlaybackState();
		if (snapshot is not null)
		{
			serviceIntent.PutExtra(ExtraUri, snapshot.Track.ContentUri);
			serviceIntent.PutExtra(ExtraTitle, snapshot.Track.Title);
			serviceIntent.PutExtra(ExtraArtist, snapshot.Track.Artist);
			serviceIntent.PutExtra(ExtraPosition, snapshot.Position);
		}

		return PendingIntent.GetService(
			this,
			requestCode,
			serviceIntent,
			PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)
			?? throw new InvalidOperationException("Could not create the notification action.");
	}
}
