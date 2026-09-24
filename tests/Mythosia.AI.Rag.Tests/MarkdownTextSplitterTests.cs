using System.Text;
using System.Diagnostics;
using Mythosia.AI.Rag.Splitters;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class MarkdownTextSplitterTests
{
    private static string[] Split(string text, int size = 1000, bool breadcrumbs = true) =>
        new MarkdownTextSplitter(size) { IncludeHeadingBreadcrumb = breadcrumbs }
            .Split(new RagDocument("manual", text, "manual.md")).Select(c => c.Content).ToArray();

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void InvalidSize_IsRejectedAfterPropertyMutationEvenForEmptyInput(int size)
    {
        var splitter = new MarkdownTextSplitter { ChunkSize = size };
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Split(new RagDocument()));
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Split(new RagDocument { Content = "hello" }));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(7)]
    public void InvalidHeadingLevel_IsRejected(int level)
    {
        var splitter = new MarkdownTextSplitter { MinSplitHeadingLevel = level };
        Assert.Throws<ArgumentOutOfRangeException>(() => splitter.Split(new RagDocument()));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void HeadingsRemainSearchableWithoutBody(bool breadcrumbs)
    {
        const string text = "# Refunds unavailable\n# Shipping delayed";
        var chunks = Split(text, breadcrumbs: breadcrumbs);
        Assert.IsTrue(chunks.Any(c => c.Contains("Refunds unavailable")));
        Assert.IsTrue(chunks.Any(c => c.Contains("Shipping delayed")));
    }

    [TestMethod]
    public void EmptyAncestors_ArePreservedByChildWithoutRedundantChunks()
    {
        var chunks = Split("# Manual\n## Refund\nWithin seven days.");
        Assert.HasCount(1, chunks);
        Assert.AreEqual("# Manual\n## Refund\n\nWithin seven days.", chunks[0]);
    }

    [TestMethod]
    public void DisablingBreadcrumbs_PreservesEachOriginalHeadingOnce()
    {
        var chunks = Split("# Manual\n## Refund\n" + string.Join(" ", Enumerable.Repeat("seven days", 30)), 35, false);
        Assert.AreEqual(1, chunks.Count(c => c.Contains("# Manual")));
        Assert.AreEqual(1, chunks.Count(c => c.Contains("## Refund")));
        Assert.IsTrue(chunks.All(c => c.Length <= 35));
        Assert.AreEqual(30, string.Join(" ", chunks).Split("seven days").Length - 1);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(20)]
    [DataRow(100)]
    public void PlainTextBudgetAndUnicode_ArePreserved(int size)
    {
        var text = new string('A', 99) + "😀𠀀" + new string('B', 61);
        var chunks = Split(text, size);
        Assert.AreEqual(text, string.Concat(chunks));
        foreach (var chunk in chunks)
        {
            _ = new UTF8Encoding(false, true).GetBytes(chunk);
            Assert.IsTrue(chunk.Length <= size || size == 1 && chunk.Length == 2 && char.IsSurrogatePair(chunk, 0));
        }
    }

    [TestMethod]
    public void BudgetExcludesRepeatedBreadcrumb()
    {
        var chunks = Split("# Title\n" + new string('a', 40), 20);
        Assert.HasCount(2, chunks);
        Assert.IsTrue(chunks.All(c => c.StartsWith("# Title\n\n") && c.Length == "# Title\n\n".Length + 20));
    }

    [TestMethod]
    [DataRow("```", "```")]
    [DataRow("```", "````")]
    [DataRow("````", "````")]
    [DataRow("~~~~", "~~~~~")]
    [DataRow("   ```", "  ````\t ")]
    public void FenceClosesWithSameMarkerAndAtLeastOpeningLength(string open, string close)
    {
        var chunks = Split($"# First\n{open}text\ncode\n{close}\n# Second\nsecond body");
        Assert.HasCount(2, chunks);
        Assert.IsTrue(chunks[1].StartsWith("# Second\n\n"));
        Assert.IsFalse(chunks[0].Contains("# Second"));
    }

    [TestMethod]
    [DataRow("```")]
    [DataRow("~~~~")]
    [DataRow("```` trailing")]
    [DataRow("    ````")]
    public void InvalidClosingFence_RemainsInsideCode(string invalidClose)
    {
        var chunks = Split($"# First\n````text\n{invalidClose}\n# inside code\n````\n# Second\nsecond body", 20);
        Assert.IsTrue(chunks.Any(c => c.Contains("# inside code") && c.Contains("````text")));
        Assert.IsFalse(chunks.Any(c => c.StartsWith("# inside code")));
        Assert.IsTrue(chunks.Any(c => c.StartsWith("# Second\n\n")));
    }

    [TestMethod]
    public void OversizedAndUnclosedCode_RemainsAtomic()
    {
        var code = "```text\n" + new string('x', 400) + "\n# literal heading";
        CollectionAssert.AreEqual(new[] { code }, Split(code, 20));
    }

    [TestMethod]
    public void CodeInformationStringWithBacktick_IsNotAnOpeningFence()
    {
        var chunks = Split("```bad`info\n" + new string('x', 100) + "\n# Real\nbody", 20);
        Assert.IsTrue(chunks.Any(c => c.StartsWith("# Real\n\n")));
        Assert.IsTrue(chunks.Where(c => !c.StartsWith("# Real")).All(c => c.Length <= 20));
    }

    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    [DataRow("\r")]
    public void LineEndings_DoNotChangeStructureOrBudgets(string newline)
    {
        var text = string.Join(newline, "# Title", "first body", "## Code", "```csharp", "return 42;", "```", "## End", "last body");
        CollectionAssert.AreEqual(Split(text.Replace(newline, "\n"), 20), Split(text, 20));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TablesWithOptionalOuterPipes_RepeatHeaderAndPreserveRows(bool outerPipes)
    {
        var rows = Enumerable.Range(0, 12).Select(i => $"entry{i:00} | value{i:00}").ToArray();
        var lines = new[] { "Name | Value", ":--- | ---:" }.Concat(rows);
        if (outerPipes) lines = lines.Select(l => "| " + l + " |");
        var chunks = Split(string.Join("\n", lines), 80);
        var header = outerPipes ? "| Name | Value |\n| :--- | ---: |" : "Name | Value\n:--- | ---:";
        Assert.IsTrue(chunks.Length > 1);
        Assert.IsTrue(chunks.All(c => c.StartsWith(header)));
        foreach (var row in rows) Assert.AreEqual(1, chunks.Count(c => c.Contains(row)));
        Assert.IsTrue(chunks.All(c => c.Length <= 80));
    }

    [TestMethod]
    public void MixedPipesEscapedCellsAndOversizedRow_RetainHeader()
    {
        var header = "| Na\\|me | Value |\n--- | ---";
        var longRow = "first | " + new string('x', 100);
        var chunks = Split(header + "\n" + longRow + "\nlast | value", 25);
        Assert.HasCount(2, chunks);
        Assert.IsTrue(chunks.All(c => c.StartsWith(header)));
        Assert.Contains(longRow, chunks[0]);
        Assert.Contains("last | value", chunks[1]);
    }

    [TestMethod]
    public void PipeTextWithoutDelimiterRow_IsNotTreatedAsTable()
    {
        var text = "alpha | beta\n" + new string('x', 100);
        var chunks = Split(text, 20);
        Assert.IsTrue(chunks.All(c => c.Length <= 20));
        Assert.AreEqual(1, chunks.Count(c => c.Contains("alpha")));
    }

    [TestMethod]
    public void BoldLabelInsideCode_IsNotRepeatedAsSectionContext()
    {
        var chunks = Split("Intro\n\n```text\n**example label**\n```\n\n" + new string('x', 80) + "\n\nlast", 60);
        Assert.AreEqual(1, chunks.Count(c => c.Contains("**example label**")));
    }

    [TestMethod]
    public void RepeatedBoldLabel_CannotBreakPlainTextBudget()
    {
        var chunks = Split("**Label**\n\n" + new string('x', 100) + "\n\nlast", 20);
        Assert.IsTrue(chunks.All(c => c.Length <= 20));
        Assert.Contains("last", chunks[^1]);
    }

    [TestMethod]
    public void BoldTableCell_IsNotRepeatedAboveAnUnrelatedRow()
    {
        var chunks = Split("Rule | Customer\n--- | ---\n**No refund** | ACorp\nYes | BCorp", 55);
        Assert.HasCount(2, chunks);
        Assert.AreEqual("Rule | Customer\n--- | ---\nYes | BCorp", chunks[1]);
        Assert.AreEqual(1, chunks.Count(c => c.Contains("**No refund**")));
    }

    [TestMethod]
    public void BoldTableHeader_IsRepeatedOnlyAsPartOfItsHeader()
    {
        const string header = "**Rule** | Customer\n--- | ---";
        var chunks = Split(header + "\nNever | ACorp\nYes | BCorp", 50);
        Assert.HasCount(2, chunks);
        Assert.IsTrue(chunks.All(c => c.StartsWith(header)));
        Assert.IsTrue(chunks.All(c => c.Split("**Rule**").Length == 2));
    }

    [TestMethod]
    public void BoldTableCell_DoesNotBecomeAFollowingParagraphsContext()
    {
        var chunks = Split("Rule | Customer\n--- | ---\n**No refund** | ACorp\n\nShipping takes 3 days.", 55);
        var shipping = chunks.Single(c => c.Contains("Shipping takes 3 days."));
        Assert.AreEqual("Shipping takes 3 days.", shipping);
    }

    [TestMethod]
    [DataRow("```text\nexample code with no policy\n```")]
    [DataRow("Name | Value\n--- | ---\nentry | value")]
    public void ProseLabel_StopsAtCodeOrTableBoundary(string atomicBlock)
    {
        var chunks = Split("**No refund**\n" + new string('x', 60) + "\n\n" + atomicBlock + "\n\nAfter the block.", 30);
        var after = chunks.Single(c => c.Contains("After the block."));
        Assert.DoesNotContain("**No refund**", after);
        Assert.IsFalse(chunks.Any(c => c.StartsWith("**No refund**\n" + atomicBlock)));
    }

    [TestMethod]
    [DataRow("**No refund** applies only to ACorp.")]
    [DataRow("    **No refund**")]
    [DataRow("- **No refund**")]
    public void InlineOrIndentedEmphasis_DoesNotBecomeAProseLabel(string emphasizedLine)
    {
        var chunks = Split(emphasizedLine + "\n\n" + new string('x', 80) + "\n\nlast", 40);
        Assert.AreEqual(1, chunks.Count(c => c.Contains("**No refund**")));
        Assert.DoesNotContain("**No refund**", chunks[^1]);
    }

    [TestMethod]
    public void StandaloneProseLabels_RepeatWithinTheirOwnScopeOnly()
    {
        var text = "**Policy A**\n" + new string('a', 65) + "\ntailA\n\n**Policy B**\n" + new string('b', 65) + "\ntailB";
        var chunks = Split(text, 30);
        var tailA = chunks.Single(c => c.Contains("tailA"));
        var tailB = chunks.Single(c => c.Contains("tailB"));
        Assert.Contains("**Policy A**", tailA);
        Assert.DoesNotContain("**Policy B**", tailA);
        Assert.Contains("**Policy B**", tailB);
        Assert.DoesNotContain("**Policy A**", tailB);
        Assert.IsTrue(chunks.All(c => c.Length <= 30));
    }

    [TestMethod]
    [DataRow(true, "\n")]
    [DataRow(false, "\n")]
    [DataRow(true, "\r\n")]
    [DataRow(false, "\r\n")]
    public void SoftWrappedEmphasis_DoesNotCreateANewParagraph(bool breadcrumbs, string newline)
    {
        var input = string.Join(newline, "This is", "**not**", "allowed.");
        CollectionAssert.AreEqual(new[] { "This is\n**not**\nallowed." }, Split(input, 1000, breadcrumbs));
    }

    [TestMethod]
    public void SoftWrappedEmphasis_IsNotRepeatedAsALabel()
    {
        var input = "It is false that\n**No refunds**\napplies to Product B.\n"
            + string.Join(" ", Enumerable.Repeat("Product B accepts refunds.", 10));
        var chunks = Split(input, 45);
        Assert.AreEqual(1, chunks.Sum(c => c.Split("**No refunds**").Length - 1));
        Assert.DoesNotContain("**No refunds**", chunks[^1]);
        Assert.AreEqual(new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray()),
            new string(string.Concat(chunks).Where(c => !char.IsWhiteSpace(c)).ToArray()));
    }

    [TestMethod]
    public void LabelImmediatelyAfterFence_IsRecognizedAtTheNewTextBlock()
    {
        var input = "```text\nexample\n```\n**Policy**\n" + new string('x', 65) + "\ntail";
        var chunks = Split(input, 30);
        Assert.Contains("**Policy**", chunks.Single(c => c.Contains("tail")));
        Assert.DoesNotContain("**Policy**", chunks[0]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExcessiveRepeatedContext_ThrowsInsteadOfProducingAmplifiedOutput(bool table)
    {
        var input = table
            ? "| " + new string('h', 4096) + " | Value |\n| --- | --- |\n"
                + string.Concat(Enumerable.Repeat("| x | y |\n", 256))
            : "# " + new string('h', 2048) + "\n"
                + string.Concat(Enumerable.Repeat("## Child\nx\n", 128));
        var exception = Assert.Throws<InvalidOperationException>(() => Split(input));
        Assert.Contains("output budget", exception.Message);
        Assert.Contains("UTF-16", exception.Message);
    }

    [TestMethod]
    public void OutputBudget_IncludesAllSectionsAndAllowsTheExactMinimumLimit()
    {
        // Each child produces exactly 1,024 UTF-16 units, including its heading path.
        // Both inputs are under 2,048 units, so their total output limit is 65,536.
        var prefix = "# " + new string('h', 1014) + "\n";
        var input = prefix + string.Concat(Enumerable.Repeat("## c\nx\n", 64));
        var chunks = Split(input);
        Assert.HasCount(64, chunks);
        Assert.AreEqual(65_536, chunks.Sum(c => c.Length));

        var exception = Assert.Throws<InvalidOperationException>(() => Split(input + "## c\nx\n"));
        Assert.Contains("65536", exception.Message);
    }

    [TestMethod]
    public void OutputBudget_UsesTheRelativeLimitForLargerDocuments()
    {
        var prefix = "# " + new string('h', 2000) + "\n";
        var input = prefix + string.Concat(Enumerable.Repeat("## c\nx\n", 35));
        Assert.IsGreaterThan(2_048, input.Length);
        var chunks = Split(input);
        Assert.HasCount(35, chunks);
        Assert.IsTrue(chunks.Sum(c => (long)c.Length) <= 32L * input.Length);
        Assert.IsGreaterThan(65_536L, chunks.Sum(c => (long)c.Length));

        var tooMuch = input + "## c\nx\n";
        var exception = Assert.Throws<InvalidOperationException>(() => Split(tooMuch));
        Assert.Contains((32L * tooMuch.Length).ToString(), exception.Message);
    }

    [TestMethod]
    public async Task SharedSplitter_ConcurrentCallsHaveIndependentOutputBudgets()
    {
        var splitter = new MarkdownTextSplitter();
        var prefix = "# " + new string('h', 1014) + "\n";
        var exactLimit = prefix + string.Concat(Enumerable.Repeat("## c\nx\n", 64));
        var exceedsLimit = exactLimit + "## c\nx\n";

        var calls = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            var document = new RagDocument(index.ToString(), index % 2 == 0 ? exactLimit : exceedsLimit, "shared.md");
            if (index % 2 == 0)
                Assert.AreEqual(65_536, splitter.Split(document).Sum(chunk => chunk.Content.Length));
            else
                Assert.Throws<InvalidOperationException>(() => splitter.Split(document));
        }));
        await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));

        // A failed parallel call must not consume a later document's budget.
        Assert.AreEqual(65_536, splitter.Split(new RagDocument("after", exactLimit, "after.md"))
            .Sum(chunk => chunk.Content.Length));
    }

    [TestMethod]
    [DataRow("code")]
    [DataRow("table")]
    [DataRow("prose")]
    public void LargeSourceContentWithoutAmplification_RemainsSupported(string kind)
    {
        var content = new string('x', 200_000);
        var input = kind switch
        {
            "code" => "   ```text\n" + content + "\n   ```",
            "table" => "| " + content + " | Value |\n| --- | --- |\n| x | y |",
            _ => content
        };
        var chunks = Split(input, 80);
        Assert.AreEqual(input, string.Concat(chunks));
        if (kind != "prose") Assert.HasCount(1, chunks);
    }

    [TestMethod]
    [DataRow(20, false)]
    [DataRow(20, true)]
    [DataRow(1000, false)]
    [DataRow(1000, true)]
    public void IndentedFences_PreserveOpeningAndBodyIndentation(int size, bool heading)
    {
        foreach (var marker in new[] { "```", "~~~" })
        for (int indentation = 0; indentation <= 3; indentation++)
        {
            var indent = new string(' ', indentation);
            var code = $"{indent}{marker}python\n{indent}if True:\n{indent}    print(42)\n{indent}{marker}  ";
            var input = (heading ? "# Example\n" : "") + code;
            var expected = (heading ? "# Example\n\n" : "") + code;
            CollectionAssert.AreEqual(new[] { expected }, Split(input, size), $"indent={indentation}, marker={marker}");
        }
    }

    [TestMethod]
    [DataRow(20, false)]
    [DataRow(20, true)]
    [DataRow(1000, false)]
    [DataRow(1000, true)]
    public void UnclosedFences_PreserveTrailingCodeWhitespace(int size, bool heading)
    {
        const string code = "   ```text\n   significant trailing spaces  \n  \t \n\n";
        var input = (heading ? "# Example\n" : "") + code;
        var expected = (heading ? "# Example\n\n" : "") + code;
        CollectionAssert.AreEqual(new[] { expected }, Split(input, size));
    }

    [TestMethod]
    [Timeout(15000)]
    [DataRow(false)]
    [DataRow(true)]
    public void LongFenceAndManyShortLines_DoNotRescanTheOpeningFence(bool closed)
    {
        var fence = new string('`', 160_000);
        var code = fence + "\n" + string.Concat(Enumerable.Repeat("x\n", 160_000));
        if (closed) code += fence;
        var input = code + (closed ? "\n# Following\nbody" : "");
        var stopwatch = Stopwatch.StartNew();
        var chunks = Split(input, 100);
        stopwatch.Stop();

        Assert.AreEqual(code, chunks[0]);
        Assert.HasCount(closed ? 2 : 1, chunks);
        if (closed) Assert.AreEqual("# Following\n\nbody", chunks[1]);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"A sub-megabyte fence took {stopwatch.Elapsed}; the opening fence must not be reparsed per line.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void AncestorChange_EndsPreviousBreadcrumbScope(bool breadcrumbs)
    {
        var text = "# Company A\n## Policy\nOld policy.\n# Company B\nNew policy applies now.";
        var splitter = new MarkdownTextSplitter(30) { MinSplitHeadingLevel = 2, IncludeHeadingBreadcrumb = breadcrumbs };
        var chunks = splitter.Split(new RagDocument("companies", text, "companies.md"));
        var newPolicy = chunks.Single(c => c.Content.Contains("New policy applies now."));
        Assert.DoesNotContain("Company A", newPolicy.Content);
        Assert.DoesNotContain("## Policy", newPolicy.Content);
        Assert.IsTrue(chunks.Any(c => c.Content.Contains("Company B")));
        Assert.IsTrue(chunks.Any(c => c.Content.Contains("Old policy.")));
        if (breadcrumbs) Assert.Contains("# Company B", newPolicy.Content);
    }

    [TestMethod]
    public void MetadataAndIndexes_AreStableAndIndependent()
    {
        var doc = new RagDocument("manual", "# A\nfirst\n# B\nsecond", "manual.md");
        doc.Metadata["tenant"] = "a";
        var chunks = new MarkdownTextSplitter().Split(doc);
        Assert.HasCount(2, chunks);
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.AreEqual($"manual_chunk_{i}", chunks[i].Id);
            Assert.AreEqual(i, chunks[i].Index);
            Assert.AreEqual("manual", chunks[i].DocumentId);
            Assert.AreEqual("manual.md", chunks[i].Metadata["source"]);
        }
        chunks[0].Metadata["tenant"] = "changed";
        Assert.AreEqual("a", chunks[1].Metadata["tenant"]);
        Assert.AreEqual("a", doc.Metadata["tenant"]);
    }
}
