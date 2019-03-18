﻿using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using MLS.Agent.CommandLine;
using MLS.Agent.Markdown;
using Recipes;

namespace MLS.Agent.Controllers
{
    public class DocumentationController : Controller
    {
        private readonly MarkdownProject _markdownProject;
        private readonly StartupOptions _startupOptions;
        private static readonly string _cacheBuster = VersionSensor.Version().ToString();

        public DocumentationController(MarkdownProject markdownProject, StartupOptions startupOptions)
        {
            _markdownProject = markdownProject ??
                               throw new ArgumentNullException(nameof(markdownProject));
            _startupOptions = startupOptions;
        }

        [HttpGet]
        [Route("{*path}")]
        public async Task<IActionResult> ShowMarkdownFile(string path)
        {
            if (_startupOptions.IsInHostedMode)
            {
                return Ok();
            }

            if (string.IsNullOrEmpty(path))
            {
                var links = string.Join(
                    "\n",
                    _markdownProject.GetAllMarkdownFiles()
                                    .Select(f =>
                                     $@"<li ><a class=""code-example"" href=""{f.Path.Value.HtmlAttributeEncode()}""><span class=""icon is-small""><i class=""source-file""></i></span><span>{f.Path.Value}</span></a></li>"));

                return Content(Index(links).ToString(), "text/html");
            }

            var relativeFilePath = new RelativeFilePath(path);

            if (!_markdownProject.TryGetMarkdownFile(relativeFilePath, out var markdownFile))
            {
                return NotFound();
            }

            var hostUrl = Request.GetUri();

            var blocks = await markdownFile.GetCodeLinkBlocks();
            var maxEditorPerSession = blocks.Any()
                                          ? blocks
                                            .GroupBy(b => b.Session)
                                            .Max(editors => editors.Count())
                                          : 0;

            var pipeline = _markdownProject.GetMarkdownPipelineFor(markdownFile.Path);
            var extension = pipeline.Extensions.FindExact<CodeLinkExtension>();
            if (extension != null)
            {
                extension.InlineControls = maxEditorPerSession <= 1;
                extension.EnablePreviewFeatures = _startupOptions.EnablePreviewFeatures;
            }

            var content = maxEditorPerSession <= 1
                              ? await OneColumnLayoutScaffold(
                                    $"{hostUrl.Scheme}://{hostUrl.Authority}",
                                    markdownFile)
                              : await TwoColumnLayoutScaffold(
                                    $"{hostUrl.Scheme}://{hostUrl.Authority}",
                                    markdownFile);

            return Content(content.ToString(), "text/html");
        }

        public static async Task<IHtmlContent> SessionControlsHtml(MarkdownFile markdownFile, bool enablePreviewFeatures = false)
        {
            var sessions= (await markdownFile
                   .GetCodeLinkBlocks())
                   .GroupBy(b => b.Session);

            var sb = new StringBuilder();

            foreach (var session in sessions)
            {
                sb.AppendLine($@"<button class=""run-button"" data-trydotnet-mode=""run"" data-trydotnet-session-id=""{session.Key}"" data-trydotnet-run-args=""{session.First().RunArgs.HtmlAttributeEncode()}"">{session.Key}</button>");
                if (enablePreviewFeatures)
                {
                    sb.AppendLine($@"<div class=""output-panel"" data-trydotnet-mode=""runResult"" data-trydotnet-output-type=""terminal"" data-trydotnet-session-id=""{session.Key}""></div>");
                }
                else
                {
                    sb.AppendLine($@"<div class=""output-panel"" data-trydotnet-mode=""runResult"" data-trydotnet-session-id=""{session.Key}""></div>");
                }
             
            }

            return new HtmlString(sb.ToString());
        }

