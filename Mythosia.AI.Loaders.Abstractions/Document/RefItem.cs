using System;

namespace Mythosia.AI.Loaders.Document
{
    /// <summary>
    /// A JSON-pointer style reference to another item in the document.
    /// Follows the docling RefItem convention (e.g. "#/texts/0", "#/tables/1").
    /// </summary>
    public class RefItem : IEquatable<RefItem>
    {
        /// <summary>
        /// The JSON pointer reference string (e.g. "#/texts/0").
        /// </summary>
        public string Ref { get; set; } = string.Empty;

        public RefItem() { }

        public RefItem(string @ref)
        {
            Ref = @ref;
        }

        /// <summary>
        /// Resolves this reference against the given document, returning the referenced NodeItem.
        /// </summary>
        public NodeItem? Resolve(DoclingDocument doc)
        {
            if (string.IsNullOrEmpty(Ref) || doc == null)
                return null;

            var parts = Ref.Split('/');
            // Expected format: "#/collection/index" or "#/body" or "#/furniture"
            if (parts.Length == 2)
            {
                var path = parts[1];
                if (path == "body") return doc.Body;
                if (path == "furniture") return doc.Furniture;
            }
            else if (parts.Length == 3)
            {
                var path = parts[1];
                if (!int.TryParse(parts[2], out var index))
                    return null;

                switch (path)
                {
                    case "texts":
                        return index >= 0 && index < doc.Texts.Count ? doc.Texts[index] : null;
                    case "tables":
                        return index >= 0 && index < doc.Tables.Count ? doc.Tables[index] : null;
                    case "pictures":
                        return index >= 0 && index < doc.Pictures.Count ? doc.Pictures[index] : null;
                    case "groups":
                        return index >= 0 && index < doc.Groups.Count ? doc.Groups[index] : null;
                }
            }

            return null;
        }

        public bool Equals(RefItem? other)
        {
            if (other is null) return false;
            return Ref == other.Ref;
        }

        public override bool Equals(object? obj) => Equals(obj as RefItem);
        public override int GetHashCode() => Ref?.GetHashCode() ?? 0;
        public override string ToString() => Ref;
    }
}
