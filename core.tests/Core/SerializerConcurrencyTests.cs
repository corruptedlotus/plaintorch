using System.Collections.Concurrent;
using System.Reflection;
using Pleiades.Resources;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Markdown;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The frontmatter serializer is one host singleton shared by the watcher and the API, so parallel deserializations
/// must neither throw nor disagree on field nullability. Its nullability lookup used to go through one shared
/// <see cref="NullabilityInfoContext"/>, whose internal caches are plain dictionaries that concurrent first-time
/// lookups corrupted ("Operations that change non-concurrent collections must have exclusive access").
/// </summary>
public sealed class SerializerConcurrencyTests : VaultTestBase
{
	private const int NotesPerProbe = 4;

	private static readonly Type[] ProbeTags =
	[
		typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
		typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(string), typeof(Guid), typeof(DateTime),
	];

	private static readonly string ProbeMarkdown = string.Join(Environment.NewLine,
		"---",
		"title: Probe",
		"summary:",
		"tags: [\"alpha\", \"beta\"]",
		"---",
		"Body.") + Environment.NewLine;

	[Fact]
	public void Parallel_deserializations_of_cold_models_neither_throw_nor_disagree_on_nullability()
	{
		var serializer = Vault.GetSingleton<MarkdownFrontMatterSerializer>();

		// The serializer caches nullability per property for the process lifetime, and other tests have already warmed
		// it for every real entity model. Each closed probe type has properties no other test has touched, so every
		// worker below races through the cold (first-lookup) path at once rather than reading a warm cache.
		var deserializers = ProbeTags
			.SelectMany(first => ProbeTags.Select(second => typeof(NullabilityProbe<,>).MakeGenericType(first, second)))
			.Select(probeType => typeof(SerializerConcurrencyTests)
				.GetMethod(nameof(DeserializeProbe), BindingFlags.NonPublic | BindingFlags.Static)!
				.MakeGenericMethod(probeType)
				.CreateDelegate<Func<MarkdownFrontMatterSerializer, string, IReadOnlyList<MarkdownValidationIssue>>>())
			.ToArray();
		var failures = new ConcurrentQueue<Exception>();
		var issueSets = new ConcurrentQueue<IReadOnlyList<MarkdownValidationIssue>>();

		Parallel.For(0, deserializers.Length * NotesPerProbe, index =>
		{
			try
			{
				issueSets.Enqueue(deserializers[index % deserializers.Length](serializer, ProbeMarkdown));
			}
			catch (Exception exception)
			{
				failures.Enqueue(exception);
			}
		});

		Assert.Empty(failures);
		Assert.Equal(deserializers.Length * NotesPerProbe, issueSets.Count);

		// Only the missing non-nullable field is reported: the empty and missing nullable fields read as null.
		Assert.All(issueSets, issues =>
		{
			var issue = Assert.Single(issues);
			Assert.Equal("aliases", issue.FieldPath);
			Assert.Equal(MarkdownMessages.FieldRequired, issue.Message);
		});
	}

	private static IReadOnlyList<MarkdownValidationIssue> DeserializeProbe<TProbe>(MarkdownFrontMatterSerializer serializer, string markdown)
		where TProbe : class, new()
	{
		return serializer.Deserialize<TProbe>(markdown).Issues;
	}

	/// <summary>
	/// A frontmatter model whose type arguments only serve to mint distinct, never-before-reflected closed types.
	/// It mixes non-nullable and nullable reference fields so every nullability call site is exercised.
	/// </summary>
	private sealed class NullabilityProbe<TFirst, TSecond>
	{
		[MarkdownField("title")]
		public string Title { get; set; } = string.Empty;

		[MarkdownField("summary")]
		public string? Summary { get; set; }

		[MarkdownField("tags")]
		public List<string>? Tags { get; set; }

		[MarkdownField("aliases")]
		public List<string> Aliases { get; set; } = [];

		[MarkdownField("note")]
		public string? Note { get; set; }
	}
}
