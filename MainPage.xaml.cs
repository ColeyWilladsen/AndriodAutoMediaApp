namespace AndroidAutoMediaApp;

public partial class MainPage : ContentPage
{
	private readonly IMusicLibraryService musicLibraryService;
	private readonly IMusicSearchService musicSearchService;
	private readonly PhoneMusicCollectionStore collectionStore;
	private IReadOnlyList<MusicTrack> libraryTracks = Array.Empty<MusicTrack>();
	private bool isIndexing;
	private bool isUpdatingProgress;
	private bool playbackTimerRunning;

	public MainPage(
		IMusicLibraryService musicLibraryService,
		IMusicSearchService musicSearchService,
		PhoneMusicCollectionStore collectionStore)
	{
		this.musicLibraryService = musicLibraryService;
		this.musicSearchService = musicSearchService;
		this.collectionStore = collectionStore;
		InitializeComponent();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		StartPlaybackTimer();
		await RefreshLibraryAsync();
		UpdatePlaybackUi();
	}

	private async Task RefreshLibraryAsync()
	{
		if (isIndexing)
			return;

		if (await Permissions.CheckStatusAsync<LocalAudioPermission>() != PermissionStatus.Granted)
		{
			TabContent.IsVisible = false;
			AccessPanel.IsVisible = true;
			MiniPlayer.IsVisible = false;
			return;
		}

		isIndexing = true;
		AccessPanel.IsVisible = false;
		TabContent.IsVisible = true;
		try
		{
			if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu &&
				await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
				await Permissions.RequestAsync<Permissions.PostNotifications>();

			LibraryStatusLabel.Text = "Scanning music on this device…";
			var count = await musicLibraryService.BuildIndexAsync();
			libraryTracks = musicLibraryService.Search();
			LibraryList.ItemsSource = libraryTracks;
			LibraryCountLabel.Text = $"{count} {(count == 1 ? "song" : "songs")}";
			LibraryStatusLabel.Text = count == 0
				? "No local audio found. Add music to your device and restart the app to scan again."
				: "Your local collection is ready to play.";
			RefreshFavorites();
			RefreshPlaylists();
			RefreshSearchResults(SearchEntry.Text);
		}
		catch (Exception exception)
		{
			LibraryStatusLabel.Text = $"Could not index music: {exception.Message}";
		}
		finally
		{
			isIndexing = false;
		}
	}

	private async void OnGrantAccessClicked(object? sender, EventArgs e)
	{
		var status = await Permissions.RequestAsync<LocalAudioPermission>();
		if (status != PermissionStatus.Granted)
		{
			AccessStatusLabel.Text = "Audio access is required to index and play music stored on this device.";
			return;
		}

		await RefreshLibraryAsync();
	}

	private void OnTabClicked(object? sender, EventArgs e)
	{
		if (sender is not Button button || button.CommandParameter is not string tab)
			return;

		NowPlayingPanel.IsVisible = tab == "Now";
		LibraryPanel.IsVisible = tab == "Library";
		SearchPanel.IsVisible = tab == "Search";
		FavoritesPanel.IsVisible = tab == "Favorites";
		PlaylistsPanel.IsVisible = tab == "Playlists";

		SetTabButton(NowTabButton, tab == "Now");
		SetTabButton(LibraryTabButton, tab == "Library");
		SetTabButton(SearchTabButton, tab == "Search");
		SetTabButton(FavoritesTabButton, tab == "Favorites");
		SetTabButton(PlaylistsTabButton, tab == "Playlists");

		if (tab == "Favorites")
			RefreshFavorites();
		else if (tab == "Playlists")
			RefreshPlaylists();
		else if (tab == "Search")
			RefreshSearchResults(SearchEntry.Text);
		else if (tab == "Now")
			UpdatePlaybackUi();
	}

	private static void SetTabButton(Button button, bool isSelected)
	{
		button.BackgroundColor = isSelected ? Color.FromArgb("#202923") : Colors.Transparent;
		button.TextColor = isSelected ? Color.FromArgb("#B8F36B") : Color.FromArgb("#8C969D");
	}

