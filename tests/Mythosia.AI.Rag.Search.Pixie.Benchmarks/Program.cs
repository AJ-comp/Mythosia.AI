using Mythosia.AI.Rag.Evaluation;

// Compatibility entry point; comparison logic is shared with all retrieval evaluations.
var forwarded = args.ToList();
if (!forwarded.Contains("--methods"))
{
    var denseIndex = forwarded.IndexOf("--dense");
    var hasDense = denseIndex >= 0 && denseIndex + 1 < forwarded.Count && forwarded[denseIndex + 1] != "none";
    forwarded.AddRange(["--methods", hasDense ? "bm25,pixie,dense,hybrid-bm25,hybrid-pixie" : "bm25,pixie"]);
}
return await EvaluationCommand.RunAsync(forwarded.ToArray());
