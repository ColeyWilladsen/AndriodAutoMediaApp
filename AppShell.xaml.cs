namespace AndroidAutoMediaApp;

public partial class AppShell : Shell
{
	public AppShell(
		IMusicLibraryService musicLibraryService,
		IMusicSearchService musicSearchService,
		PhoneMusicCollectionStore collectionStore)
	{
		InitializeComponent();
		Items.Add(new ShellContent
		{
			Title = "Home",
			Route = "MainPage",
			Content = new MainPage(musicLibraryService, musicSearchService, collectionStore)
		});
	}
}
