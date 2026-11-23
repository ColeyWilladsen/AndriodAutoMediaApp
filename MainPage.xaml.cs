namespace AndroidAutoMediaApp;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	private async void OnGrantAudioAccessClicked(object? sender, EventArgs e)
	{
		var status = await Permissions.RequestAsync<LocalAudioPermission>();
		StatusLabel.Text = status == PermissionStatus.Granted
			? "Audio access is enabled. Connect Android Auto and ask Assistant to play a song."
			: "Audio access is required to browse and play music stored on this device.";
	}
}
