namespace AndroidAutoMediaApp;

public partial class MainPage : ContentPage
{
	private readonly IMusicLibraryService musicLibraryService;
	private bool isIndexing;

	public MainPage(IMusicLibraryService musicLibraryService)
	{
		this.musicLibraryService = musicLibraryService;
		InitializeComponent();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await RefreshLibraryAsync();
	}

	private async void OnGrantAudioAccessClicked(object? sender, EventArgs e)
	{
		var status = await Permissions.RequestAsync<LocalAudioPermission>();
		if (status != PermissionStatus.Granted)
		{
			StatusLabel.Text = "Audio access is required to index and play music stored on this device.";
			return;
		}

		await RefreshLibraryAsync();
	}

	private async Task RefreshLibraryAsync()
	{
		if (isIndexing || await Permissions.CheckStatusAsync<LocalAudioPermission>() != PermissionStatus.Granted)
			return;

		isIndexing = true;
		try
		{
			if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu &&
				await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
				await Permissions.RequestAsync<Permissions.PostNotifications>();

			StatusLabel.Text = "Indexing music on this device…";
			var count = await musicLibraryService.BuildIndexAsync();
			StatusLabel.Text = $"Indexed {count} songs. Connect Android Auto and ask Assistant to play music.";
		}
		catch (Exception exception)
		{
			StatusLabel.Text = $"Could not index music: {exception.Message}";
		}
		finally
		{
			isIndexing = false;
		}
	}
}
