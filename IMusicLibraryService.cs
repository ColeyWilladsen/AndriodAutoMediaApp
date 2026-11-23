namespace AndroidAutoMediaApp;

public interface IMusicLibraryService
{
	bool IsIndexed { get; }

	Task<int> BuildIndexAsync(CancellationToken cancellationToken = default);

	IReadOnlyList<MusicTrack> Search(string? query = null);

	MusicTrack? FindByContentUri(string contentUri);
}

public sealed record MusicTrack(
	string Title,
	string Artist,
	string Album,
	long Duration,
	string ContentUri);