	private void OnPlayTrackClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: MusicTrack track })
			PlayTracks(new[] { track });
	}

	private void PlayTracks(IReadOnlyList<MusicTrack> tracks)
	{
		if (tracks.Count == 0)
			return;

		MediaSessionManager.Play(tracks);
		SwitchToNowPlaying();
		UpdatePlaybackUi();
	}

	private void SwitchToNowPlaying()
	{
		NowPlayingPanel.IsVisible = true;
		LibraryPanel.IsVisible = false;
		SearchPanel.IsVisible = false;
		FavoritesPanel.IsVisible = false;
		PlaylistsPanel.IsVisible = false;
		SetTabButton(NowTabButton, true);
		SetTabButton(LibraryTabButton, false);
		SetTabButton(SearchTabButton, false);
		SetTabButton(FavoritesTabButton, false);
		SetTabButton(PlaylistsTabButton, false);
	}

	private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) =>
		RefreshSearchResults(e.NewTextValue);

	private void RefreshSearchResults(string? query)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			SearchResultsList.ItemsSource = libraryTracks;
			SearchResultsLabel.Text = $"YOUR MUSIC · {libraryTracks.Count}";
			return;
		}

		var directMatches = musicLibraryService.Search(query);
		var matches = directMatches.Count > 0
			? directMatches
			: musicSearchService.Search(query).Tracks;
		SearchResultsList.ItemsSource = matches;
		SearchResultsLabel.Text = matches.Count == 0 ? "NO MATCHES" : $"MATCHES · {matches.Count}";
	}

	private void OnFavoriteClicked(object? sender, EventArgs e)
	{
		if (sender is Button { CommandParameter: MusicTrack track })
		{
			collectionStore.ToggleFavorite(track.ContentUri);
			RefreshFavorites();
			UpdatePlaybackUi();
		}
	}

	private void OnCurrentFavoriteClicked(object? sender, EventArgs e)
	{
		var track = MediaSessionManager.GetPlaybackSnapshot()?.Track;
		if (track is null)
			return;

		collectionStore.ToggleFavorite(track.ContentUri);
		RefreshFavorites();
		UpdatePlaybackUi();
	}

	private void RefreshFavorites()
	{
		var favoriteUris = collectionStore.GetFavoriteUris();
		var favorites = libraryTracks
			.Where(track => favoriteUris.Contains(track.ContentUri))
			.ToList();
		FavoritesList.ItemsSource = favorites;
		FavoritesCountLabel.Text = $"{favorites.Count} {(favorites.Count == 1 ? "song" : "songs")}";
	}

	private async void OnAddToPlaylistClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: MusicTrack track })
			return;

		var playlists = collectionStore.GetPlaylists();
		if (playlists.Count == 0)
		{
			await DisplayAlert("No playlists yet", "Create a playlist first, then add songs from your library.", "OK");
			return;
		}

		var cancel = "Cancel";
		var selectedName = await DisplayActionSheet(
			"Add to playlist",
			cancel,
			null,
			playlists.Select(playlist => playlist.Name).ToArray());
		if (string.IsNullOrEmpty(selectedName) || selectedName == cancel)
			return;

		var playlist = playlists.FirstOrDefault(item => item.Name == selectedName);
		if (playlist is null)
			return;

		collectionStore.AddTrackToPlaylist(playlist.Id, track.ContentUri);
		RefreshPlaylists();
	}

	private async void OnCreatePlaylistClicked(object? sender, EventArgs e)
	{
		var name = await DisplayPromptAsync("New playlist", "Name your playlist");
		if (string.IsNullOrWhiteSpace(name))
			return;

		name = name.Trim();
		if (collectionStore.GetPlaylists().Any(item =>
				string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
		{
			await DisplayAlert("Playlist already exists", "Choose a different name for this playlist.", "OK");
			return;
		}

		collectionStore.CreatePlaylist(name);
		RefreshPlaylists();
	}

	private void RefreshPlaylists() =>
		PlaylistsList.ItemsSource = collectionStore.GetPlaylists();

	private async void OnPlaylistPlayClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: PhoneMusicPlaylist playlist })
			return;

		var tracksByUri = libraryTracks.ToDictionary(track => track.ContentUri, StringComparer.Ordinal);
		var tracks = playlist.TrackUris
			.Where(tracksByUri.ContainsKey)
			.Select(uri => tracksByUri[uri])
			.ToList();
		if (tracks.Count == 0)
		{
			await DisplayAlert("Playlist is empty", "Add songs from your library using the + button.", "OK");
			return;
		}

		PlayTracks(tracks);
	}

	private void OnPlayPauseClicked(object? sender, EventArgs e)
	{
		var snapshot = MediaSessionManager.GetPlaybackSnapshot();
		if (snapshot is null)
			return;

		if (snapshot.IsPlaying)
			MediaSessionManager.PausePlayback();
		else
			MediaSessionManager.RequestResumePlayback();

		UpdatePlaybackUi();
	}

	private void OnPreviousClicked(object? sender, EventArgs e)
	{
		MediaSessionManager.SkipToPrevious();
		UpdatePlaybackUi();
	}

	private void OnNextClicked(object? sender, EventArgs e)
	{
		MediaSessionManager.SkipToNext();
		UpdatePlaybackUi();
	}

	private void OnStopClicked(object? sender, EventArgs e)
	{
		MediaSessionManager.StopPlayback();
		UpdatePlaybackUi();
	}

	private void OnPlaybackSliderChanged(object? sender, ValueChangedEventArgs e)
	{
		if (isUpdatingProgress || PlaybackSlider.Maximum <= 1)
			return;

		MediaSessionManager.SeekTo((long)e.NewValue);
		PlaybackPosition.Text = FormatTime((long)e.NewValue);
	}

	private void OnMiniPlayerTapped(object? sender, TappedEventArgs e) =>
		OnTabClicked(NowTabButton, EventArgs.Empty);

	private void StartPlaybackTimer()
	{
		if (playbackTimerRunning)
			return;

		playbackTimerRunning = true;
		Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
		{
			if (!IsVisible)
			{
				playbackTimerRunning = false;
				return false;
			}

			UpdatePlaybackUi();
			return true;
		});
	}

	private void UpdatePlaybackUi()
	{
		var snapshot = MediaSessionManager.GetPlaybackSnapshot();
		if (snapshot is null)
		{
			NowPlayingTitle.Text = "Choose a song";
			NowPlayingArtist.Text = "Your library is ready when you are";
			MiniPlayer.IsVisible = false;
			PlaybackSlider.Value = 0;
			PlaybackSlider.Maximum = 1;
			PlaybackSlider.IsEnabled = false;
			PlaybackPosition.Text = "0:00";
			PlaybackDuration.Text = "0:00";
			PlayPauseButton.Text = "▶";
			MiniPlayerPlayPauseButton.Text = "▶";
			CurrentFavoriteButton.Text = "♡";
			return;
		}

		var track = musicLibraryService.FindByContentUri(snapshot.Track.ContentUri) ?? snapshot.Track;
		NowPlayingTitle.Text = track.Title;
		NowPlayingArtist.Text = track.Artist;
		MiniPlayerTitle.Text = track.Title;
		MiniPlayerArtist.Text = track.Artist;
		MiniPlayer.IsVisible = true;
		PlayPauseButton.Text = snapshot.IsPlaying ? "Ⅱ" : "▶";
		MiniPlayerPlayPauseButton.Text = snapshot.IsPlaying ? "Ⅱ" : "▶";
		CurrentFavoriteButton.Text = collectionStore.IsFavorite(track.ContentUri) ? "♥" : "♡";

		var duration = Math.Max(0, track.Duration);
		var position = Math.Clamp(snapshot.Position, 0, duration);
		isUpdatingProgress = true;
		PlaybackSlider.Maximum = Math.Max(1, duration);
		PlaybackSlider.Value = position;
		PlaybackSlider.IsEnabled = duration > 0;
		isUpdatingProgress = false;
		PlaybackPosition.Text = FormatTime(position);
		PlaybackDuration.Text = FormatTime(duration);
	}

	private static string FormatTime(long milliseconds)
	{
		var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
		return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
	}
}