        private IHtmlContent Layout(string hostUrl, MarkdownFile markdownFile, IHtmlContent content) =>
            $@"
<!DOCTYPE html>
<html lang=""en"">

<head>
    <meta http-equiv=""Content-Type"" content=""text/html;charset=utf-8"">
    <script src=""/api/trydotnet.min.js?v={_cacheBuster}""></script>
    <script src=""/api/trydotnet-layout.min.js?v={_cacheBuster}""></script>
    <link rel=""stylesheet"" href=""/css/trydotnet.css?v={_cacheBuster}"">
    <title>dotnet try - {markdownFile.Path.Value.HtmlEncode()}</title>
</head>

<body>
    {Header()}
    <div class=""content"">
        <div class=""documentation-container columns"">
           {content} 
        </div>
    </div>

    {Footer()}

    <script>
        trydotnet.autoEnable(new URL(""{hostUrl}""));
        trydotnetLayout.trackTopmostSession(document.getElementById(""documentation-container""), function (e){{ console.log(e); }});
    </script>
</body>

</html>".ToHtmlContent();

        private async Task<IHtmlContent> OneColumnLayoutScaffold(string hostUrl, MarkdownFile markdownFile) =>
            Layout(hostUrl, markdownFile,
                   $@"
            <div id=""documentation-container"" class=""code-single-column"">
                {await markdownFile.ToHtmlContentAsync()}
            </div>".ToHtmlContent());

        private async Task<IHtmlContent> TwoColumnLayoutScaffold(string hostUrl, MarkdownFile markdownFile) =>
            Layout(hostUrl, markdownFile,
                   $@"
            <div id=""documentation-container"" class=""code-column"">
                {await markdownFile.ToHtmlContentAsync()}
            </div>
            <div class=""control-column"">
                {await SessionControlsHtml(markdownFile, _startupOptions.EnablePreviewFeatures)}
            </div>".ToHtmlContent());

        private IHtmlContent Index(string html) =>
            $@"
<!DOCTYPE html>
<html lang=""en"">

<head>
    <meta http-equiv=""Content-Type"" content=""text/html;charset=utf-8"">
    <link rel=""stylesheet"" href=""/css/trydotnet.css?v={_cacheBuster}"">
    <title>dotnet try - {_startupOptions.RootDirectory.FullName.HtmlEncode()}</title>
</head>

<body>
    {Header()}
    <div class=""content"">
        <ul class=""code-example-list"">
            {html}
        </ul>
    </div>

    {Footer()}

</body>

</html>".ToHtmlContent();

        private IHtmlContent Header() => $@"
<header class=""dotnet-try-header"">
        <div>
            <span class=""dotnet-try"">dotnet try</span>
            <span class=""project-file-path"">{_startupOptions.RootDirectory.FullName.ToLowerInvariant().HtmlEncode()}</span>
        </div>
        <a href=""https://dotnet.microsoft.com/platform/try-dotnet"">Powered by Try .NET</a>
    </div>
</header>".ToHtmlContent();

        private IHtmlContent Footer() => @"
<footer class=""footer"">
  <div class=""content has-text-centered"">
    <ul>
        <li>
            <a href=""https://teams.microsoft.com/l/channel/19%3a32c2f8c34d4b4136b4adf554308363fc%40thread.skype/Try%2520.NET?groupId=fdff90ed-0b3b-4caa-a30a-efb4dd47665f&tenantId=72f988bf-86f1-41af-91ab-2d7cd011db47"">Ask a question or tell us about a bug</a>
        </li>
        <li>
            <a href=""https://dotnet.microsoft.com/platform/support-policy"">Support Policy</a>
        </li>
        <li>
            <a href=""https://go.microsoft.com/fwlink/?LinkId=521839"">Privacy &amp; Cookies</a>
        </li>
        <li>
            <a href=""https://go.microsoft.com/fwlink/?LinkID=206977"">Terms of Use</a>
        </li>
        <li>
            <a href=""https://www.microsoft.com/trademarks"">Trademarks</a>
        </li>
        <li>
            © Microsoft 2019
        </li>
    </ul>
  </div>
</footer>".ToHtmlContent();
    }
}