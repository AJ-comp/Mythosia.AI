using System.Text.Json;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    #region Test Models for Structured Output

    public class WeatherResponse
    {
        public string City { get; set; } = "";
        public double Temperature { get; set; }
        public string Unit { get; set; } = "";
        public string Condition { get; set; } = "";
    }

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

    public class CountryListResponse
    {
        public List<CountryItem> Countries { get; set; } = new();
    }

    public class CountryItem
    {
        public string Name { get; set; } = "";
        public string Capital { get; set; } = "";
    }

    #endregion

    #region Integration Tests ? 실제 API 호출

    /// <summary>
    /// 기본 구조화된 출력 테스트 ? 간단한 DTO
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task StructuredOutput_SimpleDto_ReturnsDeserializedObject()
    {
        await RunIfSupported(
            () => SupportsStructuredOutput(),
            async () =>
            {
                var result = await AI.GetCompletionAsync<MathResult>(
                    "What is 15 + 27? Return the expression and result.");

                Assert.IsNotNull(result);
                Assert.AreEqual(42, result.Result, 0.01,
                    "15 + 27 should equal 42");
                Assert.IsFalse(string.IsNullOrEmpty(result.Expression),
                    "Expression should not be empty");

                Console.WriteLine($"[StructuredOutput Simple] Expression={result.Expression}, Result={result.Result}");
            },
            "StructuredOutput");
    }

    /// <summary>
    /// 중첩 객체 구조화된 출력 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task StructuredOutput_NestedObject_ReturnsNestedProperties()
    {
        await RunIfSupported(
            () => SupportsStructuredOutput(),
            async () =>
            {
                var result = await AI.GetCompletionAsync<PersonProfile>(
                    "Create a fictional person profile. Name: John Doe, Age: 30, lives in Seoul, South Korea.");

                Assert.IsNotNull(result);
                Assert.IsFalse(string.IsNullOrEmpty(result.Name), "Name should not be empty");
                Assert.IsTrue(result.Age > 0, "Age should be positive");
                Assert.IsNotNull(result.Address, "Address should not be null");
                Assert.IsFalse(string.IsNullOrEmpty(result.Address.City), "City should not be empty");
                Assert.IsFalse(string.IsNullOrEmpty(result.Address.Country), "Country should not be empty");

                Console.WriteLine($"[StructuredOutput Nested] Name={result.Name}, Age={result.Age}, " +
                    $"City={result.Address.City}, Country={result.Address.Country}");
            },
            "StructuredOutput");
    }

    /// <summary>
    /// 리스트 포함 구조화된 출력 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task StructuredOutput_WithList_ReturnsPopulatedList()
    {
        await RunIfSupported(
            () => SupportsStructuredOutput(),
            async () =>
            {
                var result = await AI.GetCompletionAsync<CountryListResponse>(
                    "List exactly 3 countries in Asia with their capitals.");

                Assert.IsNotNull(result);
                Assert.IsNotNull(result.Countries, "Countries list should not be null");
                Assert.AreEqual(3, result.Countries.Count,
                    "Should return exactly 3 countries");

                foreach (var country in result.Countries)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(country.Name),
                        "Country name should not be empty");
                    Assert.IsFalse(string.IsNullOrEmpty(country.Capital),
                        "Capital should not be empty");
                    Console.WriteLine($"  {country.Name} ? {country.Capital}");
                }

                Console.WriteLine($"[StructuredOutput List] {result.Countries.Count} countries returned");
            },
            "StructuredOutput");
    }

    /// <summary>
    /// 시스템 메시지가 설정된 상태에서 구조화된 출력이 함께 동작하는지 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task StructuredOutput_WithSystemMessage_PreservesSystemMessage()
    {
        await RunIfSupported(
            () => SupportsStructuredOutput(),
            async () =>
            {
                AI.ActivateChat.SystemMessage = "You are a weather expert. Always use Celsius.";

                var result = await AI.GetCompletionAsync<WeatherResponse>(
                    "What is the typical summer weather in Tokyo?");

                Assert.IsNotNull(result);
                Assert.IsFalse(string.IsNullOrEmpty(result.City), "City should not be empty");
                Assert.IsFalse(string.IsNullOrEmpty(result.Condition), "Condition should not be empty");

                Console.WriteLine($"[StructuredOutput + SystemMsg] City={result.City}, " +
                    $"Temp={result.Temperature}, Unit={result.Unit}, Condition={result.Condition}");
            },
            "StructuredOutput");
    }

    /// <summary>
    /// 구조화된 출력 후 일반 호출이 정상 동작하는지 테스트 (스키마 정리 확인)
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("StructuredOutput")]
    [TestMethod]
    public async Task StructuredOutput_ThenNormalCall_SchemaIsCleared()
    {
        await RunIfSupported(
            () => SupportsStructuredOutput(),
            async () =>
            {
                // 1) 구조화된 출력 호출
                var structured = await AI.GetCompletionAsync<MathResult>(
                    "What is 10 + 5?");
                Assert.IsNotNull(structured);
                Console.WriteLine($"[Structured] {structured.Expression} = {structured.Result}");

                // 2) 일반 텍스트 호출 ? JSON 강제 없이 정상 응답해야 함
                var normalResponse = await AI.GetCompletionAsync(
                    "Say hello in Korean.");
                Assert.IsNotNull(normalResponse);
                Assert.IsTrue(normalResponse.Length > 0, "Normal response should not be empty");
                Console.WriteLine($"[Normal after Structured] {normalResponse}");
            },
            "StructuredOutput");
    }

    #endregion
}
