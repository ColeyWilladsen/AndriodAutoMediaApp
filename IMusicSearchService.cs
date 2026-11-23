namespace AndroidAutoMediaApp;

public interface IMusicSearchService
{
	MusicSearchResult Search(string phrase);
}

public enum MusicSearchType
{
	Track,
	Artist,
	Album
}

public sealed record MusicSearchResult(
	MusicSearchType Type,
	IReadOnlyList<MusicTrack> Tracks);
