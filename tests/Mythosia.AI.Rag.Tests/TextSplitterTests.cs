using System.Text;
using Mythosia.AI.Rag.Splitters;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class TextSplitterTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    [TestMethod]
    [DataRow("character", 0)]
    [DataRow("character", -1)]
    [DataRow("character", int.MinValue)]
    [DataRow("recursive", 0)]
    [DataRow("recursive", -1)]
    [DataRow("recursive", int.MinValue)]
    [DataRow("token", 0)]
    [DataRow("token", -1)]
    [DataRow("token", int.MinValue)]
    public void InvalidSize_IsRejectedAfterChangingPublicProperty(string kind, int size)
    {
        var splitter = Create(kind, 10, 0);
        SetSize(splitter, size);

        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Split(Document("hello")));
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Split(Document("")),
            "An empty document must not hide an invalid configuration.");
    }

    [TestMethod]
    [DataRow("character", -1)]
    [DataRow("character", int.MinValue)]
    [DataRow("recursive", -1)]
    [DataRow("recursive", int.MinValue)]
    [DataRow("token", -1)]
    [DataRow("token", int.MinValue)]
    public void NegativeOverlap_IsRejectedAfterChangingPublicProperty(string kind, int overlap)
    {
        var splitter = Create(kind, 4, 0);
        SetOverlap(splitter, overlap);

        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Split(Document("A B C D E F")));
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Split(Document("")));
    }

    [TestMethod]
    [DataRow("character", 4)]
    [DataRow("character", 200)]
    [DataRow("character", int.MaxValue)]
    [DataRow("recursive", 4)]
    [DataRow("recursive", 200)]
    [DataRow("recursive", int.MaxValue)]
    [DataRow("token", 4)]
    [DataRow("token", 50)]
    [DataRow("token", int.MaxValue)]
    public void OverlapAtLeastChunkSize_IsEquivalentToZeroOverlap(string kind, int overlap)
    {
        var document = Document("A B C D E F G H I J K L M N O P Q R");
        var expected = Create(kind, 4, 0).Split(document).Select(chunk => chunk.Content).ToArray();
        var actual = Create(kind, 4, overlap).Split(document).Select(chunk => chunk.Content).ToArray();

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void SmallSizeWithExistingDefaultOverlap_RemainsUsable()
    {
        var document = Document("A B C D E F G H I");
        foreach (var splitter in new ITextSplitter[]
        {
            new CharacterTextSplitter(3),
            new RecursiveTextSplitter(3),
            new TokenTextSplitter(3)
        })
        {
            var chunks = splitter.Split(document);
            Assert.IsNotEmpty(chunks);
            Assert.AreEqual(WithoutWhitespace(document.Content), WithoutWhitespace(string.Concat(chunks.Select(c => c.Content))));
        }
    }

    [TestMethod]
    [DataRow("character", "")]
    [DataRow("character", " \r\n\t ")]
    [DataRow("recursive", "")]
    [DataRow("recursive", " \r\n\t ")]
    [DataRow("token", "")]
    [DataRow("token", " \r\n\t ")]
    public void EmptyOrWhitespaceInput_DoesNotProduceEmptyChunks(string kind, string content)
    {
        Assert.IsEmpty(Create(kind, 8, 2).Split(Document(content)));
    }

    [TestMethod]
    [DataRow("character")]
    [DataRow("recursive")]
    [DataRow("token")]
    public void Split_CopiesMetadataAndAssignsStableIndependentChunkIds(string kind)
    {
        var document = Document("alpha beta gamma delta epsilon zeta eta theta");
        document.Metadata["tenant"] = "company-a";
        document.Metadata["source"] = "metadata-source";
        document.Metadata["chunk_index"] = "metadata-index";
        var splitter = Create(kind, kind == "token" ? 2 : 8, 0);

        var chunks = splitter.Split(document);
        Assert.IsTrue(chunks.Count > 1);
        for (var index = 0; index < chunks.Count; index++)
        {
            Assert.AreEqual($"{document.Id}_chunk_{index}", chunks[index].Id);
            Assert.AreEqual(document.Id, chunks[index].DocumentId);
            Assert.AreEqual(index, chunks[index].Index);
            Assert.AreEqual(document.Source, chunks[index].Metadata["source"]);
            Assert.AreEqual(index.ToString(), chunks[index].Metadata["chunk_index"]);
            Assert.AreEqual("company-a", chunks[index].Metadata["tenant"]);
        }

        chunks[0].Metadata["tenant"] = "changed-chunk";
        Assert.AreEqual("company-a", chunks[1].Metadata["tenant"]);
        Assert.AreEqual("company-a", document.Metadata["tenant"]);
        Assert.AreEqual("metadata-source", document.Metadata["source"]);
        Assert.AreEqual("metadata-index", document.Metadata["chunk_index"]);
        document.Metadata["tenant"] = "changed-document";
        Assert.AreEqual("company-a", chunks[1].Metadata["tenant"]);

        CollectionAssert.AreEqual(chunks.Select(c => c.Id).ToArray(),
            splitter.Split(document).Select(c => c.Id).ToArray());
    }

    [TestMethod]
    [DataRow("abcdef", 10, 2)]
    [DataRow("abcdef", 6, 2)]
    [DataRow("abcdefgh", 6, 2)]
    public void Character_DoesNotAddOverlapOnlyTail(string content, int size, int overlap)
    {
        var chunks = new CharacterTextSplitter(size, overlap, null).Split(Document(content));

        Assert.AreEqual(content.Length <= size ? 1 : 2, chunks.Count);
        Assert.AreEqual(content[^1], chunks[^1].Content[^1]);
        if (chunks.Count == 2)
            CollectionAssert.AreEqual(new[] { "abcdef", "efgh" }, chunks.Select(c => c.Content).ToArray());
    }

    [TestMethod]
    [DataRow("abc|def", 3, "|")]
    [DataRow("ab<->cd<->ef", 4, "<->")]
    [DataRow("ab<->cd<->ef", 3, "<->")]
    [DataRow("A||B||C", 1, "||")]
    [DataRow("a\n\nb\n\nc", 3, "\n\n")]
    [DataRow("abcdef", 2, "")]
    public void Character_SeparatorMustFitEntirelyInsideChunk(string content, int size, string separator)
    {
        var chunks = new CharacterTextSplitter(size, 0, separator).Split(Document(content));

        Assert.IsTrue(chunks.All(c => c.Content.Length <= size));
        Assert.AreEqual(WithoutWhitespace(content), WithoutWhitespace(string.Concat(chunks.Select(c => c.Content))));
    }

    [TestMethod]
    public void Recursive_ZeroOverlap_DoesNotRepeatPreviousWordOrExceedSize()
    {
        var chunks = new RecursiveTextSplitter(5, 0).Split(Document("aaaa bbbb cccc"));

        Assert.IsTrue(chunks.All(c => c.Content.Length <= 5));
        Assert.AreEqual("aaaabbbbcccc", WithoutWhitespace(string.Concat(chunks.Select(c => c.Content))));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(50)]
    [DataRow(449)]
    public void Recursive_ParagraphAndSentenceBoundaries_RespectLengthLimit(int overlap)
    {
        var paragraphs = Enumerable.Range(1, 18).Select(index =>
            $"Section{index:D2}: A document can contain long paragraphs with several short sentences. " +
            "Every boundary must leave enough room for the next piece. " +
            "UTF-16 text can also include 한글, emoji 😀, and punctuation. " +
            $"EndOfSection{index:D2}.");
        var text = string.Join("\r\n\r\n", paragraphs);

        var chunks = new RecursiveTextSplitter(450, overlap).Split(Document(text));

        Assert.IsTrue(chunks.Count > 1);
        Assert.IsTrue(chunks.All(c => c.Content.Length <= 450));
        AssertValidUnicode(chunks.Select(c => c.Content));
        if (overlap == 0)
            Assert.AreEqual(WithoutWhitespace(text), WithoutWhitespace(string.Concat(chunks.Select(c => c.Content))));
    }

    [TestMethod]
    public void Recursive_KeepSeparatorFalse_ReinsertsDelimiterBetweenMergedPieces()
    {
        var splitter = new RecursiveTextSplitter(10, 0, new[] { "::", "" }) { KeepSeparator = false };

        var chunks = splitter.Split(Document("one::two::three"));

        CollectionAssert.AreEqual(new[] { "one::two", "three" }, chunks.Select(c => c.Content).ToArray());
    }

    [TestMethod]
    public void Recursive_KeepSeparatorFalse_DoesNotGlueWordsTogether()
    {
        var splitter = new RecursiveTextSplitter(11, 0, new[] { " ", "" }) { KeepSeparator = false };

        var chunks = splitter.Split(Document("alpha beta gamma delta"));

        Assert.AreEqual("alpha beta gamma delta", string.Join(" ", chunks.Select(c => c.Content)));
        Assert.IsTrue(chunks.All(c => c.Content.Length <= 11));
    }

    [TestMethod]
    public void Recursive_NoMatchingSeparator_UsesBoundedFallback()
    {
        var content = "abcdefghijklmnop😀qrstuvwxyz";
        var splitter = new RecursiveTextSplitter(4, 0, new[] { "\n\n", "::" });

        var chunks = splitter.Split(Document(content));

        Assert.IsTrue(chunks.All(c => c.Content.Length <= 4));
        Assert.AreEqual(content, string.Concat(chunks.Select(c => c.Content)));
        AssertValidUnicode(chunks.Select(c => c.Content));
    }

    [TestMethod]
    public void Recursive_EmptySeparatorList_StillSplitsLongText()
    {
        var content = "abcdefghijklmnop";
        var chunks = new RecursiveTextSplitter(3, 0, Array.Empty<string>()).Split(Document(content));

        Assert.IsTrue(chunks.All(c => c.Content.Length <= 3));
        Assert.AreEqual(content, string.Concat(chunks.Select(c => c.Content)));
    }

    [TestMethod]
    public void Recursive_SplitText_ValidatesSettingsWithoutDocumentWrapper()
    {
        var splitter = new RecursiveTextSplitter { ChunkSize = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.SplitText("hello"));
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.SplitText(""));

        splitter.ChunkSize = 5;
        splitter.ChunkOverlap = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.SplitText("hello"));
    }

    [TestMethod]
    public void Recursive_NullSeparatorArrayOrEntry_IsRejectedBeforeProcessing()
    {
        var splitter = new RecursiveTextSplitter { Separators = null! };
        Assert.Throws<ArgumentException>(() => splitter.Split(Document("hello")));
        Assert.Throws<ArgumentException>(() => splitter.SplitText(""));

        splitter.Separators = new[] { "\n\n", null!, "" };
        Assert.Throws<ArgumentException>(() => splitter.Split(Document("hello")));
        Assert.Throws<ArgumentException>(() => splitter.SplitText(""));
    }

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(true, 3)]
    [DataRow(true, 10)]
    [DataRow(false, 0)]
    [DataRow(false, 3)]
    [DataRow(false, 10)]
    public void Recursive_DuplicateSeparators_PreserveFirstOccurrenceAndCallerArray(bool keepSeparator, int overlap)
    {
        var separators = new[] { "::", "::", "--", "::", " ", "--", "", "", "!" };
        var original = separators.ToArray();
        var splitter = new RecursiveTextSplitter(10, overlap)
        {
            Separators = separators,
            KeepSeparator = keepSeparator
        };
        var unique = new RecursiveTextSplitter(10, overlap, ["::", "--", " ", "", "!"])
        {
            KeepSeparator = keepSeparator
        };
        const string text = "one::two--three::four--five six seven::abcdefghijk!終😀";

        CollectionAssert.AreEqual(unique.SplitText(text), splitter.SplitText(text));
        CollectionAssert.AreEqual(unique.Split(Document(text)).Select(c => c.Content).ToArray(),
            splitter.Split(Document(text)).Select(c => c.Content).ToArray());
        Assert.AreSame(separators, splitter.Separators);
        CollectionAssert.AreEqual(original, separators);
    }

    [TestMethod]
    public void Recursive_SeparatorPriority_IsNotSortedDuringNormalization()
    {
        const string text = "one::two--three::four--five";
        var first = new RecursiveTextSplitter(10, 0, ["::", "--", "::", ""]) { KeepSeparator = false };
        var second = new RecursiveTextSplitter(10, 0, ["--", "::", "--", ""]) { KeepSeparator = false };

        CollectionAssert.AreEqual(new[] { "one", "two--three", "four--five" }, first.SplitText(text));
        CollectionAssert.AreEqual(new[] { "one::two", "three", "four", "five" }, second.SplitText(text));
    }

    [TestMethod]
    public void Recursive_512DuplicateSeparators_DoNotAmplifyPerDocumentAllocations()
    {
        var text = "\n" + new string('x', 10_000);
        var unique = new RecursiveTextSplitter(100, 0, ["\n", ""]);
        var repeated = new RecursiveTextSplitter(100, 0, Enumerable.Repeat("\n", 512).Append(""));
        _ = unique.SplitText(text); // Warm the same splitting and merging paths before measuring.

        var beforeUnique = GC.GetAllocatedBytesForCurrentThread();
        var expected = unique.SplitText(text);
        var uniqueBytes = GC.GetAllocatedBytesForCurrentThread() - beforeUnique;
        var beforeRepeated = GC.GetAllocatedBytesForCurrentThread();
        var actual = repeated.SplitText(text);
        var repeatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRepeated;

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(new string('x', 10_000), string.Concat(actual));
        Assert.IsTrue(actual.All(chunk => chunk.Length <= 100));
        // A broad relative ceiling avoids timing/JIT dependence, while detecting the
        // previous hundreds-fold repeated splitting and remaining-array allocation.
        Assert.IsTrue(repeatedBytes < uniqueBytes * 4 + 128_000,
            $"Duplicate separators amplified allocations: unique={uniqueBytes}, repeated={repeatedBytes}.");
    }

    [TestMethod]
    public void Recursive_LongUniqueSeparatorChain_PreservesContentAndChunkOrder()
    {
        var separators = Enumerable.Range(1, 1024).Reverse().Select(size => new string('x', size)).Append("").ToArray();
        var text = new string('x', 1024) + "終😀tail";
        var splitter = new RecursiveTextSplitter(32, 0, separators);

        var chunks = splitter.SplitText(text);

        Assert.AreEqual(text, string.Concat(chunks));
        Assert.IsTrue(chunks.All(chunk => chunk.Length <= 32));
        AssertValidUnicode(chunks);
    }

    [TestMethod]
    [DataRow("::abcdef", "::", 4, 0, true, new[] { "::ab", "cdef" })]
    [DataRow("::abcdef", "::", 4, 1, true, new[] { "::ab", "bcde", "ef" })]
    [DataRow("::abcdef", "::", 4, 4, true, new[] { "::ab", "cdef" })]
    [DataRow("::ab::cd", "::", 4, 0, true, new[] { "::ab", "::cd" })]
    [DataRow("ab::cd::ef", "::", 4, 0, true, new[] { "ab", "::cd", "::ef" })]
    [DataRow("abcd::", "::", 4, 0, true, new[] { "abcd", "::" })]
    [DataRow("abcdef", "abcdef", 4, 0, true, new[] { "abcd", "ef" })]
    [DataRow("ababaZ", "aba", 3, 0, true, new[] { "aba", "baZ" })]
    [DataRow("xabaabaZ", "aba", 4, 0, true, new[] { "xaba", "abaZ" })]
    [DataRow("\nabcdef ", "\n", 4, 0, true, new[] { "\nabc", "def " })]
    [DataRow("😀abcdef", "😀", 4, 0, true, new[] { "😀ab", "cdef" })]
    [DataRow("PREFIXtail", "PREFIX", 3, 0, true, new[] { "PRE", "FIX", "tai", "l" })]
    [DataRow("::abcdef", "::", 4, 0, false, new[] { "abcd", "ef" })]
    [DataRow("ab::cd::ef", "::", 4, 0, false, new[] { "ab", "cd", "ef" })]
    [DataRow("abcd::", "::", 4, 0, false, new[] { "abcd" })]
    [DataRow("abcdef", "abcdef", 4, 0, false, new string[0])]
    public void Recursive_LeadingSeparatorReuse_PreservesSplitAndFallbackBoundaries(
        string text, string separator, int size, int overlap, bool keepSeparator, string[] expected)
    {
        var splitter = new RecursiveTextSplitter(size, overlap, [separator]) { KeepSeparator = keepSeparator };

        var actual = splitter.SplitText(text);

        CollectionAssert.AreEqual(expected, actual);
        AssertValidUnicode(actual);
    }

    [TestMethod]
    public void Recursive_512DistinctLeadingPrefixes_DoNotRepeatedlyCopyTheBody()
    {
        var text = new string('a', 1024) + new string('x', 100_000);
        var separators = Enumerable.Range(513, 512).Reverse().Select(size => new string('a', size)).Append("").ToArray();
        var direct = new RecursiveTextSplitter(1000, 0, [""]);
        var prefixes = new RecursiveTextSplitter(1000, 0, separators);
        _ = direct.SplitText(text);
        _ = prefixes.SplitText("warmup");

        var beforeDirect = GC.GetAllocatedBytesForCurrentThread();
        var expected = direct.SplitText(text);
        var directBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDirect;
        var beforePrefixes = GC.GetAllocatedBytesForCurrentThread();
        var actual = prefixes.SplitText(text);
        var prefixBytes = GC.GetAllocatedBytesForCurrentThread() - beforePrefixes;

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(text, string.Concat(actual));
        Assert.IsTrue(prefixBytes < directBytes * 3 + 256_000,
            $"Unchanged prefix splits amplified allocations: direct={directBytes}, prefixes={prefixBytes}.");
    }

    [TestMethod]
    [DataRow("character", 1)]
    [DataRow("character", 2)]
    [DataRow("character", 3)]
    [DataRow("recursive", 1)]
    [DataRow("recursive", 2)]
    [DataRow("recursive", 3)]
    public void CharacterBasedSplitters_PreserveSupplementaryUnicodeCharacters(string kind, int size)
    {
        const string text = "a😀b𠀀c😁한글🚀끝";
        var chunks = Create(kind, size, 0).Split(Document(text));

        Assert.AreEqual(text, string.Concat(chunks.Select(c => c.Content)));
        AssertValidUnicode(chunks.Select(c => c.Content));
        Assert.IsTrue(chunks.All(c => c.Content.Length <= size ||
            size == 1 && c.Content.Length == 2 && char.IsSurrogatePair(c.Content, 0)),
            "A two-unit scalar may exceed a one-unit budget, but must never be split in half.");
    }

    [TestMethod]
    [DataRow("character")]
    [DataRow("recursive")]
    public void CharacterBasedSplitters_OverlapStartsOnUnicodeBoundary(string kind)
    {
        var chunks = Create(kind, 3, 1).Split(Document("a😀b😁c🚀d𠀀e"));

        AssertValidUnicode(chunks.Select(c => c.Content));
        Assert.IsTrue(chunks.All(c => c.Content.Length <= 3));
        var combined = string.Concat(chunks.Select(c => c.Content));
        foreach (var scalar in "a😀b😁c🚀d𠀀e".EnumerateRunes())
            Assert.Contains(scalar.ToString(), combined);
    }

    [TestMethod]
    [DataRow("character")]
    [DataRow("recursive")]
    public void CharacterBasedSplitters_BoundedMixedTextCases_PreserveContentAndSize(string kind)
    {
        var random = new Random(712937);
        var alphabet = new[] { "a", "B", "한", "글", "😀", "𠀀", ".", "|", " ", "\n", "\r\n", "\n\n" };
        for (var sample = 0; sample < 90; sample++)
        {
            var size = random.Next(1, 28);
            var text = string.Concat(Enumerable.Range(0, random.Next(1, 100)).Select(_ => alphabet[random.Next(alphabet.Length)]));
            var chunks = Create(kind, size, 0).Split(Document(text));

            Assert.AreEqual(WithoutWhitespace(text), WithoutWhitespace(string.Concat(chunks.Select(c => c.Content))),
                $"Content changed in case {sample}, size {size}.");
            AssertValidUnicode(chunks.Select(c => c.Content));
            Assert.IsTrue(chunks.All(c => c.Content.Length <= size ||
                size == 1 && c.Content.Length == 2 && char.IsSurrogatePair(c.Content, 0)),
                $"Size exceeded in case {sample}, size {size}.");
            Assert.IsTrue(chunks.All(c => !string.IsNullOrWhiteSpace(c.Content)));
        }
    }

    [TestMethod]
    [DataRow("A B C D E", 5, 2, 1)]
    [DataRow("A B C D", 5, 2, 1)]
    [DataRow("A B C D E F G H", 5, 2, 2)]
    public void Token_DoesNotAddOverlapOnlyTail(string text, int size, int overlap, int expectedCount)
    {
        var chunks = new TokenTextSplitter(size, overlap).Split(Document(text));

        Assert.AreEqual(expectedCount, chunks.Count);
        Assert.AreEqual(text.Split(' ')[^1], chunks[^1].Content.Split(' ')[^1]);
        if (expectedCount == 2)
            CollectionAssert.AreEqual(new[] { "A B C D E", "D E F G H" }, chunks.Select(c => c.Content).ToArray());
    }

    [TestMethod]
    public void Token_WhitespaceUnits_PreserveWordOrderWithZeroOverlap()
    {
        const string text = "A\tB\r\nC  D E\nF G\tH";
        var chunks = new TokenTextSplitter(3, 0).Split(Document(text));

        CollectionAssert.AreEqual(new[] { "A B C", "D E F", "G H" }, chunks.Select(c => c.Content).ToArray());
    }

    [TestMethod]
    public void Token_CustomSeparators_NormalizeToSpaces()
    {
        var splitter = new TokenTextSplitter(2, 0) { TokenSeparators = new[] { ',', ';' } };

        var chunks = splitter.Split(Document("alpha,beta;;gamma,delta"));

        CollectionAssert.AreEqual(new[] { "alpha beta", "gamma delta" }, chunks.Select(c => c.Content).ToArray());
    }

    [TestMethod]
    public void Token_NullSeparatorArray_IsRejectedAfterChangingPublicProperty()
    {
        var splitter = new TokenTextSplitter { TokenSeparators = null! };

        Assert.Throws<ArgumentException>(() => splitter.Split(Document("hello")));
        Assert.Throws<ArgumentException>(() => splitter.Split(Document("")));
    }

    [TestMethod]
    public void Token_DoesNotPretendWhitespaceUnitsAreModelTokens()
    {
        var unbrokenText = new string('한', 1000) + "😀";

        var chunks = new TokenTextSplitter(2, 0).Split(Document(unbrokenText));

        Assert.HasCount(1, chunks);
        Assert.AreEqual(unbrokenText, chunks[0].Content,
            "This compatibility splitter counts whitespace-delimited units, not model tokenizer IDs.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void RagBuilder_RejectsNonPositiveChunkSizeImmediately(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RagBuilder().WithChunkSize(size));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void RagBuilder_RejectsNegativeOverlapImmediately(int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RagBuilder().WithChunkOverlap(overlap));
    }

    [TestMethod]
    public void RagBuilder_AllowsSizeAndOverlapToBeConfiguredInEitherOrder()
    {
        var builder = new RagBuilder();
        Assert.AreSame(builder, builder.WithChunkSize(1).WithChunkOverlap(200));
        Assert.AreSame(builder, builder.WithChunkOverlap(200).WithChunkSize(1));
        Assert.AreSame(builder, builder.WithChunkOverlap(0).WithChunkSize(int.MaxValue));
    }

    [TestMethod]
    [DataRow(0xD83D)]
    [DataRow(0xDE00)]
    public void CustomSeparators_CannotSplitUnicodeScalars(int codeUnit)
    {
        // Attribute strings are UTF-8 encoded in metadata; construct the invalid unit at runtime.
        var separator = new string((char)codeUnit, 1);
        Assert.Throws<ArgumentException>(() => new RecursiveTextSplitter(2, 0, [separator]).Split(Document("a😀b")));
        Assert.Throws<ArgumentException>(() => new CharacterTextSplitter(2, 0, separator).Split(Document("a😀b")));
        Assert.Throws<ArgumentException>(() => new TokenTextSplitter(2, 0) { TokenSeparators = separator.ToCharArray() }.Split(Document("a😀b")));
    }

    [TestMethod]
    public void CompleteUnicodeSeparator_RemainsSupported()
    {
        const string text = "a😀b😀c😀d";
        var recursive = new RecursiveTextSplitter(4, 0, ["😀", ""]);
        var character = new CharacterTextSplitter(4, 0, "😀");
        foreach (var splitter in new ITextSplitter[] { recursive, character })
        {
            var chunks = splitter.Split(Document(text));
            Assert.AreEqual(text, string.Concat(chunks.Select(c => c.Content)));
            AssertValidUnicode(chunks.Select(c => c.Content));
        }
    }

    private static ITextSplitter Create(string kind, int size, int overlap) => kind switch
    {
        "character" => new CharacterTextSplitter(size, overlap),
        "recursive" => new RecursiveTextSplitter(size, overlap),
        "token" => new TokenTextSplitter(size, overlap),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static RagDocument Document(string content) => new("test-document", content, "source/example.txt");

    private static string WithoutWhitespace(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static void AssertValidUnicode(IEnumerable<string> chunks)
    {
        foreach (var chunk in chunks)
            _ = StrictUtf8.GetBytes(chunk);
    }

    private static void SetSize(ITextSplitter splitter, int size)
    {
        switch (splitter)
        {
            case CharacterTextSplitter character: character.ChunkSize = size; break;
            case RecursiveTextSplitter recursive: recursive.ChunkSize = size; break;
            case TokenTextSplitter token: token.MaxTokensPerChunk = size; break;
            default: throw new ArgumentOutOfRangeException(nameof(splitter));
        }
    }

    private static void SetOverlap(ITextSplitter splitter, int overlap)
    {
        switch (splitter)
        {
            case CharacterTextSplitter character: character.ChunkOverlap = overlap; break;
            case RecursiveTextSplitter recursive: recursive.ChunkOverlap = overlap; break;
            case TokenTextSplitter token: token.TokenOverlap = overlap; break;
            default: throw new ArgumentOutOfRangeException(nameof(splitter));
        }
    }
}
