using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AndroidAutoMediaApp;

public sealed partial class MusicSearchService(IMusicLibraryService musicLibraryService) : IMusicSearchService
{
	private const double MinimumMatchScore = 0.62;

	public MusicSearchResult Search(string phrase)
	{
		var query = Normalize(phrase);
		query = StripLeadingPlay(query);
		if (query.Length == 0)
			return new MusicSearchResult(MusicSearchType.Track, Array.Empty<MusicTrack>());

		if (TryRemovePrefix(query, "songs by ", out var artistQuery) ||
			TryRemovePrefix(query, "song by ", out artistQuery) ||
			TryRemovePrefix(query, "artist ", out artistQuery))
		{
			return FindCollection(artistQuery, MusicSearchType.Artist, track => track.Artist);
		}

		if (TryRemovePrefix(query, "album ", out var albumQuery))
			return FindCollection(albumQuery, MusicSearchType.Album, track => track.Album);

		var separator = query.LastIndexOf(" by ", StringComparison.Ordinal);
		if (separator > 0 && separator + 4 < query.Length)
			return FindTrack(query[..separator], query[(separator + 4)..]);

		return FindTrack(query, null);
	}

	private MusicSearchResult FindTrack(string titleQuery, string? artistQuery)
	{
		var candidates = musicLibraryService.Search()
			.Select(track =>
			{
				var titleScore = Similarity(titleQuery, track.Title);
				var artistScore = artistQuery is null ? 0 : Similarity(artistQuery, track.Artist);
				var score = artistQuery is null
					? titleScore
					: titleScore * 0.75 + artistScore * 0.25;
				return (Track: track, TitleScore: titleScore, ArtistScore: artistScore, Score: score);
			})
			.Where(candidate =>
				candidate.TitleScore >= MinimumMatchScore &&
				(artistQuery is null || candidate.ArtistScore >= MinimumMatchScore))
			.OrderByDescending(candidate => candidate.Score)
			.ToArray();

		return new MusicSearchResult(
			MusicSearchType.Track,
			candidates.Take(1)
				.Select(candidate => candidate.Track)
				.ToArray());
	}

	private MusicSearchResult FindCollection(
		string query,
		MusicSearchType type,
		Func<MusicTrack, string> getValue)
	{
		if (query.Length == 0)
			return new MusicSearchResult(type, Array.Empty<MusicTrack>());

		var tracks = musicLibraryService.Search();
		var match = tracks
			.Select(track => getValue(track))
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(value => (Value: value, Score: Similarity(query, value)))
			.OrderByDescending(candidate => candidate.Score)
			.FirstOrDefault();

		if (match.Value is null || match.Score < MinimumMatchScore)
			return new MusicSearchResult(type, Array.Empty<MusicTrack>());

		return new MusicSearchResult(
			type,
			tracks.Where(track => string.Equals(getValue(track), match.Value, StringComparison.OrdinalIgnoreCase))
				.ToArray());
	}

	private static double Similarity(string left, string right)
	{
		left = Normalize(left);
		right = Normalize(right);
		if (left.Length == 0 || right.Length == 0)
			return 0;
		if (string.Equals(left, right, StringComparison.Ordinal))
			return 1;

		var distance = LevenshteinDistance(left, right);
		return 1d - (double)distance / Math.Max(left.Length, right.Length);
	}

	private static int LevenshteinDistance(string left, string right)
	{
		var previous = new int[right.Length + 1];
		var current = new int[right.Length + 1];
		for (var column = 0; column <= right.Length; column++)
			previous[column] = column;

		for (var row = 1; row <= left.Length; row++)
		{
			current[0] = row;
			for (var column = 1; column <= right.Length; column++)
			{
				var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
				current[column] = Math.Min(
					Math.Min(current[column - 1] + 1, previous[column] + 1),
					previous[column - 1] + substitutionCost);
			}

			(previous, current) = (current, previous);
		}

		return previous[right.Length];
	}

	private static string Normalize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;

		var decomposed = value.Normalize(NormalizationForm.FormD);
		var builder = new StringBuilder(decomposed.Length);
		foreach (var character in decomposed)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
				continue;
			builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
		}

		return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
	}

	private static string StripLeadingPlay(string query)
	{
		return TryRemovePrefix(query, "play ", out var remainder) ? remainder : query;
	}

	private static bool TryRemovePrefix(string value, string prefix, out string remainder)
	{
		if (value.StartsWith(prefix, StringComparison.Ordinal))
		{
			remainder = value[prefix.Length..].Trim();
			return true;
		}

		remainder = string.Empty;
		return false;
	}

	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRegex();
}
