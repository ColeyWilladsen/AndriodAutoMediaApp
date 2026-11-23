namespace AndroidAutoMediaApp;

public partial class App : Application
{
	private readonly IMusicLibraryService musicLibraryService;
	private readonly IMusicSearchService musicSearchService;
	private readonly PhoneMusicCollectionStore collectionStore;

	public App(
		IMusicLibraryService musicLibraryService,
		IMusicSearchService musicSearchService,
		PhoneMusicCollectionStore collectionStore)
	{
		this.musicLibraryService = musicLibraryService;
		this.musicSearchService = musicSearchService;
		this.collectionStore = collectionStore;
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell(musicLibraryService, musicSearchService, collectionStore));
	}
}