// Compiled by RoslynCodeTaskFactory during consumer builds; not part of the runtime library.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

public sealed class GOLogTraceRewrite : Task
{
    [Required] public ITaskItem[] Sources { get; set; }
    [Required] public ITaskItem[] References { get; set; }
    [Required] public string OutputDirectory { get; set; }
    [Required] public string ProjectDirectory { get; set; }
    public string Constants { get; set; }
    public string LanguageVersion { get; set; }
    [Output] public ITaskItem[] OriginalSources { get; set; } = new ITaskItem[0];
    [Output] public ITaskItem[] RewrittenSources { get; set; } = new ITaskItem[0];

    public override bool Execute()
    {
        try { return Rewrite(); }
        catch (Exception error) { Log.LogError("GOLOGTRACE000: Method tracing instrumentation failed: " + error); return false; }
    }
    private bool Rewrite()
    {
        Microsoft.CodeAnalysis.CSharp.LanguageVersion version;
        if (!LanguageVersionFacts.TryParse(LanguageVersion ?? "12", out version)) version = Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12;
        var parseOptions = new CSharpParseOptions(version, preprocessorSymbols:
            (Constants ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        var inputs = Sources.Where(s => File.Exists(FullPath(s.ItemSpec))).GroupBy(s => FullPath(s.ItemSpec), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
        var trees = inputs.Select(s => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(FullPath(s.ItemSpec)), Encoding.UTF8), parseOptions, FullPath(s.ItemSpec))).ToArray();
        if (!trees.Any(t => t.GetRoot().DescendantNodes().OfType<AttributeSyntax>().Any())) return true;
        var references = References.Select(r => FullPath(r.ItemSpec)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
        var compilation = CSharpCompilation.Create("GOLog.Trace.Analysis", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var originals = new List<ITaskItem>();
        var rewritten = new List<ITaskItem>();
        for (var index = 0; index < trees.Length; index++)
        {
            var tree = trees[index];
            var model = compilation.GetSemanticModel(tree);
            var changes = new List<TextChange>();
            var handled = new HashSet<MethodDeclarationSyntax>();
            foreach (var syntax in tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>())
            {
                var constructor = model.GetSymbolInfo(syntax).Symbol as IMethodSymbol;
                if (constructor == null || !IsTraceAttribute(constructor.ContainingType)) continue;
                var method = syntax.Parent.Parent as MethodDeclarationSyntax;
                if (method == null || (method.Body == null && method.ExpressionBody == null))
                { Error(syntax, "GOLOGTRACE001", "Trace decorators require a method implementation with a body; local functions, accessors and operators are not supported."); continue; }
                if (!handled.Add(method)) { Error(syntax, "GOLOGTRACE002", "Use only one Trace decorator per method."); continue; }
                var symbol = model.GetDeclaredSymbol(method);
                var attribute = symbol.GetAttributes().FirstOrDefault(a => a.ApplicationSyntaxReference != null && a.ApplicationSyntaxReference.Span == syntax.Span);
                if (attribute == null) continue;
                int level;
                string member, operation;
                if (attribute.AttributeClass.ToDisplayString() == "mGO.Log.Attributes.GOTraceAttribute")
                {
                    level = (int)attribute.ConstructorArguments[0].Value;
                    member = attribute.ConstructorArguments[1].Value as string;
                    operation = attribute.ConstructorArguments[2].Value as string;
                }
                else
                {
                    var names = new[] { "TraceAttribute", "DebugAttribute", "InformationAttribute", "WarningAttribute", "ErrorAttribute", "CriticalAttribute" };
                    level = Array.IndexOf(names, attribute.AttributeClass.Name);
                    if (attribute.AttributeClass.ContainingType == null || attribute.AttributeClass.ContainingType.ToDisplayString() != "mGO.Log.Attributes.Trace") level = -1;
                    if (level < 0) { Error(syntax, "GOLOGTRACE003", "Use GOTrace or one of the built-in Trace level attributes; custom derived attributes are not supported."); continue; }
                    member = attribute.ConstructorArguments[0].Value as string;
                    operation = attribute.ConstructorArguments[1].Value as string;
                }
                if (level < 0 || level > 5 || string.IsNullOrWhiteSpace(member) ||
                    (!SyntaxFacts.IsValidIdentifier(member) && SyntaxFacts.GetKeywordKind(member) == SyntaxKind.None) ||
                    (operation != null && string.IsNullOrWhiteSpace(operation)))
                { Error(syntax, "GOLOGTRACE004", "Use a valid log level, a single ILogger member/parameter identifier and a nonempty operation name."); continue; }
                var taskType = symbol.ReturnType as INamedTypeSymbol;
                var trackTask = !symbol.IsAsync && taskType != null && taskType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
                    (taskType.Name == "Task" || taskType.Name == "ValueTask") && taskType.Arity <= 1;
                if (trackTask && (symbol.ReturnsByRef || symbol.ReturnsByRefReadonly))
                { Error(syntax, "GOLOGTRACE005", "A traced Task/ValueTask method cannot return its task by reference."); continue; }
                operation = operation ?? symbol.ContainingType.ToDisplayString() + "." + symbol.Name;
                var variable = "__goLogMethodTrace_" + method.SpanStart;
                while (method.DescendantTokens().Any(t => t.ValueText == variable)) variable += "_";
                var start = "using var " + variable + " = new global::mGO.Log.Diagnostics.GOLogMethodTrace(@" + member + ", " + Literal(operation) + ", (global::Microsoft.Extensions.Logging.LogLevel)" + level + ");";
                var path = Literal(tree.FilePath);
                if (method.Body != null)
                {
                    var position = method.Body.OpenBraceToken.Span.End;
                    changes.Add(new TextChange(new TextSpan(position, 0), "\n#line hidden\n" + start + "\n#line " + Line(tree, position) + " " + path + "\n"));
                    if (trackTask)
                        foreach (var statement in method.Body.DescendantNodes().OfType<ReturnStatementSyntax>())
                        {
                            if (statement.Expression == null || statement.Ancestors().FirstOrDefault(n => n is MethodDeclarationSyntax || n is LocalFunctionStatementSyntax || n is AnonymousFunctionExpressionSyntax) != method) continue;
                            changes.Add(new TextChange(statement.Expression.Span, Track(variable, taskType, statement.Expression.ToString())));
                        }
                }
                else
                {
                    var expression = method.ExpressionBody.Expression;
                    string statement;
                    if (expression is ThrowExpressionSyntax) statement = expression.ToString() + ";";
                    else if (symbol.ReturnsVoid || (symbol.IsAsync && taskType != null && taskType.Arity == 0)) statement = expression.ToString() + ";";
                    else statement = "return " + (trackTask ? Track(variable, taskType, expression.ToString()) : expression.ToString()) + ";";
                    var span = TextSpan.FromBounds(method.ExpressionBody.SpanStart, method.SemicolonToken.Span.End);
                    changes.Add(new TextChange(span, "{\n#line hidden\n" + start + "\n#line " + Line(tree, expression.SpanStart) + " " + path + "\n" + statement + "\n#line hidden\n}\n#line " + Line(tree, span.End) + " " + path + "\n"));
                }
            }
            if (changes.Count == 0) continue;
            var output = tree.GetText().WithChanges(changes).ToString();
            output = "#line 1 " + Literal(tree.FilePath) + "\n" + output;
            var directory = FullPath(OutputDirectory);
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, index.ToString() + "-" + Path.GetFileName(tree.FilePath));
            if (!File.Exists(file) || File.ReadAllText(file) != output) File.WriteAllText(file, output, new UTF8Encoding(false));
            originals.Add(inputs[index]);
            var item = new TaskItem(inputs[index]); item.ItemSpec = file; rewritten.Add(item);
        }
        OriginalSources = originals.ToArray(); RewrittenSources = rewritten.ToArray();
        return !Log.HasLoggedErrors;
    }
    private static bool IsTraceAttribute(INamedTypeSymbol type)
    {
        for (; type != null; type = type.BaseType)
            if (type.ContainingAssembly.Name == "mGO.Log" && type.ToDisplayString() == "mGO.Log.Attributes.GOTraceAttribute") return true;
        return false;
    }
    private static string Track(string variable, INamedTypeSymbol type, string expression) => variable + ".Track" +
        (type.Arity == 1 ? "<" + TypeName(type.TypeArguments[0]) + ">" : "") +
        "((" + TypeName(type) + ")(" + expression + "))";
    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
    private static string Literal(string value) => SyntaxFactory.Literal(value).ToString();
    private static int Line(SyntaxTree tree, int position) => tree.GetLineSpan(new TextSpan(position, 0)).StartLinePosition.Line + 1;
    private string FullPath(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectDirectory, path));
    private void Error(SyntaxNode node, string code, string message)
    {
        var location = node.GetLocation().GetLineSpan();
        Log.LogError(null, code, null, location.Path, location.StartLinePosition.Line + 1, location.StartLinePosition.Character + 1, 0, 0, message);
    }
}
