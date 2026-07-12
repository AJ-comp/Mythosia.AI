namespace Mythosia.AI.Serving.Vllm.Tests;

[TestClass]
public class PrometheusTextParserTests
{
    [TestMethod]
    public void Parse_SimpleSampleWithoutLabels()
    {
        var families = PrometheusTextParser.Parse("process_cpu_seconds_total 12.5\n");

        var sample = families["process_cpu_seconds_total"][0];
        Assert.AreEqual(12.5, sample.Value);
        Assert.AreEqual(0, sample.Labels.Count);
    }

    [TestMethod]
    public void Parse_ColonNamesAndLabelsPreserved()
    {
        var families = PrometheusTextParser.Parse(
            """vllm:num_requests_running{model_name="alias-a",engine="0"} 2""");

        var sample = families["vllm:num_requests_running"][0];
        Assert.AreEqual(2.0, sample.Value);
        Assert.AreEqual("alias-a", sample.Labels["model_name"]);
        Assert.AreEqual("0", sample.Labels["engine"]);
    }

    [TestMethod]
    public void Parse_MultipleSamplesGroupIntoOneFamily()
    {
        var families = PrometheusTextParser.Parse("""
            vllm:num_requests_running{engine="0"} 1
            vllm:num_requests_running{engine="1"} 4
            """);

        Assert.AreEqual(2, families["vllm:num_requests_running"].Count);
    }

    [TestMethod]
    public void Parse_EscapedLabelValues()
    {
        var families = PrometheusTextParser.Parse(
            "m{path=\"C:\\\\models\\\\x\",quote=\"a\\\"b\",nl=\"a\\nb\"} 1");

        var labels = families["m"][0].Labels;
        Assert.AreEqual("C:\\models\\x", labels["path"]);
        Assert.AreEqual("a\"b", labels["quote"]);
        Assert.AreEqual("a\nb", labels["nl"]);
    }

    [TestMethod]
    public void Parse_SpecialValues()
    {
        var families = PrometheusTextParser.Parse("""
            a +Inf
            b -Inf
            c NaN
            d 1.5e3
            """);

        Assert.AreEqual(double.PositiveInfinity, families["a"][0].Value);
        Assert.AreEqual(double.NegativeInfinity, families["b"][0].Value);
        Assert.IsTrue(double.IsNaN(families["c"][0].Value));
        Assert.AreEqual(1500.0, families["d"][0].Value);
    }

    [TestMethod]
    public void Parse_SkipsCommentsBlanksAndMalformedLines()
    {
        var families = PrometheusTextParser.Parse("""
            # HELP something helpful
            # TYPE vllm:x gauge

            vllm:x{unterminated="oops 1
            vllm:x not-a-number
            vllm:x 7
            """);

        Assert.AreEqual(1, families.Count);
        Assert.AreEqual(1, families["vllm:x"].Count);
        Assert.AreEqual(7.0, families["vllm:x"][0].Value);
    }

    [TestMethod]
    public void Parse_IgnoresTrailingTimestamp()
    {
        var families = PrometheusTextParser.Parse("vllm:x{engine=\"0\"} 3.14 1752300000000");

        Assert.AreEqual(3.14, families["vllm:x"][0].Value);
    }

    [TestMethod]
    public void Parse_EmptyOrNullInput_ReturnsEmpty()
    {
        Assert.AreEqual(0, PrometheusTextParser.Parse(null).Count);
        Assert.AreEqual(0, PrometheusTextParser.Parse("").Count);
    }
}
