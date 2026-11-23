using Android.App;
using Android.Content;
using Android.OS;
using Android.Support.V4.Media;
using AndroidX.Media;

namespace AndroidAutoMediaApp;

[Service(
	Name = "com.coley.androidautomediaapp.MusicBrowserService",
	Exported = true,
	Permission = "android.permission.BIND_MEDIA_BROWSER_SERVICE")]
[IntentFilter(new[] { "android.media.browse.MediaBrowserService" })]
public sealed class MusicBrowserService : MediaBrowserServiceCompat
{
	private const string RootId = "local_music";

	public override void OnCreate()
	{
		base.OnCreate();
		SessionToken = MediaSessionManager.GetOrCreate(this).SessionToken;
		if (MediaSessionManager.HasAudioPermission(this))
			_ = BuildIndexAsync();
	}

	public override BrowserRoot? OnGetRoot(string? clientPackageName, int clientUid, Bundle? rootHints) =>
		new(RootId, null);

	public override void OnLoadChildren(string? parentId, Result? result)
	{
		if (result is null)
			return;

		if (parentId != RootId || !MediaSessionManager.HasAudioPermission(this))
		{
			result.SendResult(new Java.Util.ArrayList());
			return;
		}

		result.Detach();
		_ = LoadChildrenAsync(result);
	}

	private async Task LoadChildrenAsync(Result result)
	{
		var items = new Java.Util.ArrayList();
		try
		{
			var library = AndroidMusicLibraryService.Instance;
			await library.BuildIndexAsync();
			foreach (var track in library.Search())
			{
				var descriptionBuilder = new MediaDescriptionCompat.Builder();
				descriptionBuilder.SetMediaId(track.ContentUri);
				descriptionBuilder.SetTitle(track.Title);
				descriptionBuilder.SetSubtitle(track.Artist);
				var description = descriptionBuilder.Build()
					?? throw new InvalidOperationException("Could not create a media description.");

				items.Add(new MediaBrowserCompat.MediaItem(
					description,
					MediaBrowserCompat.MediaItem.FlagPlayable));
			}
		}
		catch (Exception exception)
		{
			Android.Util.Log.Error("AndroidAutoMedia", $"Could not load music library: {exception}");
		}

		result.SendResult(items);
	}

	private async Task BuildIndexAsync()
	{
		try
		{
			await AndroidMusicLibraryService.Instance.BuildIndexAsync();
		}
		catch (Exception exception)
		{
			Android.Util.Log.Error("AndroidAutoMedia", $"Could not index music library: {exception}");
		}
	}
}
