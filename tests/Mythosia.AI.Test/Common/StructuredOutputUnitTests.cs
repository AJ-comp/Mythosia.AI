using Mythosia.AI.Utilities;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// LLM 독립 단위 테스트 ? API 키 불필요, 프로바이더 무관
/// JsonSchemaGenerator, ExtractJsonFromResponse 등 순수 로직 검증
/// </summary>
[TestClass]
public class StructuredOutputUnitTests
{
    #region Test Models

    public class MathResult
    {
        public string Expression { get; set; } = "";
        public double Result { get; set; }
    }

    public class PersonProfile
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public AddressInfo Address { get; set; } = new();
    }

    public class AddressInfo
    {
        public string City { get; set; } = "";
        public string Country { get; set; } = "";
    }

    public class BookInfo
    {
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public int Year { get; set; }
        public List<string> Genres { get; set; } = new();
    }

    public class NullableFieldsModel
    {
        public string Name { get; set; } = "";
        public int? OptionalAge { get; set; }
        public string? NickName { get; set; }
    }

    public class DeepNestedModel
    {
        public string Id { get; set; } = "";
        public Level1 Data { get; set; } = new();
    }

    public class Level1
    {
        public string Value { get; set; } = "";
        public Level2 Inner { get; set; } = new();
    }

    public class Level2
    {
        public string DeepValue { get; set; } = "";
        public int Score { get; set; }
    }

    public class ObjectListModel
    {
        public string GroupName { get; set; } = "";
        public List<ListItem> Items { get; set; } = new();
    }

    public class ListItem
    {
        public string Label { get; set; } = "";
        public double Value { get; set; }
    }

    #endregion

    #region JsonSchemaGenerator Tests

    /// <summary>
    /// JsonSchemaGenerator가 올바른 JSON 스키마를 생성하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_SimpleType_ProducesValidJsonSchema()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(MathResult));

        Assert.IsNotNull(schema);
        Assert.IsTrue(schema.Length > 0, "Schema should not be empty");

        // 스키마가 유효한 JSON인지 검증
        var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        // properties 존재 확인
        Assert.IsTrue(root.TryGetProperty("properties", out var props),
            "Schema should have 'properties'");

        // MathResult의 Expression, Result 프로퍼티 확인
        Assert.IsTrue(props.TryGetProperty("expression", out _) || props.TryGetProperty("Expression", out _),
            "Schema should contain 'Expression' property");
        Assert.IsTrue(props.TryGetProperty("result", out _) || props.TryGetProperty("Result", out _),
            "Schema should contain 'Result' property");

        Console.WriteLine($"[Schema] MathResult:\n{schema}");
    }

    /// <summary>
    /// 중첩 객체 타입의 스키마 생성 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_NestedType_IncludesNestedProperties()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(PersonProfile));

        Assert.IsNotNull(schema);

        var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        Console.WriteLine($"[Schema] PersonProfile:\n{schema}");

        // address 관련 구조가 존재하는지 확인 (NJsonSchema는 definitions 또는 inline으로 생성)
        var schemaText = schema.ToLowerInvariant();
        Assert.IsTrue(schemaText.Contains("city") && schemaText.Contains("country"),
            "Schema should contain nested AddressInfo properties (city, country)");
    }

    /// <summary>
    /// 리스트 프로퍼티가 있는 타입의 스키마 생성 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_TypeWithList_IncludesArraySchema()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(BookInfo));

        Assert.IsNotNull(schema);

        var schemaText = schema.ToLowerInvariant();
        Assert.IsTrue(schemaText.Contains("array"),
            "Schema should contain 'array' type for List<string> Genres property");

        Console.WriteLine($"[Schema] BookInfo:\n{schema}");
    }

    /// <summary>
    /// 루트 레벨에서 required 배열이 모든 프로퍼티를 포함하는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_SimpleType_RequiredIncludesAllProperties()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(MathResult));
        var root = JsonDocument.Parse(schema).RootElement;

        Assert.IsTrue(root.TryGetProperty("required", out var required), "Schema must have 'required'");
        var requiredList = new List<string>();
        foreach (var item in required.EnumerateArray())
            requiredList.Add(item.GetString()!);

        var props = root.GetProperty("properties");
        foreach (var prop in props.EnumerateObject())
        {
            Assert.IsTrue(requiredList.Contains(prop.Name),
                $"Property '{prop.Name}' must be in required array");
        }
    }

    /// <summary>
    /// 루트 레벨에서 additionalProperties가 false인지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_SimpleType_AdditionalPropertiesIsFalse()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(MathResult));
        var root = JsonDocument.Parse(schema).RootElement;

        Assert.IsTrue(root.TryGetProperty("additionalProperties", out var ap),
            "Schema must have 'additionalProperties'");
        Assert.IsFalse(ap.GetBoolean(), "additionalProperties must be false");
    }

    /// <summary>
    /// $schema 필드가 제거되었는지 검증 (OpenAI 비호환)
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_NoSchemaField()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(MathResult));
        var root = JsonDocument.Parse(schema).RootElement;

        Assert.IsFalse(root.TryGetProperty("$schema", out _),
            "Schema must not contain '$schema' field");
    }

    /// <summary>
    /// 중첩 객체에서 definitions가 $defs로 변환되고 $ref 경로가 수정되었는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_NestedType_UsesDefsNotDefinitions()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(PersonProfile));
        var root = JsonDocument.Parse(schema).RootElement;

        Assert.IsFalse(root.TryGetProperty("definitions", out _),
            "Schema must not contain 'definitions' (draft-04)");

        if (root.TryGetProperty("$defs", out _))
        {
            // $ref 경로가 #/$defs/를 사용하는지 확인
            Assert.IsFalse(schema.Contains("#/definitions/"),
                "$ref paths must use '#/$defs/' not '#/definitions/'");
        }
    }

    /// <summary>
    /// $defs 내부 객체에도 required와 additionalProperties가 적용되었는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_NestedType_DefsHaveRequiredAndAdditionalProperties()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(PersonProfile));
        var root = JsonDocument.Parse(schema).RootElement;

        if (!root.TryGetProperty("$defs", out var defs)) return;

        foreach (var def in defs.EnumerateObject())
        {
            var defObj = def.Value;
            Assert.IsTrue(defObj.TryGetProperty("required", out var req),
                $"$defs/{def.Name} must have 'required'");
            Assert.IsTrue(defObj.TryGetProperty("additionalProperties", out var ap),
                $"$defs/{def.Name} must have 'additionalProperties'");
            Assert.IsFalse(ap.GetBoolean(),
                $"$defs/{def.Name} additionalProperties must be false");

            // required가 모든 프로퍼티를 포함하는지
            if (defObj.TryGetProperty("properties", out var props))
            {
                var reqList = new List<string>();
                foreach (var r in req.EnumerateArray()) reqList.Add(r.GetString()!);
                foreach (var p in props.EnumerateObject())
                    Assert.IsTrue(reqList.Contains(p.Name),
                        $"$defs/{def.Name}: property '{p.Name}' must be in required");
            }
        }
    }

    /// <summary>
    /// 3단계 깊은 중첩에서도 모든 레벨에 required/additionalProperties 적용 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_DeepNesting_AllLevelsHaveRequiredAndAdditionalProperties()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(DeepNestedModel));
        var root = JsonDocument.Parse(schema).RootElement;

        // 모든 $defs 항목 검증
        if (root.TryGetProperty("$defs", out var defs))
        {
            foreach (var def in defs.EnumerateObject())
            {
                Assert.IsTrue(def.Value.TryGetProperty("required", out _),
                    $"$defs/{def.Name} must have 'required'");
                Assert.IsTrue(def.Value.TryGetProperty("additionalProperties", out var ap),
                    $"$defs/{def.Name} must have 'additionalProperties'");
                Assert.IsFalse(ap.GetBoolean());
            }
        }

        Console.WriteLine($"[Schema] DeepNestedModel:\n{schema}");
    }

    /// <summary>
    /// nullable 프로퍼티도 required에 포함되는지 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_NullableFields_AllInRequired()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(NullableFieldsModel));
        var root = JsonDocument.Parse(schema).RootElement;

        var required = root.GetProperty("required");
        var reqList = new List<string>();
        foreach (var r in required.EnumerateArray()) reqList.Add(r.GetString()!);

        var props = root.GetProperty("properties");
        foreach (var prop in props.EnumerateObject())
        {
            Assert.IsTrue(reqList.Contains(prop.Name),
                $"Nullable property '{prop.Name}' must also be in required array");
        }

        Console.WriteLine($"[Schema] NullableFieldsModel:\n{schema}");
    }

    /// <summary>
    /// List&lt;object&gt; 아이템이 객체인 경우 items 내부에도 required/additionalProperties 적용 검증
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void SchemaGeneration_ObjectList_ItemDefsHaveRequiredAndAdditionalProperties()
    {
        var schema = JsonSchemaGenerator.Generate(typeof(ObjectListModel));
        var root = JsonDocument.Parse(schema).RootElement;

        // $defs 내부의 ListItem 정의 검증
        if (root.TryGetProperty("$defs", out var defs))
        {
            foreach (var def in defs.EnumerateObject())
            {
                if (def.Value.TryGetProperty("properties", out _))
                {
                    Assert.IsTrue(def.Value.TryGetProperty("required", out _),
                        $"$defs/{def.Name} must have 'required'");
                    Assert.IsTrue(def.Value.TryGetProperty("additionalProperties", out var ap),
                        $"$defs/{def.Name} must have 'additionalProperties'");
                    Assert.IsFalse(ap.GetBoolean());
                }
            }
        }

        Console.WriteLine($"[Schema] ObjectListModel:\n{schema}");
    }

    #endregion

    #region ExtractJsonFromResponse Tests

    /// <summary>
    /// 마크다운 코드 블록으로 감싸진 JSON 추출 테스트
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void ExtractJson_MarkdownCodeBlock_ExtractsCorrectly()
    {
        var input = "```json\n{\"city\": \"Seoul\", \"temperature\": 22}\n```";
        var result = InvokeExtractJson(input);

        Assert.IsNotNull(result);
        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("Seoul", doc.RootElement.GetProperty("city").GetString());
    }

    /// <summary>
    /// 마크다운 없이 순수 JSON인 경우 테스트
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void ExtractJson_PureJson_ReturnsSame()
    {
        var input = "{\"expression\": \"2+3\", \"result\": 5}";
        var result = InvokeExtractJson(input);

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual(5, doc.RootElement.GetProperty("result").GetDouble());
    }

    /// <summary>
    /// JSON 앞뒤에 텍스트가 있는 경우 추출 테스트
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void ExtractJson_SurroundingText_ExtractsJsonOnly()
    {
        var input = "Here is the result:\n{\"name\": \"Test\", \"age\": 30}\nHope this helps!";
        var result = InvokeExtractJson(input);

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("Test", doc.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(30, doc.RootElement.GetProperty("age").GetInt32());
    }

    /// <summary>
    /// 코드블록(언어 태그 없이) 감싸진 JSON 추출 테스트
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public void ExtractJson_CodeBlockNoLanguageTag_ExtractsCorrectly()
    {
        var input = "```\n{\"title\": \"Dune\", \"year\": 1965}\n```";
        var result = InvokeExtractJson(input);

        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("Dune", doc.RootElement.GetProperty("title").GetString());
    }

    /// <summary>
    /// Reflection을 통해 private static ExtractJsonFromResponse 호출
    /// </summary>
    private static string InvokeExtractJson(string input)
    {
        var method = typeof(Mythosia.AI.Services.Base.AIService)
            .GetMethod("ExtractJsonFromResponse",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.IsNotNull(method, "ExtractJsonFromResponse method should exist");
        return (string)method!.Invoke(null, new object[] { input })!;
    }

    #endregion
}
