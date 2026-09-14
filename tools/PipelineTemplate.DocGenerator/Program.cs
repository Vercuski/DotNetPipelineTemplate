using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Generates Pipeline_Template_Documentation.md from the XML doc comments in the
// Domain, Application, and Infrastructure layers — the actual template library.
// Samples and the Core facade are deliberately excluded: samples aren't part of the
// distributed template, and Core has no source of its own to document.
//
// Usage: dotnet run --project tools/PipelineTemplate.DocGenerator -- <repoRoot>

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: PipelineTemplate.DocGenerator <repoRoot>");
    return 1;
}

var repoRoot = Path.GetFullPath(args[0]);

(string LayerName, string RelativePath)[] layers =
[
    ("Domain", "src/PipelineTemplate.Domain"),
    ("Application", "src/PipelineTemplate.Application"),
    ("Infrastructure", "src/PipelineTemplate.Infrastructure"),
];

var parseOptions = new CSharpParseOptions(documentationMode: DocumentationMode.Parse);
var treeToLayer = new Dictionary<SyntaxTree, string>();
var allTrees = new List<SyntaxTree>();

// The real projects have <ImplicitUsings>enable</ImplicitUsings>, which makes the SDK
// generate a GlobalUsings.g.cs file at build time — since we're parsing source
// directly rather than going through MSBuild, that file never gets generated, so
// System/System.Threading/etc. wouldn't resolve without this synthetic equivalent.
const string implicitUsings = """
    global using System;
    global using System.Collections.Generic;
    global using System.IO;
    global using System.Linq;
    global using System.Threading;
    global using System.Threading.Tasks;
    """;
allTrees.Add(CSharpSyntaxTree.ParseText(implicitUsings, parseOptions, path: "<ImplicitUsings>"));

