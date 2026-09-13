using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using System.Reflection;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class ImageOptionsContractTests
{
    [TestMethod]
    public void NewGenerationAndEditRequests_UseProviderAutomaticDefaults()
    {
        foreach (var request in new ImageGenerationRequest[] { new(), new ImageEditRequest() })
        {
            Assert.AreEqual(ImageQuality.Auto, request.Quality);
            Assert.AreEqual(ImageBackground.Auto, request.Background);
            Assert.AreEqual(ImageOutputFormat.Auto, request.OutputFormat);
            Assert.AreSame(ImageSize.Auto, request.Size);
            Assert.AreEqual(1, request.Count);
            Assert.IsNull(request.OutputCompression);
        }
    }

    [TestMethod]
    public void PublicEnums_HaveStableNamedOrdinalsAndLiveInAbstractions()
    {
        AssertEnum<ImageQuality>("Auto", "Low", "Medium", "High", "XHigh", "Max");
        AssertEnum<ImageBackground>("Auto", "Opaque", "Transparent");
        AssertEnum<ImageOutputFormat>("Auto", "Png", "Jpeg", "WebP");
        AssertEnum<ImageSizeKind>("Auto", "Pixels", "Preset");
        AssertEnum<ImageResolution>("Auto", "FiveTwelve", "OneK", "TwoK", "FourK");
        AssertEnum<ImageAspectRatio>("Auto", "OneByOne", "TwoByThree", "ThreeByTwo", "ThreeByFour", "FourByThree",
            "FourByFive", "FiveByFour", "NineBySixteen", "SixteenByNine", "TwentyOneByNine", "OneByEight", "EightByOne",
            "OneByFour", "FourByOne", "OneByTwo", "TwoByOne", "FiveByTwo", "NineByNineteenPointFive",
            "NineteenPointFiveByNine", "NineByTwenty", "TwentyByNine");
    }

    [TestMethod]
    public void SizingModes_KeepPixelsAndPresetIntentSeparate()
    {
        var automatic = ImageSize.Auto;
        Assert.AreEqual(ImageSizeKind.Auto, automatic.Kind);
        Assert.AreEqual(0, automatic.Width);
        Assert.AreEqual(0, automatic.Height);
        Assert.AreEqual(ImageResolution.Auto, automatic.Resolution);
        Assert.AreEqual(ImageAspectRatio.Auto, automatic.AspectRatio);

        var pixels = ImageSize.Pixels(1536, 1024);
        Assert.AreEqual(ImageSizeKind.Pixels, pixels.Kind);
        Assert.AreEqual(1536, pixels.Width);
        Assert.AreEqual(1024, pixels.Height);
        Assert.AreEqual(ImageResolution.Auto, pixels.Resolution);
        Assert.AreEqual(ImageAspectRatio.Auto, pixels.AspectRatio);

        var preset = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo);
        Assert.AreEqual(ImageSizeKind.Preset, preset.Kind);
        Assert.AreEqual(ImageResolution.TwoK, preset.Resolution);
        Assert.AreEqual(ImageAspectRatio.ThreeByTwo, preset.AspectRatio);
        Assert.AreEqual(0, preset.Width);
        Assert.AreEqual(0, preset.Height);
    }

    [TestMethod]
    public void Preset_CanExpressResolutionOnlyRatioOnlyAndExplicitAutomaticPreset()
    {
        var resolutionOnly = ImageSize.Preset(ImageResolution.FourK);
        Assert.AreEqual(ImageResolution.FourK, resolutionOnly.Resolution);
        Assert.AreEqual(ImageAspectRatio.Auto, resolutionOnly.AspectRatio);

        var ratioOnly = ImageSize.Preset(ImageResolution.Auto, ImageAspectRatio.SixteenByNine);
        Assert.AreEqual(ImageResolution.Auto, ratioOnly.Resolution);
        Assert.AreEqual(ImageAspectRatio.SixteenByNine, ratioOnly.AspectRatio);

        var automaticPreset = ImageSize.Preset(ImageResolution.Auto);
        Assert.AreEqual(ImageSizeKind.Preset, automaticPreset.Kind,
            "Explicit preset intent must not silently turn into the unrelated automatic sizing mode.");
        Assert.AreEqual(ImageResolution.Auto, automaticPreset.Resolution);
        Assert.AreEqual(ImageAspectRatio.Auto, automaticPreset.AspectRatio);
    }

    [TestMethod]
    public void ImageSize_CannotBeMutatedSubclassedOrCreatedWithConflictingModes()
    {
        var type = typeof(ImageSize);
        Assert.IsTrue(type.IsSealed);
        Assert.IsEmpty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.IsEmpty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Assert.IsNull(property.SetMethod, $"{property.Name} must remain immutable.");

        var factories = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName).ToArray();
        CollectionAssert.AreEquivalent(new[] { "Pixels", "Preset" }, factories.Select(method => method.Name).ToArray());
        Assert.IsFalse(factories.Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string))));
        Assert.IsNull(type.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static));
        Assert.IsNull(type.GetMethod("op_Explicit", BindingFlags.Public | BindingFlags.Static));
    }

    [TestMethod]
    [DataRow(0, 1024, "width")]
    [DataRow(-1, 1024, "width")]
    [DataRow(int.MinValue, 1024, "width")]
    [DataRow(1024, 0, "height")]
    [DataRow(1024, -1, "height")]
    [DataRow(1024, int.MinValue, "height")]
    public void Pixels_RejectsNonPositiveDimensionsAtConstruction(int width, int height, string parameter)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ImageSize.Pixels(width, height));
        Assert.AreEqual(parameter, error.ParamName);
    }

    [TestMethod]
    public void Pixels_DoesNotApplyOneProvidersResolutionLimitsToTheSharedContract()
    {
        var small = ImageSize.Pixels(1, 1);
        var large = ImageSize.Pixels(int.MaxValue, int.MaxValue);
        Assert.AreEqual(1, small.Width);
        Assert.AreEqual(1, small.Height);
        Assert.AreEqual(int.MaxValue, large.Width);
        Assert.AreEqual(int.MaxValue, large.Height);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(999)]
    public void Preset_RejectsUndefinedResolutionAndRatioAtConstruction(int invalid)
    {
        var resolutionError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ImageSize.Preset((ImageResolution)invalid));
        var ratioError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ImageSize.Preset(ImageResolution.TwoK, (ImageAspectRatio)invalid));
        Assert.AreEqual("resolution", resolutionError.ParamName);
        Assert.AreEqual("aspectRatio", ratioError.ParamName);
    }

    private static void AssertEnum<T>(params string[] expectedNames) where T : struct, Enum
    {
        Assert.AreSame(typeof(IAIService).Assembly, typeof(T).Assembly);
        CollectionAssert.AreEqual(expectedNames, Enum.GetNames<T>());
        CollectionAssert.AreEqual(Enumerable.Range(0, expectedNames.Length).ToArray(), Enum.GetValues<T>().Select(value => Convert.ToInt32(value)).ToArray());
    }
}
