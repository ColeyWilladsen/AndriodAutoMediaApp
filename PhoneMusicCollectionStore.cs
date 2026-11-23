using System.Text.Json;
using Microsoft.Maui.Storage;

namespace AndroidAutoMediaApp;

public sealed class PhoneMusicCollectionStore
{
	private const string FavoritesKey = "phone_favorite_track_uris";
	private const string PlaylistsKey = "phone_music_playlists";

	public IReadOnlySet<string> GetFavoriteUris() =>
		JsonSerializer.Deserialize<HashSet<string>>(Preferences.Default.Get(FavoritesKey, "[]"))
		?? new HashSet<string>();

	public IReadOnlyList<PhoneMusicPlaylist> GetPlaylists() =>
		JsonSerializer.Deserialize<List<PhoneMusicPlaylist>>(Preferences.Default.Get(PlaylistsKey, "[]"))
		?? new List<PhoneMusicPlaylist>();

	public bool IsFavorite(string contentUri) => GetFavoriteUris().Contains(contentUri);

	public void ToggleFavorite(string contentUri)
	{
		var favorites = GetFavoriteUris().ToHashSet(StringComparer.Ordinal);
		if (!favorites.Add(contentUri))
			favorites.Remove(contentUri);

		Preferences.Default.Set(FavoritesKey, JsonSerializer.Serialize(favorites));
	}

	public PhoneMusicPlaylist CreatePlaylist(string name)
	{
		var playlist = new PhoneMusicPlaylist(Guid.NewGuid().ToString("N"), name, new List<string>());
		var playlists = GetPlaylists().ToList();
		playlists.Add(playlist);
		SavePlaylists(playlists);
		return playlist;
	}

	public void AddTrackToPlaylist(string playlistId, string contentUri)
	{
		var playlists = GetPlaylists().ToList();
		var playlist = playlists.FirstOrDefault(item => item.Id == playlistId)
			?? throw new InvalidOperationException("The selected playlist no longer exists.");
		if (!playlist.TrackUris.Contains(contentUri, StringComparer.Ordinal))
			playlist.TrackUris.Add(contentUri);

		SavePlaylists(playlists);
	}

	private static void SavePlaylists(IReadOnlyList<PhoneMusicPlaylist> playlists) =>
		Preferences.Default.Set(PlaylistsKey, JsonSerializer.Serialize(playlists));
}

public sealed record PhoneMusicPlaylist(string Id, string Name, List<string> TrackUris)
{
	public int TrackCount => TrackUris.Count;

	public string TrackCountLabel => TrackCount == 1 ? "1 song" : $"{TrackCount} songs";
}
