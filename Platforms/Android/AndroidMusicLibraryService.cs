using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;

namespace AndroidAutoMediaApp;

public sealed class AndroidMusicLibraryService : IMusicLibraryService
{
	private static readonly Lazy<AndroidMusicLibraryService> InstanceHolder =
		new(() => new AndroidMusicLibraryService(Android.App.Application.Context));
	private readonly Context context;
	private readonly SemaphoreSlim indexLock = new(1, 1);
	private readonly object syncRoot = new();
	private IReadOnlyList<MusicTrack> tracks = Array.Empty<MusicTrack>();
	private bool isIndexed;

	private AndroidMusicLibraryService(Context context)
	{
		this.context = context.ApplicationContext ?? context;
	}

	public static AndroidMusicLibraryService Instance => InstanceHolder.Value;

	public bool IsIndexed
	{
		get
		{
			lock (syncRoot)
				return isIndexed;
		}
	}

	public async Task<int> BuildIndexAsync(CancellationToken cancellationToken = default)
	{
		if (!MediaSessionManager.HasAudioPermission(context))
			throw new UnauthorizedAccessException("Audio library permission has not been granted.");

		await indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			lock (syncRoot)
			{
				if (isIndexed)
					return tracks.Count;
			}

			var indexedTracks = await Task.Run(ReadTracks, cancellationToken).ConfigureAwait(false);
			lock (syncRoot)
			{
				tracks = indexedTracks;
				isIndexed = true;
			}

			return indexedTracks.Count;
		}
		finally
		{
			indexLock.Release();
		}
	}

	public IReadOnlyList<MusicTrack> Search(string? query = null)
	{
		IReadOnlyList<MusicTrack> snapshot;
		lock (syncRoot)
		{
			if (!isIndexed)
				return Array.Empty<MusicTrack>();
			snapshot = tracks;
		}

		var terms = query?.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (terms is null || terms.Length == 0)
			return snapshot;

		return snapshot
			.Where(track => terms.All(term =>
				track.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
				track.Artist.Contains(term, StringComparison.OrdinalIgnoreCase) ||
				track.Album.Contains(term, StringComparison.OrdinalIgnoreCase)))
			.ToArray();
	}

	public MusicTrack? FindByContentUri(string contentUri)
	{
		IReadOnlyList<MusicTrack> snapshot;
		lock (syncRoot)
		{
			if (!isIndexed)
				return null;
			snapshot = tracks;
		}

		return snapshot.FirstOrDefault(track => track.ContentUri == contentUri);
	}

	private List<MusicTrack> ReadTracks()
	{
		var audioCollection = MediaStore.Audio.Media.ExternalContentUri
			?? throw new InvalidOperationException("The device audio collection is unavailable.");
		var contentResolver = context.ContentResolver
			?? throw new InvalidOperationException("The device media provider is unavailable.");
		var projection = new[]
		{
			MediaStore.Audio.Media.InterfaceConsts.Id,
			MediaStore.Audio.Media.InterfaceConsts.Title,
			MediaStore.Audio.Media.InterfaceConsts.Artist,
			MediaStore.Audio.Media.InterfaceConsts.Album,
			MediaStore.Audio.Media.InterfaceConsts.Duration
		};

		using ICursor? cursor = contentResolver.Query(
			audioCollection,
			projection,
			null,
			null,
			$"{MediaStore.Audio.Media.InterfaceConsts.Title} COLLATE NOCASE ASC");
		if (cursor is null)
			throw new InvalidOperationException("The device media provider returned no audio cursor.");

		var idColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Id);
		var titleColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Title);
		var artistColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Artist);
		var albumColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Album);
		var durationColumn = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Duration);
		var tracks = new List<MusicTrack>(cursor.Count);

		while (cursor.MoveToNext())
		{
			var contentUri = ContentUris.WithAppendedId(audioCollection, cursor.GetLong(idColumn))
				?? throw new InvalidOperationException("Could not create an audio content URI.");
			tracks.Add(new MusicTrack(
				cursor.GetString(titleColumn) ?? "Unknown title",
				cursor.GetString(artistColumn) ?? "Unknown artist",
				cursor.GetString(albumColumn) ?? "Unknown album",
				cursor.GetLong(durationColumn),
				contentUri.ToString() ?? throw new InvalidOperationException("Could not format an audio content URI.")));
		}

		return tracks;
	}
}
