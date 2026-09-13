using Mythosia.AI.Models.Functions;
using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialThirdSnapshotTests
{
    [TestMethod]
    [DataRow("ReadOnlyCollection", false)]
    [DataRow("ReadOnlyCollection", true)]
    [DataRow("ReadOnlyDictionary", false)]
    [DataRow("ReadOnlyDictionary", true)]
    public void ReadOnlyBclChildrenKeepTheirTypeInsideTypedContainers(string kind, bool array)
    {
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        var list = new List<JsonElement> { document.RootElement };
        var dictionary = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["TITLE"] = document.RootElement
        };
        var readOnlyList = new ReadOnlyCollection<JsonElement>(list);
        var readOnlyDictionary = new ReadOnlyDictionary<string, JsonElement>(dictionary);
        object container = (kind, array) switch
        {
            ("ReadOnlyCollection", true) => new[] { readOnlyList },
            ("ReadOnlyCollection", false) => new Dictionary<string, ReadOnlyCollection<JsonElement>> { ["row"] = readOnlyList },
            ("ReadOnlyDictionary", true) => new[] { readOnlyDictionary },
            _ => new Dictionary<string, ReadOnlyDictionary<string, JsonElement>> { ["row"] = readOnlyDictionary }
        };
        var original = WithValue(container);

        var snapshot = original.Clone();
        document.Dispose();
        list.Clear();
        dictionary.Clear();

        Check(snapshot);
        Check(snapshot.Clone());

        void Check(FunctionCall call)
        {
            var copied = call.Arguments["value"];
            Assert.AreEqual(container.GetType(), copied.GetType());
            var value = (kind, array) switch
            {
                ("ReadOnlyCollection", true) => ((ReadOnlyCollection<JsonElement>[])copied)[0][0],
                ("ReadOnlyCollection", false) => ((Dictionary<string, ReadOnlyCollection<JsonElement>>)copied)["row"][0],
                ("ReadOnlyDictionary", true) => ((ReadOnlyDictionary<string, JsonElement>[])copied)[0]["title"],
                _ => ((Dictionary<string, ReadOnlyDictionary<string, JsonElement>>)copied)["row"]["title"]
            };
            Assert.AreEqual("captured", value.GetProperty("name").GetString());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReadOnlyViewsPreserveCyclesAndSharedBacking(bool dictionary)
    {
        var list = new List<object>();
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        object view;
        object backing;
        if (dictionary)
        {
            view = new ReadOnlyDictionary<string, object>(map);
            map["SELF"] = view;
            backing = map;
        }
        else
        {
            view = new ReadOnlyCollection<object>(list);
            list.Add(view);
            backing = list;
        }
        var original = new FunctionCall
        {
            Name = "inspect",
            Arguments = new Dictionary<string, object> { ["view"] = view, ["backing"] = backing }
        };
        var snapshot = original.Clone();
        list.Clear();
        map.Clear();

        if (dictionary)
        {
            var copiedView = (ReadOnlyDictionary<string, object>)snapshot.Arguments["view"];
            var copiedBacking = (Dictionary<string, object>)snapshot.Arguments["backing"];
            Assert.AreSame(copiedView, copiedView["self"]);
            copiedBacking["new"] = "shared";
            Assert.AreEqual("shared", copiedView["NEW"]);
        }
        else
        {
            var copiedView = (ReadOnlyCollection<object>)snapshot.Arguments["view"];
            var copiedBacking = (List<object>)snapshot.Arguments["backing"];
            Assert.AreSame(copiedView, copiedView[0]);
            copiedBacking.Add("shared");
            Assert.AreEqual("shared", copiedView[1]);
        }
    }

    [TestMethod]
    [DataRow("Hashtable")]
    [DataRow("SortedList")]
    public void NonGenericBclDictionariesKeepTheirLookupComparer(string kind)
    {
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        IDictionary source = kind == "Hashtable"
            ? new Hashtable(StringComparer.OrdinalIgnoreCase)
            : new SortedList(StringComparer.OrdinalIgnoreCase);
        source["TITLE"] = document.RootElement;
        var snapshot = WithValue(source).Clone();
        document.Dispose();
        source.Clear();

        Check(snapshot);
        Check(snapshot.Clone());

        void Check(FunctionCall call)
        {
            var copied = (IDictionary)call.Arguments["value"];
            Assert.AreEqual(source.GetType(), copied.GetType());
            Assert.IsTrue(copied.Contains("title"), "Copying a standard dictionary must preserve its lookup rules.");
            Assert.AreEqual("captured", ((JsonElement)copied["title"]!).GetProperty("name").GetString());
        }
    }

    private static FunctionCall WithValue(object value) => new()
    {
        Name = "inspect",
        Arguments = new Dictionary<string, object> { ["value"] = value }
    };
}