foreach (var (layerName, relativePath) in layers)
{
    var layerDir = Path.Combine(repoRoot, relativePath);
    if (!Directory.Exists(layerDir))
    {
        Console.Error.WriteLine($"Warning: layer directory not found: {layerDir}");
        continue;
    }

    var csFiles = Directory.GetFiles(layerDir, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    foreach (var file in csFiles)
    {
        var text = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(text, parseOptions, path: file);
        allTrees.Add(tree);
        treeToLayer[tree] = layerName;
    }
}

// Reference the current runtime's own assemblies — sufficient here since Domain,
// Application, and Infrastructure deliberately have zero external package
// dependencies (see the Domain.csproj comment on why that's a design invariant, not
// an accident).
var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
var references = Directory.GetFiles(runtimeDir, "*.dll")
    .Select(dll =>
    {
        try
        {
            return MetadataReference.CreateFromFile(dll);
        }
        catch
        {
            return null;
        }
    })
    .Where(r => r is not null)
    .Select(r => r!)
    .ToList();

var compilation = CSharpCompilation.Create(
    "PipelineTemplateDocGen",
    allTrees,
    references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
if (diagnostics.Count > 0)
{
    Console.Error.WriteLine($"Warning: {diagnostics.Count} compilation error(s) encountered while analyzing source for documentation. Proceeding anyway — doc generation only needs symbol/doc-comment info, not a fully error-free compile.");
    foreach (var diagnostic in diagnostics.Take(10))
    {
        Console.Error.WriteLine($"  {diagnostic}");
    }
}

var typesByLayer = new SortedDictionary<string, List<INamedTypeSymbol>>();
var seenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

foreach (var tree in allTrees)
{
    var semanticModel = compilation.GetSemanticModel(tree);
    var root = tree.GetRoot();
    if (!treeToLayer.TryGetValue(tree, out var layerName))
    {
        continue; // the synthetic implicit-usings tree has no layer and no types to document
    }

    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
    {
        if (semanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
        {
            continue;
        }

        if (symbol.DeclaredAccessibility != Accessibility.Public || !seenTypes.Add(symbol))
        {
            continue;
        }

        if (!typesByLayer.TryGetValue(layerName, out var list))
        {
            list = new List<INamedTypeSymbol>();
            typesByLayer[layerName] = list;
        }

        list.Add(symbol);
    }
}

var markdown = new StringBuilder();
markdown.AppendLine("# DotNet Pipeline Template — API Documentation");
markdown.AppendLine();
markdown.AppendLine("**Auto-generated from XML doc comments on every push to `main` — do not edit by hand.**");
markdown.AppendLine("Regenerated by `.github/workflows/generate-docs.yml` from `tools/PipelineTemplate.DocGenerator`.");
markdown.AppendLine("Covers the Domain, Application, and Infrastructure layers — the actual template library.");
markdown.AppendLine("Samples and the Core facade (no source of its own) are out of scope for this document.");
markdown.AppendLine();
markdown.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
markdown.AppendLine();

foreach (var (layerName, _) in layers)
{
    if (!typesByLayer.TryGetValue(layerName, out var types) || types.Count == 0)
    {
        continue;
    }

    markdown.AppendLine($"## {layerName} Layer");
    markdown.AppendLine();

    foreach (var type in types
        .OrderBy(t => t.ContainingNamespace?.ToDisplayString() ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(t => t.Name, StringComparer.Ordinal))
    {
        WriteType(markdown, type);
    }
}

var outputPath = Path.Combine(repoRoot, "Pipeline_Template_Documentation.md");
File.WriteAllText(outputPath, markdown.ToString());
Console.WriteLine($"Wrote {outputPath} ({typesByLayer.Values.Sum(v => v.Count)} public types documented).");
return 0;

static void WriteType(StringBuilder markdown, INamedTypeSymbol type)
{
    var kind = type.TypeKind switch
    {
        TypeKind.Interface => "interface",
        TypeKind.Struct when type.IsRecord => "record struct",
        TypeKind.Struct => "struct",
        TypeKind.Class when type.IsRecord => "record",
        TypeKind.Class when type.IsAbstract => "abstract class",
        TypeKind.Class when type.IsSealed => "sealed class",
        TypeKind.Class => "class",
        _ => "type",
    };

    var displaySignature = type.ToDisplayString(SymbolDisplayFormats.TypeSignature);
    markdown.AppendLine($"### `{displaySignature}` ({kind})");
    markdown.AppendLine();

    WriteDocComment(markdown, type.GetDocumentationCommentXml());

    var members = type.GetMembers()
        .Where(IsDocumentableMember)
        .OrderBy(m => m.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0)
        .ToList();

    foreach (var member in members)
    {
        WriteMember(markdown, member);
    }

    markdown.AppendLine("---");
    markdown.AppendLine();
}

static bool IsDocumentableMember(ISymbol member)
{
    if (member.IsImplicitlyDeclared)
    {
        return false;
    }

    if (member.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected))
    {
        return false;
    }

    if (member is IMethodSymbol method &&
        method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
            or MethodKind.EventAdd or MethodKind.EventRemove)
    {
        return false;
    }

    // Positional-record primary-constructor properties are implicitly declared and
    // already filtered above; this additionally skips the compiler-synthesized
    // Equals/GetHashCode/ToString/Deconstruct/Clone overrides records generate,
    // which are rarely worth documenting even when they carry no [CompilerGenerated]
    // marker in the symbol model.
    if (member is IMethodSymbol { Name: "Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "<Clone>$" })
    {
        return false;
    }

    return true;
}

static void WriteMember(StringBuilder markdown, ISymbol member)
{
    var signature = member switch
    {
        IMethodSymbol m => m.ToDisplayString(SymbolDisplayFormats.MemberSignature),
        IPropertySymbol p => p.ToDisplayString(SymbolDisplayFormats.MemberSignature),
        IFieldSymbol f => f.ToDisplayString(SymbolDisplayFormats.MemberSignature),
        _ => member.ToDisplayString(),
    };

    markdown.AppendLine($"#### `{signature}`");
    markdown.AppendLine();
    WriteDocComment(markdown, member.GetDocumentationCommentXml());
}

static void WriteDocComment(StringBuilder markdown, string? xml)
{
    if (string.IsNullOrWhiteSpace(xml))
    {
        markdown.AppendLine("_Undocumented._");
        markdown.AppendLine();
        return;
    }

    XElement root;
    try
    {
        root = XElement.Parse(xml);
    }
    catch
    {
        markdown.AppendLine("_Documentation comment could not be parsed._");
        markdown.AppendLine();
        return;
    }

    var summary = root.Element("summary");
    if (summary is not null)
    {
        markdown.AppendLine(DocText.RenderBlock(summary));
        markdown.AppendLine();
    }

    var typeParams = root.Elements("typeparam").ToList();
    if (typeParams.Count > 0)
    {
        markdown.AppendLine("**Type Parameters:**");
        markdown.AppendLine();
        foreach (var tp in typeParams)
        {
            markdown.AppendLine($"- `{tp.Attribute("name")?.Value}` — {DocText.RenderInlineNormalized(tp)}");
        }

        markdown.AppendLine();
    }

    var parameters = root.Elements("param").ToList();
    if (parameters.Count > 0)
    {
        markdown.AppendLine("**Parameters:**");
        markdown.AppendLine();
        foreach (var p in parameters)
        {
            markdown.AppendLine($"- `{p.Attribute("name")?.Value}` — {DocText.RenderInlineNormalized(p)}");
        }

        markdown.AppendLine();
    }

    var returns = root.Element("returns");
    if (returns is not null)
    {
        markdown.AppendLine($"**Returns:** {DocText.RenderInlineNormalized(returns)}");
        markdown.AppendLine();
    }

    var exceptions = root.Elements("exception").ToList();
    if (exceptions.Count > 0)
    {
        markdown.AppendLine("**Throws:**");
        markdown.AppendLine();
        foreach (var ex in exceptions)
        {
            var crefName = DocText.ShortNameFromCref(ex.Attribute("cref")?.Value);
            markdown.AppendLine($"- `{crefName}` — {DocText.RenderInlineNormalized(ex)}");
        }

        markdown.AppendLine();
    }

    var remarks = root.Element("remarks");
    if (remarks is not null)
    {
        markdown.AppendLine("**Remarks:**");
        markdown.AppendLine();
        markdown.AppendLine(DocText.RenderBlock(remarks));
        markdown.AppendLine();
    }
}

/// <summary>Reusable <see cref="SymbolDisplayFormat"/> instances for readable signatures.</summary>
internal static class SymbolDisplayFormats
{
    public static readonly SymbolDisplayFormat TypeSignature = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static readonly SymbolDisplayFormat MemberSignature = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeAccessibility
            | SymbolDisplayMemberOptions.IncludeConstantValue,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
}

/// <summary>Converts XML doc comment fragments into Markdown-friendly text.</summary>
internal static class DocText
{
    public static string RenderBlock(XElement element)
    {
        var paragraphs = element.Elements("para").ToList();
        if (paragraphs.Count == 0)
        {
            return NormalizeWhitespace(RenderInline(element));
        }

        return string.Join("\n\n", paragraphs.Select(p => NormalizeWhitespace(RenderInline(p))));
    }

    public static string RenderInline(XElement element)
    {        var sb = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement el when el.Name == "see":
                    sb.Append('`').Append(ShortNameFromCref(el.Attribute("cref")?.Value)
                        ?? el.Attribute("langword")?.Value ?? "").Append('`');
                    break;
                case XElement el when el.Name == "seealso":
                    sb.Append('`').Append(ShortNameFromCref(el.Attribute("cref")?.Value)).Append('`');
                    break;
                case XElement el when el.Name == "paramref" || el.Name == "typeparamref":
                    sb.Append('`').Append(el.Attribute("name")?.Value).Append('`');
                    break;
                case XElement el when el.Name == "c":
                    sb.Append('`').Append(el.Value).Append('`');
                    break;
                case XElement el when el.Name == "code":
                    sb.Append("\n```csharp\n").Append(el.Value.Trim('\n')).Append("\n```\n");
                    break;
                case XElement el when el.Name == "para":
                    sb.Append('\n').Append(RenderInline(el)).Append('\n');
                    break;
                case XElement el when el.Name == "b":
                    sb.Append("**").Append(RenderInline(el)).Append("**");
                    break;
                case XElement el:
                    sb.Append(RenderInline(el));
                    break;
            }
        }

        return sb.ToString();
    }

    public static string RenderInlineNormalized(XElement element) => NormalizeWhitespace(RenderInline(element));

    public static string? ShortNameFromCref(string? cref)
    {
        if (string.IsNullOrEmpty(cref))
        {
            return null;
        }

        // cref looks like "T:Namespace.Type" or "M:Namespace.Type.Method(Args)" —
        // strip the "X:" prefix and any parameter list, then take the last dotted
        // segment as a readable short name.
        var withoutPrefix = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var parenIndex = withoutPrefix.IndexOf('(');
        var withoutParams = parenIndex >= 0 ? withoutPrefix[..parenIndex] : withoutPrefix;
        var lastDot = withoutParams.LastIndexOf('.');
        return lastDot >= 0 ? withoutParams[(lastDot + 1)..] : withoutParams;
    }

    private static string NormalizeWhitespace(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join(' ', lines);
    }
}
