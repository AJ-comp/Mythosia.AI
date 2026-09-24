using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Rag.Search.Pixie;

// Implements this pinned tokenizer's NFC -> BertPreTokenizer -> WordPiece pipeline.
// Do not substitute a generic lowercasing BERT tokenizer: PIXIE preserves case and accents.
internal sealed class PixieTokenizer
{
    private readonly Dictionary<string, int> vocabulary;
    private readonly KeyValuePair<string, int>[] addedTokens;
    private readonly HashSet<int> specialIds;
    private readonly string[] tokens;

    internal PixieTokenizer(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        if (root.GetProperty("normalizer").GetProperty("type").GetString() != "NFC"
            || root.GetProperty("model").GetProperty("type").GetString() != "WordPiece"
            || root.GetProperty("model").GetProperty("continuing_subword_prefix").GetString() != "##")
            throw new InvalidDataException("Unsupported PIXIE tokenizer configuration.");
        vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        tokens = new string[50000];
        foreach (var entry in root.GetProperty("model").GetProperty("vocab").EnumerateObject())
        {
            var id = entry.Value.GetInt32();
            if (id < 0 || id >= tokens.Length || tokens[id] != null)
                throw new InvalidDataException("Invalid PIXIE tokenizer vocabulary.");
            vocabulary.Add(entry.Name, id);
            tokens[id] = entry.Name;
        }
        var added = new List<KeyValuePair<string, int>>();
        specialIds = new HashSet<int>();
        foreach (var entry in root.GetProperty("added_tokens").EnumerateArray())
        {
            var text = entry.GetProperty("content").GetString()!;
            var id = entry.GetProperty("id").GetInt32();
            if (id < 0 || id >= tokens.Length)
                throw new InvalidDataException("Invalid PIXIE added-token ID.");
            if (!entry.GetProperty("special").GetBoolean()
                || entry.GetProperty("normalized").GetBoolean()
                || entry.GetProperty("single_word").GetBoolean()
                || entry.GetProperty("lstrip").GetBoolean()
                || entry.GetProperty("rstrip").GetBoolean())
                throw new InvalidDataException("Unsupported PIXIE added-token behavior.");
            added.Add(new(text, id));
            specialIds.Add(id);
            // The official base WordPiece vocabulary contains 49,999 entries;
            // <pad> is added separately at ID 49,999. Added aliases are extracted
            // before WordPiece and must not change its original vocabulary.
            tokens[id] ??= text;
        }
        if (tokens.Any(token => token == null))
            throw new InvalidDataException("PIXIE requires a 50,000-token combined vocabulary.");
        addedTokens = added.OrderByDescending(x => x.Key.Length).ToArray();
    }

    internal bool IsSpecial(int tokenId) => specialIds.Contains(tokenId);

    internal string GetToken(int tokenId) => tokens[tokenId];

    internal long[] Encode(string text, int maxLength, CancellationToken cancellationToken)
    {
        var result = new List<long> { 0 };
        var plain = new StringBuilder();
        for (var offset = 0; offset < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyValuePair<string, int>? match = null;
            if (text[offset] == '<')
            {
                foreach (var item in addedTokens)
                {
                    if (text.AsSpan(offset).StartsWith(item.Key, StringComparison.Ordinal))
                    {
                        match = item;
                        break;
                    }
                }
            }
            if (match.HasValue)
            {
                AppendPlain(plain.ToString(), result, maxLength, cancellationToken);
                plain.Clear();
                result.Add(match.Value.Value);
                offset += match.Value.Key.Length;
                EnsureLength(result.Count, maxLength);
            }
            else
            {
                // Reject malformed UTF-16 rather than silently normalizing replacement characters.
                var rune = Rune.GetRuneAt(text, offset);
                plain.Append(rune.ToString());
                offset += rune.Utf16SequenceLength;
            }
        }
        AppendPlain(plain.ToString(), result, maxLength, cancellationToken);
        result.Add(1);
        return result.ToArray();
    }

    private void AppendPlain(string text, List<long> result, int maxLength, CancellationToken cancellationToken)
    {
        var word = new StringBuilder();
        foreach (var rune in text.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Rune.IsWhiteSpace(rune) || IsPunctuation(rune))
            {
                AppendWord(word.ToString(), result, maxLength);
                word.Clear();
                if (IsPunctuation(rune)) AppendWord(rune.ToString(), result, maxLength);
            }
            else word.Append(rune.ToString());
        }
        AppendWord(word.ToString(), result, maxLength);
    }

    private void AppendWord(string word, List<long> result, int maxLength)
    {
        if (word.Length == 0) return;
        var boundaries = new List<int> { 0 };
        var offset = 0;
        foreach (var rune in word.EnumerateRunes())
        {
            offset += rune.Utf16SequenceLength;
            boundaries.Add(offset);
        }
        var count = boundaries.Count - 1;
        if (count > 100)
        {
            result.Add(2);
            EnsureLength(result.Count, maxLength);
            return;
        }
        var pieces = new List<int>();
        for (var start = 0; start < count;)
        {
            var found = false;
            for (var end = count; end > start; end--)
            {
                var piece = (start > 0 ? "##" : "") + word.Substring(boundaries[start], boundaries[end] - boundaries[start]);
                if (!vocabulary.TryGetValue(piece, out var id)) continue;
                pieces.Add(id);
                start = end;
                found = true;
                break;
            }
            if (!found)
            {
                result.Add(2);
                EnsureLength(result.Count, maxLength);
                return;
            }
        }
        result.AddRange(pieces.Select(id => (long)id));
        EnsureLength(result.Count, maxLength);
    }

    private static void EnsureLength(int count, int maxLength)
    {
        if (count + 1 > maxLength)
            throw new ArgumentException($"Input exceeds the configured PIXIE limit of {maxLength} tokens including boundary tokens. Split long documents before indexing or explicitly increase MaxSequenceLength (maximum 5632).", "text");
    }

    private static bool IsPunctuation(Rune rune) => PixiePunctuation.IsPunctuation(rune);
}
