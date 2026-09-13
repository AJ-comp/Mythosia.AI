namespace Mythosia.AI.Models.Images
{
    /// <summary>Requested image quality. Each provider validates the levels its model supports.</summary>
    public enum ImageQuality
    {
        Auto = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        XHigh = 4,
        Max = 5
    }

    /// <summary>Requested image background. Explicit backgrounds require provider support.</summary>
    public enum ImageBackground
    {
        Auto = 0,
        Opaque = 1,
        Transparent = 2
    }

    /// <summary>
    /// Requested output encoding. Auto uses the provider default; inspect the returned image's MediaType.
    /// An explicit encoding is accepted only by providers with a corresponding encoding control.
    /// </summary>
    public enum ImageOutputFormat
    {
        Auto = 0,
        Png = 1,
        Jpeg = 2,
        WebP = 3
    }

    /// <summary>Whether an image size is automatic, exact pixels, or a provider resolution preset.</summary>
    public enum ImageSizeKind
    {
        Auto = 0,
        Pixels = 1,
        Preset = 2
    }

    /// <summary>Provider resolution grades, which do not guarantee exact pixel dimensions.</summary>
    public enum ImageResolution
    {
        Auto = 0,
        FiveTwelve = 1,
        OneK = 2,
        TwoK = 3,
        FourK = 4
    }

    /// <summary>Aspect ratios for provider resolution presets. Supported ratios depend on the provider.</summary>
    public enum ImageAspectRatio
    {
        Auto = 0,
        OneByOne = 1,
        TwoByThree = 2,
        ThreeByTwo = 3,
        ThreeByFour = 4,
        FourByThree = 5,
        FourByFive = 6,
        FiveByFour = 7,
        NineBySixteen = 8,
        SixteenByNine = 9,
        TwentyOneByNine = 10,
        OneByEight = 11,
        EightByOne = 12,
        OneByFour = 13,
        FourByOne = 14,
        OneByTwo = 15,
        TwoByOne = 16,
        FiveByTwo = 17,
        NineByNineteenPointFive = 18,
        NineteenPointFiveByNine = 19,
        NineByTwenty = 20,
        TwentyByNine = 21
    }
}
