using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Mythosia.Documents.Tests.Office.PowerPoint;

/// <summary>
/// Builds .pptx files programmatically using OpenXml SDK for parser testing.
/// </summary>
internal static class PowerPointTestHelper
{
    public static TempDir CreateTempDir() => new TempDir();

    /// <summary>
    /// Creates a single-slide .pptx where the slide's ShapeTree contains the supplied
    /// shape descriptors in document order. Each descriptor is either a text body or a
    /// table, allowing tests to verify that the parser preserves the in-document ordering.
    /// </summary>
    public static string CreateSlideWith(
        string directory,
        string filename,
        IEnumerable<ShapeSpec> shapes)
    {
        var path = Path.Combine(directory, filename);
        using (var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
        {
            var presPart = doc.AddPresentationPart();
            presPart.Presentation = new P.Presentation();

            // Slide master + layout (minimal viable structure)
            var masterPart = presPart.AddNewPart<SlideMasterPart>();
            masterPart.SlideMaster = new SlideMaster(
                new CommonSlideData(new ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new A.TransformGroup()))),
                new ColorMap
                {
                    Background1 = A.ColorSchemeIndexValues.Light1,
                    Text1 = A.ColorSchemeIndexValues.Dark1,
                    Background2 = A.ColorSchemeIndexValues.Light2,
                    Text2 = A.ColorSchemeIndexValues.Dark2,
                    Accent1 = A.ColorSchemeIndexValues.Accent1,
                    Accent2 = A.ColorSchemeIndexValues.Accent2,
                    Accent3 = A.ColorSchemeIndexValues.Accent3,
                    Accent4 = A.ColorSchemeIndexValues.Accent4,
                    Accent5 = A.ColorSchemeIndexValues.Accent5,
                    Accent6 = A.ColorSchemeIndexValues.Accent6,
                    Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                    FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
                });

            var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
            layoutPart.SlideLayout = new SlideLayout(
                new CommonSlideData(new ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(new A.TransformGroup()))));
            masterPart.AddPart(layoutPart);

            // Slide
            var slidePart = presPart.AddNewPart<SlidePart>();
            slidePart.AddPart(layoutPart);

            var shapeTree = new ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new A.TransformGroup()));

            uint nextId = 2;
            foreach (var spec in shapes)
            {
                shapeTree.AppendChild(spec.Build(nextId));
                nextId++;
            }

            slidePart.Slide = new Slide(new CommonSlideData(shapeTree),
                new ColorMapOverride(new A.MasterColorMapping()));

            // Wire up presentation
            var slideId = new SlideId { Id = 256U, RelationshipId = presPart.GetIdOfPart(slidePart) };
            presPart.Presentation.SlideIdList = new SlideIdList(slideId);
            presPart.Presentation.SlideMasterIdList = new SlideMasterIdList(
                new SlideMasterId { Id = 2147483648U, RelationshipId = presPart.GetIdOfPart(masterPart) });
            presPart.Presentation.SlideSize = new SlideSize { Cx = 9144000, Cy = 6858000 };
            presPart.Presentation.NotesSize = new NotesSize { Cx = 6858000, Cy = 9144000 };
        }
        return path;
    }

    // -----------------------------------------------------------------
    //  Shape specs
    // -----------------------------------------------------------------

    public abstract class ShapeSpec
    {
        public abstract OpenXmlElement Build(uint id);
    }

    public sealed class TextBoxSpec : ShapeSpec
    {
        public string Text { get; }
        public bool IsTitle { get; }

        public TextBoxSpec(string text, bool isTitle = false)
        {
            Text = text;
            IsTitle = isTitle;
        }

        public override OpenXmlElement Build(uint id)
        {
            var nvSpPr = new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"Text{id}" },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                IsTitle
                    ? new ApplicationNonVisualDrawingProperties(
                        new PlaceholderShape { Type = PlaceholderValues.Title })
                    : new ApplicationNonVisualDrawingProperties());

            var spPr = new P.ShapeProperties();

            var txBody = new TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.Run(
                        new A.RunProperties { Language = "en-US" },
                        new A.Text(Text))));

            return new P.Shape(nvSpPr, spPr, txBody);
        }
    }

    public sealed class TableSpec : ShapeSpec
    {
        public string[][] Rows { get; }

        public TableSpec(string[][] rows)
        {
            Rows = rows;
        }

        public override OpenXmlElement Build(uint id)
        {
            int numCols = Rows[0].Length;

            var tblGrid = new A.TableGrid();
            for (int c = 0; c < numCols; c++)
                tblGrid.AppendChild(new A.GridColumn { Width = 2000000L });

            var tbl = new A.Table(new A.TableProperties { FirstRow = true }, tblGrid);

            foreach (var row in Rows)
            {
                var tr = new A.TableRow { Height = 370840L };
                foreach (var cellText in row)
                {
                    tr.AppendChild(new A.TableCell(
                        new A.TextBody(
                            new A.BodyProperties(),
                            new A.ListStyle(),
                            new A.Paragraph(
                                new A.Run(
                                    new A.RunProperties { Language = "en-US" },
                                    new A.Text(cellText)))),
                        new A.TableCellProperties()));
                }
                tbl.AppendChild(tr);
            }

            var graphic = new A.Graphic(
                new A.GraphicData(tbl) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" });

            return new P.GraphicFrame(
                new P.NonVisualGraphicFrameProperties(
                    new P.NonVisualDrawingProperties { Id = id, Name = $"Table{id}" },
                    new P.NonVisualGraphicFrameDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new P.Transform(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = 8000000L, Cy = 1500000L }),
                graphic);
        }
    }

    internal sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mythosia_pptx_tests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
