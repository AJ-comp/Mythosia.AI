using Mythosia.AI.Tools.DocumentPreview;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPreviewEndpoints();

app.Run();
