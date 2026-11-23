namespace AndroidAutoMediaApp;

public partial class AppShell : Shell
{
	public AppShell(IMusicLibraryService musicLibraryService)
	{
		InitializeComponent();
		Items.Add(new ShellContent
		{
			Title = "Home",
			Route = "MainPage",
			Content = new MainPage(musicLibraryService)
		});
	}
}
