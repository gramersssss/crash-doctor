namespace CrashDoctor.Engine;

// A saved report is the same interface with the data baked in, so it opens anywhere without the app.
public static class HtmlExporter
{
    public static string UiDir => Path.Combine(AppContext.BaseDirectory, "ui");

    public static void Save(Report r, string path)
    {
        var html = File.ReadAllText(Path.Combine(UiDir, "index.html"));
        var json = Scanner.ToJson(r).Replace("</script", "<\\/script");
        var inject = "<script>window.EXPORTED=" + json + ";</script>\n</body>";
        html = html.Replace("</body>", inject);
        File.WriteAllText(path, html);
    }
}
