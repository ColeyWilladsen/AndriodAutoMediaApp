using Android.App;
using Android.Content;
using Android.OS;

namespace AndroidAutoMediaApp;

[Service(
	Name = "com.coley.androidautomediaapp.MediaPlaybackService",
	Exported = false,
	ForegroundServiceType = Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public sealed class MediaPlaybackService : Service
{
	internal const string ActionPlay = "com.coley.androidautomediaapp.action.PLAY";
	internal const string ActionResume = "com.coley.androidautomediaapp.action.RESUME";
	internal const string ExtraUri = "media_uri";
	internal const string ExtraTitle = "media_title";
	internal const string ExtraArtist = "media_artist";
	private const string ChannelId = "local_music_playback";
	private const int NotificationId = 1001;

	public override void OnCreate()
	{
		base.OnCreate();
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
		{
			var channel = new NotificationChannel(
				ChannelId,
				"Music playback",
				NotificationImportance.Low);
			if (GetSystemService(NotificationService) is NotificationManager manager)
				manager.CreateNotificationChannel(channel);
			else
				throw new InvalidOperationException("Notification service is unavailable.");
		}
	}

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		if (intent is null)
		{
			StopSelf(startId);
			return StartCommandResult.NotSticky;
		}

		var title = intent.GetStringExtra(ExtraTitle) ?? "Local music";
		var artist = intent.GetStringExtra(ExtraArtist) ?? "Playing from your device";
		var notification = BuildNotification(title, artist);
		StartForeground(NotificationId, notification);

		var uri = intent.GetStringExtra(ExtraUri);
		if (intent.Action == ActionPlay && uri is not null)
			MediaSessionManager.StartPlayback(uri, title, artist);
		else if (intent.Action == ActionResume)
			MediaSessionManager.ResumePlayback();

		return StartCommandResult.NotSticky;
	}

	public override IBinder? OnBind(Intent? intent) => null;

	public override void OnDestroy()
	{
		MediaSessionManager.ReleasePlayer();
		StopForeground(StopForegroundFlags.Remove);
		base.OnDestroy();
	}

	private Notification BuildNotification(string title, string artist)
	{
		var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
			? new Notification.Builder(this, ChannelId)
			: new Notification.Builder(this);

		return builder
			.SetContentTitle(title)
			.SetContentText(artist)
			.SetSmallIcon(Resource.Mipmap.appicon)
			.SetOngoing(true)
			.Build();
	}
}
