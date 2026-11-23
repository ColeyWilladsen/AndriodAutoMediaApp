namespace AndroidAutoMediaApp;

public partial class App : Application
{
	private readonly IMusicLibraryService musicLibraryService;

	public App(IMusicLibraryService musicLibraryService)
	{
		this.musicLibraryService = musicLibraryService;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell(musicLibraryService));
	}
}